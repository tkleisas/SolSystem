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

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine(LaunchOptions.Usage);
    return 0;
}

if (args.Length == 0)
{
    Console.WriteLine(LaunchOptions.Usage);
    return 1;
}

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
    string message = $"SolSystem.Client failed to start:\n{exception}";
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
