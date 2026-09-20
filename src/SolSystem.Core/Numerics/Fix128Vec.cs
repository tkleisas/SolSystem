namespace SolSystem.Core.Numerics;

/// <summary>
/// A 3-vector of Q64.64 fixed-point kilometres, or kilometres per second, or
/// kilometres per second squared, according to context.
/// </summary>
/// <remarks>
/// The solar frame's vector type. <see cref="Fix3"/> stays the local frame's, where the
/// magnitudes are small enough for a Q32.32 value and the arithmetic is narrower.
/// </remarks>
internal readonly struct Fix128Vec
{
    internal readonly Fix128 X;
    internal readonly Fix128 Y;
    internal readonly Fix128 Z;

    internal Fix128Vec(Fix128 x, Fix128 y, Fix128 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal static readonly Fix128Vec Zero = new(Fix128.Zero, Fix128.Zero, Fix128.Zero);

    public static Fix128Vec operator +(Fix128Vec a, Fix128Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Fix128Vec operator -(Fix128Vec a, Fix128Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Fix128Vec operator -(Fix128Vec a) => new(-a.X, -a.Y, -a.Z);

    public static Fix128Vec operator *(Fix128Vec a, Fix128 s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Fix128Vec operator *(Fix128 s, Fix128Vec a) => a * s;

    internal Fix128 LengthSquared => X * X + Y * Y + Z * Z;

    internal Fix128 Length => Fix128.Sqrt(LengthSquared);

    /// <summary>Unit vector in the same direction.</summary>
    internal Fix128Vec Normalized()
    {
        Fix128 length = Length;
        if (length == Fix128.Zero)
        {
            throw new InvalidOperationException("Cannot normalise a zero vector.");
        }

        return new Fix128Vec(X / length, Y / length, Z / length);
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
}
