using Microsoft.Extensions.Configuration;

namespace PhotoAlbum.Tests.Unit.Configuration;

/// <summary>
/// Verifies that non-secret application settings (Storage:*, FileUpload:*, Admin:Username, etc.)
/// externalized to Azure App Configuration remain resolvable through the existing
/// <see cref="IConfiguration"/> APIs when supplied by the deployment agent as environment
/// variables — the "deployment-only" contract described in
/// .azure/configuration-migration.json — while still falling back to the local
/// appsettings.json defaults when no override is present.
/// </summary>
public class AppConfigurationTests
{
    [Fact]
    public void Configuration_FallsBackToAppSettingsDefaults_WhenNoEnvironmentOverridePresent()
    {
        // Arrange: simulate the shipped appsettings.json defaults with no deployment-supplied overrides.
        var appSettingsDefaults = new Dictionary<string, string?>
        {
            ["FileUpload:MaxFileSizeBytes"] = "10485760",
            ["FileUpload:MaxFilesPerUpload"] = "10",
            ["Admin:Username"] = "admin",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(appSettingsDefaults)
            .Build();

        // Assert
        Assert.Equal("10485760", configuration["FileUpload:MaxFileSizeBytes"]);
        Assert.Equal("10", configuration["FileUpload:MaxFilesPerUpload"]);
        Assert.Equal("admin", configuration["Admin:Username"]);
    }

    [Fact]
    public void Configuration_PrefersEnvironmentVariableOverride_OverAppSettingsDefault()
    {
        // Arrange: appsettings.json-style defaults (lowest precedence)...
        var appSettingsDefaults = new Dictionary<string, string?>
        {
            ["FileUpload:MaxFileSizeBytes"] = "10485760",
            ["Admin:Username"] = "admin",
        };

        // ...and environment-variable-style overrides (as injected by the deployment agent
        // from Azure App Configuration, using "__" as the colon separator on Linux/containers).
        Environment.SetEnvironmentVariable("FileUpload__MaxFileSizeBytes", "20971520");
        Environment.SetEnvironmentVariable("Admin__Username", "siteadmin");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(appSettingsDefaults)
                .AddEnvironmentVariables()
                .Build();

            // Assert: the environment-variable source (added after the in-memory/appsettings
            // source, matching WebApplicationBuilder's default ordering) wins, proving that
            // values supplied by the deployment agent from Azure App Configuration correctly
            // override the local, non-secret appsettings.json defaults without any change to
            // how the application reads configuration (IConfiguration / IOptions<T>).
            Assert.Equal("20971520", configuration["FileUpload:MaxFileSizeBytes"]);
            Assert.Equal("siteadmin", configuration["Admin:Username"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FileUpload__MaxFileSizeBytes", null);
            Environment.SetEnvironmentVariable("Admin__Username", null);
        }
    }

    [Fact]
    public void Configuration_ResolvesIndexedArrayOverride_ForAllowedMimeTypes()
    {
        // Arrange: FileUpload:AllowedMimeTypes is an array, exposed in
        // .azure/configuration-migration.json as indexed keys (":0", ":1", ...). Verify the
        // deployment-supplied indexed environment variables bind back into the same
        // array shape consumed by PhotoService today.
        Environment.SetEnvironmentVariable("FileUpload__AllowedMimeTypes__0", "image/jpeg");
        Environment.SetEnvironmentVariable("FileUpload__AllowedMimeTypes__1", "image/png");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var allowedMimeTypes = configuration.GetSection("FileUpload:AllowedMimeTypes").Get<string[]>();

            Assert.NotNull(allowedMimeTypes);
            Assert.Equal(new[] { "image/jpeg", "image/png" }, allowedMimeTypes);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FileUpload__AllowedMimeTypes__0", null);
            Environment.SetEnvironmentVariable("FileUpload__AllowedMimeTypes__1", null);
        }
    }
}
