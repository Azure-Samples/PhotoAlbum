using Microsoft.AspNetCore.Http;
using PhotoAlbum.Services;
using PhotoAlbum.Tests.TestDoubles;

namespace PhotoAlbum.Tests.PostMigration.Infrastructure;

/// <summary>
/// Decorator over <see cref="InMemoryPhotoBlobStorage"/> that injects a configured failure
/// on <see cref="UploadAsync"/> and/or <see cref="DownloadAsync"/>, used to simulate the
/// "photo storage backend fails" mock-boundary fault scenarios (TC-WEB-012, TC-WEB-027)
/// without touching a real Azure Storage account.
/// </summary>
public sealed class ThrowingPhotoBlobStorage : IPhotoBlobStorage
{
    private readonly InMemoryPhotoBlobStorage _inner;
    private readonly Exception? _uploadException;
    private readonly Exception? _downloadException;

    public ThrowingPhotoBlobStorage(
        InMemoryPhotoBlobStorage inner,
        Exception? uploadException = null,
        Exception? downloadException = null)
    {
        _inner = inner;
        _uploadException = uploadException;
        _downloadException = downloadException;
    }

    public Task<string> UploadAsync(
        IFormFile file,
        string mimeType,
        string safeExtension,
        int? width = null,
        int? height = null,
        string? originalFileName = null)
    {
        if (_uploadException != null)
        {
            throw _uploadException;
        }

        return _inner.UploadAsync(file, mimeType, safeExtension, width, height, originalFileName);
    }

    public Task<byte[]> DownloadAsync(string blobName)
    {
        if (_downloadException != null)
        {
            throw _downloadException;
        }

        return _inner.DownloadAsync(blobName);
    }

    public Task DeleteAsync(string blobName) => _inner.DeleteAsync(blobName);

    public string GetBlobUrl(string blobName) => _inner.GetBlobUrl(blobName);
}
