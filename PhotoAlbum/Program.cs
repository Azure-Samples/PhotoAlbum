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
    builder.Services.AddSingleton<IPhotoBlobStorage, LocalPhotoBlobStorage>();
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
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
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
