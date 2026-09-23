using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PhotoAlbum.Models;
using PhotoAlbum.Tests.PostMigration.Infrastructure;

namespace PhotoAlbum.Tests.PostMigration;

/// <summary>
/// Post-migration tests for the <c>GET /</c> gallery-listing entry point
/// (<c>PhotoAlbum/Pages/Index.cshtml.cs:IndexModel.OnGetAsync</c>), generated 1:1 from
/// TC-WEB-001..TC-WEB-003 in the frozen <c>test-cases/test-cases.md</c>.
/// </summary>
public class IndexPostMigrationIT
{
    private static readonly Regex PhotoTitleRegex = new(
        "text-truncate\" title=\"([^\"]*)\"", RegexOptions.Compiled);

    [Fact]
    public async Task TcWeb001_GalleryListing_HappyPath()
    {
        // Preconditions: three rows seeded (oldest to newest) from testdata/seed-data/three-photos.json.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        var seedJson = TestDataPaths.ReadAllText("seed-data/three-photos.json");
        using var seedDoc = JsonDocument.Parse(seedJson);
        var t0 = DateTime.UtcNow;

        await using (var seedContext = factory.CreateDbContext())
        {
            foreach (var element in seedDoc.RootElement.GetProperty("photos").EnumerateArray())
            {
                seedContext.Photos.Add(new Photo
                {
                    OriginalFileName = element.GetProperty("originalFileName").GetString()!,
                    StoredFileName = element.GetProperty("storedFileName").GetString()!,
                    FilePath = element.GetProperty("filePath").GetString()!,
                    FileSize = element.GetProperty("fileSize").GetInt64(),
                    MimeType = element.GetProperty("mimeType").GetString()!,
                    UploadedAt = t0 + XmlConvertDuration(element.GetProperty("uploadedAtOffset").GetString()!),
                    Width = element.GetProperty("width").GetInt32(),
                    Height = element.GetProperty("height").GetInt32(),
                });
            }
            await seedContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var titles = PhotoTitleRegex.Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "third.jpg", "second.jpg", "first.jpg" }, titles);

        // Assert: Resource Verification — no rows added/removed/modified.
        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal(3, await verifyContext.Photos.CountAsync());
    }

    [Fact]
    public async Task TcWeb002_GalleryListing_EmptyGallery()
    {
        // Preconditions: zero rows in the Photos table.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No photos yet", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"photo-gallery\"", html, StringComparison.Ordinal);
        Assert.Empty(PhotoTitleRegex.Matches(html));

        // Assert: Resource Verification — no rows added.
        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal(0, await verifyContext.Photos.CountAsync());
    }

    [Fact]
    public async Task TcWeb003_GalleryListing_DataStoreFailureDegradesToEmptyGallery()
    {
        // Preconditions: the photo metadata store throws on the query used by GetAllPhotosAsync.
        await using var factory = new PhotoAlbumWebApplicationFactory()
            .WithDbInterceptor(new ThrowingQueryExpressionInterceptor());
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert: Expected Response — same empty-state content as TC-WEB-002.
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No photos yet", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"photo-gallery\"", html, StringComparison.Ordinal);

        // Assert: Negative Verification — response is NOT a 5xx status code.
        Assert.True((int)response.StatusCode < 500, $"Expected a non-5xx status code, got {(int)response.StatusCode}.");
    }

    /// <summary>Minimal ISO-8601 duration parser sufficient for the seed fixture's offsets (e.g. "-PT2H", "PT0S").</summary>
    private static TimeSpan XmlConvertDuration(string isoDuration) => System.Xml.XmlConvert.ToTimeSpan(isoDuration);
}
