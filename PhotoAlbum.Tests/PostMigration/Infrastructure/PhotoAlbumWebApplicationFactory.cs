using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotoAlbum.Data;
using PhotoAlbum.Services;
using PhotoAlbum.Tests.TestDoubles;

namespace PhotoAlbum.Tests.PostMigration.Infrastructure;

/// <summary>
/// Boots the real PhotoAlbum Razor Pages application (routing, antiforgery, cookie
/// authentication, Program.cs pipeline) exactly as production does, while swapping the two
/// migrated external dependencies for the mocked test doubles mandated by
/// <c>test-cases/infra-decision-table.md</c> (Integration Test Environment = mock for every
/// row): Azure Blob Storage → <see cref="InMemoryPhotoBlobStorage"/>, Azure SQL Database →
/// EF Core InMemory provider (via <see cref="PhotoAlbumContext"/>), and Azure App
/// Configuration → an in-memory <see cref="IConfiguration"/> layer supplied directly to the
/// test host instead of connecting to a live App Configuration store.
///
/// Two process environment variables are set once (idempotently, in the static constructor)
/// so that <c>Program.cs</c>'s *synchronous* startup guards — which read
/// <c>builder.Configuration</c> and throw/branch before the test host's
/// <see cref="IWebHostBuilder.ConfigureServices"/> / <c>ConfigureAppConfiguration</c>
/// callbacks are merged into the builder at <c>Build()</c> time — see a syntactically valid
/// (but never actually dialled) Blob Storage endpoint and skip the startup EF Core migration.
/// The real Blob Storage client that Program.cs registers as a side effect is replaced with
/// <see cref="InMemoryPhotoBlobStorage"/> below before any request is served, so the fake
/// endpoint is never contacted.
/// </summary>
public sealed class PhotoAlbumWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"photoalbum-it-{Guid.NewGuid():N}";
    private readonly Dictionary<string, string?> _configOverrides = new();
    private readonly List<IInterceptor> _dbInterceptors = new();

    static PhotoAlbumWebApplicationFactory()
    {
        // Syntactically valid (contains no '{' placeholder) but never actually dialled —
        // IPhotoBlobStorage is replaced with the in-memory fake in ConfigureServices below,
        // so no network call is ever made against this endpoint.
        Environment.SetEnvironmentVariable("Storage__ServiceUri", "https://it-test-storage-unused.blob.core.windows.net");
        // Skips Program.cs's startup `context.Database.MigrateAsync()` call (General
        // Precondition shared by every test case in the frozen spec).
        Environment.SetEnvironmentVariable("IsTestEnvironment", "true");
    }

    /// <summary>
    /// The single <see cref="InMemoryPhotoBlobStorage"/> instance backing this factory's
    /// <see cref="IPhotoBlobStorage"/> registration, exposed so tests can seed/assert blob
    /// state directly (Resource/Negative Verification bullets reference the storage
    /// container, not an entry point).
    /// </summary>
    public InMemoryPhotoBlobStorage BlobStorage { get; private set; } = new();

    private Func<InMemoryPhotoBlobStorage, IPhotoBlobStorage>? _blobStorageFactory;

    /// <summary>
    /// Overrides the registered <c>Admin:Username</c> / <c>Admin:Password</c> configuration
    /// values (mocked Azure App Configuration boundary) so Login test cases can exercise the
    /// "correctly configured admin" path. When never called, the shipped
    /// <c>appsettings.json</c> default (<c>Admin:Username=admin</c>, no <c>Admin:Password</c>)
    /// applies, which already satisfies the "admin not configured" precondition for
    /// TC-WEB-033 without any override.
    /// </summary>
    public PhotoAlbumWebApplicationFactory WithAdminCredentials(string username, string password)
    {
        _configOverrides["Admin:Username"] = username;
        _configOverrides["Admin:Password"] = password;
        return this;
    }

    /// <summary>
    /// Wraps the factory's <see cref="InMemoryPhotoBlobStorage"/> with a failure-injecting
    /// decorator (mock-boundary fault, e.g. TC-WEB-012, TC-WEB-027).
    /// </summary>
    public PhotoAlbumWebApplicationFactory WithBlobStorage(Func<InMemoryPhotoBlobStorage, IPhotoBlobStorage> factory)
    {
        _blobStorageFactory = factory;
        return this;
    }

    /// <summary>
    /// Adds an EF Core interceptor (e.g. <see cref="ThrowingQueryExpressionInterceptor"/>,
    /// <see cref="ThrowingSaveChangesInterceptor"/>) to the InMemory <see cref="PhotoAlbumContext"/>
    /// used by this factory, to simulate a "photo metadata store" fault (mock boundary).
    /// </summary>
    public PhotoAlbumWebApplicationFactory WithDbInterceptor(IInterceptor interceptor)
    {
        _dbInterceptors.Add(interceptor);
        return this;
    }

    /// <summary>
    /// Opens a fresh <see cref="PhotoAlbumContext"/> against the same InMemory database this
    /// factory's app uses, for direct seeding of Preconditions / reading of Resource
    /// Verification state (no application entry point exists to seed rows with fixed
    /// timestamps, so direct EF Core access is the legitimate setup/observation path per the
    /// skill's seeding rules).
    /// </summary>
    public PhotoAlbumContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PhotoAlbumContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;
        return new PhotoAlbumContext(options);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(_configOverrides);
        });

        builder.ConfigureServices(services =>
        {
            // --- Mocked Azure SQL Database boundary: replace the SQL Server DbContext
            // registration Program.cs adds with an isolated, uniquely-named EF Core InMemory
            // database for this factory instance.
            //
            // EF Core's AddDbContext registers the connection-string-building configure
            // delegate as an IDbContextOptionsConfiguration<PhotoAlbumContext> entry (not
            // directly on the DbContextOptions<PhotoAlbumContext> descriptor), and combines
            // *all* such entries when options are built — so removing only the
            // DbContextOptions<PhotoAlbumContext> descriptor leaves Program.cs's
            // connection-string guard (which throws when none is configured) registered and
            // still invoked. RemoveAll clears every EF-registered descriptor for this
            // context before re-adding a single InMemory-only configuration. ---
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<PhotoAlbumContext>));
            services.RemoveAll(typeof(DbContextOptions<PhotoAlbumContext>));
            services.RemoveAll(typeof(DbContextOptions));
            services.RemoveAll(typeof(PhotoAlbumContext));

            services.AddDbContext<PhotoAlbumContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                foreach (var interceptor in _dbInterceptors)
                {
                    options.AddInterceptors(interceptor);
                }
            });

            // --- Mocked Azure Blob Storage boundary: replace the AzurePhotoBlobStorage /
            // BlobServiceClient registrations Program.cs adds with the in-memory fake (or a
            // failure-injecting decorator over it). ---
            var blobStorageDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPhotoBlobStorage));
            if (blobStorageDescriptor != null)
            {
                services.Remove(blobStorageDescriptor);
            }

            IPhotoBlobStorage effectiveBlobStorage = _blobStorageFactory != null
                ? _blobStorageFactory(BlobStorage)
                : BlobStorage;
            services.AddSingleton(effectiveBlobStorage);
        });
    }
}
