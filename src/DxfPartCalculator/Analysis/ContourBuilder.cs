using DxfPartCalculator.Geometry;

namespace DxfPartCalculator.Analysis;

public sealed class Contour
{
    public required List<Vec2> Points { get; init; }
    public required double Length { get; init; }
    public required bool Closed { get; init; }
    public int PathCount { get; init; } = 1;

    /// <summary>Absolute enclosed area (0 for open contours).</summary>
    public double Area { get; set; }

    /// <summary>How many closed contours enclose this one. Even = material boundary, odd = hole.</summary>
    public int Depth { get; set; }

    public bool IsHole => Closed && Depth % 2 == 1;

    public Bounds Bounds => Bounds.Of(Points);
}

/// <summary>
/// Joins separate entities whose end points meet (within a tolerance) into contours.
/// CAD exports often draw one outline as dozens of LINEs and ARCs; a cutter sees it as one cut with one pierce.
/// </summary>
public static class ContourBuilder
{
    public static List<Contour> Build(IEnumerable<GeoPath> paths, double joinTolerance)
    {
        var contours = new List<Contour>();
        var open = new List<GeoPath>();

        foreach (var path in paths)
        {
            if (path.Closed)
            {
                contours.Add(new Contour { Points = path.Points, Length = path.Length, Closed = true });
            }
            else
            {
                open.Add(path);
            }
        }

        var index = new EndpointIndex(joinTolerance);
        for (var i = 0; i < open.Count; i++)
        {
            index.Add(open[i].Start, i);
            index.Add(open[i].End, i);
        }

        var used = new bool[open.Count];
        for (var i = 0; i < open.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;

            var chain = new LinkedList<Vec2>(open[i].Points);
            var length = open[i].Length;
            var count = 1;

            // Grow forward from the end, then backward from the start.
            bool IsClosed() => chain.Count >= 3 && chain.First!.Value.DistanceTo(chain.Last!.Value) <= joinTolerance;

            while (!IsClosed() && TryExtend(chain.Last!.Value, out var next, out var reversed))
            {
                foreach (var p in Oriented(open[next], reversed).Skip(1)) chain.AddLast(p);
                length += open[next].Length;
                count++;
            }

            while (!IsClosed() && TryExtend(chain.First!.Value, out var prev, out var reversed))
            {
                // Prepending needs the path in the order that ends at our start point.
                foreach (var p in Oriented(open[prev], !reversed).Reverse().Skip(1)) chain.AddFirst(p);
                length += open[prev].Length;
                count++;
            }

            var points = chain.ToList();
            var closed = points.Count >= 3 && points[0].DistanceTo(points[^1]) <= joinTolerance;
            if (closed) points.RemoveAt(points.Count - 1);

            contours.Add(new Contour { Points = points, Length = length, Closed = closed, PathCount = count });
        }

        return contours;

        // Finds an unused path with an end point at p. reversed = the path must be walked end to start.
        bool TryExtend(Vec2 p, out int found, out bool reversed)
        {
            foreach (var (point, pathIndex) in index.Near(p))
            {
                if (used[pathIndex] || point.DistanceTo(p) > joinTolerance) continue;
                used[pathIndex] = true;
                found = pathIndex;
                reversed = open[pathIndex].Start.DistanceTo(p) > joinTolerance;
                return true;
            }
            found = -1;
            reversed = false;
            return false;
        }
    }

    private static IEnumerable<Vec2> Oriented(GeoPath path, bool reversed) =>
        reversed ? Enumerable.Reverse(path.Points) : path.Points;

    /// <summary>Grid hash of end points so joining stays fast on files with thousands of entities.</summary>
    private sealed class EndpointIndex(double cellSize)
    {
        private readonly double _cell = Math.Max(cellSize, 1e-9) * 2;
        private readonly Dictionary<(long, long), List<(Vec2, int)>> _cells = [];

        public void Add(Vec2 p, int pathIndex)
        {
            var key = Key(p);
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = [];
            list.Add((p, pathIndex));
        }

        public IEnumerable<(Vec2 Point, int PathIndex)> Near(Vec2 p)
        {
            var (cx, cy) = Key(p);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (_cells.TryGetValue((cx + dx, cy + dy), out var list))
                {
                    foreach (var item in list) yield return item;
                }
            }
        }

        private (long, long) Key(Vec2 p) => ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell));
    }
}
