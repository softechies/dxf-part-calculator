using DxfPartCalculator.Analysis;
using DxfPartCalculator.Dxf;
using Xunit;

namespace DxfPartCalculator.Tests;

public class AnalyzerTests
{
    private static PartReport Analyze(DxfBuilder dxf, AnalysisOptions? options = null) =>
        PartAnalyzer.Analyze(DxfReader.Parse(dxf.ToString()), "test.dxf", options);

    private static void Near(double expected, double actual, double relative = 1e-4) =>
        Assert.True(Math.Abs(expected - actual) <= Math.Max(1e-6, Math.Abs(expected) * relative), $"expected {expected}, got {actual}");

    [Fact]
    public void Rectangle_from_four_separate_lines()
    {
        var r = Analyze(new DxfBuilder().Units(4)
            .Line(0, 0, 100, 0).Line(100, 0, 100, 50).Line(100, 50, 0, 50).Line(0, 50, 0, 0));

        Near(300, r.CutLengthMm);
        Near(5000, r.NetAreaMm2);
        Assert.Equal(1, r.Pierces);
        Assert.Equal(1, r.OuterContours);
        Near(100, r.WidthMm);
        Near(50, r.HeightMm);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Plate_with_circular_hole()
    {
        var r = Analyze(new DxfBuilder().Units(4).Rectangle(0, 0, 100, 50).Circle(50, 25, 10));

        Near(300 + 20 * Math.PI, r.CutLengthMm);
        Near(5000 - 100 * Math.PI, r.NetAreaMm2);
        Assert.Equal(2, r.Pierces);
        Assert.Equal(1, r.Holes);
    }

    [Fact]
    public void Slot_with_bulge_arcs()
    {
        // 50 x 20 rectangle with a semicircle on each short side.
        var r = Analyze(new DxfBuilder().Units(4)
            .LwPolyline(true, (0, 0, 0), (50, 0, 1), (50, 20, 0), (0, 20, 1)));

        Near(100 + 20 * Math.PI, r.CutLengthMm);
        Near(50 * 20 + 100 * Math.PI, r.NetAreaMm2);
        Near(70, r.WidthMm);
    }

    [Fact]
    public void Slot_from_shuffled_lines_and_arcs_joins_into_one_contour()
    {
        var r = Analyze(new DxfBuilder().Units(4)
            .Arc(0, 10, 10, 90, 270)        // left end
            .Line(50, 20, 0, 20)            // top, drawn right to left
            .Arc(50, 10, 10, 270, 90)       // right end, crosses 0 degrees
            .Line(0, 0, 50, 0));            // bottom

        Assert.Equal(1, r.Pierces);
        Assert.Equal(0, r.OpenContours);
        Near(100 + 20 * Math.PI, r.CutLengthMm);
        Near(1000 + 100 * Math.PI, r.NetAreaMm2);
    }

    [Fact]
    public void Island_inside_hole_counts_as_material()
    {
        var r = Analyze(new DxfBuilder().Units(4)
            .Rectangle(0, 0, 100, 100)
            .Rectangle(20, 20, 60, 60)
            .Rectangle(40, 40, 20, 20));

        Near(10000 - 3600 + 400, r.NetAreaMm2);
        Assert.Equal(2, r.OuterContours);
        Assert.Equal(1, r.Holes);
    }

    [Fact]
    public void Two_parts_side_by_side()
    {
        var r = Analyze(new DxfBuilder().Units(4).Rectangle(0, 0, 10, 10).Rectangle(20, 0, 10, 10));

        Assert.Equal(2, r.OuterContours);
        Assert.Equal(0, r.Holes);
        Near(200, r.NetAreaMm2);
        Near(30, r.WidthMm);
    }

    [Fact]
    public void Scaled_block_insert()
    {
        var r = Analyze(new DxfBuilder().Units(4)
            .Block("HOLE", b => b.Circle(0, 0, 5))
            .Rectangle(0, 0, 100, 100)
            .Insert("HOLE", 25, 25, 2, 2)
            .Insert("HOLE", 75, 75, 2, 2));

        Assert.Equal(2, r.Holes);
        Near(400 + 2 * 20 * Math.PI, r.CutLengthMm);
        Near(10000 - 2 * Math.PI * 100, r.NetAreaMm2);
    }

    [Fact]
    public void Rotated_block_with_base_point()
    {
        // A 10 x 4 rectangle, base point at its corner (10,0), rotated 90 degrees and placed at the origin.
        var r = Analyze(new DxfBuilder().Units(4)
            .Block("BAR", b => b.Rectangle(0, 0, 10, 4), baseX: 10, baseY: 0)
            .Insert("BAR", 0, 0, rotationDeg: 90));

        Near(4, r.WidthMm);
        Near(10, r.HeightMm);
        var xs = r.Contours.Single().Points.Select(p => p.X).ToList();
        Near(-4, xs.Min());
        Near(0, xs.Max());
    }

    [Fact]
    public void Non_uniform_scale_turns_circle_into_ellipse()
    {
        var r = Analyze(new DxfBuilder().Units(4)
            .Block("C", b => b.Circle(0, 0, 10))
            .Insert("C", 0, 0, 2, 1));

        Near(Math.PI * 20 * 10, r.NetAreaMm2, 1e-3);
        // Ramanujan's approximation for the perimeter of a 20 x 10 ellipse
        const double a = 20, b = 10;
        var h = Math.Pow(a - b, 2) / Math.Pow(a + b, 2);
        Near(Math.PI * (a + b) * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h))), r.CutLengthMm, 1e-3);
    }

    [Fact]
    public void Inch_drawing_is_converted_to_mm()
    {
        var r = Analyze(new DxfBuilder().Units(1).Rectangle(0, 0, 1, 1));

        Assert.Equal("in", r.Units);
        Near(4 * 25.4, r.CutLengthMm);
        Near(25.4 * 25.4, r.NetAreaMm2);
    }

    [Fact]
    public void Missing_units_are_assumed_mm_with_warning_and_can_be_forced()
    {
        var dxf = new DxfBuilder().Rectangle(0, 0, 1, 1);

        var assumed = Analyze(dxf);
        Assert.True(assumed.UnitsAssumed);
        Assert.Contains(assumed.Warnings, w => w.Contains("assuming millimetres"));

        var forced = Analyze(dxf, new AnalysisOptions { Units = "cm" });
        Near(40, forced.CutLengthMm);
    }

    [Fact]
    public void Duplicate_entities_are_ignored()
    {
        var r = Analyze(new DxfBuilder().Units(4)
            .Line(0, 0, 10, 0).Line(10, 0, 0, 0)   // same line twice, once reversed
            .Line(10, 0, 10, 10).Line(10, 10, 0, 10).Line(0, 10, 0, 0)
            .Circle(5, 5, 2).Circle(5, 5, 2));

        Assert.Equal(2, r.Pierces);
        Near(40 + 4 * Math.PI, r.CutLengthMm);
        Assert.Contains(r.Warnings, w => w.Contains("duplicate"));
    }

    [Fact]
    public void Circle_made_of_two_arcs_is_not_mistaken_for_a_duplicate()
    {
        var r = Analyze(new DxfBuilder().Units(4).Arc(0, 0, 10, 0, 180).Arc(0, 0, 10, 180, 360));

        Assert.Equal(1, r.Pierces);
        Near(100 * Math.PI, r.NetAreaMm2, 1e-3);
    }

    [Fact]
    public void Open_line_is_reported()
    {
        var r = Analyze(new DxfBuilder().Units(4).Rectangle(0, 0, 10, 10).Line(2, 5, 8, 5));

        Assert.Equal(1, r.OpenContours);
        Assert.Equal(2, r.Pierces);
        Assert.Contains(r.Warnings, w => w.Contains("open contour"));
    }

    [Fact]
    public void Ignored_layers_and_annotations_are_skipped()
    {
        var r = Analyze(new DxfBuilder().Units(4)
            .Rectangle(0, 0, 10, 10)
            .Circle(100, 100, 50, layer: "DIMENSIONS")
            .Text("PART-001"),
            new AnalysisOptions { IgnoreLayers = ["DIM*"] });

        Assert.Equal(1, r.Pierces);
        Assert.Equal(1, r.Skipped["TEXT"]);
        Assert.Contains(r.Skipped.Keys, k => k.Contains("DIMENSIONS"));
        Assert.Empty(r.Warnings); // text is expected, not a warning
    }

    [Fact]
    public void Full_ellipse()
    {
        var r = Analyze(new DxfBuilder().Units(4).Ellipse(0, 0, 10, 0, 0.5));

        Assert.Equal(1, r.OuterContours);
        Near(Math.PI * 10 * 5, r.NetAreaMm2, 1e-3);
    }

    [Fact]
    public void Mirrored_arc_uses_object_coordinate_system()
    {
        // OCS extrusion (0,0,-1): X is flipped, so center (10,0) lands at (-10,0).
        var r = Analyze(new DxfBuilder().Units(4).Arc(10, 0, 5, 0, 90, mirrored: true));

        var xs = r.Contours.Single().Points.Select(p => p.X).ToList();
        Near(-10, xs.Max());
        Near(-15, xs.Min());
    }

    [Fact]
    public void Quote_combines_cut_time_pierces_and_material()
    {
        var r = Analyze(new DxfBuilder().Units(4).Rectangle(0, 0, 1000, 500).Circle(500, 250, 100));
        var q = QuoteCalculator.Calculate(r, new QuoteOptions
        {
            CutSpeedMmPerMin = 1000, PierceSeconds = 3, RatePerHour = 60, MaterialPerM2 = 20, Quantity = 3
        });

        var cut = 3000 + 200 * Math.PI;
        Near(cut / 1000 + 2 * 3 / 60.0, q.CutMinutesEach);
        Near(q.CutMinutesEach, q.MachineCostEach);  // 60 per hour = 1 per minute
        Near(10, q.MaterialCostEach);               // 0.5 m² x 20
        Near(q.TotalEach * 3, q.Total);
    }

    [Fact]
    public void Binary_dxf_is_rejected_with_clear_message()
    {
        var bytes = "AutoCAD Binary DXF\r\n\u001a\0"u8.ToArray();
        var ex = Assert.Throws<DxfFormatException>(() => DxfReader.Read(new MemoryStream(bytes)));
        Assert.Contains("ASCII", ex.Message);
    }

    [Fact]
    public void Sample_files_parse()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "samples");
        var files = Directory.GetFiles(folder, "*.dxf");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var r = PartAnalyzer.Analyze(file);
            Assert.True(r.CutLengthMm > 0, file);
            Assert.True(r.NetAreaMm2 > 0, file);
            Assert.Equal(0, r.OpenContours);
        }
    }
}
