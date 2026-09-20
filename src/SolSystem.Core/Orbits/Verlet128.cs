using SolSystem.Core.Numerics;

namespace SolSystem.Core.Orbits;

/// <summary>
/// State in the solar frame: position in kilometres, velocity in kilometres per second.
/// </summary>
internal struct SolarState
{
    internal Fix128Vec Position;
    internal Fix128Vec Velocity;

    internal SolarState(Fix128Vec position, Fix128Vec velocity)
    {
        Position = position;
        Velocity = velocity;
    }
}

/// <summary>
/// Velocity Verlet under a single inverse-square attractor, in Q64.64 kilometres.
/// </summary>
/// <remarks>
/// <para>
/// The solar frame's integrator. Gravity needs <c>r²</c>, and for any heliocentric orbit
/// that overflows a Q32.32 value nine orders of magnitude over: <c>r</c> alone reaches
/// 1.4 × 10⁹ km at Saturn, so <c>r²</c> is 2 × 10¹⁸. That is the entire reason
/// <see cref="Fix128"/> exists.
/// </para>
/// <para>
/// Velocity Verlet rather than explicit Euler: Verlet is symplectic, so it does not bleed
/// orbital energy over a long run. The step is <c>a·dt²/2</c> written as
/// <c>a·((dt/2)·dt)</c> so only two roundings occur.
/// </para>
/// </remarks>
internal static class Verlet128
{
    /// <summary>Acceleration at <paramref name="position"/> from a point mass at the origin.</summary>
    /// <remarks>
    /// <para>
    /// <c>a = -GM · r / |r|³</c>, arranged as <c>-(GM / (|r|² · |r|)) · r</c> so that the
    /// scalar is <c>GM/|r|³</c> and multiplying by the position vector gives the right
    /// units. Two ways of writing this are wrong in ways that survive casual testing:
    /// </para>
    /// <list type="bullet">
    /// <item><c>(GM/|r|²) · r</c> is larger by a factor of <c>|r|</c> — 1.5e8 at 1 AU,
    /// which is how this first came out at 2.16 km/s² instead of 5.9e-6.</item>
    /// <item>Dividing by <c>|r|²</c> twice underflows: <c>GM/|r|²</c> is already 5.9e-15
    /// and its quotient with <c>|r|²</c> is 2.7e-31, below the 2^-64 grid, so the
    /// acceleration comes back as exactly zero. Dividing by <c>1/|r|</c> instead keeps
    /// the scale intact.</item>
    /// </list>
    /// </remarks>
    internal static Fix128Vec Gravity(Fix128Vec position, Fix128 gravitationalParameter)
    {
        Fix128 rSquared = position.LengthSquared;
        if (rSquared == Fix128.Zero)
        {
            throw new InvalidOperationException("Gravity is undefined at the position of the attractor.");
        }

        Fix128 r = Fix128.Sqrt(rSquared);
        if (r == Fix128.Zero)
        {
            throw new InvalidOperationException("Gravity is undefined at the position of the attractor.");
        }

        // GM / |r|^2 / |r| is GM/|r|^3, which multiplies the position vector to give
        // GM/|r|^2 in the direction of -r. Dividing by |r|^2 and then by |r| rather than
        // forming |r|^3 keeps every intermediate inside the type: |r|^3 overflows
        // Q64.64 at about 1 AU.
        Fix128 scale = gravitationalParameter / rSquared / r;
        return position * (scale * -Fix128.One);
    }

    /// <summary>Advances <paramref name="state"/> by <paramref name="dt"/> seconds, in place.</summary>
    internal static void Step(ref SolarState state, Fix128 gravitationalParameter, Fix128 dt)
    {
        Fix128 halfDt = dt * Fix128.FromDouble(0.5);

        Fix128Vec acceleration = Gravity(state.Position, gravitationalParameter);

        state.Position += state.Velocity * dt + acceleration * (halfDt * dt);

        Fix128Vec newAcceleration = Gravity(state.Position, gravitationalParameter);

        state.Velocity += (acceleration + newAcceleration) * halfDt;
    }

    /// <summary>Specific orbital energy: v²/2 - GM/r.</summary>
    internal static Fix128 SpecificEnergy(Fix128Vec position, Fix128Vec velocity, Fix128 gm)
    {
        Fix128 r = position.Length;
        return velocity.LengthSquared * Fix128.Half - gm / r;
    }


    internal static Fix128 SpecificEnergy(SolarState state, Fix128 gravitationalParameter)
    {
        Fix128 vSquared = state.Velocity.LengthSquared;
        Fix128 r = state.Position.Length;
        return vSquared * Fix128.FromDouble(0.5) - gravitationalParameter / r;
    }
}
