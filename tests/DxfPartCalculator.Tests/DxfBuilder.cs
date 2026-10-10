using System.Globalization;
using System.Text;

namespace DxfPartCalculator.Tests;

/// <summary>Writes minimal ASCII DXF text for tests.</summary>
internal sealed class DxfBuilder
{
    private readonly StringBuilder _entities = new();
    private readonly StringBuilder _blocks = new();
    private int? _insUnits;

    public DxfBuilder Units(int insUnits) { _insUnits = insUnits; return this; }

    public DxfBuilder Line(double x1, double y1, double x2, double y2, string layer = "0") =>
        Entity("LINE", layer, (10, x1), (20, y1), (11, x2), (21, y2));

    public DxfBuilder Circle(double cx, double cy, double r, string layer = "0") =>
        Entity("CIRCLE", layer, (10, cx), (20, cy), (40, r));

    public DxfBuilder Arc(double cx, double cy, double r, double startDeg, double endDeg, string layer = "0", bool mirrored = false)
    {
        var pairs = new List<(int, object)> { (10, cx), (20, cy), (40, r), (50, startDeg), (51, endDeg) };
        if (mirrored) { pairs.Add((210, 0)); pairs.Add((220, 0)); pairs.Add((230, -1)); }
        return Entity("ARC", layer, pairs.ToArray());
    }

    public DxfBuilder LwPolyline(bool closed, params (double X, double Y, double Bulge)[] vertices)
    {
        var pairs = new List<(int, object)> { (90, vertices.Length), (70, closed ? 1 : 0) };
        foreach (var (x, y, b) in vertices)
        {
            pairs.Add((10, x));
            pairs.Add((20, y));
            if (b != 0) pairs.Add((42, b));
        }
        return Entity("LWPOLYLINE", "0", pairs.ToArray());
    }

    public DxfBuilder Rectangle(double x, double y, double w, double h) =>
        LwPolyline(true, (x, y, 0), (x + w, y, 0), (x + w, y + h, 0), (x, y + h, 0));

    public DxfBuilder Ellipse(double cx, double cy, double majorX, double majorY, double ratio) =>
        Entity("ELLIPSE", "0", (10, cx), (20, cy), (11, majorX), (21, majorY), (40, ratio), (41, 0.0), (42, 2 * Math.PI));

    public DxfBuilder Text(string text) => Entity("TEXT", "0", (10, 0.0), (20, 0.0), (1, text));

    public DxfBuilder Insert(string block, double x, double y, double sx = 1, double sy = 1, double rotationDeg = 0, string layer = "0") =>
        Entity("INSERT", layer, (2, block), (10, x), (20, y), (41, sx), (42, sy), (50, rotationDeg));

    public DxfBuilder Block(string name, Action<DxfBuilder> content, double baseX = 0, double baseY = 0)
    {
        var inner = new DxfBuilder();
        content(inner);
        _blocks.Append($"0\nBLOCK\n8\n0\n2\n{name}\n70\n0\n10\n{N(baseX)}\n20\n{N(baseY)}\n30\n0\n3\n{name}\n");
        _blocks.Append(inner._entities);
        _blocks.Append("0\nENDBLK\n8\n0\n");
        return this;
    }

    private DxfBuilder Entity(string type, string layer, params (int Code, object Value)[] pairs)
    {
        _entities.Append($"0\n{type}\n8\n{layer}\n");
        foreach (var (code, value) in pairs)
        {
            _entities.Append(code).Append('\n').Append(value is double d ? N(d) : Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
        }
        return this;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (_insUnits is not null)
        {
            sb.Append($"0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n{_insUnits}\n0\nENDSEC\n");
        }
        if (_blocks.Length > 0)
        {
            sb.Append("0\nSECTION\n2\nBLOCKS\n").Append(_blocks).Append("0\nENDSEC\n");
        }
        sb.Append("0\nSECTION\n2\nENTITIES\n").Append(_entities).Append("0\nENDSEC\n0\nEOF\n");
        return sb.ToString();
    }

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
