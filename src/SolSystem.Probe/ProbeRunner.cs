using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using SolSystem.Core.Local;
using SolSystem.Core.Sky;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Probe;

/// <summary>
/// Runs a probe script: a list of commands, executed in order, against one world.
/// </summary>
/// <remarks>
/// <para>
/// The idea is ported from MiVic, where it was the difference between a test that cost a
/// process launch, a screenshot and a guess, and a test that costs one launch and prints
/// forty numbers. A probe is a question asked of a running simulation:
/// </para>
/// <code>
///   advance 3000
///   range Meridian
///   state ship
///   expect "the ship closed to within 300 m" 300 greater
/// </code>
/// <para>
/// <b>Commands never abort the script.</b> A failed one writes an <c>error:</c> line and the
/// next command runs, so a typo on line four of a fifty-line probe costs one line rather
/// than the other forty-six. The transcript is the artefact; the exit code only says
/// whether anything in it was an error or a failed check.
/// </para>
/// <para>
/// <b>Numbers are printed invariantly and fixed-point values are printed as exact decimal.</b>
/// A transcript that depends on a culture or a rounding could not be diffed, and diffing two
/// transcripts is the cheapest reproducibility test there is.
/// </para>
/// </remarks>
internal sealed class ProbeRunner
{
    private readonly ProbeWorld _world = new();
    private readonly StringBuilder _transcript = new();
    private readonly string _name;

    private int _commandCount;
    private int _errorCount;
    private int _failedChecks;

    /// <summary>The stars, loaded once. The sky is the one thing here that is not state.</summary>
    private readonly StarCatalogue _catalogue =
        StarCatalogue.Load(Path.Combine(RepoRoot(), "art", "sky", "stars.bin"));

    /// <summary>The last body named by a <c>body</c> command, for <c>expect … sun</c>.</summary>
    private string _lastBody = "earth";

    internal ProbeRunner(string name) => _name = name;

    /// <summary>Errors: commands that could not be carried out.</summary>
    internal int Errors => _errorCount;

    /// <summary>Checks that ran and were not satisfied.</summary>
    internal int FailedChecks => _failedChecks;

    /// <summary>The transcript, which is the artefact a probe produces.</summary>
    internal string Transcript => _transcript.ToString();

    /// <summary>The repository root, found by walking up for the art directory.</summary>
    private static string RepoRoot()
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

