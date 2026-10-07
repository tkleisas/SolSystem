using MoonSharp.Interpreter;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;

namespace SolSystem.Client;

/// <summary>
/// The mission layer: a Lua script that watches a flight and talks about it.
/// </summary>
/// <remarks>
/// <para>
/// This is the view-side interpreter of §12.3 — the pilot's telling. It reads the
/// simulation through the vocabulary the probe made (range, lateral offset, closing
/// speed, the latches' own verdict), it may command the throttle exactly as a driver's
/// request does, and it says things through the narrator's channel. It can never touch a
/// tick: the world moves on its own schedule, and the script is a passenger with opinions.
/// </para>
/// <para>
/// The interpreter is MoonSharp's soft sandbox: no <c>os</c>, no <c>io</c> file access, no
/// dynamic <c>load</c>, and no <c>math.random</c> (a script that draws lots is not a
/// deterministic accomplice). Errors disarm the script — a dead script is a quiet one, and
/// its last word stays in the comm log.
/// </para>
/// </remarks>
internal sealed class MissionHost
{
    private readonly Script _script = new(CoreModules.Preset_SoftSandbox);

    /// <summary>The outcome predicates, by name, and whether each has fired.</summary>
    private readonly Dictionary<string, DynValue> _outcomes = new();
    private readonly HashSet<string> _fired = [];

    private readonly Flight _flight;
    private readonly FlightSession _session;
    private readonly Narrator? _narrator;
    private readonly Func<bool> _helmEngaged;
    private readonly Action<string>? _note;

    /// <summary>Set once the script has thrown; every later call becomes a no-op.</summary>
    private bool _disarmed;

    private MissionHost(
        Flight flight,
        FlightSession session,
        Narrator? narrator,
        Func<bool> helmEngaged,
        Action<string>? note)
    {
        _flight = flight;
        _session = session;
        _narrator = narrator;
        _helmEngaged = helmEngaged;
        _note = note;
    }

    /// <summary>
    /// Loads a mission file and gives it the <c>sim</c> and <c>mission</c> vocabularies.
    /// </summary>
    /// <remarks>
    /// Returns null when the file ran and registered itself; the message when it did not.
    /// The file defines <c>mission.on_started()</c> and <c>mission.on_sim(dt)</c> — plain
    /// Lua tables and functions, no userdata crossing, so nothing here needs registration.
    /// </remarks>
    internal static MissionHost? Load(
        string path, Flight flight, FlightSession session, Narrator? narrator,
        Func<bool> helmEngaged, Action<string>? note)
    {
        var host = new MissionHost(flight, session, narrator, helmEngaged, note);

        host.RegisterSim(host._script);
        host.RegisterMission(host._script);
        host.RemoveLottery();

        try
        {
            string source = File.ReadAllText(path);
            host._script.DoString(source);

            DynValue mission = host._script.Globals.Get("mission");
            if (mission.Type != DataType.Table
                || mission.Table.Get("on_sim").Type != DataType.Function)
            {
                return Fail("the mission must define mission.on_sim(dt)");
            }

            try
            {
                host.Call(mission.Table.Get("on_started"));
            }
            catch (Exception exception) when (exception is ScriptRuntimeException or SyntaxErrorException)
            {
                return Fail($"the mission's opening failed: {exception.Message}");
            }

            Console.WriteLine($"  mission loaded: {Path.GetFileName(path)}");
            return host;
        }
        catch (Exception exception) when (exception is ScriptRuntimeException or SyntaxErrorException or IOException)
        {
            return Fail(exception.Message);
        }

        static MissionHost? Fail(string message)
        {
            Console.WriteLine($"  mission refused: {message}");
            return null;
        }
    }

