using DxfPartCalculator.Dxf;
using DxfPartCalculator.Geometry;

namespace DxfPartCalculator.Analysis;

public sealed class AnalysisOptions
{
    /// <summary>Force units instead of reading $INSUNITS: mm, cm, m, in, ft.</summary>
    public string? Units { get; set; }

    /// <summary>Max distance between a curve and its flattened chords, in mm.</summary>
    public double FlattenToleranceMm { get; set; } = 0.002;

    /// <summary>End points closer than this (mm) are treated as connected.</summary>
    public double JoinToleranceMm { get; set; } = 0.05;

    /// <summary>Layer names to ignore, wildcards allowed (e.g. DIM*, TEXT).</summary>
    public List<string> IgnoreLayers { get; set; } = [];
}

public sealed class PartReport
{
    public required string File { get; init; }
    public required string Units { get; init; }
    public required bool UnitsAssumed { get; init; }

    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public double CutLengthMm { get; init; }
    public double NetAreaMm2 { get; init; }
    public double BoundingAreaMm2 => WidthMm * HeightMm;

    public int OuterContours { get; init; }
    public int Holes { get; init; }
    public int OpenContours { get; init; }
    public int Pierces => OuterContours + Holes + OpenContours;

    public required IReadOnlyDictionary<string, int> Entities { get; init; }
    public required IReadOnlyDictionary<string, int> Skipped { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Contours in mm, for previews.</summary>
    public required IReadOnlyList<Contour> Contours { get; init; }
}

public static class PartAnalyzer
{
    public static PartReport Analyze(string path, AnalysisOptions? options = null) =>
        Analyze(DxfReader.Load(path), Path.GetFileName(path), options);

    public static PartReport Analyze(DxfDocument doc, string name, AnalysisOptions? options = null)
    {
        options ??= new AnalysisOptions();
        var (toMm, unitName, assumed) = DxfUnits.Resolve(doc.InsUnits, options.Units);

        var converter = new EntityConverter(options.FlattenToleranceMm / toMm, options.IgnoreLayers);
        var conversion = converter.Convert(doc);
        var warnings = new List<string>(conversion.Warnings);
        if (assumed)
        {
            warnings.Insert(0, "Drawing units not set ($INSUNITS), assuming millimetres. Use --units to override.");
        }

        // Work in millimetres from here on.
        var paths = conversion.Paths.Select(p => new GeoPath
        {
            Points = p.Points.Select(v => v * toMm).ToList(),
            Length = p.Length * toMm,
            Closed = p.Closed,
            Layer = p.Layer,
            Source = p.Source
        }).ToList();

        var duplicatePaths = RemoveDuplicatePaths(paths, options.JoinToleranceMm);
        if (duplicatePaths > 0)
        {
            warnings.Add($"{duplicatePaths} duplicate entit{(duplicatePaths == 1 ? "y" : "ies")} ignored (drawn twice on top of each other).");
        }

        var contours = ContourBuilder.Build(paths, options.JoinToleranceMm);
        RemoveDuplicates(contours, options.JoinToleranceMm, warnings);
        ComputeAreasAndNesting(contours);

        var bounds = Bounds.Of(contours.SelectMany(c => c.Points));
        var closed = contours.Where(c => c.Closed).ToList();
        var open = contours.Count(c => !c.Closed);
        if (open > 0)
        {
            warnings.Add($"{open} open contour(s): broken outlines or engraving lines. Each is counted as one pierce.");
        }
        if (contours.Count == 0)
        {
            warnings.Add("No cuttable geometry found.");
        }

        return new PartReport
        {
            File = name,
            Units = unitName,
            UnitsAssumed = assumed,
            WidthMm = bounds.Width,
            HeightMm = bounds.Height,
            CutLengthMm = contours.Sum(c => c.Length),
            NetAreaMm2 = closed.Sum(c => c.IsHole ? -c.Area : c.Area),
            OuterContours = closed.Count(c => !c.IsHole),
            Holes = closed.Count(c => c.IsHole),
            OpenContours = open,
            Entities = conversion.Used,
            Skipped = conversion.Skipped,
            Warnings = warnings,
            Contours = contours
        };
    }

