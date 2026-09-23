using System.ComponentModel.DataAnnotations;

namespace PhotoAlbum.Models;

/// <summary>
/// Represents an uploaded photo with metadata for display and management
/// </summary>
public class Photo
{
    /// <summary>
    /// Unique identifier for the photo
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Original filename as uploaded by user
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// GUID-based filename with extension stored on disk
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string StoredFileName { get; set; } = string.Empty;

    /// <summary>
    /// Relative blob path (e.g., /photos/abc123.jpg)
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public long FileSize { get; set; }

    /// <summary>
    /// MIME type (e.g., image/jpeg, image/png)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of upload
    /// </summary>
    [Required]
    public DateTime UploadedAt { get; set; }

    /// <summary>
    /// Image width in pixels (populated after upload)
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Image height in pixels (populated after upload)
    /// </summary>
    public int? Height { get; set; }
}
