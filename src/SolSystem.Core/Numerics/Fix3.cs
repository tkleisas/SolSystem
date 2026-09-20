namespace SolSystem.Core.Numerics;

/// <summary>
/// A 3-vector of fixed-point megametres, or megametres per second, or megametres
/// per second squared. The unit is the caller's business; the arithmetic is the same.
/// </summary>
/// <remarks>
/// Deliberately not a generic or SIMD type. Hardware vector lanes have their own
/// rounding behaviour, and a simulation whose results depend on which machine ran
/// it has given up the only reason it uses fixed point.
/// </remarks>
internal readonly struct Fix3 : IEquatable<Fix3>
{
    internal readonly Fix64 X;
    internal readonly Fix64 Y;
    internal readonly Fix64 Z;

    internal Fix3(Fix64 x, Fix64 y, Fix64 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal static readonly Fix3 Zero = new(Fix64.Zero, Fix64.Zero, Fix64.Zero);

    public static Fix3 operator +(Fix3 a, Fix3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Fix3 operator -(Fix3 a, Fix3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Fix3 operator -(Fix3 a) => new(-a.X, -a.Y, -a.Z);

    public static Fix3 operator *(Fix3 a, Fix64 s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Fix3 operator *(Fix64 s, Fix3 a) => a * s;

    public Fix64 LengthSquared => X * X + Y * Y + Z * Z;

    internal Fix64 Length => Fix64.Sqrt(LengthSquared);

    /// <summary>Unit vector in the same direction. Throws at the origin.</summary>
    internal Fix3 Normalized()
    {
        Fix64 length = Length;
        if (length == Fix64.Zero)
        {
            throw new InvalidOperationException("Cannot normalise a zero vector.");
        }

        return new Fix3(X / length, Y / length, Z / length);
    }

    public bool Equals(Fix3 other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is Fix3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(Fix3 a, Fix3 b) => a.Equals(b);

    public static bool operator !=(Fix3 a, Fix3 b) => !a.Equals(b);

    internal (double X, double Y, double Z) ToDoubles() => (X.ToDouble(), Y.ToDouble(), Z.ToDouble());

    public override string ToString() => $"({X}, {Y}, {Z})";
}
