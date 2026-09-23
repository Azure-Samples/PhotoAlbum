using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.EntityFrameworkCore;
using PhotoAlbum.Data;
using PhotoAlbum.Services;

var builder = WebApplication.CreateBuilder(args);

var appConfigurationEndpoint = Environment.GetEnvironmentVariable("AZURE_APP_CONFIGURATION_ENDPOINT");
var credential = new DefaultAzureCredential();

if (!string.IsNullOrWhiteSpace(appConfigurationEndpoint))
{
    builder.Configuration.AddAzureAppConfiguration(options =>
    {
        options.Connect(new Uri(appConfigurationEndpoint), credential);
    });
}

var keyVaultUri = Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_URI");
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), credential);
}

builder.Services.AddAzureAppConfiguration();

var configuredServiceUri = builder.Configuration["Storage:ServiceUri"];
var configuredAccountName = builder.Configuration["Storage:AccountName"] ?? builder.Configuration["StorageAccountName"];

if (!string.IsNullOrWhiteSpace(configuredServiceUri) && !configuredServiceUri.Contains("{", StringComparison.Ordinal))
{
    builder.Services.AddSingleton(new BlobServiceClient(new Uri(configuredServiceUri), credential));
    builder.Services.AddSingleton<IPhotoBlobStorage, AzurePhotoBlobStorage>();
}
else if (!string.IsNullOrWhiteSpace(configuredAccountName) && !configuredAccountName.Contains("{", StringComparison.Ordinal))
{
    var storageUri = $"https://{configuredAccountName}.blob.core.windows.net";
    builder.Services.AddSingleton(new BlobServiceClient(new Uri(storageUri), credential));
    builder.Services.AddSingleton<IPhotoBlobStorage, AzurePhotoBlobStorage>();
}
else
{
    // Photo storage is exclusively Azure Blob Storage (with Managed Identity) — there is
    // no local-disk fallback. Fail fast at startup if neither Storage:ServiceUri nor
    // Storage:AccountName/StorageAccountName is configured, so misconfiguration surfaces
    // immediately rather than silently writing photos to ephemeral container disk.
    throw new InvalidOperationException(
        "Azure Blob Storage is not configured. Set 'Storage:ServiceUri' or 'Storage:AccountName' " +
        "(and 'Storage:ContainerName') so photo storage can use Azure Blob Storage with Managed Identity.");
}

// Add services to the container.
builder.Services.AddRazorPages();

// Authentication/authorization: the gallery is public, but state-changing
// operations (photo delete) require an authenticated admin (CWE-306).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
    });
builder.Services.AddAuthorization();

// Add DbContext
builder.Services.AddDbContext<PhotoAlbumContext>(options =>
{
    var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(configuredConnectionString) &&
        (configuredConnectionString.Contains("<", StringComparison.Ordinal) ||
         configuredConnectionString.Contains(">", StringComparison.Ordinal) ||
         configuredConnectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase)))
    {
        configuredConnectionString = null;
    }

    var connectionString = configuredConnectionString
        ?? Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING")
        ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        var sqlServer = builder.Configuration["AzureSql:Server"]
            ?? builder.Configuration["AzureSql__Server"]
            ?? Environment.GetEnvironmentVariable("AZURE_SQL_SERVER_FQDN")
            ?? Environment.GetEnvironmentVariable("AZURE_SQL_SERVER_NAME")
            ?? Environment.GetEnvironmentVariable("SQL_SERVER_NAME");

        var sqlDatabase = builder.Configuration["AzureSql:Database"]
            ?? builder.Configuration["AzureSql__Database"]
            ?? Environment.GetEnvironmentVariable("AZURE_SQL_DATABASE_NAME")
            ?? Environment.GetEnvironmentVariable("SQL_DATABASE_NAME");

        if (!string.IsNullOrWhiteSpace(sqlServer) && !string.IsNullOrWhiteSpace(sqlDatabase))
        {
            connectionString = $"Server=tcp:{sqlServer};Initial Catalog={sqlDatabase};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        }
    }

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured. Set DefaultConnection or AzureSql:Server/AzureSql:Database (or AZURE_SQL_SERVER_FQDN/AZURE_SQL_DATABASE_NAME) to use Azure SQL with managed identity.");
    }

    options.UseSqlServer(connectionString);
});

// Register PhotoService
builder.Services.AddScoped<IPhotoService, PhotoService>();

// Configure form options for file uploads
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10485760; // 10MB
    options.ValueLengthLimit = 10485760;
    options.MultipartBoundaryLengthLimit = 128;
});

var app = builder.Build();

// Run database migrations on startup in all environments (skip only when flagged as test)
var isTestEnvironment = app.Configuration.GetValue<bool>("IsTestEnvironment");
if (!isTestEnvironment)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PhotoAlbumContext>();
        await context.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        // Log and rethrow to fail fast if migrations cannot be applied
        Console.WriteLine($"Database migration failed: {ex}");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=3600");
    }
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public partial class Program { }