    private static void ComputeAreasAndNesting(List<Contour> contours)
    {
        var closed = contours.Where(c => c.Closed && c.Points.Count >= 3).ToList();
        foreach (var c in closed)
        {
            c.Area = Math.Abs(PolygonMath.SignedArea(c.Points));
        }

        var bounds = closed.Select(c => c.Bounds).ToList();
        for (var i = 0; i < closed.Count; i++)
        {
            var probe = closed[i].Points[0];
            var depth = 0;
            for (var j = 0; j < closed.Count; j++)
            {
                if (i == j || closed[j].Area <= closed[i].Area) continue;
                var b = bounds[j];
                if (probe.X < b.MinX || probe.X > b.MaxX || probe.Y < b.MinY || probe.Y > b.MaxY) continue;
                if (PolygonMath.Contains(closed[j].Points, probe)) depth++;
            }
            closed[i].Depth = depth;
        }
    }

    /// <summary>Removes open paths that repeat another one exactly (same end points, either direction, same length).</summary>
    private static int RemoveDuplicatePaths(List<GeoPath> paths, double tolerance)
    {
        var seen = new HashSet<(long, long, long, long, long, long, long)>();
        var removed = paths.RemoveAll(p =>
        {
            if (p.Closed) return false;
            var a = Round(p.Start);
            var b = Round(p.End);
            if (a.CompareTo(b) > 0) (a, b) = (b, a);
            // The middle point tells apart two different arcs between the same end points.
            var n = p.Points.Count;
            var mid = Round((p.Points[(n - 1) / 2] + p.Points[n / 2]) / 2);
            return !seen.Add((a.Item1, a.Item2, b.Item1, b.Item2, mid.Item1, mid.Item2, (long)Math.Round(p.Length / tolerance)));
        });
        return removed;

        (long, long) Round(Vec2 v) => ((long)Math.Round(v.X / tolerance), (long)Math.Round(v.Y / tolerance));
    }

    /// <summary>Drops contours drawn twice on top of each other, a very common CAD export problem.</summary>
    private static void RemoveDuplicates(List<Contour> contours, double tolerance, List<string> warnings)
    {
        var removed = 0;
        for (var i = contours.Count - 1; i >= 0; i--)
        {
            var a = contours[i];
            var ba = a.Bounds;
            for (var j = 0; j < i; j++)
            {
                var b = contours[j];
                if (a.Closed != b.Closed) continue;
                if (Math.Abs(a.Length - b.Length) > tolerance * 2) continue;
                var bb = b.Bounds;
                if (Math.Abs(ba.MinX - bb.MinX) > tolerance || Math.Abs(ba.MinY - bb.MinY) > tolerance ||
                    Math.Abs(ba.MaxX - bb.MaxX) > tolerance || Math.Abs(ba.MaxY - bb.MaxY) > tolerance) continue;
                if (a.Closed)
                {
                    var areaA = Math.Abs(PolygonMath.SignedArea(a.Points));
                    var areaB = Math.Abs(PolygonMath.SignedArea(b.Points));
                    if (Math.Abs(areaA - areaB) > 0.005 * Math.Max(areaA, areaB)) continue;
                }

                contours.RemoveAt(i);
                removed++;
                break;
            }
        }

        if (removed > 0)
        {
            warnings.Add($"{removed} duplicate contour(s) removed (same shape drawn twice).");
        }
    }
}

public static class DxfUnits
{
    /// <summary>Returns the factor to millimetres, a display name, and whether the unit was assumed.</summary>
    public static (double ToMm, string Name, bool Assumed) Resolve(int insUnits, string? forced)
    {
        if (!string.IsNullOrWhiteSpace(forced))
        {
            return forced.Trim().ToLowerInvariant() switch
            {
                "mm" => (1, "mm", false),
                "cm" => (10, "cm", false),
                "m" => (1000, "m", false),
                "in" or "inch" or "inches" => (25.4, "in", false),
                "ft" or "feet" => (304.8, "ft", false),
                _ => throw new ArgumentException($"Unknown unit '{forced}'. Use mm, cm, m, in or ft.")
            };
        }

        return insUnits switch
        {
            1 => (25.4, "in", false),
            2 => (304.8, "ft", false),
            4 => (1, "mm", false),
            5 => (10, "cm", false),
            6 => (1000, "m", false),
            9 => (0.0254, "mil", false),
            10 => (914.4, "yd", false),
            14 => (100, "dm", false),
            _ => (1, "mm", true)
        };
    }
}