    private void RegisterSim(Script script)
    {
        var sim = new Table(script);

        // The docking report's numbers, in doubles, positive-open approach: the same
        // vocabulary the probe prints, per §12.3 the mission shares the probe's language.
        sim["range"] = (Func<double>)(() => Report().Range.ToDouble());
        sim["lateral"] = (Func<double>)(() => Report().LateralOffset.ToDouble());
        sim["closing"] = (Func<double>)(() => Report().ClosingSpeed.ToDouble());
        sim["docked"] = (Func<bool>)(() => Report().Docked);
        sim["in_contact"] = (Func<bool>)(() => Report().Contact);
        sim["docking_reason"] = (Func<string>)(() => Report().Reason);

        sim["throttle"] = (Func<double>)(() => _flight.Throttle);
        sim["set_throttle"] = new Action<double>(fraction => _flight.SetThrottle(fraction));
        sim["helm_engaged"] = (Func<bool>)(() => _helmEngaged());
        sim["propellant_t"] = (Func<double>)(() => _flight.Propellant);
        sim["delta_v"] = (Func<double>)(() => _flight.DeltaV);
        sim["speed"] = (Func<double>)(() => _flight.SpeedRelativeTo(_session.Station.Velocity));

        script.Globals["sim"] = sim;
    }

    /// <summary>
    /// Takes the draw out of the interpreter: a mission is not allowed to act at random.
    /// </summary>
    private void RemoveLottery()
    {
        DynValue math = _script.Globals.Get("math");
        if (math.Type == DataType.Table)
        {
            math.Table.Set("random", DynValue.Nil);
        }
    }

    private void RegisterMission(Script script)
    {
        var mission = new Table(script);

        mission["outcome"] = new Action<string, DynValue>((name, predicate) =>
        {
            if (predicate.Type != DataType.Function)
            {
                throw new ScriptRuntimeException("an outcome needs a predicate function");
            }

            _outcomes[name] = predicate;
        });

        // What the voice says, and what only the log shows. A line of speech lands in the
        // comm log once — the narrator's LineSpoken carries it — so a spoken line is not
        // also pre-written here; and a session without a voice still shows its text.
        mission["say"] = new Action<string>(text =>
        {
            if (_narrator is not null)
            {
                _narrator.Say(text);
            }
            else
            {
                _note?.Invoke(text);
            }
        });

        mission["note"] = new Action<string>(text =>
        {
            _note?.Invoke(text);
            Console.WriteLine($"  mission: {text}");
        });

        script.Globals["mission"] = mission;
    }

    private void ReportOutcome(string name)
    {
        _note?.Invoke($"outcome met: {name}");
        _narrator?.Say($"outcome met: {name.Replace('_', ' ')}");
        Console.WriteLine($"  outcome met: {name}");
    }

    private DockingReport Report()
    {
        Ship ship = _flight.Ship;
        return Docking.Evaluate(ship, _session.Station.Port, _session.Station.Velocity);
    }

    /// <summary>
    /// The world advanced; the mission may speak.
    /// </summary>
    /// <remarks>
    /// Outcomes are evaluated first, so a run that ended this frame declares it before the
    /// commentary hook runs. Each outcome fires once; sim seconds are the script's own
    /// business. A disarmed script does nothing.
    /// </remarks>
    internal void Advanced(double seconds)
    {
        if (_disarmed || seconds <= 0.0)
        {
            return;
        }

        foreach ((string name, DynValue predicate) in _outcomes)
        {
            if (_fired.Contains(name))
            {
                continue;
            }

            try
            {
                if (_script.Call(predicate).Boolean)
                {
                    _fired.Add(name);
                    ReportOutcome(name);
                }
            }
            catch (Exception exception) when (exception is ScriptRuntimeException or SyntaxErrorException)
            {
                Disarm($"outcome '{name}' threw: {exception.Message}");
                return;
            }
        }

        DynValue onSim = _script.Globals.Get("mission").Table?.Get("on_sim") ?? DynValue.Nil;
        if (onSim.Type != DataType.Function)
        {
            return;
        }

        try
        {
            _script.Call(onSim, seconds);
        }
        catch (Exception exception) when (exception is ScriptRuntimeException or SyntaxErrorException)
        {
            Disarm($"on_sim threw: {exception.Message}");
        }
    }

    private void Disarm(string reason)
    {
        _disarmed = true;
        _note?.Invoke($"the mission script has fallen silent: {reason}");
        Console.WriteLine($"  mission disarmed: {reason}");
    }

    private void Call(DynValue function)
    {
        if (function.Type == DataType.Function)
        {
            _script.Call(function);
        }
    }
}
