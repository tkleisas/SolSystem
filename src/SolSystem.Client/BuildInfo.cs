using System.Reflection;

namespace SolSystem.Client;

/// <summary>
/// The build's own name, from the assembly's informational version.
/// </summary>
/// <remarks>
/// Stamped by the StampGitVersion target from <c>git describe --tags</c>, so a release build
/// says "0.0.1", a development build says which commit and how dirty, and nothing has to be
/// edited by hand anywhere. The "dev" fallback is for a build with no git and no stamp,
/// which is a build that has other problems anyway.
/// </remarks>
internal static class BuildInfo
{
    internal static readonly string Version = Read();

    private static string Read()
    {
        string? info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // SourceLink appends +commit to the informational version; the display does not need it.
        return string.IsNullOrEmpty(info) ? "dev" : info.Split('+')[0];
    }
}
