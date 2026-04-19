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
    Weighting,          // future
    Genuity,            // future
    Dullness,           // future
    Overdueness,        // future
    LightVolume,        // future
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