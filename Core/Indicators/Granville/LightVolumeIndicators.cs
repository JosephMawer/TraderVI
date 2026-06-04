using System.Collections.Generic;

namespace Core.Indicators.Granville;

/// <summary>
/// Granville's Light Volume indicators (#25–#28).
///
/// These indicators read today's tape under a light-volume regime and pair it
/// with the current state of leadership <em>quality</em> (improving / deteriorating).
///
/// Inputs (three independent axes):
///   1. Volume regime — light vs not (XIU volume / SMA20Prior &lt; <see cref="LightVolumeThreshold"/>).
///   2. Direction    — sign of XIU 1-day return, with a small dead-band to suppress flat-tape noise.
///   3. Quality      — <see cref="LeadershipCalculator.ComputeQuality"/> on the breadth series.
///
/// Direction comes from XIU price (a different data source than the breadth
/// series that drive #7–#10), so #25–#28 add new information rather than
/// re-asserting the Leadership group on light-volume days.
///
/// #25: Rise + Deteriorating quality   → rise lacks conviction (Bearish)
/// #26: Rise + Improving quality       → light volume tolerable if leaders carry it (Bullish)
/// #27: Decline + Improving quality    → decline not yet exhausted (Bearish)
/// #28: Decline + Deteriorating quality → selling exhaustion (StrongBullish)
///
/// Reference: Granville, "A Strategy of Daily Stock Market Timing", Light Volume section.
/// </summary>
public sealed class LightVolumeIndicators : IGranvilleIndicatorGroup
{
    public IndicatorCategory Category => IndicatorCategory.LightVolume;
    public string Name => "Light Volume";

    /// <summary>
    /// XIU volume / prior-20-session average must be strictly below this value
    /// for the day to count as "light volume." Default 0.85 (i.e. ≥ 15% below average).
    /// </summary>
    public decimal LightVolumeThreshold { get; init; } = 0.85m;

    /// <summary>
    /// Absolute XIU 1-day return below which the day is treated as directionally flat
    /// (no rise/decline signal fires). Default 10 bps (0.0010).
    /// </summary>
    public decimal DirectionDeadBand { get; init; } = 0.0010m;

    private readonly LeadershipCalculator _leadershipCalculator = new();

    public IReadOnlyList<GranvilleResult> Evaluate(GranvilleMarketContext context)
    {
        var results = new List<GranvilleResult>(1);

        // ── Tape gate ──
        var tape = context.MarketTape;
        if (tape is null || tape.XiuVolumeRatio20 is not decimal ratio || tape.XiuReturn1d is not decimal ret)
        {
            results.Add(NeutralNoData("Light Volume: No Tape",
                "Insufficient XIU tape data (need ≥ 21 sessions for SMA20Prior and a prior close). Skipping #25–#28."));
            return results;
        }

        if (ratio >= LightVolumeThreshold)
        {
            results.Add(Neutral("Light Volume: Not Light",
                $"XIU volume ratio {ratio:F2} ≥ threshold {LightVolumeThreshold:F2}. #25–#28 do not apply today."));
            return results;
        }

        // ── Direction (XIU 1-day, with dead-band) ──
        bool isRise = ret > DirectionDeadBand;
        bool isDecline = ret < -DirectionDeadBand;
        if (!isRise && !isDecline)
        {
            results.Add(Neutral("Light Volume: Flat",
                $"XIU 1-day return {ret:+0.00%;-0.00%;0.00%} within ±{DirectionDeadBand:0.00%} dead-band. No direction fired."));
            return results;
        }

        // ── Leadership quality ──
        if (context.LeadershipHistory is not { Count: >= 12 })
        {
            results.Add(NeutralNoData("Light Volume: No Leadership",
                "Insufficient leadership history (need ≥ 12 days) to compute quality for #25–#28."));
            return results;
        }

        var quality = _leadershipCalculator.ComputeQuality(context.LeadershipHistory);
        if (quality == LeadershipQuality.Indeterminate || quality == LeadershipQuality.Stable)
        {
            results.Add(Neutral("Light Volume: Neutral Quality",
                $"Leadership quality: {quality}. #25–#28 require Improving or Deteriorating."));
            return results;
        }

        string direction = isRise ? "Rise" : "Decline";
        string tapeFacts = $"XIU ret={ret:+0.00%;-0.00%}, vol ratio={ratio:F2} (< {LightVolumeThreshold:F2}), quality={quality}.";

        // ── 2×2 firing matrix ──
        if (isRise && quality == LeadershipQuality.Deteriorating)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 25,
                Category: IndicatorCategory.LightVolume,
                Name: "Light Volume #25: Rise on Light Volume, Poor Leadership",
                Signal: IndicatorSignal.Bearish,
                GranvillePoints: -1,
                Description: $"{direction} on light volume with deteriorating leadership quality — " +
                             $"rise lacks conviction. {tapeFacts}"));
        }
        else if (isRise && quality == LeadershipQuality.Improving)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 26,
                Category: IndicatorCategory.LightVolume,
                Name: "Light Volume #26: Rise on Light Volume, Good Leadership",
                Signal: IndicatorSignal.Bullish,
                GranvillePoints: +1,
                Description: $"{direction} on light volume but leadership quality is improving — " +
                             $"light volume is not necessarily bearish when leaders carry the tape. {tapeFacts}"));
        }
        else if (isDecline && quality == LeadershipQuality.Improving)
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 27,
                Category: IndicatorCategory.LightVolume,
                Name: "Light Volume #27: Decline on Light Volume, Good Leadership",
                Signal: IndicatorSignal.Bearish,
                GranvillePoints: -1,
                Description: $"{direction} on light volume but leadership quality is improving — " +
                             $"decline is not necessarily bullish; sellers haven't capitulated yet. {tapeFacts}"));
        }
        else // isDecline && Deteriorating
        {
            results.Add(new GranvilleResult(
                IndicatorNumber: 28,
                Category: IndicatorCategory.LightVolume,
                Name: "Light Volume #28: Decline on Light Volume, Deteriorating Leadership",
                Signal: IndicatorSignal.StrongBullish,
                GranvillePoints: +2,
                Description: $"{direction} on light volume with deteriorating leadership quality — " +
                             $"classic selling-exhaustion pattern; especially bullish. {tapeFacts}"));
        }

        return results;
    }

    private static GranvilleResult Neutral(string name, string description) =>
        new(IndicatorNumber: 0,
            Category: IndicatorCategory.LightVolume,
            Name: name,
            Signal: IndicatorSignal.Neutral,
            GranvillePoints: 0,
            Description: description);

    private static GranvilleResult NeutralNoData(string name, string description) =>
        new(IndicatorNumber: 0,
            Category: IndicatorCategory.LightVolume,
            Name: name,
            Signal: IndicatorSignal.Neutral,
            GranvillePoints: 0,
            Description: description);
}
