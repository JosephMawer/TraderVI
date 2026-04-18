namespace Core.Trader;

/// <summary>
/// Runtime configuration derived from the active StrategyVersion.
/// Every threshold has a sensible default so the system works without a DB row.
/// All numeric properties are tunable dials — defaults represent reasonable starting
/// points intended to be refined based on Hercules training outputs (AUC, optimal
/// threshold, decile lift) and live performance feedback.
/// </summary>
public sealed record StrategyConfig
{
    // ── Composite gate ──

    /// <summary>
    /// Minimum blended composite score required to pass a candidate into the ranked shortlist.
    /// The composite combines breakout probability and direction edge into a single conviction score.
    /// Lower = more candidates pass, higher = only high-conviction setups survive.
    /// Tune using decile lift tables from Hercules; raise if false-positive rate is too high.
    /// Default: 0.35
    /// </summary>
    public double MinCompositeScore { get; init; } = 0.35;

    /// <summary>
    /// If a stock's breakout probability meets or exceeds this threshold, it may bypass
    /// the composite score gate entirely — the setup alone is considered strong enough.
    /// Acts as a safety valve so exceptional breakouts aren't filtered out by a weak edge signal.
    /// Only applies when <see cref="StrongEdgeOverride"/> is also met.
    /// Default: 0.60
    /// </summary>
    public double StrongBreakoutOverride { get; init; } = 0.60;

    /// <summary>
    /// Minimum direction edge required to activate the composite gate override alongside
    /// <see cref="StrongBreakoutOverride"/>. Prevents pure breakout plays with no directional
    /// conviction from sneaking through. Both conditions must be true for the override to fire.
    /// Default: 0.10
    /// </summary>
    public double StrongEdgeOverride { get; init; } = 0.10;

    // ── Direction gate ──

    /// <summary>
    /// Minimum DirectionEdge score required for a long candidate.
    /// DirectionEdge = P(up) − P(down), derived from BinaryUp10 and BinaryDown10 models.
    /// A positive value means the model favours upside over downside. Values closer to 0
    /// indicate low conviction; raise this threshold to reduce noise trades.
    /// Default: 0.05
    /// </summary>
    public double MinDirectionEdge { get; init; } = 0.05;

    /// <summary>
    /// Minimum raw up-probability from the BinaryUp10 model for a stock to qualify as a long.
    /// Acts as an absolute floor independent of the down probability, ensuring the model
    /// sees at least some upside potential before a trade is considered.
    /// Default: 0.25
    /// </summary>
    public double MinUpProb { get; init; } = 0.25;

    // ── Setup gate ──

    /// <summary>
    /// Minimum breakout probability from the BreakoutEnhanced model for a candidate to enter
    /// the pipeline. This is the first filter — stocks below this threshold are dropped before
    /// direction or composite scoring. Raise to reduce the universe to high-quality setups;
    /// lower to widen the funnel at the cost of more noise.
    /// Default: 0.30
    /// </summary>
    public double MinBreakoutProb { get; init; } = 0.30;

    // ── Down probability gate ──

    /// <summary>
    /// Maximum tolerated down-probability from the BinaryDown10 model before a trade is vetoed.
    /// BinaryDown10 predicts the probability that a stock drops ≥ 4% within the next 10 trading days.
    /// If that probability meets or exceeds this threshold, the trade is blocked regardless of how
    /// strong the breakout or direction signals are — this is a capital preservation rule.
    /// 
    /// Tighter (lower) = safer, but fewer trades pass (e.g., 0.20 may filter almost everything
    /// in a volatile small-cap universe). Looser (higher) = more trades, more downside exposure.
    /// At small capital sizes ($700 range), 0.30–0.35 is a reasonable middle ground: permissive
    /// enough to find trades, strict enough to block the worst tail-risk names.
    /// 
    /// Tune against Hercules output: if the down model's AUC is low, widen this threshold;
    /// if it is high and well-calibrated, tighten it.
    /// Default: 0.35
    /// </summary>
    public double MaxDownProb { get; init; } = 0.35;

    // ── Breadth gate ──

    /// <summary>
    /// Advance-Decline Line (ADL) change threshold below which all new longs are vetoed.
    /// The ADL is a rule-based market breadth indicator (not an ML feature). When the ADL
    /// deteriorates by this fraction or more relative to its recent baseline, it signals
    /// broad market weakness and the system pauses new entries.
    /// More negative = less sensitive (allows trading through moderate weakness);
    /// less negative (closer to 0) = more protective but risks missing rallies.
    /// Default: −0.30
    /// </summary>
    public double BreadthVetoThreshold { get; init; } = -0.30;

    // ── Position sizing ──

    /// <summary>
    /// Hard stop-loss level expressed as a negative percentage of entry price.
    /// A position that moves against the trade by this amount is exited immediately
    /// by Sentinel (planned), overriding all model predictions. Capital preservation
    /// rules always take precedence over signal conviction.
    /// Default: −10% (−0.10)
    /// </summary>
    public double StopLossPercent { get; init; } = -0.10;

    /// <summary>
    /// Early-warning level expressed as a negative percentage of entry price.
    /// When a position reaches this drawdown, Sentinel raises an alert or tightens
    /// the trailing stop — but does not necessarily exit. Use to identify deteriorating
    /// trades before the hard stop is hit.
    /// Default: −5% (−0.05)
    /// </summary>
    public double WarningPercent { get; init; } = -0.05;

    /// <summary>
    /// Maximum number of concurrent open positions. At small capital sizes a single
    /// concentrated position maximises the impact of high-conviction picks; increase
    /// as capital grows to allow diversification without over-diluting each bet.
    /// Default: 1
    /// </summary>
    public int MaxPositions { get; init; } = 1;

    // ── Regime ──

    /// <summary>
    /// When true, requires the benchmark (XIU — iShares S&amp;P/TSX 60 ETF) to be in an
    /// uptrend or show a positive recent return before any new longs are initiated.
    /// This is the market regime filter: trading with the broad market tailwind
    /// significantly reduces the risk of fighting a falling tide.
    /// Set to false only when backtesting or explicitly testing counter-trend strategies.
    /// Default: true
    /// </summary>
    public bool RequireBenchmarkUptrend { get; init; } = true;

    /// <summary>
    /// Default configuration when no strategy version is active.
    /// </summary>
    public static StrategyConfig Default => new();
}