namespace SolSystem.Core;

/// <summary>
/// The repository root, found by walking up for the art directory, once, everywhere.
/// </summary>
/// <remarks>
/// <para>
/// Everything that loads content — the client's star catalogue, the probe's own copy,
/// the hull loader — used to walk up from its own <c>AppContext.BaseDirectory</c>, by its
/// own copy of twelve lines, to the same directory. One of the copies could not fail with
/// a shared message; three could disagree about the answer. This is the one walk.
/// </para>
/// <para>
/// The marker is the <c>art/sky</c> directory because it is committed data: an <c>art</c>
/// alone would also appear where generated audio lives, and a repository checkout with
/// the art fetched is exactly the threshold where this program can do anything.
/// </para>
/// </remarks>
public static class Repository
{
    private static string? _root;

    /// <summary>The repository root, or the throw with the one shared message.</summary>
    public static string Root
    {
        get
        {
            if (_root is not null)
            {
                return _root;
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "art", "sky")))
                {
                    _root = directory.FullName;
                    return _root;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                $"could not find the repository root above {AppContext.BaseDirectory}");
        }
    }
}
