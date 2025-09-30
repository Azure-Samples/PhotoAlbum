using Microsoft.EntityFrameworkCore;
using PhotoAlbum.Data;
using PhotoAlbum.Models;
using SixLabors.ImageSharp;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Identity;

namespace PhotoAlbum.Services;

/// <summary>
/// Service for photo operations including upload, retrieval, and deletion
/// </summary>
public class PhotoService : IPhotoService
{
    private readonly PhotoAlbumContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PhotoService> _logger;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly long _maxFileSizeBytes;
    private readonly string[] _allowedMimeTypes;

    public PhotoService(
        PhotoAlbumContext context,
        IConfiguration configuration,
        ILogger<PhotoService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;

        // Azure Blob Storage configuration
        var endpoint = _configuration.GetValue<string>("AzureStorageBlob:Endpoint");
        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException("AzureStorageBlob:Endpoint configuration is required");
        }

        _blobServiceClient = new BlobServiceClient(new Uri(endpoint), new DefaultAzureCredential());
        _containerName = _configuration.GetValue<string>("AzureStorageBlob:ContainerName") ?? "photos";
        
        _maxFileSizeBytes = _configuration.GetValue<long>("FileUpload:MaxFileSizeBytes", 10485760);
        _allowedMimeTypes = _configuration.GetSection("FileUpload:AllowedMimeTypes").Get<string[]>()
            ?? new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
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
    /// Upload a photo file
    /// </summary>
    public async Task<UploadResult> UploadPhotoAsync(IFormFile file)
    {
        var result = new UploadResult
        {
            FileName = file.FileName
        };

        try
        {
            // Validate file type
            if (!_allowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            {
                result.Success = false;
                result.ErrorMessage = $"File type not supported. Please upload JPEG, PNG, GIF, or WebP images.";
                _logger.LogWarning("Upload rejected: Invalid file type {ContentType} for {FileName}",
                    file.ContentType, file.FileName);
                return result;
            }

            // Validate file size
            if (file.Length > _maxFileSizeBytes)
            {
                result.Success = false;
                result.ErrorMessage = $"File size exceeds {_maxFileSizeBytes / 1024 / 1024}MB limit.";
                _logger.LogWarning("Upload rejected: File size {FileSize} exceeds limit for {FileName}",
                    file.Length, file.FileName);
                return result;
            }

            // Validate file length
            if (file.Length <= 0)
            {
                result.Success = false;
                result.ErrorMessage = "File is empty.";
                return result;
            }

            // Generate unique filename
            var extension = Path.GetExtension(file.FileName);
            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var blobPath = $"uploads/{storedFileName}";

            // Extract image dimensions using ImageSharp
            int? width = null;
            int? height = null;
            try
            {
                using var image = await Image.LoadAsync(file.OpenReadStream());
                width = image.Width;
                height = image.Height;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not extract image dimensions for {FileName}", file.FileName);
                // Continue without dimensions - not critical
            }

            // Get blob container client
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            
            // Ensure container exists
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            // Upload file to Azure Blob Storage
            var blobClient = containerClient.GetBlobClient(blobPath);
            
            try
            {
                using var stream = file.OpenReadStream();
                
                var uploadOptions = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = file.ContentType
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["OriginalFileName"] = file.FileName,
                        ["UploadedAt"] = DateTime.UtcNow.ToString("O")
                    }
                };

                await blobClient.UploadAsync(stream, uploadOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file {FileName} to Azure Blob Storage", file.FileName);
                result.Success = false;
                result.ErrorMessage = "Error saving file. Please try again.";
                return result;
            }

            // Create photo entity
            var photo = new Photo
            {
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                FilePath = blobPath, // Store blob path instead of local path
                FileSize = file.Length,
                MimeType = file.ContentType,
                UploadedAt = DateTime.UtcNow,
                Width = width,
                Height = height
            };

            // Save to database
            try
            {
                await _context.Photos.AddAsync(photo);
                await _context.SaveChangesAsync();

                result.Success = true;
                result.PhotoId = photo.Id;

                _logger.LogInformation("Successfully uploaded photo {FileName} with ID {PhotoId} to blob {BlobPath}",
                    file.FileName, photo.Id, blobPath);
            }
            catch (Exception ex)
            {
                // Rollback: Delete blob if database save fails
                try
                {
                    await blobClient.DeleteIfExistsAsync();
                }
                catch (Exception deleteEx)
                {
                    _logger.LogError(deleteEx, "Error deleting blob {BlobPath} during rollback", blobPath);
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

    /// <summary>
    /// Delete a photo by ID
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

            // Delete blob from Azure Blob Storage
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(photo.FilePath);
            
            try
            {
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blob {BlobPath} for photo ID {PhotoId}", photo.FilePath, id);
                // Continue with database deletion even if blob deletion fails
            }

            // Delete from database
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
