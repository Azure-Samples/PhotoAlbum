using Microsoft.AspNetCore.Http;
using PhotoAlbum.Services;

namespace PhotoAlbum.Tests.TestDoubles;

/// <summary>
/// In-memory, purely managed test double for <see cref="IPhotoBlobStorage"/>.
///
/// Production photo storage goes exclusively through Azure Blob Storage
/// (<see cref="AzurePhotoBlobStorage"/>) — there is no local-disk fallback
/// implementation anymore. Unit tests that need an <see cref="IPhotoBlobStorage"/>
/// without talking to a real Azure Storage account should inject this fake
/// explicitly via <see cref="PhotoService"/>'s constructor rather than relying
/// on config-driven fallback behavior.
/// </summary>
public sealed class InMemoryPhotoBlobStorage : IPhotoBlobStorage
{
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, byte[]> Blobs => _blobs;

    public async Task<string> UploadAsync(
        IFormFile file,
        string mimeType,
        string safeExtension,
        int? width = null,
        int? height = null,
        string? originalFileName = null)
    {
        var blobName = $"photos/{Guid.NewGuid()}.{safeExtension.TrimStart('.')}";

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        _blobs[blobName] = buffer.ToArray();
        return blobName;
    }

    public Task<byte[]> DownloadAsync(string blobName)
    {
        var normalized = NormalizeBlobName(blobName);
        if (!_blobs.TryGetValue(normalized, out var content))
        {
            throw new FileNotFoundException("Blob not found in in-memory test storage.", normalized);
        }

        return Task.FromResult(content);
    }

    public Task DeleteAsync(string blobName)
    {
        _blobs.Remove(NormalizeBlobName(blobName));
        return Task.CompletedTask;
    }

    public string GetBlobUrl(string blobName)
    {
        return $"/{NormalizeBlobName(blobName)}";
    }

    private static string NormalizeBlobName(string blobName)
    {
        var trimmed = blobName.TrimStart('/');
        return trimmed.Contains('/') ? trimmed : $"photos/{trimmed}";
    }
}
