using System.Globalization;

namespace DxfPartCalculator.Dxf;

/// <summary>One entity as a flat list of (group code, value) pairs, in file order.</summary>
public sealed class DxfEntity(string type)
{
    public string Type { get; } = type;
    public List<(int Code, string Value)> Pairs { get; } = [];

    /// <summary>Child entities: VERTEX records of a POLYLINE.</summary>
    public List<DxfEntity> Children { get; } = [];

    public string Layer => GetString(8) ?? "0";

    public string? GetString(int code)
    {
        foreach (var (c, v) in Pairs)
        {
            if (c == code) return v;
        }
        return null;
    }

    public double GetDouble(int code, double fallback = 0)
    {
        var s = GetString(code);
        return s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    public int GetInt(int code, int fallback = 0)
    {
        var s = GetString(code);
        return s is not null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
    }

    /// <summary>All values for a repeated group code, in order (e.g. LWPOLYLINE vertices, spline knots).</summary>
    public IEnumerable<double> GetDoubles(int code)
    {
        foreach (var (c, v) in Pairs)
        {
            if (c == code && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                yield return d;
        }
    }
}

public sealed class DxfBlock(string name)
{
    public string Name { get; } = name;
    public double BaseX { get; set; }
    public double BaseY { get; set; }
    public List<DxfEntity> Entities { get; } = [];
}

public sealed class DxfDocument
{
    public Dictionary<string, string> Header { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DxfBlock> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DxfEntity> Entities { get; } = [];

    /// <summary>$INSUNITS, 0 when missing.</summary>
    public int InsUnits => Header.TryGetValue("$INSUNITS", out var v) && int.TryParse(v, out var u) ? u : 0;
}
