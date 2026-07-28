using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PhotoAlbum.Services;
using PhotoAlbum.Models;

namespace PhotoAlbum.Pages;

/// <summary>
/// Page model for displaying a single photo in full size
/// </summary>
public class DetailModel : PageModel
{
    private readonly IPhotoService _photoService;
    private readonly ILogger<DetailModel> _logger;

    /// <summary>
    /// Initializes a new instance of the DetailModel class
    /// </summary>
    /// <param name="photoService">Service for photo operations</param>
    /// <param name="logger">Logger instance</param>
    public DetailModel(IPhotoService photoService, ILogger<DetailModel> logger)
    {
        _photoService = photoService;
        _logger = logger;
    }

    /// <summary>
    /// Gets or sets the photo to display
    /// </summary>
    public Photo? Photo { get; set; }

    /// <summary>
    /// Gets or sets the previous photo ID for navigation
    /// </summary>
    public int? PreviousPhotoId { get; set; }

    /// <summary>
    /// Gets or sets the next photo ID for navigation
    /// </summary>
    public int? NextPhotoId { get; set; }

    /// <summary>
    /// Handles GET requests to display a photo
    /// </summary>
    /// <param name="id">The ID of the photo to display</param>
    /// <returns>The page result or NotFound if photo doesn't exist</returns>
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        try
        {
            var allPhotos = await _photoService.GetAllPhotosAsync();
            Photo = allPhotos.FirstOrDefault(p => p.Id == id);

            if (Photo == null)
            {
                return NotFound();
            }

            // Find previous and next photos for navigation
            var photoList = allPhotos.ToList();
            var currentIndex = photoList.FindIndex(p => p.Id == id);

            if (currentIndex > 0)
            {
                NextPhotoId = photoList[currentIndex - 1].Id; // Newer photo (previous in chronological order)
            }

            if (currentIndex < photoList.Count - 1)
            {
                PreviousPhotoId = photoList[currentIndex + 1].Id; // Older photo (next in chronological order)
            }

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading photo with ID {PhotoId}", id);
            return NotFound();
        }
    }

    /// <summary>
    /// Handles POST requests to delete a photo
    /// </summary>
    /// <param name="id">The ID of the photo to delete</param>
    /// <returns>Redirect to index page</returns>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // Deleting a photo is a destructive operation and requires authentication
        // (CWE-306). Anonymous callers are redirected to the login page.
        if (User.Identity is null || !User.Identity.IsAuthenticated)
        {
            return Challenge();
        }

        try
        {
            await _photoService.DeletePhotoAsync(id);
            _logger.LogInformation("Photo {PhotoId} deleted successfully", id);
            return RedirectToPage("/Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting photo {PhotoId}", id);
            TempData["Error"] = "Failed to delete photo. Please try again.";
            return RedirectToPage("/Detail", new { id });
        }
    }
}
