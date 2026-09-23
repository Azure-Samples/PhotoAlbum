using Microsoft.EntityFrameworkCore;
using PhotoAlbum.Models;
using PhotoAlbum.Tests.PostMigration.Infrastructure;
using PhotoAlbum.Tests.TestDoubles;

namespace PhotoAlbum.Tests.PostMigration;

/// <summary>
/// Post-migration tests for the <c>GET /photo/{id:int}</c> entry point
/// (<c>PhotoAlbum/Pages/PhotoFile.cshtml.cs:PhotoFileModel.OnGetAsync</c>), generated 1:1 from
/// TC-WEB-023..TC-WEB-027 in the frozen <c>test-cases/test-cases.md</c>.
/// </summary>
public class PhotoFilePostMigrationIT
{
    private static void SeedBlob(PhotoAlbumWebApplicationFactory factory, string blobName, byte[] bytes)
    {
        var field = typeof(InMemoryPhotoBlobStorage).GetField("_blobs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = (Dictionary<string, byte[]>)field.GetValue(factory.BlobStorage)!;
        dict[blobName] = bytes;
    }

    private static async Task<int> SeedPhotoRowAsync(
        PhotoAlbumWebApplicationFactory factory, string originalFileName, string filePath, string mimeType, long fileSize)
    {
        await using var context = factory.CreateDbContext();
        var photo = new Photo
        {
            OriginalFileName = originalFileName,
            StoredFileName = System.IO.Path.GetFileName(filePath),
            FilePath = filePath,
            FileSize = fileSize,
            MimeType = mimeType,
            UploadedAt = DateTime.UtcNow,
            Width = 32,
            Height = 24,
        };
        context.Photos.Add(photo);
        await context.SaveChangesAsync();
        return photo.Id;
    }

    [Fact]
    public async Task TcWeb023_PhotoFile_HappyPath()
    {
        // Preconditions: a row with MimeType image/jpeg and a matching blob exist.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        var sampleBytes = TestDataPaths.ReadAllBytes("inputs/sample.jpg");
        const string blobName = "photos/serve-happy-path.jpg";
        SeedBlob(factory, blobName, sampleBytes);
        var photoId = await SeedPhotoRowAsync(factory, "sample.jpg", blobName, "image/jpeg", sampleBytes.Length);

        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/photo/{photoId}");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("public, max-age=31536000", response.Headers.CacheControl!.ToString());
        Assert.True(response.Headers.ETag is not null && !string.IsNullOrEmpty(response.Headers.ETag.Tag));
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(sampleBytes, body);

        // Assert: Resource Verification — no rows added/removed/modified (read-only).
        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal(1, await verifyContext.Photos.CountAsync());
    }

    [Fact]
    public async Task TcWeb024_PhotoFile_NonNumericId_DoesNotRoute()
    {
        // Preconditions: none.
        var queryCounter = new QueryCountingInterceptor();
        await using var factory = new PhotoAlbumWebApplicationFactory()
            .WithDbInterceptor(queryCounter);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/photo/abc");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        // Assert: Negative Verification — no query issued; no blob read issued.
        Assert.Equal(0, queryCounter.QueryCount);
        Assert.Empty(factory.BlobStorage.Blobs);
    }

    [Fact]
    public async Task TcWeb025_PhotoFile_IdNotInDatabase_NotFound()
    {
        // Preconditions: no row with id 999999.
        await using var factory = new PhotoAlbumWebApplicationFactory()
            .WithBlobStorage(inner => new ThrowingPhotoBlobStorage(
                inner, downloadException: new InvalidOperationException("Should not be called (TC-WEB-025).")));
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/photo/999999");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        // Assert: Negative Verification — no read issued against storage (the wired blob
        // storage double throws if DownloadAsync is ever invoked; reaching NotFound here
        // without an unhandled 500 proves the download call never happened).
    }

    [Fact]
    public async Task TcWeb026_PhotoFile_RowExistsButBlobMissing_NotFound()
    {
        // Preconditions: a row exists whose referenced storage object does not exist.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        var photoId = await SeedPhotoRowAsync(factory, "missing-blob.jpg", "photos/does-not-exist.jpg", "image/jpeg", 1234);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/photo/{photoId}");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        // Assert: Resource Verification / Negative Verification — row unchanged, not deleted.
        await using var verifyContext = factory.CreateDbContext();
        var row = await verifyContext.Photos.FindAsync(photoId);
        Assert.NotNull(row);
        Assert.Equal("missing-blob.jpg", row!.OriginalFileName);
    }

    [Fact]
    public async Task TcWeb027_PhotoFile_UnexpectedStorageError_Returns500()
    {
        // Preconditions: a row exists; the storage backend throws a non-"not found" error.
        await using var factory = new PhotoAlbumWebApplicationFactory()
            .WithBlobStorage(inner => new ThrowingPhotoBlobStorage(
                inner, downloadException: new InvalidOperationException("Simulated unexpected storage error (TC-WEB-027).")));
        const string blobName = "photos/serve-error.jpg";
        var photoId = await SeedPhotoRowAsync(factory, "error.jpg", blobName, "image/jpeg", 1234);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/photo/{photoId}");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        // Assert: Resource Verification / Negative Verification — row unchanged, not deleted.
        await using var verifyContext = factory.CreateDbContext();
        var row = await verifyContext.Photos.FindAsync(photoId);
        Assert.NotNull(row);
        Assert.Equal("error.jpg", row!.OriginalFileName);
    }
}
