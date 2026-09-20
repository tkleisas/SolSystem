using SolSystem.Core.Numerics;

namespace SolSystem.Core.Orbits;

/// <summary>
/// The state of a point mass: where it is and how fast it is going.
/// </summary>
/// <remarks>
/// Position is in megametres and velocity in megametres per second. Velocity is
/// stored as an absolute rate rather than as a delta-v budget, which is a decision
/// the economy will revisit: §4 of the design prices everything in delta-v, so the
/// ledger will want the budget form. For propagation, the rate is what integrates.
/// </remarks>
internal struct State
{
    internal Fix3 Position;
    internal Fix3 Velocity;

    internal State(Fix3 position, Fix3 velocity)
    {
        Position = position;
        Velocity = velocity;
    }

    /// <summary>Circular orbit in the XY plane, counter-clockwise, starting at +X.</summary>
    internal static State CircularOrbit(Fix3 centre, Fix64 radius, Fix64 speed)
    {
        Fix3 position = new(centre.X + radius, centre.Y, centre.Z);
        Fix3 velocity = new(Fix64.Zero, speed, Fix64.Zero);
        return new State(position, velocity);
    }
}

/// <summary>
/// Velocity Verlet under a single inverse-square attractor.
/// </summary>
/// <remarks>
/// <para>
/// Velocity Verlet rather than explicit Euler. Verlet is symplectic, so it does not
/// bleed orbital energy over a long run; explicit Euler spirals outwards and would
/// make any drift measurement meaningless.
/// </para>
/// <para>
/// Note the exactness property: <c>v += a * dt</c> and <c>p += v * dt</c> are a
/// single multiply and a single add, so the only rounding in a step is one
/// truncation from the multiply and one from the add. The acceleration itself is
/// one multiply, one divide and two adds. Nothing here accumulates a running error
/// the way a sum of many small terms would.
/// </para>
/// </remarks>
internal static class Verlet
{
    /// <summary>Acceleration at <paramref name="position"/> from a point mass at the origin.</summary>
    internal static Fix3 Gravity(Fix3 position, Fix64 gravitationalParameter)
    {
        Fix64 rSquared = position.LengthSquared;
        if (rSquared == Fix64.Zero)
        {
            throw new InvalidOperationException("Gravity is undefined at the position of the attractor.");
        }

        // a = -GM * r / |r|^3, written as -(GM / |r|^2) * r_hat.
        Fix64 inverseRSquared = gravitationalParameter / rSquared;
        Fix64 inverseR = Fix64.Sqrt(inverseRSquared);
        return position * (inverseRSquared * inverseR) * -Fix64.One;
    }

    /// <summary>Advances <paramref name="state"/> by <paramref name="dt"/> seconds, in place.</summary>
    internal static void Step(ref State state, Fix64 gravitationalParameter, Fix64 dt)
    {
        Fix64 halfDt = dt * Fix64.Half;

        Fix3 acceleration = Gravity(state.Position, gravitationalParameter);

        // p += v*dt + a*dt^2/2, with the half-step written as (dt/2)*dt so that only
        // two roundings occur and neither loses significant bits.
        state.Position += state.Velocity * dt + acceleration * (halfDt * dt);

        Fix3 newAcceleration = Gravity(state.Position, gravitationalParameter);

        state.Velocity += (acceleration + newAcceleration) * halfDt;
    }

    /// <summary>Specific orbital energy, GM-independent form: v^2/2 - GM/r.</summary>
    internal static Fix64 SpecificEnergy(State state, Fix64 gravitationalParameter)
    {
        Fix64 vSquared = state.Velocity.LengthSquared;
        Fix64 r = state.Position.Length;
        return vSquared * Fix64.Half - gravitationalParameter / r;
    }
}
