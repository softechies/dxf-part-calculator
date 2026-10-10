namespace DxfPartCalculator.Geometry;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double DistanceTo(Vec2 other) => (this - other).Length;
    public Vec2 Perpendicular => new(-Y, X); // rotated 90 degrees counter clockwise

    public static Vec2 FromAngle(double radians, double length = 1) =>
        new(Math.Cos(radians) * length, Math.Sin(radians) * length);
}

/// <summary>2D affine transform: p' = M * p + T.</summary>
public readonly record struct Transform2D(double A, double B, double C, double D, double Tx, double Ty)
{
    public static readonly Transform2D Identity = new(1, 0, 0, 1, 0, 0);

    public static Transform2D Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    public static Transform2D Rotation(double radians)
    {
        var (sin, cos) = Math.SinCos(radians);
        return new(cos, -sin, sin, cos, 0, 0);
    }

    public static Transform2D Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Mirror across the Y axis: what an OCS extrusion of (0,0,-1) does to X.</summary>
    public static readonly Transform2D MirrorX = new(-1, 0, 0, 1, 0, 0);

    public Vec2 Apply(Vec2 p) => new(A * p.X + B * p.Y + Tx, C * p.X + D * p.Y + Ty);

    /// <summary>Applies this transform first, then <paramref name="outer"/>.</summary>
    public Transform2D Then(Transform2D outer) => new(
        outer.A * A + outer.B * C,
        outer.A * B + outer.B * D,
        outer.C * A + outer.D * C,
        outer.C * B + outer.D * D,
        outer.A * Tx + outer.B * Ty + outer.Tx,
        outer.C * Tx + outer.D * Ty + outer.Ty);

    /// <summary>True when the transform keeps circles as circles (rotation, mirror, uniform scale).</summary>
    public bool IsConformal
    {
        get
        {
            var col1 = A * A + C * C;
            var col2 = B * B + D * D;
            var dot = A * B + C * D;
            return Math.Abs(col1 - col2) <= 1e-9 * Math.Max(1, col1) && Math.Abs(dot) <= 1e-9 * Math.Max(1, col1);
        }
    }

    /// <summary>Length scale factor, valid when <see cref="IsConformal"/>.</summary>
    public double UniformScale => Math.Sqrt(Math.Abs(A * D - B * C));
}

/// <summary>A flattened piece of geometry from one entity, with its true length.</summary>
public sealed class GeoPath
{
    public required List<Vec2> Points { get; init; }
    public required double Length { get; init; }
    public bool Closed { get; init; }
    public string Layer { get; init; } = "0";
    public string Source { get; init; } = "";

    public Vec2 Start => Points[0];
    public Vec2 End => Points[^1];
}

public static class PolygonMath
{
    /// <summary>Signed area by the shoelace formula, positive for counter clockwise.</summary>
    public static double SignedArea(IReadOnlyList<Vec2> points)
    {
        double sum = 0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            sum += (points[j].X * points[i].Y) - (points[i].X * points[j].Y);
        }
        return sum / 2;
    }

    /// <summary>Ray casting point in polygon test.</summary>
    public static bool Contains(IReadOnlyList<Vec2> polygon, Vec2 p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    public static double PolylineLength(IReadOnlyList<Vec2> points)
    {
        double length = 0;
        for (var i = 1; i < points.Count; i++) length += points[i - 1].DistanceTo(points[i]);
        return length;
    }
}

public readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly Bounds Empty = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX;
    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;

    public Bounds Include(Vec2 p) => new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public static Bounds Of(IEnumerable<Vec2> points) => points.Aggregate(Empty, (b, p) => b.Include(p));
}
