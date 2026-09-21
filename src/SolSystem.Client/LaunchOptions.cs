using System.Globalization;

namespace SolSystem.Client;

/// <summary>
/// What the client was asked to do, parsed from the command line.
/// </summary>
/// <remarks>
/// Every option has a default that produces a sensible picture, because the useful thing about a
/// headless renderer is being able to ask for one frame with no arguments and get something worth
/// looking at.
/// </remarks>
internal sealed class LaunchOptions
{
    /// <summary>Where to write a single frame, or null for an interactive session.</summary>
    internal string? ShotPath { get; private set; }

    internal int Width { get; private set; } = 1280;

    internal int Height { get; private set; } = 720;

    /// <summary>Julian date to start at. J2000 by default.</summary>
    internal double JulianDate { get; private set; } = 2451545.0;

    /// <summary>Which station to start beside.</summary>
    internal string Station { get; private set; } = "Meridian";

    /// <summary>How far down the corridor to start, in metres.</summary>
    internal double Standoff { get; private set; } = 400.0;

    /// <summary>
    /// Which way to look at the start.
    /// </summary>
    /// <remarks>
    /// The interesting directions are the ones with something in them. Looking at a random patch of
    /// sky gives a picture of stars, which is lovely and tells you nothing about whether the
    /// ephemeris is right; looking at the Sun, the Earth or the Milky Way tells you immediately.
    /// </remarks>
    internal ViewAim Aim { get; private set; } = ViewAim.Earth;

    /// <summary>Simulated seconds per real second, interactively.</summary>
    internal double TimeRate { get; private set; } = 1.0;

    /// <summary>
    /// Render exactly one frame and exit.
    /// </summary>
    /// <remarks>
    /// <b>"Headless" and "render one frame" are not the same idea</b>, and conflating them cost the
    /// <c>--frames</c> option entirely: it was defined as "a shot path was given", so anything with
    /// <c>--shot</c> rendered frame zero and exited, and <c>--frames 40</c> ran for one frame. The
    /// tell was a diagnostic that printed the same timestamp for every value of the option.
    /// </remarks>
    internal bool OneShot => ShotPath is not null && Frames == 0;

    /// <summary>Whether the loop should run without reading the keyboard or the mouse.</summary>
    internal bool Headless => ShotPath is not null;

    /// <summary>Print what each body resolved to. For when a frame looks wrong.</summary>
    internal bool Verbose { get; private set; }

    /// <summary>
    /// Run the interactive loop for this many frames and then exit, saving <c>--shot</c> if asked.
    /// </summary>
    /// <remarks>
    /// The headless mode renders one frame through a path that no player ever takes: no update
    /// loop, no keyboard, no fixed timestep. This runs the loop a player runs — which is the only
    /// way to check that the thing they type actually starts.
    /// </remarks>
    internal int Frames { get; private set; }

    /// <summary>
    /// Line every asset up at its true size and render that, instead of flying.
    /// </summary>
    /// <remarks>
    /// The only honest way to judge whether a station is the right size for the ships that use it.
    /// A chase camera puts the ship a hundred metres away and the station four hundred, and
    /// perspective then makes a 56 m courier look like half the diameter of a 330 m wheel. It is not,
    /// and no amount of looking at the flying view will say so.
    /// </remarks>
    internal bool Lineup { get; private set; }

    /// <summary>Which camera to start in, for rendering a view without a mouse.</summary>
    internal string Camera { get; private set; } = "chase";

    /// <summary>Throttle to start at, 0 to 1. For rendering the plume without a keyboard.</summary>
    internal double Throttle { get; private set; }

    /// <summary>
    /// Keys to hold down for the whole run, e.g. <c>--hold D</c>.
    /// </summary>
    /// <remarks>
    /// The reason this exists is that "the controls do not work correctly" is not a thing a still
    /// frame can answer. Holding a key and printing the attitude afterwards turns it into a number:
    /// D must move the nose towards starboard, R must move it towards the deck, and E must leave the
    /// nose alone and turn the deck.
    /// </remarks>
    internal string Hold { get; private set; } = string.Empty;

    /// <summary>Open the chart at startup, and optionally select a body.</summary>
    internal bool Chart { get; private set; }

    /// <summary>Which body to select on the chart, by name.</summary>
    internal string Destination { get; private set; } = string.Empty;

