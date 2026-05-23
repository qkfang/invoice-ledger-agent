using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace InvLedgerAgent.Services;

public class BlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(string accountName, TokenCredential credential, ILogger<BlobStorageService> logger)
    {
        var serviceUri = new Uri($"https://{accountName}.blob.core.windows.net");
        var serviceClient = new BlobServiceClient(serviceUri, credential);
        _container = serviceClient.GetBlobContainerClient("notices");
        _logger = logger;
    }

    public async Task<Uri> UploadAsync(Stream content, string fileName)
    {
        await _container.CreateIfNotExistsAsync();
        var blobClient = _container.GetBlobClient(fileName);
        await blobClient.UploadAsync(content, overwrite: true);
        _logger.LogInformation("Uploaded blob: {BlobName}", fileName);
        return blobClient.Uri;
    }
}
