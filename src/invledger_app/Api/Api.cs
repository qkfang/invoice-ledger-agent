using InvLedgerAgent.Agents;
using InvLedgerAgent.Services;
using System.Text.Json;

namespace InvLedgerAgent.Api;

record NoticeUrlRequest(string Url);
record NoticeTextRequest(string Text);
record JsonRequest(string Json);
record ApproveRequest(string RunId, bool Approved);
record SendEmailRequest(string To, string Subject, string Body);
record CorrespondenceChatRequest(string PreviousResponseId, string Message);
record ProcessEmailRequest(string EmailJson, string AttachmentJson);
record ProcessScenarioRequest(string ScenarioName);

public static class Endpoints
{
    public static void MapAllEndpoints(this WebApplication app,
        InvLdgAgNotification notificationAgent,
        InvLdgAgCorrespondence correspondenceAgent,
        InvLdgAgExtractDI extractDiAgent, InvLdgAgExtractCU extractCuAgent,
        InvLdgAgIngestion ingestionAgent, InvLdgAgInvoice invoiceAgent,
        InvLdgAgProcessing processingAgent, InvLdgAgException exceptionAgent,
        InvLdgAgLedger ledgerAgent,
        DocIntelligenceService docService, ContentUnderstandingService cuService,
        BlobStorageService blobStorage, NotificationService notificationService,
        PendingApprovalStore approvalStore, FxRateService fxRateService,
        FabricLakehouseService? fabricLakehouse,
        string webRootPath,
        ILogger logger)
    {
        app.MapGet("/agents/instructions", () =>
        {
            var agents = new BaseAgent[]
            {
                ingestionAgent, invoiceAgent, processingAgent, exceptionAgent, ledgerAgent,
                extractDiAgent, extractCuAgent, notificationAgent, correspondenceAgent
            };
            return Results.Ok(agents.ToDictionary(a => a.AgentId, a => a.Instructions));
        });

        app.MapPost("/extract/di/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Extract DI upload: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var result = await docService.AnalyzeFromUrlAsync(blobUrl);
            return Results.Ok(new { markdown = result.Markdown, json = result.Json });
        });

        app.MapPost("/extract/cu/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            var fieldsJson = form["fields"].ToString();
            List<CuFieldSpec> fieldSpecs = new();
            if (!string.IsNullOrWhiteSpace(fieldsJson))
            {
                try
                {
                    fieldSpecs = JsonSerializer.Deserialize<List<CuFieldSpec>>(fieldsJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
                catch (JsonException ex)
                {
                    return Results.BadRequest(new { error = $"invalid fields JSON: {ex.Message}" });
                }
            }

            logger.LogInformation("Extract CU upload: {FileName} ({Size} bytes), {FieldCount} custom field(s)",
                Sanitize(file.FileName), file.Length, fieldSpecs.Count);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);

            if (fieldSpecs.Count == 0)
            {
                var result = await cuService.AnalyzeFromUrlAsync(blobUrl);
                return Results.Ok(new { markdown = result.Markdown, json = result.Json, fields = new Dictionary<string, CuFieldValue>() });
            }

            var extraction = await cuService.AnalyzeWithCustomFieldsAsync(blobUrl, fieldSpecs);
            return Results.Ok(new { markdown = extraction.Markdown, json = extraction.Json, fields = extraction.Fields });
        });

