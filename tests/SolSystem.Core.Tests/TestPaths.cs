namespace SolSystem.Core.Tests;

/// <summary>
/// Where the repository's data files are, found from wherever the tests happen to be running.
/// </summary>
internal static class TestPaths
{
    private static readonly Lazy<string> Root = new(FindRoot);

    internal static string StarCatalogue => Path.Combine(Root.Value, "art", "sky", "stars.bin");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "art", "sky")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find the repository root above {AppContext.BaseDirectory}");
    }
}
