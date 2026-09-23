using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhotoAlbum.Data;
using PhotoAlbum.Models;
using PhotoAlbum.Services;
using PhotoAlbum.Tests.TestDoubles;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace PhotoAlbum.Tests.Unit.Services;

public class PhotoServiceTests : IDisposable
{
    private readonly PhotoAlbumContext _context;
    private readonly IPhotoService _photoService;
    private readonly InMemoryPhotoBlobStorage _photoBlobStorage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PhotoService> _logger;

    public PhotoServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<PhotoAlbumContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new PhotoAlbumContext(options);

        // Setup configuration
        var inMemorySettings = new Dictionary<string, string>
        {
            {"FileUpload:MaxFileSizeBytes", "10485760"},
            {"FileUpload:AllowedMimeTypes:0", "image/jpeg"},
            {"FileUpload:AllowedMimeTypes:1", "image/png"},
            {"FileUpload:AllowedMimeTypes:2", "image/gif"},
            {"FileUpload:AllowedMimeTypes:3", "image/webp"}
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();

        // Setup logger
        _logger = new LoggerFactory().CreateLogger<PhotoService>();

        // Photo storage is exclusively Azure Blob Storage in production
        // (AzurePhotoBlobStorage); there is no local-disk fallback anymore.
        // Unit tests inject an in-memory, managed test double explicitly so
        // they don't require a real Azure Storage account.
        _photoBlobStorage = new InMemoryPhotoBlobStorage();

        // Create PhotoService instance
        _photoService = new PhotoService(_context, _configuration, _logger, _photoBlobStorage);
    }

    [Fact]
    public async Task UploadPhotoAsync_WithValidImage_ReturnsSuccess()
    {
        // Arrange
        var file = CreateImageFormFile("test.jpg", "image/jpeg");

        // Act
        var result = await _photoService.UploadPhotoAsync(file);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.PhotoId);
        Assert.Equal("test.jpg", result.FileName);
        Assert.Null(result.ErrorMessage);

