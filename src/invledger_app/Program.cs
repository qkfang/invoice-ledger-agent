using Azure.AI.Projects;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using InvLedgerAgent.Agents;
using InvLedgerAgent.Api;
using InvLedgerAgent.Mcp;
using InvLedgerAgent.Services;
using OpenAI.Responses;
using OpenTelemetry.Instrumentation.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
    builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options =>
    {
        options.FilterHttpRequestMessage = req =>
        {
            var host = req.RequestUri?.Host;
            if (string.IsNullOrEmpty(host)) return true;
            return !host.EndsWith("livediagnostics.monitor.azure.com", StringComparison.OrdinalIgnoreCase);
        };
    });
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();

var endpoint = builder.Configuration["AZURE_AI_PROJECT_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");
var docIntelligenceEndpoint = builder.Configuration["AZURE_DOC_INTELLIGENCE_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_DOC_INTELLIGENCE_ENDPOINT is not set.");
var foundryEndpoint = builder.Configuration["AZURE_AI_FOUNDRY_ENDPOINT"]
    ?? new Uri(endpoint).GetLeftPart(UriPartial.Authority);

var tenantId = builder.Configuration["AZURE_TENANT_ID"];
var credential = new Azure.Identity.DefaultAzureCredential(new Azure.Identity.DefaultAzureCredentialOptions
{
    TenantId = tenantId,
    ExcludeVisualStudioCodeCredential = true,
    ExcludeSharedTokenCacheCredential = true
});

builder.Services.AddSingleton(sp => new DocIntelligenceService(
    docIntelligenceEndpoint, credential, sp.GetRequiredService<ILogger<DocIntelligenceService>>()));

var storageAccountName = builder.Configuration["AZURE_STORAGE_ACCOUNT_NAME"]
    ?? throw new InvalidOperationException("AZURE_STORAGE_ACCOUNT_NAME is not set.");
builder.Services.AddSingleton(sp => new BlobStorageService(
    storageAccountName, credential, sp.GetRequiredService<ILogger<BlobStorageService>>()));
var cuGpt41Deployment = builder.Configuration["AZURE_CU_GPT41_DEPLOYMENT"] ?? "gpt-4.1";
var cuGpt41MiniDeployment = builder.Configuration["AZURE_CU_GPT41_MINI_DEPLOYMENT"] ?? "gpt-4.1-mini";
var cuEmbeddingDeployment = builder.Configuration["AZURE_CU_EMBEDDING_DEPLOYMENT"] ?? "text-embedding-3-large";
builder.Services.AddSingleton(sp => new ContentUnderstandingService(
    foundryEndpoint, credential, cuGpt41Deployment, cuGpt41MiniDeployment, cuEmbeddingDeployment,
    sp.GetRequiredService<ILogger<ContentUnderstandingService>>()));
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<PendingApprovalStore>();
builder.Services.AddSingleton<GeneralLedgerService>();
builder.Services.AddSingleton<FxRateService>();

builder.Services.AddMcpServer()
    .WithHttpTransport(options => { options.Stateless = true; })
    .WithTools<InvLedgerMcpTools>();

builder.Services.AddCors();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

// Normalize Accept header for MCP requests from Foundry agent server
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp"))
    {
        var accept = context.Request.Headers.Accept.ToString();
        if (string.IsNullOrEmpty(accept) || !accept.Contains("text/event-stream"))
        {
            context.Request.Headers.Accept = "application/json, text/event-stream";
        }
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapMcp("/mcp");
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/index.html"));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

var deploymentName = app.Configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"]
    ?? throw new InvalidOperationException("AZURE_AI_MODEL_DEPLOYMENT_NAME is not set.");

var aiProjectClient = new AIProjectClient(new Uri(endpoint), credential);
var appMcpUrl = app.Configuration["APP_MCP_URL"] ?? "http://localhost:5001";
var appMcpTool = ResponseTool.CreateMcpTool(
    serverLabel: "invledger-mcp",
    serverUri: new Uri($"{appMcpUrl}/mcp"),
    toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval));

var appMcpToolWithApproval = ResponseTool.CreateMcpTool(
    serverLabel: "invledger-mcp",
    serverUri: new Uri($"{appMcpUrl}/mcp"),
    toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval));

var notificationAgent = new InvLdgAgNotification(aiProjectClient, deploymentName, [appMcpTool], loggerFactory.CreateLogger<InvLdgAgNotification>());
var correspondenceAgent = new InvLdgAgCorrespondence(aiProjectClient, deploymentName, [appMcpToolWithApproval], loggerFactory.CreateLogger<InvLdgAgCorrespondence>());
var extractDiAgent = new InvLdgAgExtractDI(aiProjectClient, deploymentName, [appMcpTool], loggerFactory.CreateLogger<InvLdgAgExtractDI>());
var extractCuAgent = new InvLdgAgExtractCU(aiProjectClient, deploymentName, loggerFactory.CreateLogger<InvLdgAgExtractCU>());
var ingestionAgent = new InvLdgAgIngestion(aiProjectClient, deploymentName, null, loggerFactory.CreateLogger<InvLdgAgIngestion>());
var invoiceAgent = new InvLdgAgInvoice(aiProjectClient, deploymentName, [appMcpTool], loggerFactory.CreateLogger<InvLdgAgInvoice>());
var processingAgent = new InvLdgAgProcessing(aiProjectClient, deploymentName, null, loggerFactory.CreateLogger<InvLdgAgProcessing>());
var exceptionAgent = new InvLdgAgException(aiProjectClient, deploymentName, [appMcpTool], loggerFactory.CreateLogger<InvLdgAgException>());
var ledgerAgent = new InvLdgAgLedger(aiProjectClient, deploymentName, null, loggerFactory.CreateLogger<InvLdgAgLedger>());
var docService = app.Services.GetRequiredService<DocIntelligenceService>();
var cuService = app.Services.GetRequiredService<ContentUnderstandingService>();
await cuService.InitializeAsync();
var blobStorage = app.Services.GetRequiredService<BlobStorageService>();
var notificationService = app.Services.GetRequiredService<NotificationService>();
var approvalStore = app.Services.GetRequiredService<PendingApprovalStore>();

var fabricWorkspaceId = app.Configuration["FABRIC_LAKEHOUSE_WORKSPACE_ID"];
var fabricLakehouseId = app.Configuration["FABRIC_LAKEHOUSE_ID"];
FabricLakehouseService? fabricLakehouse = null;
if (!string.IsNullOrWhiteSpace(fabricWorkspaceId) && !string.IsNullOrWhiteSpace(fabricLakehouseId))
{
    var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();
    fabricLakehouse = new FabricLakehouseService(fabricWorkspaceId, fabricLakehouseId, credential,
        httpClientFactory, loggerFactory.CreateLogger<FabricLakehouseService>());
}

var fxRateService = app.Services.GetRequiredService<FxRateService>();
app.MapAllEndpoints(notificationAgent, correspondenceAgent, extractDiAgent, extractCuAgent,
    ingestionAgent, invoiceAgent, processingAgent, exceptionAgent, ledgerAgent,
    docService, cuService, blobStorage, notificationService, approvalStore, fxRateService, fabricLakehouse, logger);

await app.RunAsync();
