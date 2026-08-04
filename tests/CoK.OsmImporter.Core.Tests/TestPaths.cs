namespace CoK.OsmImporter.Core.Tests;

/// <summary>
/// Locates the (optional, gitignored) real sample data files at the repo root — an .osm export
/// and a .mommap used as a template. These are bring-your-own (see README.md): a fresh clone won't
/// have them, so tests that depend on them check for null and no-op instead of failing.
/// </summary>
internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string? TemplateMommap => ExistingOrNull(Path.Combine(RepoRoot, "template.mommap"));
    public static string? SampleOsm => ExistingOrNull(Path.Combine(RepoRoot, "mejka_map.osm"));

    private static string? ExistingOrNull(string path) => File.Exists(path) ? path : null;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CoK.OsmImporter.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find repo root (CoK.OsmImporter.slnx) above {AppContext.BaseDirectory}.");
    }
}
