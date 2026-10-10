namespace DxfPartCalculator.Analysis;

/// <summary>Machine and material settings for a rough cutting quote.</summary>
public sealed class QuoteOptions
{
    /// <summary>Cutting feed rate in mm per minute for this material and thickness.</summary>
    public double CutSpeedMmPerMin { get; set; }

    /// <summary>Time to pierce the sheet once, in seconds.</summary>
    public double PierceSeconds { get; set; } = 0.5;

    /// <summary>Machine rate per hour, any currency.</summary>
    public double RatePerHour { get; set; }

    /// <summary>Sheet price per square metre. Charged on the part's bounding rectangle.</summary>
    public double MaterialPerM2 { get; set; }

    public int Quantity { get; set; } = 1;

    public bool IsSet => CutSpeedMmPerMin > 0;
}

public sealed record QuoteResult(
    double CutMinutesEach,
    double MachineCostEach,
    double MaterialCostEach,
    int Quantity)
{
    public double TotalEach => MachineCostEach + MaterialCostEach;
    public double Total => TotalEach * Quantity;
}

public static class QuoteCalculator
{
    public static QuoteResult Calculate(PartReport part, QuoteOptions options)
    {
        if (options.CutSpeedMmPerMin <= 0)
            throw new ArgumentException("Cut speed must be greater than zero");

        var cutMinutes = part.CutLengthMm / options.CutSpeedMmPerMin + part.Pierces * options.PierceSeconds / 60;
        var machine = cutMinutes / 60 * options.RatePerHour;
        var material = part.BoundingAreaMm2 / 1_000_000 * options.MaterialPerM2;
        return new QuoteResult(cutMinutes, machine, material, Math.Max(1, options.Quantity));
    }
}
