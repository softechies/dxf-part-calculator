using System.Globalization;
using DxfPartCalculator.Analysis;
using DxfPartCalculator.Dxf;
using DxfPartCalculator.Output;

const string Usage = """
    dxfcalc: cut length, pierce count, area and a rough quote from DXF files.

    Usage: dxfcalc <file.dxf> [more files or folders...] [options]

    Analysis
      --units <mm|cm|m|in|ft>     override the drawing units ($INSUNITS)
      --ignore-layers <a,b,...>   skip layers, wildcards allowed: --ignore-layers "DIM*,TEXT,BEND"
      --join-tolerance <mm>       gap allowed when joining entities into contours (default 0.05)
      --svg <file|folder>         write a preview: outer black, holes blue, open paths orange, pierces red
      --json                      machine readable output

    Quote (optional)
      --speed <mm/min>            cutting speed for the material and thickness
      --pierce <seconds>          time per pierce (default 0.5)
      --rate <per hour>           machine rate
      --material <per m²>         sheet price, charged on the bounding rectangle
      --qty <n>                   quantity

    Example:
      dxfcalc bracket.dxf --speed 3500 --pierce 0.8 --rate 90 --material 45 --qty 20 --svg bracket.svg
    """;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(Usage);
    return 0;
}

var files = new List<string>();
var analysis = new AnalysisOptions();
var quote = new QuoteOptions();
string? svgTarget = null;
var json = false;

try
{
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
        double Num() => double.Parse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture);

        switch (args[i])
        {
            case "--units": analysis.Units = Next(); break;
            case "--ignore-layers": analysis.IgnoreLayers.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
            case "--join-tolerance": analysis.JoinToleranceMm = Num(); break;
            case "--svg": svgTarget = Next(); break;
            case "--json": json = true; break;
            case "--speed": quote.CutSpeedMmPerMin = Num(); break;
            case "--pierce": quote.PierceSeconds = Num(); break;
            case "--rate": quote.RatePerHour = Num(); break;
            case "--material": quote.MaterialPerM2 = Num(); break;
            case "--qty": quote.Quantity = (int)Num(); break;
            default:
                if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unknown option {args[i]}");
                files.AddRange(ExpandInput(args[i]));
                break;
        }
    }
}
catch (Exception ex) when (ex is ArgumentException or FormatException or FileNotFoundException or DirectoryNotFoundException)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 2;
}

if (files.Count == 0)
{
    Console.Error.WriteLine("No DXF files given.");
    return 2;
}

if ((quote.RatePerHour > 0 || quote.MaterialPerM2 > 0) && !quote.IsSet)
{
    Console.Error.WriteLine("A quote needs --speed (mm/min).");
    return 2;
}

var results = new List<(PartReport Part, QuoteResult? Quote)>();
var failed = 0;

foreach (var file in files)
{
    try
    {
        var part = PartAnalyzer.Analyze(file, analysis);
        var q = quote.IsSet ? QuoteCalculator.Calculate(part, quote) : null;
        results.Add((part, q));

        if (svgTarget is not null)
        {
            var svgPath = files.Count == 1 && svgTarget.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? svgTarget
                : Path.Combine(svgTarget, Path.GetFileNameWithoutExtension(file) + ".svg");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(svgPath))!);
            File.WriteAllText(svgPath, SvgPreview.Render(part));
        }
    }
    catch (Exception ex) when (ex is DxfFormatException or IOException or ArgumentException)
    {
        failed++;
        Console.Error.WriteLine($"{Path.GetFileName(file)}: {ex.Message}");
    }
}

if (json)
{
    Console.WriteLine(JsonReport.Render(results));
}
else
{
    foreach (var (part, q) in results)
    {
        Console.WriteLine(TextReport.Render(part, q));
        Console.WriteLine();
    }
    if (results.Count > 1)
    {
        Console.WriteLine(TextReport.Table(results.Select(r => r.Part).ToList()));
    }
}

return failed == 0 ? 0 : 1;

static IEnumerable<string> ExpandInput(string input)
{
    if (Directory.Exists(input))
    {
        return Directory.EnumerateFiles(input, "*.dxf", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }
    if (File.Exists(input))
    {
        return [input];
    }
    throw new FileNotFoundException($"Not found: {input}");
}