        // Verify photo was saved to database
        var photo = await _context.Photos.FindAsync(result.PhotoId);
        Assert.NotNull(photo);
        Assert.Equal("test.jpg", photo.OriginalFileName);
        Assert.Equal("image/jpeg", photo.MimeType);
        Assert.True(photo.FileSize > 0);
    }

    [Fact]
    public async Task UploadPhotoAsync_WithInvalidMimeType_ReturnsError()
    {
        // Arrange
        var file = CreateMockFormFile("document.pdf", "application/pdf", 1024);

        // Act
        var result = await _photoService.UploadPhotoAsync(file);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.PhotoId);
        Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadPhotoAsync_WithOversizedFile_ReturnsError()
    {
        // Arrange
        var file = CreateMockFormFile("huge.jpg", "image/jpeg", 11 * 1024 * 1024); // 11MB

        // Act
        var result = await _photoService.UploadPhotoAsync(file);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.PhotoId);
        Assert.Contains("exceeds", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadPhotoAsync_CreatesFileInUploadsDirectory()
    {
        // Arrange
        var file = CreateImageFormFile("test.png", "image/png");

        // Act
        var result = await _photoService.UploadPhotoAsync(file);

        // Assert
        Assert.True(result.Success);

        // Verify blob exists in Azure Blob Storage (in-memory test double)
        var photo = await _context.Photos.FindAsync(result.PhotoId);
        Assert.NotNull(photo);
        var blobName = $"photos/{photo.StoredFileName}";
        Assert.True(_photoBlobStorage.Blobs.ContainsKey(blobName));
    }

    [Fact]
    public async Task UploadPhotoAsync_ThenDownloadAsync_RoundTripsBytesThroughBlobStorage()
    {
        // Arrange
        var file = CreateImageFormFile("roundtrip.png", "image/png");

        // Act
        var uploadResult = await _photoService.UploadPhotoAsync(file);
        Assert.True(uploadResult.Success);

        var photo = await _context.Photos.FindAsync(uploadResult.PhotoId);
        Assert.NotNull(photo);
        var blobName = $"photos/{photo.StoredFileName}";

        var downloaded = await _photoBlobStorage.DownloadAsync(blobName);

        // Assert: photo storage exclusively goes through IPhotoBlobStorage
        // (Azure Blob Storage in production) — no local disk is involved,
        // and the uploaded bytes are retrievable through the same interface.
        Assert.True(downloaded.Length > 0);
        Assert.Equal(photo.FileSize, downloaded.Length);
    }

    [Fact]
    public void Constructor_WithNoInjectedStorageAndNoAzureConfiguration_ThrowsInvalidOperationException()
    {
        // Arrange: no IPhotoBlobStorage supplied, and no Storage:ServiceUri /
        // Storage:AccountName configured — there is no local-disk fallback
        // to fall back to anymore, so construction must fail fast.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"FileUpload:MaxFileSizeBytes", "10485760"}
            })
            .Build();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new PhotoService(_context, configuration, _logger));
    }

    [Fact]
    public async Task UploadPhotoAsync_SavesMetadataToDatabase()
    {
        // Arrange
        var file = CreateImageFormFile("photo.jpg", "image/jpeg");

        // Act
        var result = await _photoService.UploadPhotoAsync(file);

        // Assert
        Assert.True(result.Success);

        var photo = await _context.Photos.FindAsync(result.PhotoId);
        Assert.NotNull(photo);
        Assert.Equal("photo.jpg", photo.OriginalFileName);
        Assert.NotEmpty(photo.StoredFileName);
        Assert.NotEmpty(photo.FilePath);
        Assert.True(photo.UploadedAt <= DateTime.UtcNow);
        Assert.True(photo.UploadedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task GetAllPhotosAsync_ReturnsPhotosOrderedByDate()
    {
        // Arrange
        var photo1 = new Photo
        {
            OriginalFileName = "first.jpg",
            StoredFileName = "guid1.jpg",
            FilePath = "/photos/guid1.jpg",
            FileSize = 1024,
            MimeType = "image/jpeg",
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        };
        var photo2 = new Photo
        {
            OriginalFileName = "second.jpg",
            StoredFileName = "guid2.jpg",
            FilePath = "/photos/guid2.jpg",
            FileSize = 2048,
            MimeType = "image/jpeg",
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        };
        var photo3 = new Photo
        {
            OriginalFileName = "third.jpg",
            StoredFileName = "guid3.jpg",
            FilePath = "/photos/guid3.jpg",
            FileSize = 3072,
            MimeType = "image/jpeg",
            UploadedAt = DateTime.UtcNow
        };

        await _context.Photos.AddRangeAsync(photo1, photo2, photo3);
        await _context.SaveChangesAsync();

        // Act
        var photos = await _photoService.GetAllPhotosAsync();

        // Assert
        Assert.Equal(3, photos.Count);
        Assert.Equal("third.jpg", photos[0].OriginalFileName); // Most recent first
        Assert.Equal("second.jpg", photos[1].OriginalFileName);
        Assert.Equal("first.jpg", photos[2].OriginalFileName);
    }

    [Fact]
    public async Task DeletePhotoAsync_RemovesFileAndDatabaseRecord()
    {
        // Arrange
        var file = CreateImageFormFile("todelete.jpg", "image/jpeg");
        var uploadResult = await _photoService.UploadPhotoAsync(file);
        var photoId = uploadResult.PhotoId!.Value;

        var photo = await _context.Photos.FindAsync(photoId);
        var blobName = $"photos/{photo!.StoredFileName}";

        // Act
        var result = await _photoService.DeletePhotoAsync(photoId);

        // Assert
        Assert.True(result);
        Assert.Null(await _context.Photos.FindAsync(photoId));
        Assert.False(_photoBlobStorage.Blobs.ContainsKey(blobName));
    }

    private IFormFile CreateMockFormFile(string fileName, string contentType, long size)
    {
        var content = new byte[size];
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, size, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    // Produces a real (decodable) image so hardened upload validation accepts it.
    private static IFormFile CreateImageFormFile(string fileName, string contentType)
    {
        using var image = new Image<Rgba32>(16, 16);
        var stream = new MemoryStream();
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".png": image.SaveAsPng(stream); break;
            case ".gif": image.SaveAsGif(stream); break;
            case ".webp": image.SaveAsWebp(stream); break;
            default: image.SaveAsJpeg(stream); break;
        }
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
