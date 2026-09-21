using System.Globalization;
using System.Runtime.CompilerServices;

using SolSystem.Probe;

[assembly: InternalsVisibleTo("SolSystem.Probe")]

// A probe runner. One process, one script, one transcript.
//
//   SolSystem.Probe --probe tools/probe/docking.probe --probe-out artifacts/probe/docking.txt
//   SolSystem.Probe --probe ... --pilot-debug      prints the pilot's decisions per tick
//
// The exit code says whether anything went wrong: 0 clean, 1 a command failed, 2 a check
// failed, 3 the script could not be read. The transcript is the real output and is written
// even when the run fails, because a failed run is the one worth reading.

string? probePath = null;
string? outPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--probe":
            probePath = i + 1 < args.Length ? args[++i] : null;
            break;
        case "--probe-out":
            outPath = i + 1 < args.Length ? args[++i] : null;
            break;
        case "--manual":
            ProbeWorld.Manual = true;
            break;
        case "--pilot-debug":
            ProbeWorld.Debug = true;
            break;
        case "--help":
        case "-h":
            Console.WriteLine("usage: SolSystem.Probe --probe <script> [--probe-out <file>]");
            return 0;
        default:
            Console.Error.WriteLine($"unknown argument '{args[i]}'");
            return 3;
    }
}

if (probePath is null)
{
    Console.Error.WriteLine("usage: SolSystem.Probe --probe <script> [--probe-out <file>]");
    return 3;
}

ProbeScript script;
try
{
    script = ProbeScript.Load(probePath);
}
catch (ProbeException error)
{
    Console.Error.WriteLine(error.Message);
    return 3;
}

ProbeRunner runner = ProbeRunner.Run(script);
Console.Write(runner.Transcript);

if (outPath is not null)
{
    string? directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
    if (directory is not null)
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllText(outPath, runner.Transcript);
}

if (runner.Errors > 0)
{
    return 1;
}

return runner.FailedChecks > 0 ? 2 : 0;
