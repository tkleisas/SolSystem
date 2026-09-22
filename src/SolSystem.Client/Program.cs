using SolSystem.Client;

// The client: a window you can fly in, and a headless mode that renders a frame to a PNG.
//
//   SolSystem.Client                                  interactive
//   SolSystem.Client --shot out.png [options]         one frame, then exit
//
// The headless mode is not a debugging afterthought. A 3D scene is hard to test and easy to
// believe, and the only honest check on "does the sky look right" is to render it and look. Every
// claim this project makes about the Milky Way being in the right place, or the Sun being where the
// ephemeris says on a given date, is checkable against a frame from this mode.

// No arguments means FLY. The first version of this printed the usage and exited, because it was
// written when the client could only render a frame and had nothing to do with no arguments — and
// then the interactive mode was added underneath it and nobody ran `dotnet run` with no arguments
// again. The single most likely thing a person types is the one that did nothing.
if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine(LaunchOptions.Usage);
    return 0;
}

// The build names itself before anything else runs: a headless transcript or a bug report
// that does not say which version produced it is a transcript you cannot reproduce.
Console.WriteLine($"SolSystem.Client {BuildInfo.Version}");

try
{
    LaunchOptions options = LaunchOptions.Parse(args);
    using var game = new FlightGame(options);
    game.Run();
    return Environment.ExitCode;
}
catch (Exception exception)
{
    // A windowed executable has no console, so a startup failure looks like nothing happening.
    string message = $"SolSystem.Client {BuildInfo.Version} failed to start:\n{exception}";
    Console.Error.WriteLine(message);

    try
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), message);
    }
    catch (IOException)
    {
        // Nothing useful left to do if even the log cannot be written.
    }

    return 1;
}
