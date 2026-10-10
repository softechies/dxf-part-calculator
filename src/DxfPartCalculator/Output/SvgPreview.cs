using System.Globalization;
using System.Text;
using DxfPartCalculator.Analysis;
using DxfPartCalculator.Geometry;

namespace DxfPartCalculator.Output;

/// <summary>
/// Writes an SVG of how the part was understood: outer contours, holes and open paths
/// in different colours, with a dot at each pierce point. Quick visual check before quoting.
/// </summary>
public static class SvgPreview
{
    public static string Render(PartReport report)
    {
        var all = report.Contours.SelectMany(c => c.Points).ToList();
        var b = Bounds.Of(all);
        if (b.IsEmpty) b = new Bounds(0, 0, 1, 1);

        var size = Math.Max(Math.Max(b.Width, b.Height), 1e-6);
        var margin = size * 0.05;
        var stroke = size / 400;
        var minX = b.MinX - margin;
        var minY = b.MinY - margin;
        var w = b.Width + margin * 2;
        var h = b.Height + margin * 2;

        var sb = new StringBuilder();
        sb.AppendLine(Inv($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{minX:0.###} {-(minY + h):0.###} {w:0.###} {h:0.###}\" width=\"800\" height=\"{800 * h / w:0}\">"));
        sb.AppendLine($"<title>{Escape(report.File)}</title>");
        sb.AppendLine(Inv($"<rect x=\"{minX:0.###}\" y=\"{-(minY + h):0.###}\" width=\"{w:0.###}\" height=\"{h:0.###}\" fill=\"#fafafa\"/>"));
        sb.AppendLine("<g fill=\"none\" stroke-linejoin=\"round\" stroke-linecap=\"round\">");

        // Draw by nesting depth so holes cover their part and islands sit on top of their hole.
        foreach (var c in report.Contours.OrderBy(c => c.Closed ? c.Depth : int.MaxValue))
        {
            var (color, dash) = !c.Closed ? ("#d9480f", Inv($" stroke-dasharray=\"{stroke * 6:0.###} {stroke * 4:0.###}\"")) : c.IsHole ? ("#1971c2", "") : ("#212529", "");
            var fill = !c.Closed ? "" : c.IsHole ? " fill=\"#fafafa\"" : " fill=\"#e9ecef\"";
            var tag = c.Closed ? "polygon" : "polyline";
            var points = string.Join(" ", c.Points.Select(p => Inv($"{p.X:0.###},{-p.Y:0.###}")));
            sb.AppendLine(Inv($"<{tag} points=\"{points}\" stroke=\"{color}\" stroke-width=\"{stroke * 2:0.####}\"{fill}{dash}/>"));
        }

        sb.AppendLine("</g>");
        sb.AppendLine("<g fill=\"#e03131\">");
        foreach (var c in report.Contours)
        {
            var p = c.Points[0];
            sb.AppendLine(Inv($"<circle cx=\"{p.X:0.###}\" cy=\"{-p.Y:0.###}\" r=\"{stroke * 4:0.####}\"/>"));
        }
        sb.AppendLine("</g>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
