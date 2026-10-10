namespace DxfPartCalculator.Geometry;

/// <summary>Turns curves into point lists within a chord tolerance and gives their exact lengths.</summary>
public static class Curves
{
    private const int MaxSegments = 4096;

    /// <summary>Number of segments so the chord never strays more than tolerance from the true arc.</summary>
    public static int ArcSegments(double radius, double sweepRadians, double tolerance)
    {
        sweepRadians = Math.Abs(sweepRadians);
        if (radius <= 0 || sweepRadians == 0) return 1;

        var step = tolerance >= radius ? Math.PI / 2 : 2 * Math.Acos(1 - tolerance / radius);
        step = Math.Min(step, Math.PI / 8); // keep at least 16 segments per full circle for good area accuracy
        return Math.Clamp((int)Math.Ceiling(sweepRadians / step), 1, MaxSegments);
    }

    /// <summary>Points along an arc, sweeping from startAngle by sweep (radians, positive is counter clockwise).</summary>
    public static List<Vec2> Arc(Vec2 center, double radius, double startAngle, double sweep, double tolerance)
    {
        var n = ArcSegments(radius, sweep, tolerance);
        var points = new List<Vec2>(n + 1);
        for (var i = 0; i <= n; i++)
        {
            points.Add(center + Vec2.FromAngle(startAngle + sweep * i / n, radius));
        }
        return points;
    }

    /// <summary>
    /// Arc between two polyline vertices from a DXF bulge (tan of a quarter of the included angle).
    /// Returns the points after p1 (excluding p1) and the exact arc length.
    /// </summary>
    public static (List<Vec2> Points, double Length) Bulge(Vec2 p1, Vec2 p2, double bulge, double tolerance)
    {
        var chord = p2 - p1;
        var d = chord.Length;
        if (Math.Abs(bulge) < 1e-12 || d < 1e-12)
        {
            return ([p2], d);
        }

        var theta = 4 * Math.Atan(bulge); // signed included angle
        var normal = chord.Perpendicular / d;
        var center = (p1 + p2) / 2 + normal * (d / (2 * Math.Tan(theta / 2)));
        var radius = center.DistanceTo(p1);
        var start = Math.Atan2(p1.Y - center.Y, p1.X - center.X);

        var points = Arc(center, radius, start, theta, tolerance);
        points.RemoveAt(0);
        points[^1] = p2; // snap to the exact vertex
        return (points, Math.Abs(theta) * radius);
    }

    /// <summary>Ellipse arc. minorAxis already includes the ratio and the orientation.</summary>
    public static List<Vec2> Ellipse(Vec2 center, Vec2 majorAxis, Vec2 minorAxis, double startParam, double endParam, double tolerance)
    {
        var sweep = endParam - startParam;
        var radius = Math.Max(majorAxis.Length, 1e-9);
        var n = Math.Max(ArcSegments(radius, sweep, tolerance), 32);
        var points = new List<Vec2>(n + 1);
        for (var i = 0; i <= n; i++)
        {
            var t = startParam + sweep * i / n;
            points.Add(center + majorAxis * Math.Cos(t) + minorAxis * Math.Sin(t));
        }
        return points;
    }

    /// <summary>
    /// Evaluates a (rational) B spline with de Boor's algorithm.
    /// Knot vector length must be controlPoints + degree + 1.
    /// </summary>
    public static List<Vec2> Nurbs(int degree, IReadOnlyList<double> knots, IReadOnlyList<Vec2> controlPoints, IReadOnlyList<double>? weights, int samples)
    {
        var n = controlPoints.Count;
        if (degree < 1 || n <= degree || knots.Count != n + degree + 1)
        {
            throw new ArgumentException($"Invalid spline: degree {degree}, {n} control points, {knots.Count} knots");
        }

        var w = weights is { Count: > 0 } && weights.Count == n ? weights : Enumerable.Repeat(1.0, n).ToList();
        var u0 = knots[degree];
        var u1 = knots[n];
        var points = new List<Vec2>(samples + 1);

        for (var s = 0; s <= samples; s++)
        {
            var u = u0 + (u1 - u0) * s / samples;
            points.Add(DeBoor(degree, knots, controlPoints, w, u, n));
        }
        return points;
    }

    private static Vec2 DeBoor(int p, IReadOnlyList<double> knots, IReadOnlyList<Vec2> ctrl, IReadOnlyList<double> w, double u, int n)
    {
        // Find span k with knots[k] <= u < knots[k+1], clamped to the valid range.
        var k = p;
        while (k < n - 1 && u >= knots[k + 1]) k++;

        // Homogeneous coordinates so weights work.
        Span<double> x = stackalloc double[p + 1];
        Span<double> y = stackalloc double[p + 1];
        Span<double> h = stackalloc double[p + 1];
        for (var j = 0; j <= p; j++)
        {
            var i = j + k - p;
            x[j] = ctrl[i].X * w[i];
            y[j] = ctrl[i].Y * w[i];
            h[j] = w[i];
        }

        for (var r = 1; r <= p; r++)
        {
            for (var j = p; j >= r; j--)
            {
                var i = j + k - p;
                var denom = knots[i + p - r + 1] - knots[i];
                var alpha = denom == 0 ? 0 : (u - knots[i]) / denom;
                x[j] = (1 - alpha) * x[j - 1] + alpha * x[j];
                y[j] = (1 - alpha) * y[j - 1] + alpha * y[j];
                h[j] = (1 - alpha) * h[j - 1] + alpha * h[j];
            }
        }

        return new Vec2(x[p] / h[p], y[p] / h[p]);
    }
}
