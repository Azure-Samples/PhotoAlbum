using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;

namespace PhotoAlbum.Services;

public interface IPhotoBlobStorage
{
    Task<string> UploadAsync(
        IFormFile file,
        string mimeType,
        string safeExtension,
        int? width = null,
        int? height = null,
        string? originalFileName = null);

    Task<byte[]> DownloadAsync(string blobName);

    Task DeleteAsync(string blobName);

    string GetBlobUrl(string blobName);
}

public sealed class AzurePhotoBlobStorage : IPhotoBlobStorage
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzurePhotoBlobStorage> _logger;

    public AzurePhotoBlobStorage(
        BlobServiceClient blobServiceClient,
        IConfiguration configuration,
        ILogger<AzurePhotoBlobStorage> logger)
    {
        _blobServiceClient = blobServiceClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        IFormFile file,
        string mimeType,
        string safeExtension,
        int? width = null,
        int? height = null,
        string? originalFileName = null)
    {
        var containerName = GetContainerName();
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var blobName = $"photos/{Guid.NewGuid()}.{safeExtension.TrimStart('.') }";
        var blobClient = containerClient.GetBlobClient(blobName);

        var metadata = new Dictionary<string, string>
        {
            ["original-file-name"] = originalFileName ?? file.FileName,
            ["mime-type"] = mimeType,
            ["content-length"] = file.Length.ToString(),
            ["width"] = width?.ToString() ?? string.Empty,
            ["height"] = height?.ToString() ?? string.Empty
        };

        // Unconditional overwrite preserves the source S3 PutObject semantics.
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = mimeType },
            Metadata = metadata
        };

        await using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, uploadOptions);

        return blobName;
    }

    public async Task<byte[]> DownloadAsync(string blobName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(GetContainerName());
        var normalizedBlobName = NormalizeBlobName(blobName);
        var blobClient = containerClient.GetBlobClient(normalizedBlobName);

        var response = await blobClient.DownloadStreamingAsync();
        await using var stream = response.Value.Content;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public async Task DeleteAsync(string blobName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(GetContainerName());
        var normalizedBlobName = NormalizeBlobName(blobName);
        var blobClient = containerClient.GetBlobClient(normalizedBlobName);
        await blobClient.DeleteIfExistsAsync();
    }

    public string GetBlobUrl(string blobName)
    {
        return $"/{NormalizeBlobName(blobName)}";
    }

    private string GetContainerName()
    {
        var containerName = _configuration["Storage:ContainerName"] ?? "photos";
        return containerName.Trim('/');
    }

    private static string NormalizeBlobName(string blobName)
    {
        var trimmed = blobName.TrimStart('/');
        return trimmed.Contains('/') ? trimmed : $"photos/{trimmed}";
    }
}

public sealed class LocalPhotoBlobStorage : IPhotoBlobStorage
{
    private readonly string _storageRoot;

    public LocalPhotoBlobStorage(IConfiguration configuration)
    {
        _storageRoot = configuration["Storage:FallbackRootPath"]
            ?? Path.Combine(Path.GetTempPath(), "PhotoAlbumLocalStorage");

        Directory.CreateDirectory(_storageRoot);
    }

    public Task<string> UploadAsync(
        IFormFile file,
        string mimeType,
        string safeExtension,
        int? width = null,
        int? height = null,
        string? originalFileName = null)
    {
        var blobName = $"photos/{Guid.NewGuid()}.{safeExtension.TrimStart('.')}";
        var targetPath = Path.Combine(_storageRoot, blobName.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = file.OpenReadStream();
        using var output = File.Create(targetPath);
        stream.CopyTo(output);

        return Task.FromResult(blobName);
    }

    public Task<byte[]> DownloadAsync(string blobName)
    {
        var normalizedBlobName = NormalizeBlobName(blobName);
        var targetPath = GetSafeTargetPath(normalizedBlobName);
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("Blob not found in local fallback storage.", targetPath);
        }

        return Task.FromResult(File.ReadAllBytes(targetPath));
    }

    public Task DeleteAsync(string blobName)
    {
        var normalizedBlobName = NormalizeBlobName(blobName);
        var targetPath = GetSafeTargetPath(normalizedBlobName);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        return Task.CompletedTask;
    }

    public string GetBlobUrl(string blobName)
    {
        return $"/local-blob/{NormalizeBlobName(blobName)}";
    }

    private string GetSafeTargetPath(string normalizedBlobName)
    {
        var relativePath = normalizedBlobName.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_storageRoot, relativePath));
        var storageRoot = Path.GetFullPath(_storageRoot);
        var relativeToRoot = Path.GetRelativePath(storageRoot, fullPath);

        if (relativeToRoot.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeToRoot))
        {
            throw new InvalidOperationException("Blob path escapes the local fallback storage root.");
        }

        return fullPath;
    }

    private static string NormalizeBlobName(string blobName)
    {
        var trimmed = blobName.TrimStart('/');
        return trimmed.Contains('/') ? trimmed : $"photos/{trimmed}";
    }
}
