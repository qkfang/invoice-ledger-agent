using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace InvLedgerAgent.Services;

public class BlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobStorageService> _logger;

    public string AccountName { get; }

    public BlobStorageService(string accountName, TokenCredential credential, ILogger<BlobStorageService> logger)
    {
        AccountName = accountName;
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

    public async Task<(Stream Content, string ContentType)?> DownloadAsync(string blobPath)
    {
        var blobClient = _container.GetBlobClient(blobPath);
        if (!await blobClient.ExistsAsync()) return null;
        var resp = await blobClient.DownloadStreamingAsync();
        var contentType = resp.Value.Details.ContentType ?? "application/octet-stream";
        return (resp.Value.Content, contentType);
    }

    public async Task<List<string>> ListRunFoldersAsync(string prefix = "run-")
    {
        var folders = new HashSet<string>();
        await foreach (var item in _container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, default))
        {
            var idx = item.Name.IndexOf('/');
            if (idx > 0) folders.Add(item.Name[..idx]);
        }
        return folders.OrderByDescending(n => n, StringComparer.Ordinal).ToList();
    }

    public async Task<List<string>> ListBlobsInFolderAsync(string folder)
    {
        var prefix = folder.EndsWith('/') ? folder : folder + "/";
        var names = new List<string>();
        await foreach (var item in _container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, default))
            names.Add(item.Name[prefix.Length..]);
        return names;
    }
}
