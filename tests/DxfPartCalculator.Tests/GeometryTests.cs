using DxfPartCalculator.Analysis;
using DxfPartCalculator.Dxf;
using DxfPartCalculator.Geometry;
using DxfPartCalculator.Output;
using Xunit;

namespace DxfPartCalculator.Tests;

public class GeometryTests
{
    [Fact]
    public void Rational_spline_quarter_circle_is_exact()
    {
        var w = Math.Sqrt(2) / 2;
        var pts = Curves.Nurbs(2, [0, 0, 0, 1, 1, 1], [new(1, 0), new(1, 1), new(0, 1)], [1, w, 1], 200);

        foreach (var p in pts)
        {
            Assert.Equal(1.0, p.Length, 9); // every point on the unit circle
        }
        Assert.Equal(Math.PI / 2, PolygonMath.PolylineLength(pts), 4);
    }

    [Fact]
    public void Degree_one_spline_is_its_control_polygon()
    {
        var pts = Curves.Nurbs(1, [0, 0, 1, 2, 2], [new(0, 0), new(10, 0), new(10, 5)], null, 100);
        Assert.Equal(15, PolygonMath.PolylineLength(pts), 6);
    }

    [Fact]
    public void Arc_flattening_respects_tolerance()
    {
        const double r = 100, tol = 0.01;
        var pts = Curves.Arc(new Vec2(0, 0), r, 0, Math.PI, tol);
        for (var i = 1; i < pts.Count; i++)
        {
            var mid = (pts[i - 1] + pts[i]) / 2;
            Assert.True(r - mid.Length <= tol + 1e-12, "chord sagitta exceeds tolerance");
        }
    }

    [Fact]
    public void Negative_bulge_goes_clockwise()
    {
        var (pts, length) = Curves.Bulge(new Vec2(0, 0), new Vec2(10, 0), -1, 0.001);
        Assert.Equal(5 * Math.PI, length, 9);
        // Clockwise from the left end to the right end passes over the top of the chord.
        Assert.True(pts.Max(p => p.Y) > 4.99);
        Assert.Equal(new Vec2(10, 0), pts[^1]);
    }

    [Fact]
    public void Transform_composition_order()
    {
        var t = Transform2D.Scale(2, 2).Then(Transform2D.Rotation(Math.PI / 2)).Then(Transform2D.Translation(10, 0));
        var p = t.Apply(new Vec2(1, 0)); // scale -> (2,0), rotate -> (0,2), move -> (10,2)
        Assert.Equal(10, p.X, 9);
        Assert.Equal(2, p.Y, 9);
        Assert.True(t.IsConformal);
        Assert.Equal(2, t.UniformScale, 9);
        Assert.False(Transform2D.Scale(2, 1).IsConformal);
    }

    [Fact]
    public void Reader_handles_polyline_vertices_and_header()
    {
        const string dxf = """
            0
            SECTION
            2
            HEADER
            9
            $ACADVER
            1
            AC1009
            9
            $INSUNITS
            70
            5
            0
            ENDSEC
            0
            SECTION
            2
            ENTITIES
            0
            POLYLINE
            8
            CUT
            66
            1
            70
            1
            0
            VERTEX
            8
            CUT
            10
            0
            20
            0
            0
            VERTEX
            8
            CUT
            10
            2
            20
            0
            0
            VERTEX
            8
            CUT
            10
            2
            20
            1
            0
            SEQEND
            8
            CUT
            0
            ENDSEC
            0
            EOF
            """;

        var doc = DxfReader.Parse(dxf);
        Assert.Equal(5, doc.InsUnits);
        var poly = Assert.Single(doc.Entities);
        Assert.Equal(3, poly.Children.Count);

        var r = PartAnalyzer.Analyze(doc, "r12.dxf");
        Assert.Equal("cm", r.Units);
        Assert.Equal(10, r.NetAreaMm2 / 10, 6); // triangle 2 x 1 cm / 2 = 1 cm² = 100 mm²
    }

    [Fact]
    public void Svg_preview_draws_each_contour()
    {
        var doc = DxfReader.Parse(new DxfBuilder().Units(4).Rectangle(0, 0, 10, 10).Circle(5, 5, 2).Line(0, 20, 5, 20).ToString());
        var svg = SvgPreview.Render(PartAnalyzer.Analyze(doc, "x.dxf"));

        Assert.Equal(2, CountOf(svg, "<polygon"));
        Assert.Equal(1, CountOf(svg, "<polyline"));
        Assert.Equal(3, CountOf(svg, "<circle"));
    }

    private static int CountOf(string text, string token)
    {
        var count = 0;
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + 1, StringComparison.Ordinal)) count++;
        return count;
    }
}