    /// <summary>Camera yaw to start at, in degrees. For rendering a look without a mouse.</summary>
    internal double CameraYaw { get; private set; }

    /// <summary>Camera pitch to start at, in degrees.</summary>
    internal double CameraPitch { get; private set; } = double.NaN;

    /// <summary>Camera zoom to start at, in metres.</summary>
    internal double CameraDistance { get; private set; }

    /// <summary>The command line, for when nobody knows what to type.</summary>
    internal const string Usage = """
        SolSystem.Client — fly a ship in the solar system

          --shot <file>        render one frame to a PNG and exit
          --width <n>          frame width  (default 1280)
          --height <n>         frame height (default 720)
          --at <jd>            Julian date (default 2451545.0, J2000)
          --station <name>     which station to start beside (default Meridian)
          --standoff <m>       how far off the dock to start (default 400)
          --earthward          look at the Earth (default)
          --sunward            look at the Sun
          --milkyway           look at the galactic centre
          --dockward           look down the corridor at the station
          --rate <n>           simulated seconds per real second (default 1)
          --verbose            print what each body resolved to
          --frames <n>         run the interactive loop for n frames, then exit
          --camera <mode>      chase, orbit, cockpit or port (default chase)
          --throttle <0-1>     start with the engine lit, for rendering the plume
          --chart              open the solar-system chart
          --destination <name> select a body on the chart, e.g. Mars
          --hold <keys>        hold these keys down, e.g. --hold D
          --camera-yaw <deg>   start the camera at this yaw
          --camera-pitch <deg> start the camera at this pitch
          --camera-distance <m> start the camera at this distance
          --lineup             draw every asset at true size, side by side
        """;

    internal enum ViewAim
    {
        Earth,
        Sun,
        MilkyWay,
        Station,
    }

    internal static LaunchOptions Parse(string[] args)
    {
        var options = new LaunchOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shot":
                    options.ShotPath = Require(args, ref i);
                    break;

                case "--width":
                    options.Width = (int)Number(args, ref i, 16, 8192);
                    break;

                case "--height":
                    options.Height = (int)Number(args, ref i, 16, 8192);
                    break;

                case "--at":
                    options.JulianDate = Number(args, ref i, 0.0, double.MaxValue);
                    break;

                case "--station":
                    options.Station = Require(args, ref i);
                    break;

                case "--standoff":
                    options.Standoff = Number(args, ref i, 1.0, 1.0e9);
                    break;

                case "--rate":
                    options.TimeRate = Number(args, ref i, 0.0, 1.0e6);
                    break;

                case "--throttle":
                    options.Throttle = Number(args, ref i, 0.0, 1.0);
                    break;

                case "--chart":
                    options.Chart = true;
                    break;

                case "--destination":
                    options.Destination = Require(args, ref i);
                    break;

                case "--hold":
                    options.Hold = Require(args, ref i);
                    break;

                case "--camera-yaw":
                    options.CameraYaw = Number(args, ref i, -360.0, 360.0);
                    break;

                case "--camera-pitch":
                    options.CameraPitch = Number(args, ref i, -89.0, 89.0);
                    break;

                case "--camera-distance":
                    options.CameraDistance = Number(args, ref i, 25.0, 4000.0);
                    break;

                case "--camera":
                    options.Camera = Require(args, ref i);
                    break;

                case "--lineup":
                    options.Lineup = true;
                    break;

                case "--frames":
                    options.Frames = (int)Number(args, ref i, 1, 100_000);
                    break;

                case "--verbose":
                    options.Verbose = true;
                    break;

                case "--sunward":
                    options.Aim = ViewAim.Sun;
                    break;

                case "--earthward":
                    options.Aim = ViewAim.Earth;
                    break;

                case "--milkyway":
                    options.Aim = ViewAim.MilkyWay;
                    break;

                case "--dockward":
                    options.Aim = ViewAim.Station;
                    break;

                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }

        return options;
    }

    private static string Require(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"'{args[i]}' needs a value");
        }

        return args[++i];
    }

    private static double Number(string[] args, ref int i, double min, double max)
    {
        string raw = Require(args, ref i);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new ArgumentException($"'{args[i - 1]}' expected a number, got '{raw}'");
        }

        if (value < min || value > max)
        {
            throw new ArgumentException($"'{args[i - 1]}' wants {min} to {max}, got {value}");
        }

        return value;
    }
}