        app.MapPost("/extract/agent/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Extract Agent upload: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var response = await extractDiAgent.RunAsync(blobUrl.ToString());
            return Results.Ok(new { response });
        });

        app.MapPost("/notification/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Notification upload: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var cuResult = await cuService.AnalyzeFromUrlAsync(blobUrl);
            var response = await extractCuAgent.RunAsync(cuResult.Markdown);
            return Results.Ok(new { markdown = cuResult.Markdown, json = cuResult.Json, response });
        });

        app.MapPost("/notification/assign", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Notification assign request ({Length} chars)", request.Json.Length);
            var response = await notificationAgent.RunAsync(request.Json);
            return Results.Ok(new { response });
        });

        app.MapPost("/correspondence/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Correspondence upload: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var cuResult = await cuService.AnalyzeFromUrlAsync(blobUrl);
            var extracted = await extractCuAgent.RunAsync(cuResult.Markdown);

            var step = await correspondenceAgent.StartRunAsync(extracted);
            return BuildCorrespondenceResponse(step, approvalStore, extracted);
        });

        app.MapPost("/correspondence/chat", async (CorrespondenceChatRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.PreviousResponseId) || string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "previousResponseId and message are required" });

            logger.LogInformation("Correspondence chat ({Length} chars)", request.Message.Length);
            var step = await correspondenceAgent.ChatAsync(request.PreviousResponseId, request.Message);
            return BuildCorrespondenceResponse(step, approvalStore, null);
        });

        app.MapPost("/correspondence/approve", async (ApproveRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.RunId))
                return Results.BadRequest(new { error = "runId is required" });

            var state = approvalStore.Get(request.RunId);
            if (state is null)
                return Results.NotFound(new { error = "run not found or already completed" });

            approvalStore.Remove(request.RunId);
            logger.LogInformation("Correspondence approval: runId={RunId} approved={Approved}", Sanitize(request.RunId), request.Approved);

            var step = await correspondenceAgent.ContinueRunAsync(state.PreviousResponseId, state.ApprovalItemId, request.Approved);
            return BuildCorrespondenceResponse(step, approvalStore, null);
        });

        app.MapPost("/ingestion/run", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Ingestion run request ({Length} chars)", request.Json.Length);
            var response = await ingestionAgent.RunAsync(
                $"Validate and extract the envelope from this vendor invoice document:\n\n{request.Json}");
            return Results.Ok(new { response });
        });

        app.MapPost("/invoice/run", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Invoice run request ({Length} chars)", request.Json.Length);
            var response = await invoiceAgent.RunAsync(
                $"Extract structured invoice details from this vendor invoice document:\n\n{request.Json}");
            return Results.Ok(new { response });
        });

        app.MapPost("/invoice/process-doc", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Invoice process-doc: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var cuResult = await cuService.AnalyzeFromUrlAsync(blobUrl);
            var response = await invoiceAgent.RunAsync(
                $"Extract structured invoice details from this document. Use the extractDoc_CU tool with this document URL: {blobUrl}");
            return Results.Ok(new { markdown = cuResult.Markdown, json = cuResult.Json, response });
        });

        app.MapPost("/processing/run", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Processing run request ({Length} chars)", request.Json.Length);
            var input = $"Process the following invoice. Use the fx_convert MCP tool to convert the totalAmount to AUD, then return the standardised extracted invoice JSON.\n\nInvoice:\n{request.Json}";
            var response = await processingAgent.RunAsync(input);
            return Results.Ok(new { input, response });
        });

        app.MapPost("/exception/draft", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Exception draft request ({Length} chars)", request.Json.Length);
            var response = await exceptionAgent.RunAsync(request.Json);
            return Results.Ok(new { response });
        });

        app.MapPost("/ledger/ask", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Ledger ask request ({Length} chars)", request.Json.Length);
            var response = await ledgerAgent.RunAsync(request.Json);
            return Results.Ok(new { response });
        });

        app.MapPost("/ingestion/save-to-fabric", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            if (fabricLakehouse is null)
                return Results.BadRequest(new { error = "Fabric Lakehouse is not configured. Set FABRIC_LAKEHOUSE_WORKSPACE_ID and FABRIC_LAKEHOUSE_ID." });

            logger.LogInformation("Save to Fabric: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var path = await fabricLakehouse.UploadAsync(stream, file.FileName, file.Length);
            return Results.Ok(new { path, documentName = file.FileName });
        });

        app.MapGet("/fx-rates", () => Results.Ok(fxRateService.GetRates()));

        app.MapPut("/fx-rates", (List<FxRate> rates) =>
        {
            fxRateService.UpdateRates(rates);
            return Results.Ok(fxRateService.GetRates());
        });

        app.MapPost("/ingestion/process-email", async (ProcessEmailRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.EmailJson))
                return Results.BadRequest(new { error = "emailJson is required" });

            var dt = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var folder = $"email-{dt}";
            var savedFiles = new List<object>();

            async Task<object> SaveJsonFile(string json, string fileName)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                var blobPath = $"{folder}/{fileName}";
                Uri blobUrl;
                using (var ms = new MemoryStream(bytes))
                    blobUrl = await blobStorage.UploadAsync(ms, blobPath);
                string? fabricPath = null;
                if (fabricLakehouse != null)
                {
                    using var ms = new MemoryStream(bytes);
                    fabricPath = await fabricLakehouse.UploadAsync(ms, blobPath, bytes.Length);
                }
                return new { name = fileName, folder, blobUrl = blobUrl.ToString(), fabricPath };
            }

            savedFiles.Add(await SaveJsonFile(request.EmailJson, $"{folder}-body.json"));
            if (!string.IsNullOrWhiteSpace(request.AttachmentJson))
                savedFiles.Add(await SaveJsonFile(request.AttachmentJson, $"{folder}-attachment.json"));

            var prompt = string.IsNullOrWhiteSpace(request.AttachmentJson)
                ? $"Process this email:\n\n{request.EmailJson}"
                : $"Process this email and its attachments:\n\n{request.EmailJson}\n\nAttachment data:\n{request.AttachmentJson}";

            logger.LogInformation("Process email: saved {Count} file(s) to folder {Folder}", savedFiles.Count, Sanitize(folder));
            var agentResponse = await ingestionAgent.RunAsync(prompt);
            return Results.Ok(new { folder, agentResponse, savedFiles });
        });

        app.MapGet("/ingestion/scenarios", async () =>
        {
            var scenariosPath = Path.Combine(webRootPath, "scenarios");
            if (!Directory.Exists(scenariosPath))
                return Results.Ok(Array.Empty<object>());

            var scenarios = new List<object>();
            foreach (var dir in Directory.GetDirectories(scenariosPath).OrderBy(d => d))
            {
                var name = Path.GetFileName(dir);
                var emailPath = Path.Combine(dir, "email.json");
                if (!File.Exists(emailPath)) continue;

                var emailJson = await File.ReadAllTextAsync(emailPath);
                JsonElement emailObj;
                try { emailObj = JsonSerializer.Deserialize<JsonElement>(emailJson); }
                catch { continue; }

                var pdfs = Directory.GetFiles(dir, "*.pdf").Select(Path.GetFileName).ToList();
                scenarios.Add(new { name, email = emailObj, pdfs });
            }
            return Results.Ok(scenarios);
        });

        app.MapPost("/ingestion/process-scenario", async (ProcessScenarioRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.ScenarioName))
                return Results.BadRequest(new { error = "scenarioName is required" });

            var scenarioDir = Path.Combine(webRootPath, "scenarios", request.ScenarioName);
            if (!Directory.Exists(scenarioDir))
                return Results.BadRequest(new { error = "Scenario not found" });

            var emailPath = Path.Combine(scenarioDir, "email.json");
            if (!File.Exists(emailPath))
                return Results.BadRequest(new { error = "email.json not found in scenario" });

            var emailJson = await File.ReadAllTextAsync(emailPath);
            JsonElement emailObj;
            try { emailObj = JsonSerializer.Deserialize<JsonElement>(emailJson); }
            catch { return Results.BadRequest(new { error = "Invalid email.json" }); }

            var dt = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var runName = $"run-{dt}";
            var runDir = Path.Combine(Path.GetTempPath(), "invledger-temp", runName);
            Directory.CreateDirectory(runDir);

            await File.WriteAllTextAsync(Path.Combine(runDir, "email.json"), emailJson);

            var pdfFileNames = new List<string>();
            if (emailObj.TryGetProperty("attachments", out var attachments))
            {
                foreach (var att in attachments.EnumerateArray())
                {
                    if (att.TryGetProperty("name", out var nameEl))
                    {
                        var pdfName = nameEl.GetString() ?? "";
                        if (!pdfName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                        pdfFileNames.Add(pdfName);

                        var scenarioPdfPath = Path.Combine(scenarioDir, pdfName);
                        if (!File.Exists(scenarioPdfPath))
                        {
                            var sourcePath = Path.Combine(webRootPath, "invoices", pdfName);
                            if (File.Exists(sourcePath))
                                File.Copy(sourcePath, scenarioPdfPath);
                            else
                                logger.LogWarning("PDF {PdfName} not found in scenario or invoices folder", Sanitize(pdfName));
                        }
                    }
                }
            }

            var blobUrls = new Dictionary<string, string>();
            var savedFiles = new List<string> { "email.json" };

            foreach (var pdfName in pdfFileNames)
            {
                var scenarioPdfPath = Path.Combine(scenarioDir, pdfName);
                if (!File.Exists(scenarioPdfPath)) continue;

                File.Copy(scenarioPdfPath, Path.Combine(runDir, pdfName), overwrite: true);
                savedFiles.Add(pdfName);

                using var stream = File.OpenRead(scenarioPdfPath);
                var blobUrl = await blobStorage.UploadAsync(stream, $"{runName}/{pdfName}");
                blobUrls[pdfName] = blobUrl.ToString();
            }

            var urlList = string.Join("\n", blobUrls.Select(kv => $"- {kv.Key}: {kv.Value}"));
            var prompt = string.IsNullOrEmpty(urlList)
                ? $"Process this email (no PDF attachments were found to extract):\n\n{emailJson}"
                : $"Process this email and extract invoice details from each attached PDF.\n\nEmail:\n{emailJson}\n\nPDF Documents (use extractDoc_DI tool for each URL):\n{urlList}";

            logger.LogInformation("Process scenario: {Scenario}, {Count} PDF(s)", Sanitize(request.ScenarioName), pdfFileNames.Count);
            var agentResponse = await ingestionAgent.RunAsync(prompt);

            var extractedFiles = new List<string>();
            foreach (var pdfName in blobUrls.Keys)
            {
                var baseName = Path.GetFileNameWithoutExtension(pdfName);
                var extractedFileName = $"{baseName}.extracted.json";
                var extractedContent = JsonSerializer.Serialize(new
                {
                    source = pdfName,
                    agentResponse,
                    processedAt = DateTime.UtcNow.ToString("o")
                }, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(Path.Combine(runDir, extractedFileName), extractedContent);
                extractedFiles.Add(extractedFileName);
                savedFiles.Add(extractedFileName);
            }

            return Results.Ok(new { runFolder = runName, agentResponse, extractedFiles, savedFiles });
        });

    }

    private static IResult BuildCorrespondenceResponse(AgentStepResult step, PendingApprovalStore approvalStore, string? extracted)
    {
        if (step.Pending is not null)
        {
            var runId = approvalStore.Add(step.Pending.ResponseId, step.Pending.ApprovalItemId, step.Pending.ServerLabel);
            return Results.Ok(new
            {
                status = "pending_approval",
                runId,
                previousResponseId = step.ResponseId,
                toolCall = new { serverLabel = step.Pending.ServerLabel },
                extracted
            });
        }

        return Results.Ok(new
        {
            status = "complete",
            response = step.Result,
            previousResponseId = step.ResponseId,
            extracted
        });
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');
}
