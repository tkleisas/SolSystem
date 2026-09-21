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

        // Keep the magnitude inside pi so the axis-angle pair stays unique.
        Fix128 angle = RotationVector.Length;
        if (angle > Pi)
        {
            // Fold back into the unique range, and note that folding is not scaling.
            //
            // A rotation of theta > pi about an axis is the same rotation as 2pi - theta
            // about the OPPOSITE axis. Scaling the vector down to pi keeps the axis and
            // changes the rotation, which is a different thing entirely — and it is a trap
            // that took three attempts to see, because the scaled version looks like the
            // obvious way to "keep it under pi" and every value it produces is in range.
            //
            // What it does in practice is pin a reversing ship at the limit forever. The
            // commanded rotation advances the vector past pi; the fold hauls it back to just
            // under pi; the command is still lit, so the next tick advances it past pi again.
            // The ship sits at exactly half a turn, nose at -x, and never moves, while the
            // pilot waits for an alignment that cannot come.
            //
            // Half a turn is the degenerate case of this: at exactly pi both axes describe
            // the same rotation, so nothing is lost by leaving the axis alone there.
            Fix128 folded = TwoPi - angle;
            RotationVector = RotationVector * (-(folded / angle));
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
