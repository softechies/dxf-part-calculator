using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DxfPartCalculator.Analysis;

namespace DxfPartCalculator.Output;

public static class TextReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(PartReport r, QuoteResult? quote)
    {
        var sb = new StringBuilder();
        void Row(string label, string value) => sb.AppendLine($"{label,-16}{value}");

        Row("File", r.File);
        Row("Units", r.UnitsAssumed ? $"{r.Units} (assumed)" : r.Units);
        Row("Size", $"{F(r.WidthMm)} x {F(r.HeightMm)} mm");
        Row("Cut length", $"{F(r.CutLengthMm)} mm");
        Row("Pierces", $"{r.Pierces}  ({r.OuterContours} outer, {r.Holes} hole{(r.Holes == 1 ? "" : "s")}, {r.OpenContours} open)");
        Row("Part area", $"{F(r.NetAreaMm2)} mm²  ({F(r.NetAreaMm2 / 100)} cm²)");
        Row("Bounding area", $"{F(r.BoundingAreaMm2)} mm²");
        if (r.BoundingAreaMm2 > 0 && r.NetAreaMm2 > 0)
        {
            Row("Utilisation", $"{r.NetAreaMm2 / r.BoundingAreaMm2:P1} of the bounding rectangle");
        }
        Row("Entities", Join(r.Entities));
        if (r.Skipped.Count > 0) Row("Skipped", Join(r.Skipped));

        if (quote is not null)
        {
            sb.AppendLine();
            Row("Cut time", $"{quote.CutMinutesEach.ToString("0.00", Inv)} min each");
            Row("Machine", Money(quote.MachineCostEach) + " each");
            Row("Material", Money(quote.MaterialCostEach) + " each (bounding rectangle)");
            Row("Total", quote.Quantity > 1
                ? $"{Money(quote.TotalEach)} each, {Money(quote.Total)} for {quote.Quantity}"
                : Money(quote.TotalEach));
        }

        foreach (var warning in r.Warnings)
        {
            sb.AppendLine("! " + warning);
        }

        return sb.ToString().TrimEnd();
    }

    public static string Table(IReadOnlyList<PartReport> reports)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"File",-28} {"W x H (mm)",-20} {"Cut (mm)",12} {"Pierces",8} {"Area (mm²)",14}");
        foreach (var r in reports)
        {
            var name = r.File.Length > 28 ? r.File[..27] + "…" : r.File;
            sb.AppendLine($"{name,-28} {$"{F(r.WidthMm)} x {F(r.HeightMm)}",-20} {F(r.CutLengthMm),12} {r.Pierces,8} {F(r.NetAreaMm2),14}");
        }
        sb.AppendLine($"{"Total",-28} {"",-20} {F(reports.Sum(r => r.CutLengthMm)),12} {reports.Sum(r => r.Pierces),8} {F(reports.Sum(r => r.NetAreaMm2)),14}");
        return sb.ToString().TrimEnd();
    }

    private static string F(double v) => v.ToString("#,0.00", Inv);
    private static string Money(double v) => v.ToString("#,0.00", Inv);
    private static string Join(IReadOnlyDictionary<string, int> counts) =>
        counts.Count == 0 ? "-" : string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}"));
}

public static class JsonReport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Render(IEnumerable<(PartReport Part, QuoteResult? Quote)> items)
    {
        var data = items.Select(i => new
        {
            file = i.Part.File,
            units = i.Part.Units,
            unitsAssumed = i.Part.UnitsAssumed,
            widthMm = Math.Round(i.Part.WidthMm, 3),
            heightMm = Math.Round(i.Part.HeightMm, 3),
            cutLengthMm = Math.Round(i.Part.CutLengthMm, 3),
            pierces = i.Part.Pierces,
            outerContours = i.Part.OuterContours,
            holes = i.Part.Holes,
            openContours = i.Part.OpenContours,
            netAreaMm2 = Math.Round(i.Part.NetAreaMm2, 3),
            boundingAreaMm2 = Math.Round(i.Part.BoundingAreaMm2, 3),
            entities = i.Part.Entities,
            skipped = i.Part.Skipped,
            warnings = i.Part.Warnings,
            quote = i.Quote is null ? null : new
            {
                cutMinutesEach = Math.Round(i.Quote.CutMinutesEach, 3),
                machineCostEach = Math.Round(i.Quote.MachineCostEach, 2),
                materialCostEach = Math.Round(i.Quote.MaterialCostEach, 2),
                totalEach = Math.Round(i.Quote.TotalEach, 2),
                quantity = i.Quote.Quantity,
                total = Math.Round(i.Quote.Total, 2)
            }
        }).ToList();

        return JsonSerializer.Serialize(data.Count == 1 ? (object)data[0] : data, Options);
    }
}
