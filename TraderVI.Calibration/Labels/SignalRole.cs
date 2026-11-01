namespace Core.ML.Engine.Profit;

/// <summary>
/// Semantic role of a profit model in the decision pipeline.
/// The engine consumes signals by role, not by model name.
/// </summary>
public enum SignalRole
{
    /// <summary>
    /// Primary setup filter — "Is a tradeable setup present?"
    /// Gate: must exceed minimum threshold to consider any trade.
    /// </summary>
    Setup,

    /// <summary>
    /// Upside direction — "Will price rise ≥ X% in N days?"
    /// Used for DirectionEdge and composite scoring.
    /// </summary>
    DirectionUp,

    /// <summary>
    /// Downside veto — "Will price fall ≥ X% in N days?"
    /// Used as binary veto AND continuous penalty.
    /// </summary>
    Veto,

    /// <summary>
    /// Volatility/regime confirmation — "Is a big move expected?"
    /// Adds conviction but doesn't drive the decision alone.
    /// </summary>
    Confirmation,

    /// <summary>
    /// Cross-sectional momentum — "Is this outperforming the benchmark?"
    /// Ranking tiebreaker and composite contributor.
    /// </summary>
    Momentum
}