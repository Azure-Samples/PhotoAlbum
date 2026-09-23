using Microsoft.EntityFrameworkCore;
using PhotoAlbum.Data;
using PhotoAlbum.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace PhotoAlbum.Services;

/// <summary>
/// Service for photo operations including upload, retrieval, and deletion.
/// The storage backend is configuration-driven and defaults to a local fallback
/// when Azure Blob Storage is not yet configured.
/// </summary>
public class PhotoService : IPhotoService
{
    private readonly PhotoAlbumContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PhotoService> _logger;
    private readonly IPhotoBlobStorage _photoBlobStorage;
    private readonly long _maxFileSizeBytes;
    private readonly string[] _allowedMimeTypes;

    // Only these real (content-detected) raster image formats may be stored.
    private static readonly HashSet<string> _allowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "gif", "webp" };

    public PhotoService(
        PhotoAlbumContext context,
        IConfiguration configuration,
        ILogger<PhotoService> logger,
        IPhotoBlobStorage? photoBlobStorage = null,
        ILoggerFactory? loggerFactory = null)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;

        _photoBlobStorage = photoBlobStorage ?? CreateFallbackStorage(configuration, logger, loggerFactory ?? new LoggerFactory());
        _maxFileSizeBytes = _configuration.GetValue<long>("FileUpload:MaxFileSizeBytes", 10485760);
        _allowedMimeTypes = _configuration.GetSection("FileUpload:AllowedMimeTypes").Get<string[]>()
            ?? new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
    }

    private static IPhotoBlobStorage CreateFallbackStorage(
        IConfiguration configuration,
        ILogger<PhotoService> logger,
        ILoggerFactory loggerFactory)
    {
        var serviceUri = configuration["Storage:ServiceUri"] ?? string.Empty;
        var accountName = configuration["Storage:AccountName"] ?? configuration["StorageAccountName"] ?? string.Empty;
        var containerName = configuration["Storage:ContainerName"] ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(serviceUri) && !serviceUri.Contains("{", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(containerName))
        {
            var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(
                new Uri(serviceUri),
                new Azure.Identity.DefaultAzureCredential());
            return new AzurePhotoBlobStorage(blobServiceClient, configuration, loggerFactory.CreateLogger<AzurePhotoBlobStorage>());
        }

        if (!string.IsNullOrWhiteSpace(accountName) && !accountName.Contains("{", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(containerName))
        {
            var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(
                new Uri($"https://{accountName}.blob.core.windows.net"),
                new Azure.Identity.DefaultAzureCredential());
            return new AzurePhotoBlobStorage(blobServiceClient, configuration, loggerFactory.CreateLogger<AzurePhotoBlobStorage>());
        }

        logger.LogInformation("Azure Blob Storage configuration not detected; using local fallback storage for this environment.");
        return new LocalPhotoBlobStorage(configuration);
    }

    /// <summary>
    /// Get all photos ordered by upload date (newest first)
    /// </summary>
    public async Task<List<Photo>> GetAllPhotosAsync()
    {
        try
        {
            return await _context.Photos
                .OrderByDescending(p => p.UploadedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving photos from database");
            throw;
        }
    }

    /// <summary>
    /// Get a specific photo by ID
    /// </summary>
    public async Task<Photo?> GetPhotoByIdAsync(int id)
    {
        try
        {
            return await _context.Photos.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving photo with ID {PhotoId}", id);
            throw;
        }
    }

    /// <summary>
    /// Upload a photo file.
    /// </summary>
    public async Task<UploadResult> UploadPhotoAsync(IFormFile file)
    {
        var result = new UploadResult
        {
            FileName = file.FileName
        };

        try
        {
            if (file.Length > _maxFileSizeBytes)
            {
                result.Success = false;
                result.ErrorMessage = $"File size exceeds {_maxFileSizeBytes / 1024 / 1024}MB limit.";
                _logger.LogWarning("Upload rejected: File size {FileSize} exceeds limit for {FileName}",
                    file.Length, file.FileName);
                return result;
            }

            if (file.Length <= 0)
            {
                result.Success = false;
                result.ErrorMessage = "File is empty.";
                return result;
            }

            int? width = null;
            int? height = null;
            IImageFormat imageFormat;
            try
            {
                await using var probeStream = file.OpenReadStream();
                var imageInfo = await Image.IdentifyAsync(probeStream);
                imageFormat = imageInfo.Metadata.DecodedImageFormat
                    ?? throw new InvalidOperationException("Unknown image format.");
                width = imageInfo.Width;
                height = imageInfo.Height;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Upload rejected: not a valid image {FileName}", file.FileName);
                result.Success = false;
                result.ErrorMessage = "File type not supported. Please upload JPEG, PNG, GIF, or WebP images.";
                return result;
            }

            var safeExtension = imageFormat.FileExtensions.FirstOrDefault();
            if (safeExtension == null || !_allowedImageExtensions.Contains(safeExtension))
            {
                result.Success = false;
                result.ErrorMessage = "File type not supported. Please upload JPEG, PNG, GIF, or WebP images.";
                _logger.LogWarning("Upload rejected: unsupported image format {Format} for {FileName}",
                    imageFormat.Name, file.FileName);
                return result;
            }

            string storedBlobName;
            string relativePath;
            try
            {
                storedBlobName = await _photoBlobStorage.UploadAsync(file, imageFormat.DefaultMimeType, safeExtension, width, height, file.FileName);
                relativePath = $"/{storedBlobName.TrimStart('/')}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving file {FileName} to blob storage", file.FileName);
                result.Success = false;
                result.ErrorMessage = "Error saving file. Please try again.";
                return result;
            }

            var photo = new Photo
            {
                OriginalFileName = Path.GetFileName(file.FileName),
                StoredFileName = Path.GetFileName(storedBlobName),
                FilePath = relativePath,
                FileSize = file.Length,
                MimeType = imageFormat.DefaultMimeType,
                UploadedAt = DateTime.UtcNow,
                Width = width,
                Height = height
            };

            try
            {
                await _context.Photos.AddAsync(photo);
                await _context.SaveChangesAsync();

                result.Success = true;
                result.PhotoId = photo.Id;

                _logger.LogInformation("Successfully uploaded photo {FileName} with ID {PhotoId}",
                    file.FileName, photo.Id);
            }
            catch (Exception ex)
            {
                try
                {
                    await _photoBlobStorage.DeleteAsync(storedBlobName);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogError(deleteEx, "Error deleting blob {BlobName} during rollback", storedBlobName);
                }

                _logger.LogError(ex, "Error saving photo metadata to database for {FileName}", file.FileName);
                result.Success = false;
                result.ErrorMessage = "Error saving photo information. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during photo upload for {FileName}", file.FileName);
            result.Success = false;
            result.ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        return result;
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

    /// <summary>
    /// Delete a photo by ID.
    /// </summary>
    public async Task<bool> DeletePhotoAsync(int id)
    {
        try
        {
            var photo = await _context.Photos.FindAsync(id);
            if (photo == null)
            {
                _logger.LogWarning("Photo with ID {PhotoId} not found for deletion", id);
                return false;
            }

            var blobName = string.IsNullOrWhiteSpace(photo.FilePath)
                ? $"photos/{photo.StoredFileName}"
                : NormalizeBlobPath(photo.FilePath, photo.StoredFileName);

            try
            {
                await _photoBlobStorage.DeleteAsync(blobName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blob for photo ID {PhotoId}", id);
            }

            _context.Photos.Remove(photo);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully deleted photo ID {PhotoId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting photo with ID {PhotoId}", id);
            throw;
        }
    }
}
