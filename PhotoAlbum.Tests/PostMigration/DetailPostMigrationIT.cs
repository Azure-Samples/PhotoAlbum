using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoAlbum.Models;
using PhotoAlbum.Tests.PostMigration.Infrastructure;

namespace PhotoAlbum.Tests.PostMigration;

/// <summary>
/// Post-migration tests for the <c>GET /Detail/{id?}</c> entry point
/// (<c>PhotoAlbum/Pages/Detail.cshtml.cs:DetailModel.OnGetAsync</c>), generated 1:1 from
/// TC-WEB-014..TC-WEB-018 in the frozen <c>test-cases/test-cases.md</c>.
/// </summary>
public class DetailPostMigrationIT
{
    private static async Task<Dictionary<string, int>> SeedThreePhotosAsync(PhotoAlbumWebApplicationFactory factory)
    {
        var seedJson = TestDataPaths.ReadAllText("seed-data/three-photos.json");
        using var seedDoc = JsonDocument.Parse(seedJson);
        var t0 = DateTime.UtcNow;
        var idsByFileName = new Dictionary<string, int>();

        await using var seedContext = factory.CreateDbContext();
        foreach (var element in seedDoc.RootElement.GetProperty("photos").EnumerateArray())
        {
            var photo = new Photo
            {
                OriginalFileName = element.GetProperty("originalFileName").GetString()!,
                StoredFileName = element.GetProperty("storedFileName").GetString()!,
                FilePath = element.GetProperty("filePath").GetString()!,
                FileSize = element.GetProperty("fileSize").GetInt64(),
                MimeType = element.GetProperty("mimeType").GetString()!,
                UploadedAt = t0 + System.Xml.XmlConvert.ToTimeSpan(element.GetProperty("uploadedAtOffset").GetString()!),
                Width = element.GetProperty("width").GetInt32(),
                Height = element.GetProperty("height").GetInt32(),
            };
            seedContext.Photos.Add(photo);
            await seedContext.SaveChangesAsync();
            idsByFileName[photo.OriginalFileName] = photo.Id;
        }

        return idsByFileName;
    }

    [Fact]
    public async Task TcWeb014_Detail_HappyPath_BothNeighbors()
    {
        // Preconditions: same 3-row seed as TC-WEB-001.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        var ids = await SeedThreePhotosAsync(factory);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/Detail/{ids["second.jpg"]}");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("second.jpg", html, StringComparison.Ordinal);
        Assert.Contains($"/Detail/{ids["first.jpg"]}", html, StringComparison.Ordinal);
        Assert.Contains("Previous Photo", html, StringComparison.Ordinal);
        Assert.Contains($"/Detail/{ids["third.jpg"]}", html, StringComparison.Ordinal);
        Assert.Contains("Next Photo", html, StringComparison.Ordinal);

        // Assert: Resource Verification — no rows added/removed/modified.
        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal(3, await verifyContext.Photos.CountAsync());
    }

    [Fact]
    public async Task TcWeb015_Detail_NewestPhoto_NoNextNeighbor()
    {
        // Preconditions: same 3-row seed as TC-WEB-001.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        var ids = await SeedThreePhotosAsync(factory);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/Detail/{ids["third.jpg"]}");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("third.jpg", html, StringComparison.Ordinal);
        Assert.Contains($"/Detail/{ids["second.jpg"]}", html, StringComparison.Ordinal);
        Assert.Contains("Previous Photo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Next Photo", html, StringComparison.Ordinal);

        // Assert: Resource Verification — no rows added/removed/modified.
        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal(3, await verifyContext.Photos.CountAsync());
    }

    [Fact]
    public async Task TcWeb016_Detail_OldestPhoto_NoPreviousNeighbor()
    {
        // Preconditions: same 3-row seed as TC-WEB-001.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        var ids = await SeedThreePhotosAsync(factory);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/Detail/{ids["first.jpg"]}");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("first.jpg", html, StringComparison.Ordinal);
        Assert.Contains($"/Detail/{ids["second.jpg"]}", html, StringComparison.Ordinal);
        Assert.Contains("Next Photo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Previous Photo", html, StringComparison.Ordinal);

        // Assert: Resource Verification — no rows added/removed/modified.
        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal(3, await verifyContext.Photos.CountAsync());
    }

    [Fact]
    public async Task TcWeb017_Detail_MissingRouteId_NotFound()
    {
        // Preconditions: none.
        var queryCounter = new QueryCountingInterceptor();
        await using var factory = new PhotoAlbumWebApplicationFactory()
            .WithDbInterceptor(queryCounter);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/Detail");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        // Assert: Negative Verification — no query is issued against the Photos table.
        Assert.Equal(0, queryCounter.QueryCount);
    }

    [Fact]
    public async Task TcWeb018_Detail_IdDoesNotExist_NotFound()
    {
        // Preconditions: the Photos table contains zero rows.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/Detail/999999");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        // Assert: Negative Verification — no row is added/removed/modified.
        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal(0, await verifyContext.Photos.CountAsync());
    }
}
