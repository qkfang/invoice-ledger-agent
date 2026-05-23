using Microsoft.Extensions.Logging;

namespace InvLedgerAgent.Services;

public class LocalRunStorageService
{
    private readonly string _rootPath;
    private readonly ILogger<LocalRunStorageService> _logger;

    public string RootPath => _rootPath;

    public LocalRunStorageService(ILogger<LocalRunStorageService> logger, IWebHostEnvironment env)
    {
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _rootPath = Path.Combine(webRoot, "temp");
        Directory.CreateDirectory(_rootPath);
        _logger = logger;
        _logger.LogInformation("Local run storage root: {Root}", _rootPath);
    }

    public string GetRunDir(string runName) => Path.Combine(_rootPath, runName);

    public string EnsureRunDir(string runName)
    {
        var dir = GetRunDir(runName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public List<string> ListRuns(string prefix = "run-")
    {
        if (!Directory.Exists(_rootPath)) return new List<string>();
        return Directory.GetDirectories(_rootPath)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n) && n!.StartsWith(prefix, StringComparison.Ordinal))
            .Select(n => n!)
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public List<string> ListFiles(string runName)
    {
        var dir = GetRunDir(runName);
        if (!Directory.Exists(dir)) return new List<string>();
        return Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToList();
    }

    public bool FileExists(string runName, string fileName) =>
        File.Exists(Path.Combine(GetRunDir(runName), fileName));

    public Stream? OpenRead(string runName, string fileName)
    {
        var full = Path.Combine(GetRunDir(runName), fileName);
        if (!File.Exists(full)) return null;
        return File.OpenRead(full);
    }

    public async Task WriteAllBytesAsync(string runName, string fileName, byte[] bytes)
    {
        var dir = EnsureRunDir(runName);
        await File.WriteAllBytesAsync(Path.Combine(dir, fileName), bytes);
    }

    public async Task WriteAllTextAsync(string runName, string fileName, string content)
    {
        var dir = EnsureRunDir(runName);
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), content);
    }

    public async Task CopyFromAsync(string runName, string fileName, string sourcePath)
    {
        var dir = EnsureRunDir(runName);
        var destPath = Path.Combine(dir, fileName);
        await using var src = File.OpenRead(sourcePath);
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst);
    }

    public static string ContentTypeFor(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".txt" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}
