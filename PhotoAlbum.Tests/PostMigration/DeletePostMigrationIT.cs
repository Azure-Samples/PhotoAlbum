using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PhotoAlbum.Models;
using PhotoAlbum.Tests.PostMigration.Infrastructure;
using PhotoAlbum.Tests.TestDoubles;

namespace PhotoAlbum.Tests.PostMigration;

/// <summary>
/// Post-migration tests for the <c>POST /Detail/{id}?handler=Delete</c> entry point
/// (<c>PhotoAlbum/Pages/Detail.cshtml.cs:DetailModel.OnPostDeleteAsync</c>), generated 1:1 from
/// TC-WEB-019..TC-WEB-022 in the frozen <c>test-cases/test-cases.md</c>.
/// </summary>
public class DeletePostMigrationIT
{
    private const string AdminUsername = "baseline-admin";
    private const string AdminPassword = "Baseline-Test-Pwd!23";

    private static HttpClient CreateNoRedirectClient(PhotoAlbumWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task LoginAsAdminAsync(HttpClient client)
    {
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = AdminUsername,
            ["Password"] = AdminPassword,
            ["ReturnUrl"] = "",
        };
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(form));
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
    }

    private static async Task<int> SeedSinglePhotoAsync(PhotoAlbumWebApplicationFactory factory, string blobName, byte[] bytes)
    {
        await using var context = factory.CreateDbContext();
        var photo = new Photo
        {
            OriginalFileName = "sample.jpg",
            StoredFileName = System.IO.Path.GetFileName(blobName),
            FilePath = blobName,
            FileSize = bytes.Length,
            MimeType = "image/jpeg",
            UploadedAt = DateTime.UtcNow,
            Width = 32,
            Height = 24,
        };
        context.Photos.Add(photo);
        await context.SaveChangesAsync();
        return photo.Id;
    }

    [Fact]
    public async Task TcWeb019_Delete_AuthenticatedAdmin_HappyPath()
    {
        // Preconditions: a row exists with a corresponding blob object; caller authenticated as admin.
        await using var factory = new PhotoAlbumWebApplicationFactory().WithAdminCredentials(AdminUsername, AdminPassword);
        var sampleBytes = TestDataPaths.ReadAllBytes("inputs/sample.jpg");
        const string blobName = "photos/delete-happy-path.jpg";
        var photoId = await SeedSinglePhotoAsync(factory, blobName, sampleBytes);
        SeedBlob(factory, blobName, sampleBytes);

        using var client = CreateNoRedirectClient(factory);
        await LoginAsAdminAsync(client);
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };

        // Act
        var response = await client.PostAsync($"/Detail/{photoId}?handler=Delete", new FormUrlEncodedContent(form));

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());

        // Assert: Resource Verification — row no longer exists.
        await using var verifyContext = factory.CreateDbContext();
        Assert.Null(await verifyContext.Photos.FindAsync(photoId));

        // Assert: Negative Verification — blob removed; other rows unaffected (none seeded here).
        Assert.False(factory.BlobStorage.Blobs.ContainsKey(blobName));
        Assert.Equal(0, await verifyContext.Photos.CountAsync());
    }

    private static void SeedBlob(PhotoAlbumWebApplicationFactory factory, string blobName, byte[] bytes)
    {
        // Directly seed the in-memory blob fake (no application entry point exists to seed a
        // blob at a fixed known name) — legitimate setup per the skill's seeding rules.
        var field = typeof(InMemoryPhotoBlobStorage).GetField("_blobs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = (Dictionary<string, byte[]>)field.GetValue(factory.BlobStorage)!;
        dict[blobName] = bytes;
    }

    [Fact]
    public async Task TcWeb020_Delete_NonexistentId_NoOpRedirect()
    {
        // Preconditions: no row with id 999999; caller authenticated as admin.
        await using var factory = new PhotoAlbumWebApplicationFactory().WithAdminCredentials(AdminUsername, AdminPassword);
        using var client = CreateNoRedirectClient(factory);
        await LoginAsAdminAsync(client);
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };

        // Act
        var response = await client.PostAsync("/Detail/999999?handler=Delete", new FormUrlEncodedContent(form));

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());

        // Assert: Negative Verification — no row added/removed/modified; no blob deleted (none existed).
        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal(0, await verifyContext.Photos.CountAsync());
        Assert.Empty(factory.BlobStorage.Blobs);
    }

    [Fact]
    public async Task TcWeb021_Delete_Unauthenticated_ChallengedToLogin()
    {
        // Preconditions: a row exists with a corresponding blob object; no auth cookie present.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        var sampleBytes = TestDataPaths.ReadAllBytes("inputs/sample.jpg");
        const string blobName = "photos/delete-unauth.jpg";
        var photoId = await SeedSinglePhotoAsync(factory, blobName, sampleBytes);
        SeedBlob(factory, blobName, sampleBytes);

        using var client = CreateNoRedirectClient(factory);
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };

        // Act — no login performed; client has no auth cookie.
        var response = await client.PostAsync($"/Detail/{photoId}?handler=Delete", new FormUrlEncodedContent(form));

        // Assert: Expected Response — 302 to /Login?ReturnUrl=/Detail/{id}[?handler=Delete]
        // (post-re-freeze — see Post-Freeze Amendment Log in test-cases.md: the framework's
        // default challenge handler includes the original query string in ReturnUrl).
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("/Login", location.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.StartsWith($"/Detail/{photoId}", query["ReturnUrl"]);

        // Assert: Resource Verification — row and blob unchanged.
        await using var verifyContext = factory.CreateDbContext();
        var row = await verifyContext.Photos.FindAsync(photoId);
        Assert.NotNull(row);
        Assert.True(factory.BlobStorage.Blobs.ContainsKey(blobName));

        // Assert: Negative Verification — no row removed/modified; no blob deleted.
        Assert.Equal(1, await verifyContext.Photos.CountAsync());
        Assert.Single(factory.BlobStorage.Blobs);
    }

    [Fact]
    public async Task TcWeb022_Delete_DeletionFailure_SurfacesErrorPreservesData()
    {
        // Preconditions: a row exists; the delete path is configured to throw; caller authenticated as admin.
        var throwingInterceptor = new ThrowingSaveChangesInterceptor();
        await using var factory = new PhotoAlbumWebApplicationFactory()
            .WithAdminCredentials(AdminUsername, AdminPassword)
            .WithDbInterceptor(throwingInterceptor);
        var sampleBytes = TestDataPaths.ReadAllBytes("inputs/sample.jpg");
        const string blobName = "photos/delete-failure.jpg";
        var photoId = await SeedSinglePhotoAsync(factory, blobName, sampleBytes);
        SeedBlob(factory, blobName, sampleBytes);

        using var client = CreateNoRedirectClient(factory);
        await LoginAsAdminAsync(client);
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };

        // Act
        var response = await client.PostAsync($"/Detail/{photoId}?handler=Delete", new FormUrlEncodedContent(form));

        // Assert: Expected Response — 302 to /Detail/{id}
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"/Detail/{photoId}", response.Headers.Location!.ToString());

        // Follow the redirect (same cookie-enabled client) to observe TempData["Error"] rendered.
        var followUp = await client.GetAsync(response.Headers.Location);
        Assert.Equal(System.Net.HttpStatusCode.OK, followUp.StatusCode);
        var html = await followUp.Content.ReadAsStringAsync();
        Assert.Contains("Failed to delete photo. Please try again.", html, StringComparison.Ordinal);

        // Assert: Resource Verification / Negative Verification — row still exists, unchanged.
        await using var verifyContext = factory.CreateDbContext();
        var row = await verifyContext.Photos.FindAsync(photoId);
        Assert.NotNull(row);
        Assert.Equal("sample.jpg", row!.OriginalFileName);
        Assert.Equal(1, await verifyContext.Photos.CountAsync());
    }
}
