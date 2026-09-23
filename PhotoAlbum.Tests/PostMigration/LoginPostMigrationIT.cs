using Microsoft.AspNetCore.Mvc.Testing;
using PhotoAlbum.Tests.PostMigration.Infrastructure;

namespace PhotoAlbum.Tests.PostMigration;

/// <summary>
/// Post-migration tests for the <c>GET /Login</c> and <c>POST /Login</c> entry points
/// (<c>PhotoAlbum/Pages/Login.cshtml.cs:LoginModel.OnGet</c> / <c>OnPostAsync</c>), generated
/// 1:1 from TC-WEB-028..TC-WEB-033 in the frozen <c>test-cases/test-cases.md</c>.
/// </summary>
public class LoginPostMigrationIT
{
    private const string AdminUsername = "baseline-admin";
    private const string AdminPassword = "Baseline-Test-Pwd!23";

    private static HttpClient CreateNoRedirectClient(PhotoAlbumWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task TcWeb028_Login_Get_RendersEmptyForm()
    {
        // Preconditions: none.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/Login");

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Username", html, StringComparison.Ordinal);
        Assert.Contains("Password", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid username or password.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Admin account is not configured.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TcWeb029_Login_CorrectCredentials_SignsInAndRedirects()
    {
        // Preconditions: Admin:Username/Password configured to match admin-credentials.json.
        await using var factory = new PhotoAlbumWebApplicationFactory().WithAdminCredentials(AdminUsername, AdminPassword);
        using var client = CreateNoRedirectClient(factory);
        var token = await AntiforgeryHelper.GetTokenAsync(client, "/Login");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = AdminUsername,
            ["Password"] = AdminPassword,
            ["ReturnUrl"] = "",
        };

        // Act
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(form));

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());
        Assert.True(response.Headers.Contains("Set-Cookie"), "Expected a Set-Cookie header for the cookie-auth scheme.");
    }

    [Fact]
    public async Task TcWeb030_Login_ValidLocalReturnUrl_Honored()
    {
        // Preconditions: same admin configuration as TC-WEB-029.
        await using var factory = new PhotoAlbumWebApplicationFactory().WithAdminCredentials(AdminUsername, AdminPassword);
        using var client = CreateNoRedirectClient(factory);
        var token = await AntiforgeryHelper.GetTokenAsync(client, "/Login");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = AdminUsername,
            ["Password"] = AdminPassword,
            ["ReturnUrl"] = "/Detail/1",
        };

        // Act
        var response = await client.PostAsync("/Login?ReturnUrl=%2FDetail%2F1", new FormUrlEncodedContent(form));

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Detail/1", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task TcWeb031_Login_ExternalReturnUrl_Ignored()
    {
        // Preconditions: same admin configuration as TC-WEB-029.
        await using var factory = new PhotoAlbumWebApplicationFactory().WithAdminCredentials(AdminUsername, AdminPassword);
        using var client = CreateNoRedirectClient(factory);
        var token = await AntiforgeryHelper.GetTokenAsync(client, "/Login");
        const string externalUrl = "https://evil.example.com/phish";
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = AdminUsername,
            ["Password"] = AdminPassword,
            ["ReturnUrl"] = externalUrl,
        };

        // Act
        var response = await client.PostAsync(
            $"/Login?ReturnUrl={Uri.EscapeDataString(externalUrl)}", new FormUrlEncodedContent(form));

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());

        // Assert: Negative Verification — redirect target is not the external host.
        Assert.NotEqual(externalUrl, response.Headers.Location!.ToString());
        Assert.False(response.Headers.Location!.IsAbsoluteUri && response.Headers.Location!.Host == "evil.example.com");
    }

    [Fact]
    public async Task TcWeb032_Login_WrongPassword_Rejected()
    {
        // Preconditions: same admin configuration as TC-WEB-029.
        await using var factory = new PhotoAlbumWebApplicationFactory().WithAdminCredentials(AdminUsername, AdminPassword);
        using var client = CreateNoRedirectClient(factory);
        var token = await AntiforgeryHelper.GetTokenAsync(client, "/Login");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = AdminUsername,
            ["Password"] = "wrong-password-value",
            ["ReturnUrl"] = "",
        };

        // Act
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(form));

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password.", html, StringComparison.Ordinal);

        // Assert: Negative Verification — no authentication cookie is issued.
        Assert.False(response.Headers.Contains("Set-Cookie"), "Expected no Set-Cookie header on rejected login.");
    }

    [Fact]
    public async Task TcWeb033_Login_AdminNotConfigured_FailsClosed()
    {
        // Preconditions: no Admin:Username/Password configured — the default factory (no
        // WithAdminCredentials override) already satisfies this: appsettings.json ships
        // Admin:Username=admin with no Admin:Password key at all.
        await using var factory = new PhotoAlbumWebApplicationFactory();
        using var client = CreateNoRedirectClient(factory);
        var token = await AntiforgeryHelper.GetTokenAsync(client, "/Login");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = "admin",
            ["Password"] = "any-value",
            ["ReturnUrl"] = "",
        };

        // Act
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(form));

        // Assert: Expected Response
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin account is not configured.", html, StringComparison.Ordinal);

        // Assert: Negative Verification — no authentication cookie is issued.
        Assert.False(response.Headers.Contains("Set-Cookie"), "Expected no Set-Cookie header when admin is not configured.");
    }
}
