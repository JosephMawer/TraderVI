namespace Core.Indicators.Granville;

/// <summary>
/// The 20+ indicator categories from Granville's "56 Day-to-Day Basic Indicators."
/// Each category contains one or more numbered indicators (1–56).
/// </summary>
public enum IndicatorCategory
{
    Plurality,          // Indicators 1–4
    Disparity,          // Indicators 5–6
    Leadership,         // Indicators 7–10
    Features,           // Indicators 11–14: most-active stock gains/losses vs. benchmark
    Weighting,          // Indicators 15–16: narrow-advance warning gate (ADR-0003)
    Genuity,            // Indicators 17–20: US confirming-index validation (ADR-0004)
    Dullness,           // future
    Overdueness,        // future
    LightVolume,        // Indicators 25–28: light-volume tape × leadership quality (ADR-0006)
    HeavyVolume,        // future
    Reversals,          // future
    GoldIndicator,      // future
    ThreeDayRule,       // future
    Churning,           // future
    News,               // future
    ErraticPriceMovement, // future
    GeneralMotorsIndicator, // future
    TheClosing,         // future
    OddLots,           // future
    ReboundsAndDeclines, // future
    HighsAndLows        // future
}