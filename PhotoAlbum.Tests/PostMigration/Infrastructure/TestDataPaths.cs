namespace PhotoAlbum.Tests.PostMigration.Infrastructure;

/// <summary>
/// Resolves paths under the frozen <c>test-cases/testdata/</c> bundle regardless of the test
/// runner's current/base directory (typically the build output folder, not the repo root).
/// </summary>
public static class TestDataPaths
{
    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PhotoAlbum.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new DirectoryNotFoundException(
                "Could not locate repository root (PhotoAlbum.sln) from test base directory.");
        }

        return dir.FullName;
    });

    private static string TestDataRoot => Path.Combine(
        RepoRoot.Value, "PhotoAlbum.Tests", "test-cases", "testdata");

    /// <summary>Resolves a path such as <c>inputs/sample.jpg</c> under <c>testdata/</c>.</summary>
    public static string Resolve(string relativePath) =>
        Path.Combine(TestDataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static byte[] ReadAllBytes(string relativePath) => File.ReadAllBytes(Resolve(relativePath));

    public static string ReadAllText(string relativePath) => File.ReadAllText(Resolve(relativePath));
}
