using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoAlbum.Data;
using PhotoAlbum.Tests.PostMigration.Infrastructure;

namespace PhotoAlbum.Tests.PostMigration;

/// <summary>
/// Post-migration tests for the <c>POST /?handler=Upload</c> entry point
/// (<c>PhotoAlbum/Pages/Index.cshtml.cs:IndexModel.OnPostUploadAsync</c>), generated 1:1 from
/// TC-WEB-004..TC-WEB-013 in the frozen <c>test-cases/test-cases.md</c>.
/// </summary>
public class UploadPostMigrationIT
{
    private const string UploadUrl = "/?handler=Upload";

    [Fact]
    public async Task TcWeb004_Upload_SingleValidJpeg_HappyPath()
    {
        // Preconditions: zero rows in Photos; blob backend accepts writes (default in-memory fake).
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var sampleBytes = TestDataPaths.ReadAllBytes("inputs/sample.jpg");

        using var content = BuildUploadMultipart(token, ("sample.jpg", "image/jpeg", sampleBytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var uploadedPhotos = root.GetProperty("uploadedPhotos");
        Assert.Equal(1, uploadedPhotos.GetArrayLength());
        var uploaded = uploadedPhotos[0];
        Assert.Equal("sample.jpg", uploaded.GetProperty("originalFileName").GetString());
        Assert.Equal(32, uploaded.GetProperty("width").GetInt32());
        Assert.Equal(24, uploaded.GetProperty("height").GetInt32());
        Assert.Equal(sampleBytes.Length, uploaded.GetProperty("fileSize").GetInt64());
        Assert.True(uploaded.TryGetProperty("id", out var idProp) && idProp.ValueKind != JsonValueKind.Null);
        Assert.Equal(0, root.GetProperty("failedUploads").GetArrayLength());
        var photoId = idProp.GetInt32();

        // Assert: Resource Verification
        await using var dbContext = factory.CreateDbContext();
        var photo = await dbContext.Photos.SingleAsync(p => p.Id == photoId);
        Assert.Equal("sample.jpg", photo.OriginalFileName);
        Assert.Equal("image/jpeg", photo.MimeType);
        Assert.Equal(32, photo.Width);
        Assert.Equal(24, photo.Height);
        Assert.Equal(sampleBytes.Length, photo.FileSize);

        var blobKey = photo.FilePath.TrimStart('/');
        Assert.True(factory.BlobStorage.Blobs.TryGetValue(blobKey, out var storedBytes));
        Assert.Equal(sampleBytes, storedBytes);
    }

    [Fact]
    public async Task TcWeb005_Upload_MultipleFilesMixedValidInvalid()
    {
        // Preconditions: zero rows in Photos.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var pngBytes = TestDataPaths.ReadAllBytes("inputs/sample.png");
        var pdfBytes = TestDataPaths.ReadAllBytes("inputs/not-an-image.pdf");

        using var content = BuildUploadMultipart(
            token,
            ("sample.png", "image/png", pngBytes),
            ("not-an-image.pdf", "application/pdf", pdfBytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var uploadedPhotos = root.GetProperty("uploadedPhotos");
        Assert.Equal(1, uploadedPhotos.GetArrayLength());
        Assert.Equal("sample.png", uploadedPhotos[0].GetProperty("originalFileName").GetString());
        var failedUploads = root.GetProperty("failedUploads");
        Assert.Equal(1, failedUploads.GetArrayLength());
        Assert.Equal("not-an-image.pdf", failedUploads[0].GetProperty("fileName").GetString());
        Assert.Equal(
            "File type not supported. Please upload JPEG, PNG, GIF, or WebP images.",
            failedUploads[0].GetProperty("error").GetString());

        // Assert: Resource Verification
        await using var dbContext = factory.CreateDbContext();
        Assert.Equal(1, await dbContext.Photos.CountAsync());
        Assert.True(await dbContext.Photos.AnyAsync(p => p.OriginalFileName == "sample.png"));
        Assert.False(await dbContext.Photos.AnyAsync(p => p.OriginalFileName == "not-an-image.pdf"));

        // Assert: Negative Verification — no object written for the invalid file.
        Assert.DoesNotContain(factory.BlobStorage.Blobs.Values, v => v.Length == pdfBytes.Length && v.SequenceEqual(pdfBytes));
    }

    [Fact]
    public async Task TcWeb006_Upload_NoFilesProvided()
    {
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
        };

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("No files provided", root.GetProperty("error").GetString());

        // Assert: Negative Verification
        await using var dbContext = factory.CreateDbContext();
        Assert.Equal(0, await dbContext.Photos.CountAsync());
        Assert.Empty(factory.BlobStorage.Blobs);
    }

    [Fact]
    public async Task TcWeb007_Upload_UnicodeFilenamePreserved()
    {
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var bytes = TestDataPaths.ReadAllBytes("inputs/unicode-name-source.jpg");
        const string unicodeFileName = "Ünïcødé 名前 📷.jpg";

        using var content = BuildUploadMultipart(token, (unicodeFileName, "image/jpeg", bytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var uploadedPhotos = root.GetProperty("uploadedPhotos");
        Assert.Equal(1, uploadedPhotos.GetArrayLength());
        Assert.Equal(unicodeFileName, uploadedPhotos[0].GetProperty("originalFileName").GetString());
        Assert.Equal(0, root.GetProperty("failedUploads").GetArrayLength());

        // Assert: Resource Verification
        await using var dbContext = factory.CreateDbContext();
        Assert.True(await dbContext.Photos.AnyAsync(p => p.OriginalFileName == unicodeFileName));
    }

    [Fact]
    public async Task TcWeb008_Upload_OversizedFileRejected()
    {
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var oversizedBytes = TestDataPaths.ReadAllBytes("inputs/oversized.bin");
        Assert.Equal(11534336, oversizedBytes.Length);

        using var content = BuildUploadMultipart(token, ("oversized.bin", "image/jpeg", oversizedBytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response (post-re-freeze — see Post-Freeze Amendment Log in test-cases.md)
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);

        // Assert: Negative Verification
        await using var dbContext = factory.CreateDbContext();
        Assert.Equal(0, await dbContext.Photos.CountAsync());
        Assert.Empty(factory.BlobStorage.Blobs);
    }

    [Fact]
    public async Task TcWeb009_Upload_ZeroByteFileRejected()
    {
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var emptyBytes = TestDataPaths.ReadAllBytes("inputs/empty.jpg");
        Assert.Empty(emptyBytes);

        using var content = BuildUploadMultipart(token, ("empty.jpg", "image/jpeg", emptyBytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("uploadedPhotos").GetArrayLength());
        var failedUploads = root.GetProperty("failedUploads");
        Assert.Equal(1, failedUploads.GetArrayLength());
        Assert.Equal("empty.jpg", failedUploads[0].GetProperty("fileName").GetString());
        Assert.Equal("File is empty.", failedUploads[0].GetProperty("error").GetString());

        // Assert: Negative Verification
        await using var dbContext = factory.CreateDbContext();
        Assert.Equal(0, await dbContext.Photos.CountAsync());
        Assert.Empty(factory.BlobStorage.Blobs);
    }

    [Fact]
    public async Task TcWeb010_Upload_NonImageContentRejected()
    {
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var pdfBytes = TestDataPaths.ReadAllBytes("inputs/not-an-image.pdf");

        using var content = BuildUploadMultipart(token, ("not-an-image.pdf", "application/pdf", pdfBytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("uploadedPhotos").GetArrayLength());
        var failedUploads = root.GetProperty("failedUploads");
        Assert.Equal(1, failedUploads.GetArrayLength());
        Assert.Equal("not-an-image.pdf", failedUploads[0].GetProperty("fileName").GetString());
        Assert.Equal(
            "File type not supported. Please upload JPEG, PNG, GIF, or WebP images.",
            failedUploads[0].GetProperty("error").GetString());

        // Assert: Negative Verification
        await using var dbContext = factory.CreateDbContext();
        Assert.Equal(0, await dbContext.Photos.CountAsync());
        Assert.Empty(factory.BlobStorage.Blobs);
    }

    [Fact]
    public async Task TcWeb011_Upload_CorruptedImageBytesRejected()
    {
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var corruptedBytes = TestDataPaths.ReadAllBytes("inputs/corrupted.jpg");
        Assert.Equal(2048, corruptedBytes.Length);

        using var content = BuildUploadMultipart(token, ("corrupted.jpg", "image/jpeg", corruptedBytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("uploadedPhotos").GetArrayLength());
        var failedUploads = root.GetProperty("failedUploads");
        Assert.Equal(1, failedUploads.GetArrayLength());
        Assert.Equal("corrupted.jpg", failedUploads[0].GetProperty("fileName").GetString());
        Assert.Equal(
            "File type not supported. Please upload JPEG, PNG, GIF, or WebP images.",
            failedUploads[0].GetProperty("error").GetString());

        // Assert: Negative Verification
        await using var dbContext = factory.CreateDbContext();
        Assert.Equal(0, await dbContext.Photos.CountAsync());
        Assert.Empty(factory.BlobStorage.Blobs);
    }

    [Fact]
    public async Task TcWeb012_Upload_StorageBackendWriteFailure()
    {
        // Preconditions: the photo storage backend is configured to throw on the write call.
        await using var factory = new PhotoAlbumWebApplicationFactory()
            .WithBlobStorage(inner => new ThrowingPhotoBlobStorage(
                inner, uploadException: new InvalidOperationException("Simulated blob write failure.")));
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var sampleBytes = TestDataPaths.ReadAllBytes("inputs/sample.jpg");

        using var content = BuildUploadMultipart(token, ("sample.jpg", "image/jpeg", sampleBytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("uploadedPhotos").GetArrayLength());
        var failedUploads = root.GetProperty("failedUploads");
        Assert.Equal(1, failedUploads.GetArrayLength());
        Assert.Equal("sample.jpg", failedUploads[0].GetProperty("fileName").GetString());
        Assert.Equal("Error saving file. Please try again.", failedUploads[0].GetProperty("error").GetString());

        // Assert: Negative Verification
        await using var dbContext = factory.CreateDbContext();
        Assert.Equal(0, await dbContext.Photos.CountAsync());
    }

    [Fact]
    public async Task TcWeb013_Upload_DatabaseSaveFailureTriggersStorageRollback()
    {
        // Preconditions: storage write succeeds, but the subsequent SaveChangesAsync throws.
        await using var factory = new PhotoAlbumWebApplicationFactory()
            .WithDbInterceptor(new ThrowingSaveChangesInterceptor());
        using var client = factory.CreateClient();
        var token = await AntiforgeryHelper.GetTokenAsync(client);
        var sampleBytes = TestDataPaths.ReadAllBytes("inputs/sample.jpg");

        using var content = BuildUploadMultipart(token, ("sample.jpg", "image/jpeg", sampleBytes));

        // Act
        var response = await client.PostAsync(UploadUrl, content);

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("uploadedPhotos").GetArrayLength());
        var failedUploads = root.GetProperty("failedUploads");
        Assert.Equal(1, failedUploads.GetArrayLength());
        Assert.Equal("sample.jpg", failedUploads[0].GetProperty("fileName").GetString());
        Assert.Equal(
            "Error saving photo information. Please try again.",
            failedUploads[0].GetProperty("error").GetString());

        // Assert: Negative Verification — no row, and the blob written during the attempt was rolled back.
        // (The SaveChanges failure is injected at the interceptor level, so no row was ever
        // persisted into the InMemory database used for verification either.)
        Assert.DoesNotContain(factory.BlobStorage.Blobs.Values, v => v.SequenceEqual(sampleBytes));
    }

    private static MultipartFormDataContent BuildUploadMultipart(
        string antiforgeryToken, params (string fileName, string contentType, byte[] bytes)[] files)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(antiforgeryToken), "__RequestVerificationToken" },
        };

        foreach (var (fileName, contentType, bytes) in files)
        {
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "files", fileName);
        }

        return content;
    }
}
