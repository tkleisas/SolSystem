namespace SolSystem.Core.Tests;

/// <summary>
/// Where the repository's data files are, found from wherever the tests happen to be running.
/// </summary>
internal static class TestPaths
{
    internal static string StarCatalogue =>
        Path.Combine(Repository.Root, "art", "sky", "stars.bin");
}
