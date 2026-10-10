using System.Text.RegularExpressions;
using DxfPartCalculator.Dxf;

namespace DxfPartCalculator.Geometry;

public sealed class ConversionResult
{
    public List<GeoPath> Paths { get; } = [];

    /// <summary>Entity types that produced geometry, with counts (blocks expanded).</summary>
    public SortedDictionary<string, int> Used { get; } = new(StringComparer.Ordinal);

    /// <summary>Entity types ignored on purpose (text, dimensions, hatches...).</summary>
    public SortedDictionary<string, int> Skipped { get; } = new(StringComparer.Ordinal);

    public List<string> Warnings { get; } = [];
}

/// <summary>
/// Converts DXF entities into flattened paths in world coordinates.
/// Supports LINE, ARC, CIRCLE, LWPOLYLINE, POLYLINE (2D), ELLIPSE, SPLINE and INSERT (nested blocks).
/// </summary>
public sealed class EntityConverter(double tolerance, IReadOnlyCollection<string>? ignoreLayers = null)
{
    private const int MaxBlockDepth = 16;

    private static readonly HashSet<string> Annotation = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT", "MTEXT", "DIMENSION", "LEADER", "MLEADER", "MULTILEADER", "HATCH", "SOLID", "POINT",
        "VIEWPORT", "ATTRIB", "ATTDEF", "TOLERANCE", "IMAGE", "WIPEOUT", "OLE2FRAME", "ACAD_TABLE"
    };

    private readonly List<Regex> _ignore = (ignoreLayers ?? [])
        .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase))
        .ToList();

    public ConversionResult Convert(DxfDocument doc)
    {
        var result = new ConversionResult();
        foreach (var entity in doc.Entities)
        {
            ConvertEntity(doc, entity, Transform2D.Identity, null, 0, result);
        }
        return result;
    }

    private void ConvertEntity(DxfDocument doc, DxfEntity e, Transform2D world, string? parentLayer, int depth, ConversionResult result)
    {
        // Entities on layer 0 inside a block take the layer of the INSERT.
        var layer = e.Layer == "0" && parentLayer is not null ? parentLayer : e.Layer;
        if (_ignore.Any(r => r.IsMatch(layer)))
        {
            Count(result.Skipped, $"{e.Type} (layer {layer})");
            return;
        }

        // Entities drawn with extrusion (0,0,-1) are mirrored in X (common in mirrored CAD exports).
        var ocs = e.GetDouble(230, 1) < 0 ? Transform2D.MirrorX.Then(world) : world;

        switch (e.Type)
        {
            case "LINE":
                Add(result, e, layer, world, [new Vec2(e.GetDouble(10), e.GetDouble(20)), new Vec2(e.GetDouble(11), e.GetDouble(21))], closed: false, exactLength: null);
                break;

            case "CIRCLE":
            {
                var r = e.GetDouble(40);
                var pts = Curves.Arc(new Vec2(e.GetDouble(10), e.GetDouble(20)), r, 0, 2 * Math.PI, LocalTolerance(ocs));
                Add(result, e, layer, ocs, pts, closed: true, exactLength: 2 * Math.PI * r);
                break;
            }

            case "ARC":
            {
                var r = e.GetDouble(40);
                var start = e.GetDouble(50) * Math.PI / 180;
                var end = e.GetDouble(51) * Math.PI / 180;
                var sweep = end - start;
                while (sweep <= 0) sweep += 2 * Math.PI; // arcs always run counter clockwise
                var pts = Curves.Arc(new Vec2(e.GetDouble(10), e.GetDouble(20)), r, start, sweep, LocalTolerance(ocs));
                Add(result, e, layer, ocs, pts, closed: false, exactLength: r * sweep);
                break;
            }

            case "LWPOLYLINE":
                ConvertLwPolyline(e, layer, ocs, result);
                break;

            case "POLYLINE":
                ConvertPolyline(e, layer, ocs, result);
                break;

            case "ELLIPSE":
                ConvertEllipse(e, layer, world, result);
                break;

            case "SPLINE":
                ConvertSpline(e, layer, world, result);
                break;

            case "INSERT":
                ConvertInsert(doc, e, layer, world, depth, result);
                break;

            default:
                Count(result.Skipped, e.Type);
                if (!IsAnnotation(e.Type) && result.Skipped[e.Type] == 1)
                {
                    result.Warnings.Add($"Unsupported entity type {e.Type} ignored");
                }
                break;
        }
    }

    private void ConvertLwPolyline(DxfEntity e, string layer, Transform2D world, ConversionResult result)
    {
        // Vertices are repeated 10/20 pairs; a 42 (bulge) after a vertex belongs to that vertex.
        var vertices = new List<(Vec2 P, double Bulge)>();
        double? x = null;
        foreach (var (code, value) in e.Pairs)
        {
            var v = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
            switch (code)
            {
                case 10: x = v; break;
                case 20 when x is not null: vertices.Add((new Vec2(x.Value, v), 0)); x = null; break;
                case 42 when vertices.Count > 0: vertices[^1] = (vertices[^1].P, v); break;
            }
        }

        var closed = (e.GetInt(70) & 1) != 0;
        AddPolyline(result, e, layer, world, vertices, closed);
    }

    private void ConvertPolyline(DxfEntity e, string layer, Transform2D world, ConversionResult result)
    {
        var flags = e.GetInt(70);
        if ((flags & (16 | 64)) != 0)
        {
            Count(result.Skipped, "POLYLINE (mesh)");
            return;
        }

        var vertices = e.Children
            .Where(v => (v.GetInt(70) & 16) == 0) // skip spline frame control points
            .Select(v => (new Vec2(v.GetDouble(10), v.GetDouble(20)), v.GetDouble(42)))
            .ToList();
        AddPolyline(result, e, layer, world, vertices, (flags & 1) != 0);
    }

    private void AddPolyline(ConversionResult result, DxfEntity e, string layer, Transform2D world, List<(Vec2 P, double Bulge)> vertices, bool closed)
    {
        if (vertices.Count < 2)
        {
            result.Warnings.Add($"{e.Type} on layer {layer} has fewer than 2 vertices, ignored");
            return;
        }

        var tol = LocalTolerance(world);
        var points = new List<Vec2> { vertices[0].P };
        double length = 0;
        var segmentCount = closed ? vertices.Count : vertices.Count - 1;

        for (var i = 0; i < segmentCount; i++)
        {
            var (p1, bulge) = vertices[i];
            var p2 = vertices[(i + 1) % vertices.Count].P;
            var (pts, len) = Curves.Bulge(p1, p2, bulge, tol);
            points.AddRange(pts);
            length += len;
        }

        Add(result, e, layer, world, points, closed, length);
    }

    private void ConvertEllipse(DxfEntity e, string layer, Transform2D world, ConversionResult result)
    {
        var center = new Vec2(e.GetDouble(10), e.GetDouble(20));
        var major = new Vec2(e.GetDouble(11), e.GetDouble(21));
        var ratio = e.GetDouble(40, 1);
        var start = e.GetDouble(41, 0);
        var end = e.GetDouble(42, 2 * Math.PI);
        while (end <= start) end += 2 * Math.PI;

        // Minor axis is the major rotated 90 degrees about the extrusion direction.
        var minor = major.Perpendicular * ratio;
        if (e.GetDouble(230, 1) < 0) minor = minor * -1;

        var pts = Curves.Ellipse(center, major, minor, start, end, LocalTolerance(world) / 4);
        var full = end - start >= 2 * Math.PI - 1e-9;
        Add(result, e, layer, world, pts, closed: full, exactLength: null);
    }

    private void ConvertSpline(DxfEntity e, string layer, Transform2D world, ConversionResult result)
    {
        var degree = e.GetInt(71, 3);
        var knots = e.GetDoubles(40).ToList();
        var weights = e.GetDoubles(41).ToList();
        var controls = Zip(e.GetDoubles(10), e.GetDoubles(20));
        var fits = Zip(e.GetDoubles(11), e.GetDoubles(21));
        var closed = (e.GetInt(70) & 1) != 0;

        List<Vec2> pts;
        if (controls.Count > degree && knots.Count == controls.Count + degree + 1)
        {
            var samples = Math.Clamp(controls.Count * 32, 64, 8192);
            pts = Curves.Nurbs(degree, knots, controls, weights, samples);
        }
        else if (fits.Count >= 2)
        {
            pts = fits;
            result.Warnings.Add($"SPLINE on layer {layer} has no usable control points, fit points used (approximate length)");
        }
        else
        {
            result.Warnings.Add($"SPLINE on layer {layer} could not be read, ignored");
            return;
        }

        if (closed && pts[0].DistanceTo(pts[^1]) > 1e-9) pts.Add(pts[0]);
        Add(result, e, layer, world, pts, closed, exactLength: null);
    }

    private void ConvertInsert(DxfDocument doc, DxfEntity e, string layer, Transform2D world, int depth, ConversionResult result)
    {
        var name = e.GetString(2) ?? "";
        if (!doc.Blocks.TryGetValue(name, out var block))
        {
            result.Warnings.Add($"INSERT references missing block '{name}'");
            return;
        }
        if (depth >= MaxBlockDepth)
        {
            result.Warnings.Add($"Block nesting deeper than {MaxBlockDepth} at '{name}', stopped");
            return;
        }
        if (e.GetInt(70, 1) > 1 || e.GetInt(71, 1) > 1)
        {
            result.Warnings.Add($"MINSERT array of '{name}' counted once");
        }

        // block space -> move base point to origin -> scale -> rotate -> move to insertion point
        var local = Transform2D.Translation(-block.BaseX, -block.BaseY)
            .Then(Transform2D.Scale(e.GetDouble(41, 1), e.GetDouble(42, 1)))
            .Then(Transform2D.Rotation(e.GetDouble(50) * Math.PI / 180))
            .Then(Transform2D.Translation(e.GetDouble(10), e.GetDouble(20)));
        if (e.GetDouble(230, 1) < 0) local = local.Then(Transform2D.MirrorX);

        var combined = local.Then(world);
        foreach (var child in block.Entities)
        {
            ConvertEntity(doc, child, combined, layer, depth + 1, result);
        }
    }

    /// <summary>
    /// Adds a path in world space. Exact lengths from arcs and bulges are kept when the
    /// transform preserves shape; otherwise the length is measured on the flattened points.
    /// </summary>
    private void Add(ConversionResult result, DxfEntity e, string layer, Transform2D world, List<Vec2> local, bool closed, double? exactLength)
    {
        var points = local.Select(world.Apply).ToList();
        if (closed && points.Count > 1 && points[0].DistanceTo(points[^1]) < 1e-12)
        {
            points.RemoveAt(points.Count - 1); // closed paths store each vertex once
        }

        var length = exactLength is not null && world.IsConformal
            ? exactLength.Value * world.UniformScale
            : PolygonMath.PolylineLength(points) + (closed && points.Count > 2 ? points[^1].DistanceTo(points[0]) : 0);

        if (length <= 0 && points.Count < 2)
        {
            return;
        }

        result.Paths.Add(new GeoPath { Points = points, Length = length, Closed = closed, Layer = layer, Source = e.Type });
        Count(result.Used, e.Type);
    }

    /// <summary>Tolerance in the entity's own coordinates so scaled blocks are flattened just as finely.</summary>
    private double LocalTolerance(Transform2D world)
    {
        var scale = world.IsConformal ? world.UniformScale : Math.Max(Math.Sqrt(world.A * world.A + world.C * world.C), Math.Sqrt(world.B * world.B + world.D * world.D));
        return scale > 0 ? tolerance / scale : tolerance;
    }

    private static List<Vec2> Zip(IEnumerable<double> xs, IEnumerable<double> ys) =>
        xs.Zip(ys, (x, y) => new Vec2(x, y)).ToList();

    private static void Count(SortedDictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;

    public static bool IsAnnotation(string type) => Annotation.Contains(type);
}
