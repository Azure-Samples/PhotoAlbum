using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PhotoAlbum.Services;

namespace PhotoAlbum.Pages;

/// <summary>
/// Page model for serving photo files with indirect access
/// </summary>
public class PhotoFileModel : PageModel
{
    private readonly IPhotoService _photoService;
    private readonly ILogger<PhotoFileModel> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _uploadPath;

    /// <summary>
    /// Initializes a new instance of the PhotoFileModel class
    /// </summary>
    /// <param name="photoService">Service for photo operations</param>
    /// <param name="configuration">Configuration instance</param>
    /// <param name="logger">Logger instance</param>
    public PhotoFileModel(IPhotoService photoService, IConfiguration configuration, ILogger<PhotoFileModel> logger)
    {
        _photoService = photoService;
        _configuration = configuration;
        _logger = logger;

        _uploadPath = _configuration["FileUpload:UploadPath"]
            ?? throw new InvalidOperationException("FileUpload:UploadPath is not configured.");
    }

    /// <summary>
    /// Serves a photo file by ID
    /// </summary>
    /// <param name="id">The ID of the photo to serve</param>
    /// <returns>File result with the photo, or NotFound if photo doesn't exist</returns>
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

            // Construct the physical file path
            // photo.FilePath is stored as "/uploads/filename.jpg"
            // We need to read from "wwwroot/uploads/filename.jpg"
            var fileName = Path.GetFileName(photo.FilePath);
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), _uploadPath, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogError("Physical file not found for photo ID {PhotoId} at path {FilePath}", id, filePath);
                return NotFound();
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);

            _logger.LogDebug("Serving photo ID {PhotoId} ({FileName}, {FileSize} bytes)",
                id, photo.OriginalFileName, fileBytes.Length);

            // Return the file with appropriate content type and enable caching
            Response.Headers.CacheControl = "public,max-age=31536000"; // Cache for 1 year
            Response.Headers.ETag = $"\"{photo.Id}-{photo.UploadedAt.Ticks}\"";

            return File(fileBytes, photo.MimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving photo with ID {PhotoId}", id);
            return StatusCode(500);
        }
    }
}
