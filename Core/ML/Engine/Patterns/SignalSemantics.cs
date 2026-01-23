namespace Core.ML.Engine.Patterns;

public enum SignalSemantics
{
    /// <summary>
    /// Default safe behavior: pattern true is bullish, false is not bearish.
    /// </summary>
    BullishWhenTrue,

    /// <summary>
    /// Pattern true is bearish, false is not bullish.
    /// </summary>
    BearishWhenTrue,

    /// <summary>
    /// True implies Buy, false implies Sell (with hold band in-between).
    /// Use sparingly—many patterns are not symmetric like this.
    /// </summary>
    BullishBearishSymmetric
}