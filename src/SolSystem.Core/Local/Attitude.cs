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

    /// <summary>
    /// Advances the attitude by one tick, composing the commanded rotation properly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rotation is composed, not added, and adding it was wrong everywhere but worst at the
    /// ship's own starting attitude.</b>
    /// </para>
    /// <para>
    /// The old form was <c>RotationVector += ω·dt</c>, justified as exact for small steps. It is
    /// exact to first order in <c>δ = ω·dt</c>, and the error is of order <c>|δ|·|v|</c> — the
    /// Baker–Campbell–Hausdorff commutator term, <c>log(exp(δ)·exp(v)) = δ + v + ½[δ,v] + …</c>.
    /// That error is negligible while the accumulated rotation is small, which is why it survived
    /// the docking tests: a ship on final approach is barely rotating.
    /// </para>
    /// <para>
    /// But a ship that has turned to face its docking port is at <b>exactly π</b>, and π is the
    /// worst place there is. At <c>|v| = π</c> and a full-rate tick of six degrees, the error term
    /// is <c>½ · 0.105 · 3.14 = 0.165</c> radians — <b>nine degrees of error from a six-degree
    /// command</b>, about an axis that has nothing to do with the one asked for. Rolling the ship
    /// about its own nose moved the nose instead.
    /// </para>
    /// <para>
    /// So the two rotations are composed as quaternions — <c>R_new = exp(δ) ∘ R</c>, which is what
    /// a world-frame angular velocity means — and the result is converted back to the axis-angle
    /// form the rest of the engine reads. The conversions are well conditioned everywhere except
    /// the identity, which is handled separately.
    /// </para>
    /// </remarks>
    internal void Step(Fix128 dt)
    {
        if (AngularVelocity.IsZero)
        {
            return;
        }

        Fix128Vec delta = AngularVelocity * dt;

        (Fix128 deltaW, Fix128Vec deltaV) = ToQuaternion(delta);

        if (deltaW == Fix128.One && deltaV.IsZero)
        {
            return;
        }

        (Fix128 rotationW, Fix128Vec rotationV) = ToQuaternion(RotationVector);

        // q_new = q_delta * q_rotation, in that order: the commanded rotation is in world axes, so
        // it applies on the LEFT of the attitude the ship already has.
        Fix128 w = (deltaW * rotationW) - Dot(deltaV, rotationV);
        Fix128Vec v = (rotationV * deltaW) + (deltaV * rotationW) + Cross(deltaV, rotationV);

        RotationVector = ToRotationVector(w, v);
    }

    /// <summary>A unit quaternion for a rotation vector: <c>(cos θ/2, n·sin θ/2)</c>.</summary>
    private static (Fix128 W, Fix128Vec V) ToQuaternion(Fix128Vec rotationVector)
    {
        Fix128 angle = rotationVector.Length;
        if (angle == Fix128.Zero)
        {
            return (Fix128.One, Fix128Vec.Zero);
        }

        Fix128 half = angle * Fix128.FromDouble(0.5);
        Fix128 turns = half / TwoPi;

        return (Trig128.CosTurn(turns), rotationVector * (Trig128.SinTurn(turns) / angle));
    }

    /// <summary>
    /// The shortest rotation vector for a quaternion.
    /// </summary>
    /// <remarks>
    /// <c>q</c> and <c>−q</c> are the same rotation, so the representative with a non-negative real
    /// part is taken first — which is what keeps the magnitude at or below π and the answer unique.
    /// Without it the ship would flip between two axis-angle descriptions of one orientation, and
    /// every interpolation and every comparison would be wrong half the time.
    /// </remarks>
    private static Fix128Vec ToRotationVector(Fix128 w, Fix128Vec v)
    {
        if (w < Fix128.Zero)
        {
            w = -w;
            v = -v;
        }

        Fix128 length = v.Length;
        if (length == Fix128.Zero)
        {
            return Fix128Vec.Zero;
        }

        // For a small angle, sin(θ/2) ≈ θ/2, so θ·v/|v| ≈ 2v. Below this threshold the division
        // and the arctangent both lose more than they give.
        if (length < Fix128.FromDouble(1e-9))
        {
            return v * Fix128.FromDouble(2.0);
        }

        Fix128 angle = Fix128.Atan2(length, w) * Fix128.FromDouble(2.0);
        return v * (angle / length);
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
