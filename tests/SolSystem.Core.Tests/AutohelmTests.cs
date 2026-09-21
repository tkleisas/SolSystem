using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// The flight computer, flown against a target a long way away.
/// </summary>
/// <remarks>
/// This is the test that decides whether the autopilot works, and it is a ballistic problem rather
/// than an orbital one: the ship crosses tens of millions of kilometres under thrust while the Sun
/// pulls at a few millimetres per second squared. What it must not do is arrive at speed, and the
/// whole difficulty is that the reversal takes half a minute during which the engine is pointing the
/// wrong way.
/// </remarks>
public class AutohelmTests
{
    private readonly ITestOutputHelper _o;

    public AutohelmTests(ITestOutputHelper o) => _o = o;

    private static Fix128 F(double v) => Fix128.FromDouble(v);

    /// <summary>A courier: 5.5 kN on 140 tonnes, so four milligee.</summary>
    private static Ship Courier(Fix128Vec position, Fix128Vec velocity) => new(
        position,
        velocity,
        F(100.0),
        F(40.0),
        Engine.Crewed(F(5.5), Engine.CrewedSpecificImpulse),
        new Attitude(Fix128Vec.Zero, Fix128Vec.Zero));

    [Fact]
    public void TheReversal_HasToBePaidForInDistance()
    {
        // The figure that governs every torch crossing. At a peak of 50 km/s a thirty-second
        // reversal is 1 500 km of coasting, on top of the 32 000 km the brake itself needs.
        Fix128 turn = Autohelm.ReversalSeconds;

        Assert.Equal(30.0, turn.ToDouble(), 1);

        Fix128 atFifty = Autohelm.BrakingDistance(F(50_000.0), F(0.0392), turn);
        Fix128 atFive = Autohelm.BrakingDistance(F(5_000.0), F(0.0392), turn);

        _o.WriteLine($"from 50 km/s: {atFifty.ToDouble() / 1000.0:N0} km to stop "
            + $"({50_000.0 * 30.0 / 1000.0:N0} km of it coasting)");
        _o.WriteLine($"from  5 km/s: {atFive.ToDouble() / 1000.0:N0} km to stop");

        // From fifty kilometres a second that is thirty-two MILLION kilometres — two tenths of an
        // astronomical unit — and the thirty-second turn adds fifteen hundred more. The scale is the
        // point: a torch ship at speed needs a fifth of the way to Mars just to stop.
        Assert.InRange(atFifty.ToDouble(), 3.1e10, 3.3e10);
        Assert.InRange(atFive.ToDouble(), 3.1e8, 3.3e8);
    }

    [Fact]
    public void ToTheMoon_UnderAutohelm_ArrivesSlowly()
    {
        // A crossing of 384 400 km at four milligee, ignoring the Moon's own gravity: roughly
        // 2*sqrt(3.844e8/0.0392) = 198 000 s, about 2.3 days, with a peak near 3.9 km/s.
        Fix128Vec start = Fix128Vec.Zero;
        Fix128Vec target = new(F(384_400_000.0), Fix128.Zero, Fix128.Zero);

        Ship ship = Courier(start, Fix128Vec.Zero);
        Autohelm helm = Autohelm.To(target, F(50_000.0), Fix128.One);

        Span<GravitySource> none = [];

        var closest = double.MaxValue;
        Autohelm.Phase previous = helm.Stage;
        double arrivalSeconds = 0.0;
        Fix128 finalSpeed = Fix128.Zero;

        // One-second ticks. The crossing is two and a half days; at sixty ticks a second this loop
        // would be twelve million iterations, and it would also have been six days at 1/60 s, which
        // is 31 million. The first version ran 518 400 of them and reported a ship that had covered
        // fifteen hundred kilometres — because it had, in the two and a half hours it was actually
        // given.
        const double Dt = 1.0;

        for (int tick = 0; tick < 400_000; tick++)
        {
            helm.Step(ref ship, none, F(Dt));

            double range = (target - ship.Position).Length.ToDouble();
            if (range < closest)
            {
                closest = range;
            }

            if (helm.Stage != previous)
            {
                _o.WriteLine($"  t={tick * Dt,7:F0} s  {helm.Stage,-12} "
                    + $"range {range / 1000.0,10:N0} km  speed {ship.Velocity.Length.ToDouble(),8:F0} m/s");
                previous = helm.Stage;
            }

            if (helm.Stage == Autohelm.Phase.Arrived)
            {
                arrivalSeconds = tick * Dt;
                finalSpeed = ship.Velocity.Length;
                break;
            }
        }

        _o.WriteLine($"phase {helm.Describe()}, closest {closest / 1000.0:N1} km, "
            + $"arrived at {arrivalSeconds / 86400.0:F2} days, "
            + $"closing {finalSpeed.ToDouble():F0} m/s, "
            + $"propellant {ship.Propellant.ToDouble():F2} t");

        Assert.Equal(Autohelm.Phase.Arrived, helm.Stage);

        // The whole point: it must not arrive at speed.
        Assert.True(finalSpeed.ToDouble() < 200.0,
            $"arrived at {finalSpeed.ToDouble():F0} m/s, which is not an arrival");
    }
}
