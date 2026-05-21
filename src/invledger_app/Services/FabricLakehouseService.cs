using Azure.Core;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace InvLedgerAgent.Services;

public class FabricLakehouseService
{
    private readonly TokenCredential _credential;
    private readonly string _workspaceId;
    private readonly string _lakehouseId;
    private readonly HttpClient _http;
    private readonly ILogger<FabricLakehouseService> _logger;

    public FabricLakehouseService(string workspaceId, string lakehouseId, TokenCredential credential, ILogger<FabricLakehouseService> logger)
    {
        _workspaceId = workspaceId;
        _lakehouseId = lakehouseId;
        _credential = credential;
        _http = new HttpClient();
        _logger = logger;
    }

    public async Task<string> UploadAsync(Stream content, string fileName)
    {
        var tokenContext = new TokenRequestContext(["https://storage.azure.com/.default"]);
        var token = await _credential.GetTokenAsync(tokenContext, CancellationToken.None);

        var escapedFile = Uri.EscapeDataString(fileName);
        var baseUrl = $"https://onelake.dfs.fabric.microsoft.com/{Uri.EscapeDataString(_workspaceId)}/{Uri.EscapeDataString(_lakehouseId)}/Files/invoices/{escapedFile}";

        using var createReq = new HttpRequestMessage(HttpMethod.Put, $"{baseUrl}?resource=file");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        var createRes = await _http.SendAsync(createReq);
        createRes.EnsureSuccessStatusCode();

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        var data = ms.ToArray();

        using var appendReq = new HttpRequestMessage(HttpMethod.Patch, $"{baseUrl}?action=append&position=0");
        appendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        appendReq.Content = new ByteArrayContent(data);
        var appendRes = await _http.SendAsync(appendReq);
        appendRes.EnsureSuccessStatusCode();

        using var flushReq = new HttpRequestMessage(HttpMethod.Patch, $"{baseUrl}?action=flush&position={data.Length}");
        flushReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        var flushRes = await _http.SendAsync(flushReq);
        flushRes.EnsureSuccessStatusCode();

        _logger.LogInformation("Uploaded to Fabric Lakehouse: {FileName}", fileName);
        return $"onelake://{_workspaceId}/{_lakehouseId}/Files/invoices/{fileName}";
    }
}
