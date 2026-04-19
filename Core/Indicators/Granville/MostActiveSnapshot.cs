namespace Core.Indicators.Granville;

/// <summary>
/// Represents a single stock from the "most active by volume" list for a given day.
/// Used by Granville's Most Active indicators (#11–#14).
/// </summary>
/// <param name="Ticker">Stock symbol.</param>
/// <param name="Open">Opening price.</param>
/// <param name="Close">Closing price.</param>
/// <param name="Volume">Daily volume (used to rank activity).</param>
public sealed record MostActiveSnapshot(
    string Ticker,
    decimal Open,
    decimal Close,
    long Volume)
{
    /// <summary>True if the stock closed above its open (gain).</summary>
    public bool IsGain => Close > Open;

    /// <summary>True if the stock closed below its open (loss).</summary>
    public bool IsLoss => Close < Open;
}