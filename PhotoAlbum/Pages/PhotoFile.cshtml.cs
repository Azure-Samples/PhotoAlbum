using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PhotoAlbum.Services;

namespace PhotoAlbum.Pages;

/// <summary>
/// Page model for serving photo files from Azure Blob Storage or the local fallback.
/// </summary>
public class PhotoFileModel : PageModel
{
    private readonly IPhotoService _photoService;
    private readonly IPhotoBlobStorage _photoBlobStorage;
    private readonly ILogger<PhotoFileModel> _logger;

    public PhotoFileModel(
        IPhotoService photoService,
        IPhotoBlobStorage photoBlobStorage,
        ILogger<PhotoFileModel> logger)
    {
        _photoService = photoService;
        _photoBlobStorage = photoBlobStorage;
        _logger = logger;
    }

    /// <summary>
    /// Serves a photo file by ID.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            _logger.LogWarning("Photo file request with null ID");
            return NotFound();
        }

        try
        {
            var photo = await _photoService.GetPhotoByIdAsync(id.Value);

            if (photo == null)
            {
                _logger.LogWarning("Photo with ID {PhotoId} not found", id);
                return NotFound();
            }

            var blobName = string.IsNullOrWhiteSpace(photo.FilePath)
                ? $"photos/{photo.StoredFileName}"
                : NormalizeBlobPath(photo.FilePath, photo.StoredFileName);

            byte[] fileBytes;
            try
            {
                fileBytes = await _photoBlobStorage.DownloadAsync(blobName);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("Blob not found for photo ID {PhotoId} with blob name {BlobName}", id, blobName);
                return NotFound();
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning(ex, "Blob not found for photo ID {PhotoId} with blob name {BlobName}", id, blobName);
                return NotFound();
            }

            _logger.LogDebug("Serving photo ID {PhotoId} ({FileName}, {FileSize} bytes)",
                id, photo.OriginalFileName, fileBytes.Length);

            Response.Headers.CacheControl = "public,max-age=31536000";
            Response.Headers.ETag = $"\"{photo.Id}-{photo.UploadedAt.Ticks}\"";

            return File(fileBytes, photo.MimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving photo with ID {PhotoId}", id);
            return StatusCode(500);
        }
    }

    private static string NormalizeBlobPath(string filePath, string storedFileName)
    {
        var candidate = filePath.TrimStart('/');

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.IsNullOrWhiteSpace(storedFileName)
                ? "photos"
                : $"photos/{storedFileName}";
        }

        if (candidate.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return $"photos/{Path.GetFileName(candidate)}";
        }

        if (!candidate.Contains('/'))
        {
            return $"photos/{candidate}";
        }

        return candidate;
    }
}