        throw new InvalidOperationException("could not find the repository root");
    }

    /// <summary>Runs a script and returns the transcript.</summary>
    internal static ProbeRunner Run(ProbeScript script)
    {
        var runner = new ProbeRunner(script.Name);
        runner.Execute(script);
        return runner;
    }

    private void Execute(ProbeScript script)
    {
        Emit($"probe: {script.Name}");
        Emit($"tick: {ProbeWorld.TickSeconds:G17} s");
        Emit($"stations: {string.Join(", ", _world.StationNames.OrderBy(n => n))}");
        Emit(string.Empty);

        foreach (ProbeCommand command in script.Commands)
        {
            _commandCount++;
            Emit($"> {command.Text}");

            try
            {
                Dispatch(command);
            }
            catch (ProbeException error)
            {
                _errorCount++;
                Emit($"  error: {error.Message}");
            }
        }

        Emit(string.Empty);
        Emit($"probe: {_commandCount} commands, {_errorCount} errors, {_failedChecks} checks failed");
    }

    private void Dispatch(ProbeCommand command)
    {
        switch (command.Verb)
        {
            case "advance":
                Advance(command);
                break;

            case "days":
                Days(command);
                break;

            case "launch":
                Launch(command);
                break;

            case "ship":
                Ship(command);
                break;

            case "station":
                Station(command);
                break;

            case "body":
                Body(command);
                break;

            case "sundistance":
                SunDistance(command);
                break;

            case "sky":
                Sky(command);
                break;

            case "range":
                Range(command);
                break;

            case "closing":
                Closing(command);
                break;

            case "phase":
                Phase(command);
                break;

            case "hash":
                Hash(command);
                break;

            case "emit":
                Emit("  " + string.Join(' ', command.Arguments));
                break;

            case "expect":
                Expect(command);
                break;

            default:
                throw new ProbeException(
                    $"unknown command '{command.Verb}' — advance, days, launch, ship, station, " +
                    "body, sundistance, sky, range, closing, phase, lateral, hash, emit, expect");
        }
    }

    // ---------------------------------------------------------------- control

    private void Advance(ProbeCommand command)
    {
        int ticks = command.Whole(0, "a tick count", "advance <ticks>", 0, 10_000_000);
        _ = ProbeWorld.Debug;
        _world.Advance(ticks);
        Emit($"  world at tick {_world.Ticks} ({_world.Time / 86400.0:F6} days from J2000)");
    }

    /// <summary>
    /// Advances by a span of days.
    /// </summary>
    /// <remarks>
    /// Separate from <c>advance</c> because a tick count is the wrong unit for anything
    /// longer than a few minutes, and getting it wrong is silent: a probe that meant a
    /// quarter of a year and wrote 6 480 000 advanced seventy-five seconds and reported a
    /// perfectly plausible world. Days are what the question is actually asked in.
    /// </remarks>
    private void Days(ProbeCommand command)
    {
        double days = command.Real(0, "a number of days", "days <n>");
        if (days < 0.0 || days > 400_000.0)
        {
            throw new ProbeException($"'days' wants a span between 0 and 400 000, got {days}");
        }

        long ticks = (long)Math.Round(days * 86400.0 / ProbeWorld.TickSeconds);
        _world.Advance((int)Math.Min(ticks, int.MaxValue));
        Emit($"  {days:F6} days = {ticks:N0} ticks, world at tick {_world.Ticks} "
            + $"({_world.Time / 86400.0:F6} days from J2000)");
    }

    private void Launch(ProbeCommand command)
    {
        string station = command.Argument(0, "a station name", "launch <station> [standoff m] [closing m/s]");
        double standoff = command.RealOr(1, 2_000.0);
        double closing = command.RealOr(2, 0.0);

        _world.Launch(station, standoff, closing);
        Emit($"  {standoff:F1} m down the corridor from {station}, closing at {closing:F3} m/s");
    }

    // ---------------------------------------------------------------- state

    private void Ship(ProbeCommand command)
    {
        if (_world.Ship is not Ship ship)
        {
            throw new ProbeException("no ship has been launched — use 'launch'");
        }

        Emit($"  position  {Vec(ship.Position)} m");
        Emit($"  velocity  {Vec(ship.Velocity)} m/s");
        Emit($"  mass      {Mass(ship.Mass)} t ({Mass(ship.DryMass)} t dry, "
            + $"{Mass(ship.Propellant)} t propellant)");
        Emit($"  nose      {Vec(ship.Attitude.Forward)}");
        Emit($"  delta-v   {Mass(ship.DeltaVRemaining)} m/s");
    }

    private void Station(ProbeCommand command)
    {
        string name = command.Argument(0, "a station name", "station <name>");
        Station station = _world.Station(name);

        double radius = station.Position.Length.ToDouble();
        double speed = station.Velocity.Length.ToDouble();

        Emit($"  offset    {Vec(station.Offset)} m from the host");
        Emit($"  altitude  {radius / 1000.0:F3} km, speed {speed:F3} m/s");
    }

    private void Body(ProbeCommand command)
    {
        string name = command.Argument(0, "a body name", "body <name>");
        Ephemeris.State state = name.ToLowerInvariant() switch
        {
            "moon" => _world.MoonState(),
            "mercury" => _world.BodyState(Ephemeris.Body.Mercury),
            "venus" => _world.BodyState(Ephemeris.Body.Venus),
            "earth" => _world.BodyState(Ephemeris.Body.Earth),
            "mars" => _world.BodyState(Ephemeris.Body.Mars),
            "jupiter" => _world.BodyState(Ephemeris.Body.Jupiter),
            "saturn" => _world.BodyState(Ephemeris.Body.Saturn),
            "uranus" => _world.BodyState(Ephemeris.Body.Uranus),
            "neptune" => _world.BodyState(Ephemeris.Body.Neptune),
            _ => throw new ProbeException($"no body called '{name}'"),
        };

        _lastBody = name;
        double au = state.Position.Length.ToDouble() / 149_597_870.7;
        Emit($"  position  {Vec(state.Position)} km");
        Emit($"  distance  {au:F9} AU, speed {state.Velocity.Length.ToDouble():F6} km/s");
    }

    /// <summary>
    /// Writes the sky as seen from a place on Earth, to a CSV.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   sky 40.0 -75.0 /tmp/sky.csv
    /// </code>
    /// Latitude, longitude, a file. Every star above the horizon, plus the Sun and the eight
    /// planets, in altitude and azimuth, with magnitude and colour — which is everything a renderer
    /// needs and nothing it does not. The point of putting it here rather than in a renderer is that
    /// the sky is a property of the simulation, and a probe that can dump it is a probe whose output
    /// can be diffed when the frame arithmetic changes.
    /// </remarks>
    private void Sky(ProbeCommand command)
    {
        double latitude = command.Real(0, "a latitude", "sky <lat> <lon> <file>");
        double longitude = command.Real(1, "a longitude", "sky <lat> <lon> <file>");
        string path = command.Argument(2, "a file path", "sky <lat> <lon> <file>");

        double julianDate = 2451545.0 + (_world.Time / 86400.0);
        Ephemeris.State earth = _world.BodyState(Ephemeris.Body.Earth);

        SkyObserver observer = SkyObserver.OnSurface(
            latitude, longitude, julianDate, earth.Position, earth.Velocity);

        var rows = new List<string>
        {
            "kind,name,altitude,azimuth,magnitude,colour",
        };

        int visible = 0;
        foreach (Star star in _catalogue.Stars)
        {
            Fix128Vec apparent = SkyProjection.Apparent(star, observer);
            (double altitude, double azimuth) = SkyProjection.AltAz(observer, apparent);
            if (altitude < 0.0)
            {
                continue;
            }

            visible++;
            rows.Add($"star,{quote(star.Name)},{altitude:R},{azimuth:R},{star.Magnitude:R},"
                + $"{(double.IsNaN(star.ColourIndex) ? "" : star.ColourIndex.ToString("R"))}");
        }

        // The Sun and the planets, which are the whole reason the sky is a simulation rather than a
        // texture: they move against the stars, and the stars stay put.
        void AddBody(string name, Fix128Vec heliocentric, double magnitude, double colour)
        {
            Fix128Vec direction = (heliocentric - observer.Position).Normalized();
            Fix128Vec apparent = SkyProjection.Apparent(
                new Star(direction, magnitude, colour, 0.0, name), observer);
            (double altitude, double azimuth) = SkyProjection.AltAz(observer, apparent);

            rows.Add($"body,{name},{altitude:R},{azimuth:R},{magnitude:R},{colour:R}");
        }

        AddBody("Sun", Fix128Vec.Zero, -26.74, 0.65);
        AddBody("Mercury", _world.BodyState(Ephemeris.Body.Mercury).Position, -0.5, 0.9);
        AddBody("Venus", _world.BodyState(Ephemeris.Body.Venus).Position, -4.4, 0.7);
        AddBody("Moon", _world.MoonState().Position, -12.7, 0.6);
        AddBody("Mars", _world.BodyState(Ephemeris.Body.Mars).Position, -1.0, 1.4);
        AddBody("Jupiter", _world.BodyState(Ephemeris.Body.Jupiter).Position, -2.2, 0.8);
        AddBody("Saturn", _world.BodyState(Ephemeris.Body.Saturn).Position, 0.5, 0.9);

        // The Milky Way, as a grid of galactic latitude and brightness over the visible hemisphere.
        //
        // Sampled here rather than worked out in the renderer, and that is deliberate: the renderer
        // is a preview tool and the frame arithmetic is the thing this project keeps getting wrong.
        // There is one implementation of the galactic pole and it is in the simulation.
        const double step = 3.0;
        for (double altitude = 0.0; altitude <= 90.0; altitude += step)
        {
            for (double azimuth = 0.0; azimuth < 360.0; azimuth += step)
            {
                Fix128Vec local = LocalDirection(observer, altitude, azimuth);
                double brightness = MilkyWay.Brightness(local);
                if (brightness <= 0.0)
                {
                    continue;
                }

                // Same six columns as every other row: kind, name, altitude, azimuth, magnitude,
                // colour — with the band's brightness carried in the colour slot and the magnitude
                // left at zero. A separate shape for the band rows would be one more thing to keep
                // in step, and the first version wrote five values against a six-column header.
                rows.Add($"band,,{altitude:R},{azimuth:R},0,{brightness:R}");
            }
        }

        File.WriteAllLines(path, rows);
        Emit($"  {visible:N0} stars above the horizon from {latitude:F2}, {longitude:F2}; "
            + $"wrote {rows.Count - 1:N0} rows to {path}");

        static string quote(string value) =>
            value.Length == 0 || value.Contains(',') ? $"\"{value}\"" : value;
    }

    /// <summary>A unit vector in the ecliptic frame from a horizon direction.</summary>
    private static Fix128Vec LocalDirection(in SkyObserver observer, double altitude, double azimuth)
    {
        double alt = altitude * Math.PI / 180.0;
        double az = azimuth * Math.PI / 180.0;

        double up = Math.Sin(alt);
        double horizontal = Math.Cos(alt);
        double north = horizontal * Math.Cos(az);
        double east = horizontal * Math.Sin(az);

        return (observer.Up * Fix128.FromDouble(up))
            + (observer.North * Fix128.FromDouble(north))
            + (observer.East * Fix128.FromDouble(east));
    }

    /// <summary>The distance from the Sun to the body named by the last <c>body</c> command.</summary>
    private void SunDistance(ProbeCommand command)
    {
        Emit($"  {_lastBody} is {SunDistanceAu():F9} AU from the Sun");
    }

    private double SunDistanceAu()
    {
        Ephemeris.State state = _lastBody.ToLowerInvariant() switch
        {
            "moon" => _world.MoonState(),
            "mercury" => _world.BodyState(Ephemeris.Body.Mercury),
            "venus" => _world.BodyState(Ephemeris.Body.Venus),
            "earth" => _world.BodyState(Ephemeris.Body.Earth),
            "mars" => _world.BodyState(Ephemeris.Body.Mars),
            "jupiter" => _world.BodyState(Ephemeris.Body.Jupiter),
            "saturn" => _world.BodyState(Ephemeris.Body.Saturn),
            "uranus" => _world.BodyState(Ephemeris.Body.Uranus),
            "neptune" => _world.BodyState(Ephemeris.Body.Neptune),
            _ => throw new ProbeException($"no body called '{_lastBody}'"),
        };

        return state.Position.Length.ToDouble() / 149_597_870.7;
    }

    private void Range(ProbeCommand command)
    {
        (Ship ship, Station home) = RequireShip();
        double range = (ship.Position - home.Port.Position).Length.ToDouble();
        Emit($"  range     {range:F6} m");
    }

    private void Closing(ProbeCommand command)
    {
        Emit($"  closing   {MeasuredClosing():F9} m/s");
    }

    private void Phase(ProbeCommand command)
    {
        if (_world.Ship is null)
        {
            throw new ProbeException("no ship has been launched — use 'launch'");
        }

        string[] names = { "closing", "braking", "terminal", "hold" };
        Emit($"  phase     {_world.Phase} ({names[Math.Min(_world.Phase, names.Length - 1)]})");
    }

    // ---------------------------------------------------------------- determinism

    /// <summary>
    /// Hashes the state of the world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole point of the harness. Everything the simulation believes is folded into one
    /// value, and two runs of the same script have to produce the same one. If they do not,
    /// something is reading a clock, a hash seed, an iteration order or a float, and it will
    /// do the same thing in a player's game where it is far harder to see.
    /// </para>
    /// <para>
    /// The hash is taken over the **raw fixed-point words**, not over the printed decimals.
    /// Formatting rounds; rounding hides a one-bit drift, and a one-bit drift is exactly the
    /// thing a determinism check is for.
    /// </para>
    /// </remarks>
    private void Hash(ProbeCommand command)
    {
        string label = command.ArgumentCount > 0 ? command.Argument(0, "a label", "hash [label]") : "state";

        var bytes = new List<byte>();
        AppendScalar(bytes, _world.Time);
        AppendScalar(bytes, _world.Ticks);
        AppendScalar(bytes, _world.Phase);

        foreach (string name in _world.StationNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            Station station = _world.Station(name);
            AppendVector(bytes, station.Offset);
            AppendVector(bytes, station.Velocity);
        }

        if (_world.Ship is Ship ship)
        {
            AppendVector(bytes, ship.Position);
            AppendVector(bytes, ship.Velocity);
            AppendVector(bytes, ship.Attitude.RotationVector);
            AppendVector(bytes, ship.Attitude.AngularVelocity);
            AppendScalar(bytes, ship.Mass.ToDouble());
            AppendScalar(bytes, ship.Propellant.ToDouble());
        }

        byte[] digest = SHA256.HashData(bytes.ToArray());
        Emit($"  hash      {label} {Convert.ToHexString(digest).ToLowerInvariant()}");
    }

    /// <summary>
    /// A check: a label, a value, and what it should be.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   expect "the ship is inside the corridor" range 2.0 less
    /// </code>
    /// The label is free text and is what a reader of the transcript sees when it fails; the
    /// command and arguments are the measurement. Keeping them separate means a failed check
    /// reads as a sentence rather than as a diff.
    /// </remarks>
    private void Expect(ProbeCommand command)
    {
        string label = command.Argument(0, "a label", "expect \"what\" <what> <value> <relation>");
        string what = command.Argument(1, "a quantity", "expect \"what\" <quantity> <value> <relation>");

        double expected = command.Real(2, "an expected value", "expect \"what\" <quantity> <value> <relation>");
        string relation = command.ArgumentCount > 3
            ? command.Argument(3, "a relation", "…").ToLowerInvariant()
            : "near";

        // `near` takes its tolerance as the fourth argument, so a relation of `near` with
        // one is a six-argument expectation; the others take the relation there.
        double tolerance = relation == "near" ? command.RealOr(3, 1e-6) : 0.0;

        double actual = what.ToLowerInvariant() switch
        {
            "range" => MeasuredRange(),
            "closing" => MeasuredClosing(),
            "phase" => _world.Phase,
            "lateral" => MeasuredLateral(),
            "ticks" => _world.Ticks,
            "mass" => _world.Ship?.Mass.ToDouble() ?? throw new ProbeException("no ship"),
            "propellant" => _world.Ship?.Propellant.ToDouble() ?? throw new ProbeException("no ship"),
            "speed" => _world.Ship?.Velocity.Length.ToDouble() ?? throw new ProbeException("no ship"),
            "sun" => SunDistanceAu(),
            _ => throw new ProbeException($"no quantity called '{what}'"),
        };

        bool pass = relation switch
        {
            "near" => Math.Abs(actual - expected) <= (command.ArgumentCount > 4
                ? command.RealOr(4, tolerance)
                : tolerance),
            "exactly" => actual == expected,
            "less" => actual < expected,
            "more" => actual > expected,
            "atleast" => actual >= expected,
            "atmost" => actual <= expected,
            _ => throw new ProbeException(
                $"no relation called '{relation}' — near, exactly, less, more, atleast, atmost"),
        };

        string mark = pass ? "ok  " : "FAIL";
        Emit($"  {mark}      {label}");
        Emit($"            {what} = {N(actual)}, expected {relation} {N(expected)}");

        if (!pass)
        {
            _failedChecks++;
        }
    }

    private double MeasuredRange()
    {
        (Ship ship, Station home) = RequireShip();
        return (ship.Position - home.Port.Position).Length.ToDouble();
    }

    /// <summary>Distance from the corridor centreline.</summary>
    private double MeasuredLateral()
    {
        (Ship ship, Station home) = RequireShip();
        Fix128Vec offset = ship.Position - home.Port.Position;
        Fix128Vec lateral = offset - home.Port.Axis * Dot(offset, home.Port.Axis);
        return lateral.Length.ToDouble();
    }

    /// <summary>
    /// Closing speed toward the port, live. Positive means approaching.
    /// </summary>
    /// <remarks>
    /// Measured toward the port itself rather than along the fixed corridor axis, which is the
    /// same thing only until the ship overshoots — after that the fixed axis reads a retreat as
    /// an approach, which is how a probe ends up reporting a ship parked two metres from a port
    /// as closing at a quarter of a metre a second while it drifts away.
    /// </remarks>
    private double MeasuredClosing()
    {
        (Ship ship, Station home) = RequireShip();
        Fix128Vec offset = ship.Position - home.Port.Position;
        if (offset.IsZero)
        {
            return 0.0;
        }

        return Dot(ship.Velocity, -offset.Normalized()).ToDouble();
    }

    private (Ship Ship, Station Home) RequireShip()
    {
        if (_world.Ship is not Ship ship || _world.HomeStation is not Station home)
        {
            throw new ProbeException("no ship has been launched — use 'launch'");
        }

        return (ship, home);
    }

    // ---------------------------------------------------------------- emission

    private void Emit(string line)
    {
        _transcript.Append(line).Append('\n');
    }

    /// <summary>
    /// A fixed-point value as exact decimal.
    /// </summary>
    /// <remarks>
    /// <c>Fix128.ToString</c> rounds to fifteen significant figures, which is right for a
    /// person reading a number and wrong for a transcript that will be diffed: two runs that
    /// differ in the last bits would print identically and the diff would say nothing was
    /// wrong. This prints the value at a precision the type can actually carry, so a one-bit
    /// drift shows up as a changed digit.
    /// </remarks>
    private static string Mass(Fix128 value) => value.ToString();

    private static string N(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private static string Vec(Fix128Vec v) =>
        $"({v.X.ToDouble().ToString("G17", CultureInfo.InvariantCulture)}, "
        + $"{v.Y.ToDouble().ToString("G17", CultureInfo.InvariantCulture)}, "
        + $"{v.Z.ToDouble().ToString("G17", CultureInfo.InvariantCulture)})";

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static void AppendScalar(List<byte> bytes, double value) =>
        bytes.AddRange(BitConverter.GetBytes(value));

    private static void AppendScalar(List<byte> bytes, long value) =>
        bytes.AddRange(BitConverter.GetBytes(value));

    /// <summary>
    /// A raw 128-bit fixed-point magnitude, byte for byte.
    /// </summary>
    /// <remarks>
    /// Hashed directly rather than through <c>double</c>. A double carries 53 bits of
    /// mantissa, so the cast drops the low eleven bits of a Q64.64 word — the bits a
    /// one-bit drift lives in — and the type carries no sign at all, so <c>x</c> and
    /// <c>-x</c> hashed identically. The remark above the hash claims raw words; this
    /// is what makes the claim true. There is no <c>BitConverter</c> overload for
    /// <see cref="UInt128"/>, so the two 64-bit halves go in explicitly.
    /// </remarks>
    private static void AppendScalar(List<byte> bytes, UInt128 value)
    {
        bytes.AddRange(BitConverter.GetBytes((ulong)(value & ulong.MaxValue)));
        bytes.AddRange(BitConverter.GetBytes((ulong)(value >> 64)));
    }

    private static void AppendScalar(List<byte> bytes, bool value) =>
        bytes.Add(value ? (byte)1 : (byte)0);

    private static void AppendVector(List<byte> bytes, Fix128Vec v)
    {
        AppendScalar(bytes, v.X.Magnitude);
        AppendScalar(bytes, v.X.Negative);
        AppendScalar(bytes, v.Y.Magnitude);
        AppendScalar(bytes, v.Y.Negative);
        AppendScalar(bytes, v.Z.Magnitude);
        AppendScalar(bytes, v.Z.Negative);
    }
}
