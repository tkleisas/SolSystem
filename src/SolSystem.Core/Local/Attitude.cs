using SolSystem.Core.Numerics;

namespace SolSystem.Core.Local;

/// <summary>
/// Orientation and rotation, as a rotation vector with an angular velocity.
/// </summary>
/// <remarks>
/// <para>
/// A ship's main engine pushes along its nose, so a ship that cannot turn cannot brake,
/// cannot correct laterally, and cannot dock. Attitude is not decoration here — it is the
/// whole reason docking is a skill rather than a steering problem.
/// </para>
/// <para>
/// A rotation vector — an axis scaled by an angle, in radians — is used rather than a
/// quaternion because it composes with a single small-angle addition per tick. Applying it
/// uses Rodrigues' formula, which needs only a square root and no transcendentals:
/// </para>
/// <code>
///   v' = v·cosθ + (k × v)·sinθ + k·(k·v)·(1 - cosθ)
/// </code>
/// <para>
/// That matters because this runs at 120 Hz on every ship. A quaternion would need
/// renormalisation and a sine of its own.
/// </para>
/// </remarks>
internal struct Attitude
{
    /// <summary>
    /// A hair, for the fold in <see cref="Step"/>. Large enough to survive the fixed-point
    /// representation and small enough to be unobservable: a millionth of a radian is a
    /// twentieth of an arc-second.
    /// </summary>
    private static readonly Fix128 Epsilon = Fix128.FromDouble(1.0 / 1_048_576.0);

    /// <summary>Axis scaled by angle in radians. Zero is "pointing along +x".</summary>
    internal Fix128Vec RotationVector;

    /// <summary>Angular velocity, radians per second, about each axis.</summary>
    internal Fix128Vec AngularVelocity;

    internal Attitude(Fix128Vec rotationVector, Fix128Vec angularVelocity)
    {
        RotationVector = rotationVector;
        AngularVelocity = angularVelocity;
    }

    /// <summary>The nose direction: +x rotated by the current attitude.</summary>
    internal readonly Fix128Vec Forward => Rotate(new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero));

    /// <summary>
    /// The largest turn rate a crewed hull may use, in radians per second.
    /// </summary>
    /// <remarks>
    /// About 6 degrees per second. Faster is possible and costs nothing in energy — the
    /// moment of inertia of a hundred-tonne hull is small — but a crew cannot work through
    /// a faster tumble, and a docking approach needs a pilot who can see what is happening.
    /// </remarks>
    internal static readonly Fix128 CrewedMaxTurnRate = Fix128.FromDouble(6.0 * Math.PI / 180.0);

    /// <summary>Turns a desired angular velocity into what the hull will actually accept.</summary>
    internal static Fix128Vec ClampAngularVelocity(Fix128Vec commanded, Fix128 maxRate)
    {
        Fix128 speed = commanded.Length;
        if (speed <= maxRate || speed == Fix128.Zero)
        {
            return commanded;
        }

        return commanded * (maxRate / speed);
    }

    /// <summary>Advances the attitude by one tick.</summary>
    internal void Step(Fix128 dt)
    {
        if (AngularVelocity.IsZero)
        {
            return;
        }

        // The rotation vector advances by ω·dt, and because it is a vector along the
        // rotation axis its magnitude is the angle, the addition is exact for small steps.
        RotationVector += AngularVelocity * dt;

        // Keep the magnitude below pi so the axis-angle pair stays unique. Past a half turn
        // the same rotation has two representations and the axis would flip.
        //
        // The fold has to land STRICTLY INSIDE the limit, and that is a real case rather
        // than a boundary curiosity. A ship told to reverse reaches exactly pi with the
        // rotation still commanded the same way; the next step wants 2pi; a fold that maps
        // that to pi puts the ship straight back where it was, and it tumbles on the spot
        // forever while the pilot waits for an alignment that never comes. Folding to a hair
        // under the limit instead leaves room for the next step to make progress, and the
        // turn completes.
        //
        // Two versions of this were wrong before that was clear. Folding only when the angle
        // exceeded the limit made the equality case terminal. Folding when it reached the
        // limit made every case that hit the limit terminal, because the fold's own output
        // satisfies the condition that triggered it.
        Fix128 angle = RotationVector.Length;
        Fix128 limit = Pi * (Fix128.One - Epsilon);
        if (angle >= limit)
        {
            RotationVector = RotationVector * (limit / angle);
        }
    }

    /// <summary>Rotates a vector by this attitude, using Rodrigues' formula.</summary>
    internal readonly Fix128Vec Rotate(Fix128Vec v)
    {
        Fix128 angleSquared = RotationVector.LengthSquared;
        if (angleSquared == Fix128.Zero)
        {
            return v;
        }

        Fix128 angle = Fix128.Sqrt(angleSquared);

        // cos and sin from the turn-based trigonometry, so the whole engine stays in fixed
        // point. angle/(2pi) converts radians to turns.
        Fix128 turns = angle / TwoPi;
        Fix128 cosAngle = Trig128.CosTurn(turns);
        Fix128 sinAngle = Trig128.SinTurn(turns);

        Fix128Vec axis = RotationVector * (Fix128.One / angle);
        Fix128Vec axisCrossV = Cross(axis, v);
        Fix128 axisDotV = Dot(axis, v);
        Fix128 oneMinusCos = Fix128.One - cosAngle;

        Fix128Vec rotated = v * cosAngle + axisCrossV * sinAngle + axis * (axisDotV * oneMinusCos);

        // Renormalise. Rodrigues preserves length only if sin²+cos² is exactly one, and the
        // turn-based trigonometry carries about 1.2e-8 of interpolation error, so the raw
        // result scales by 1 - 1.6e-8. That is a small error and a real one: a rotation that
        // shrinks its input will slowly shrink a nose vector, and a direction that is used
        // over and over drifts. Scaling back costs one square root.
        Fix128 length = v.Length;
        Fix128 rotatedLength = rotated.Length;
        if (length == Fix128.Zero || rotatedLength == Fix128.Zero)
        {
            return rotated;
        }

        return rotated * (length / rotatedLength);
    }

    private static readonly Fix128 Pi = Fix128.FromDouble(Math.PI);
    private static readonly Fix128 TwoPi = Fix128.FromDouble(2.0 * Math.PI);

    private static Fix128Vec Cross(Fix128Vec a, Fix128Vec b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
