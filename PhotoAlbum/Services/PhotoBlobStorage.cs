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
