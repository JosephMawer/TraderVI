namespace Core
{
    public class Constants
    {
        public const string apiKey = "6IQSWE3D7UZHLKTB";

        /// <summary>
        /// Rolling retention window (months) for per-symbol OBV history in dbo.SymbolObv.
        /// Hermes prunes rows older than this after each update; the Sandbox backfill can
        /// override it to load deeper history for testing/backtesting. Safe to prune because
        /// the running cumulative is already baked into the retained rows — deleting the tail
        /// never alters the head. Six months comfortably covers several breakout cycles at the
        /// default 20-session window.
        /// </summary>
        public const int ObvRetentionMonths = 6;

        /// <summary>
        /// Rolling lookback (sessions) each XIU-60 name's OBV must break above/below to
        /// register an UP/DOWN designation when tallying the market-wide Climax (CLX).
        /// Shared in spirit with the per-symbol OBV window — kept separate so CLX can be
        /// tuned independently. Initial default intended to be refined from live calibration
        /// (divergence hit-rate vs XIU); larger = fewer, more significant flips.
        /// Default: 20
        /// </summary>
        public const int ClimaxBreakoutWindow = 20;

        /// <summary>
        /// Graceful-degradation floor: the minimum number of XIU-60 names that must produce
        /// a classifiable OBV series before a CLX reading is trusted/persisted. Mirrors the
        /// Weighting calibration's ≥50/60 coverage requirement so a sparse data day doesn't
        /// emit a misleading net tally. Initial default, tunable as coverage stabilises.
        /// Default: 50
        /// </summary>
        public const int ClimaxMinConstituents = 50;
    }
}
