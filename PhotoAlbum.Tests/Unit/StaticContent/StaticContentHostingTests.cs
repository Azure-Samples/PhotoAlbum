using System.IO;
using System.Linq;

namespace PhotoAlbum.Tests.Unit.StaticContent;

/// <summary>
/// Guards the outcome of the static-content hosting review (wwwroot: css/js/lib/images).
/// The review concluded that the small set of actively-referenced assets (site.css,
/// site.js, upload.js, favicon.ico) should remain packaged in the container image and
/// served in-process via ASP.NET Core's built-in static file/asset pipeline, while the
/// large, entirely-unreferenced vendor payload (wwwroot/lib: Bootstrap, jQuery,
/// jQuery Validation) is removed rather than shipped to Azure Container Apps.
///
/// These tests protect that decision from regressing: they fail if the dead vendor
/// library payload is reintroduced, if one of the actively-used assets goes missing
/// (which would change page behavior/URLs), or if the in-container static payload
/// balloons back up in size.
/// </summary>
public class StaticContentHostingTests
{
    private static readonly string[] ExpectedActiveAssets =
    {
        Path.Combine("css", "site.css"),
        Path.Combine("js", "site.js"),
        Path.Combine("js", "upload.js"),
        "favicon.ico",
    };

    // Generous ceiling for the in-container static payload (actual active content is a
    // few tens of KB). This is intentionally far below the ~9.5MB the unused vendor
    // libraries previously added, so the test fails if bulky vendor assets return.
    private const long MaxTotalStaticContentBytes = 1 * 1024 * 1024; // 1 MB

    private static string GetWwwrootPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotoAlbum.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new DirectoryNotFoundException("Could not locate repository root (PhotoAlbum.sln) from test base directory.");
        }

        return Path.Combine(dir.FullName, "PhotoAlbum", "wwwroot");
    }

    [Fact]
    public void Wwwroot_ContainsAllActivelyReferencedAssets()
    {
        var wwwroot = GetWwwrootPath();

        foreach (var relativePath in ExpectedActiveAssets)
        {
            var fullPath = Path.Combine(wwwroot, relativePath);
            Assert.True(File.Exists(fullPath), $"Expected actively-referenced static asset '{relativePath}' to exist under wwwroot.");
        }
    }

    [Fact]
    public void Wwwroot_DoesNotContainUnreferencedVendorLibraryPayload()
    {
        var wwwroot = GetWwwrootPath();
        var libDirectory = Path.Combine(wwwroot, "lib");

        Assert.False(
            Directory.Exists(libDirectory),
            "wwwroot/lib (Bootstrap/jQuery/jQuery Validation) is unreferenced by any page and should not be " +
            "packaged into the Azure Container Apps image. Bootstrap CSS/JS is loaded from an external CDN in " +
            "_Layout.cshtml, and no page renders the jQuery validation scripts partial.");
    }

    [Fact]
    public void Wwwroot_DoesNotContainStaleUploadsPlaceholder()
    {
        var wwwroot = GetWwwrootPath();
        var uploadsDirectory = Path.Combine(wwwroot, "uploads");

        Assert.False(
            Directory.Exists(uploadsDirectory),
            "wwwroot/uploads was a local-disk placeholder from before the exclusive Azure Blob Storage " +
            "migration; photo bytes are never read from or written to this path, so it should not remain in " +
            "the container image.");
    }

    [Fact]
    public void Wwwroot_TotalStaticContentSize_StaysWellUnderContainerBudget()
    {
        var wwwroot = GetWwwrootPath();
        var totalBytes = Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);

        Assert.True(
            totalBytes < MaxTotalStaticContentBytes,
            $"Total wwwroot static content is {totalBytes} bytes, which exceeds the {MaxTotalStaticContentBytes} byte " +
            "budget for keeping static assets bundled in the Azure Container Apps image. Re-evaluate whether " +
            "Blob Storage/CDN-backed hosting is now warranted before adding large static payloads.");
    }
}
