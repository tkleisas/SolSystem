using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;

namespace SolSystem.Client;

/// <summary>
/// The ship the player is flying, and the controls that fly it.
/// </summary>
/// <remarks>
/// <para>
/// This is the phase 0 gate made tangible: <i>if flying this ship is not fun with nothing else
/// attached, nothing else rescues it</i>. Everything here is either a key or a view of what the
/// simulation already computes — there is no second physics model, and the engine, the mass and the
/// propellant are the same ones the docking tests fly.
/// </para>
/// <para>
/// <b>The controls are the real constraint, not a convention.</b> The helm is rate-limited to six
/// degrees a second because that is what a crewed hull can do, and the engine fires along the nose
/// because it is bolted to the back of it. So a player who wants to slow down has to turn round
/// first, and that takes thirty seconds and a kilometre of corridor. Any control scheme that hid
/// that would be hiding the game.
/// </para>
/// </remarks>
internal sealed class Flight
{
    /// <summary>
    /// How much of the tank a fresh hull starts with.
    /// </summary>
    /// <remarks>
    /// Not full. A hull that starts full has no reason to think about propellant until the moment it
    /// runs out, and the whole point of the resource is that it is visible from the first frame.
    /// </remarks>
    private const double StartingPropellantTonnes = 40.0;

    private const double DryMassTonnes = 100.0;

    /// <summary>Throttle change per second while a key is held, as a fraction of full thrust.</summary>
    private const double ThrottleRate = 0.8;

    /// <summary>Turn command, as a fraction of the crewed maximum rate.</summary>
    private const double TurnCommand = 1.0;

    private Ship _ship;
    private double _throttle;

    internal Flight(Ship ship)
    {
        _ship = ship;
    }

    /// <summary>The hull being flown.</summary>
    internal ref Ship Ship => ref _ship;

    /// <summary>The throttle the pilot has set, 0 to 1.</summary>
    internal double Throttle => _throttle;

    /// <summary>
    /// A crewed hull with a torch, sized to the freighter that was modelled.
    /// </summary>
    /// <remarks>
    /// The thrust is chosen so that the acceleration is the crewed steady figure of four milligee,
    /// which is what the radiator can reject rather than what the engine could produce. A hundred
    /// and forty tonnes at four milligee is 5.5 kilonewtons, and the engine is quoted at the thrust
    /// that delivers it.
    /// </remarks>
    internal static Flight Start(Fix128Vec position, Fix128Vec velocity, Attitude attitude)
    {
        Fix128 mass = Fix128.FromDouble(DryMassTonnes + StartingPropellantTonnes);

        var engine = Engine.Crewed(
            Fix128.FromDouble(5.5),
            Engine.CrewedSpecificImpulse);

        return new Flight(new Ship(
            position,
            velocity,
            Fix128.FromDouble(DryMassTonnes),
            Fix128.FromDouble(StartingPropellantTonnes),
            engine,
            attitude));
    }

    /// <summary>
    /// Reads the controls and returns this tick's command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scheme is a flying one rather than a driving one, because a spacecraft has no road:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>W</c> and <c>S</c> — throttle up and down. The engine burns along the nose.</description></item>
    /// <item><description><c>A</c> and <c>D</c> — yaw. Turn the nose and the thrust comes with it.</description></item>
    /// <item><description><c>Q</c> and <c>E</c> — roll.</description></item>
    /// <item><description><c>R</c> and <c>F</c> — pitch, which is how a hull changes its orbital plane.</description></item>
    /// <item><description><c>X</c> — kill the throttle. <c>Z</c> — full.</description></item>
    /// </list>
    /// <para>
    /// Throttle is a rate rather than a level, so a tap is a nudge and a hold is a burn. A ship that
    /// jumped straight to full on a keypress would make the docking corridor unflyable by hand, and
    /// the corridor is the thing this ship exists to fly.
    /// </para>
    /// </remarks>
    internal Command Read(KeyboardState keys, double seconds)
    {
        if (keys.IsKeyDown(Keys.W))
        {
            _throttle = Math.Min(1.0, _throttle + (ThrottleRate * seconds));
        }

        if (keys.IsKeyDown(Keys.S))
        {
            _throttle = Math.Max(0.0, _throttle - (ThrottleRate * seconds));
        }

        if (keys.IsKeyDown(Keys.X))
        {
            _throttle = 0.0;
        }

        if (keys.IsKeyDown(Keys.Z))
        {
            _throttle = 1.0;
        }

        // The helm. Each axis is commanded as a fraction of the crewed maximum, and the ship clamps
        // it — so a player who holds a key turns at exactly six degrees a second and no faster.
        double yaw = Axis(keys, Keys.D, Keys.A);
        double pitch = Axis(keys, Keys.R, Keys.F);
        double roll = Axis(keys, Keys.E, Keys.Q);

        var turn = new Fix128Vec(
            Fix128.Zero,
            Fix128.Zero,
            Fix128.FromDouble(roll * TurnCommand));

        // Yaw and pitch are rotations about the ship's own up and right, which is why they are
        // composed here rather than written into the rotation vector directly.
        // The hull's own axes. The nose is +x in the ship's frame and the deck is +z, which is the
        // convention the model was built in — so a turn command is a rotation about one of those two
        // rather than about a world axis, and the ship rolls as it turns the way a ship does.
        Fix128Vec nose = _ship.Attitude.Forward;
        Fix128Vec up = _ship.Attitude.Rotate(new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One));

        Fix128Vec lateral = FlightSession.Cross(up, nose).Normalized();

        turn += up * Fix128.FromDouble(yaw * TurnCommand);
        turn += lateral * Fix128.FromDouble(-pitch * TurnCommand);

        var thrust = _throttle > 0.0 ? nose : Fix128Vec.Zero;

        return new Command(thrust, Fix128.FromDouble(_throttle), turn);
    }

    private static double Axis(KeyboardState keys, Keys positive, Keys negative)
    {
        double value = 0.0;

        if (keys.IsKeyDown(positive))
        {
            value += 1.0;
        }

        if (keys.IsKeyDown(negative))
        {
            value -= 1.0;
        }

        return value;
    }

    /// <summary>
    /// Advances the ship one tick under whatever gravity is acting on it.
    /// </summary>
    internal void Step(ReadOnlySpan<GravitySource> sources, double seconds, in Command command)
    {
        Fix128 dt = Fix128.FromDouble(seconds);
        _ship.Step(sources, dt, command);
    }

    /// <summary>Speed relative to a frame, in metres per second.</summary>
    internal double SpeedRelativeTo(Fix128Vec velocity) =>
        (_ship.Velocity - velocity).Length.ToDouble();

    /// <summary>Delta-v still in the tanks, in metres per second.</summary>
    internal double DeltaV => _ship.DeltaVRemaining.ToDouble();

    /// <summary>Propellant remaining, in tonnes.</summary>
    internal double Propellant => _ship.Propellant.ToDouble();

    /// <summary>Propellant as a fraction of the ship's mass.</summary>
    internal double PropellantFraction => _ship.PropellantFraction.ToDouble();

    /// <summary>
    /// How long the ship can burn at full throttle on what is left.
    /// </summary>
    /// <remarks>
    /// The number that turns delta-v from an abstraction into a decision. Ten kilometres a second is
    /// a comfortable figure until it is four hours of burn.
    /// </remarks>
    internal double FullThrottleSeconds
    {
        get
        {
            double flow = _ship.Engine.MassFlowTonnesPerSecond.ToDouble();
            return flow <= 0.0 ? 0.0 : Propellant / flow;
        }
    }
}
