using Core.Db;
using Core.DataQuality;
using Core.Indicators;
using Core.Indicators.Granville;
using Core.ML;
using Core.TMX.Models.Domain;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Core.Runtime;

/// <summary>
/// Builds two Delphi output reports:
/// 1. Diagnostic — detailed, machine-parseable, for feeding back into Copilot analysis
/// 2. Summary — concise, human-readable market overview and recommendation
/// </summary>
public sealed class DelphiReportBuilder
{
    // ── Inputs (set before calling Build) ──
    public DateTime RecommendationDate { get; set; }
    public DateTime MarketDataAsOf { get; set; }
    public MarketRegime? Regime { get; set; }
    public IReadOnlyList<ADLineEntry> AdLine { get; set; } = [];
    public double BreadthScore { get; set; }
    public bool BearishDivergence { get; set; }
    public GranvilleDailyForecast? Granville { get; set; }
    public WeightingSnapshot? Weighting { get; set; }
    public MarketTapeContext? MarketTape { get; set; }
    public IReadOnlyList<SectorIndexSnapshot> SectorSnapshots { get; set; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<UsIndexBar>> UsIndexBars { get; set; } = new Dictionary<string, IReadOnlyList<UsIndexBar>>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<RankedPick> TopPicks { get; set; } = [];
    public RankedPick? BestPick { get; set; }
    public PositionSizeResult? Size { get; set; }
    public Dictionary<string, Core.RelativeStrength.RelativeStrengthRow> RsScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IReadOnlyList<DailyBar>> AllBars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // On-Balance Volume (OBV) field-trend results, keyed by symbol. Reporting-only:
    // the soft ranking tilt is applied in the engine; here we surface the verdict
    // (Rising/Falling/Doubtful) and latest UP/DOWN designation as confirmation.
    public Dictionary<string, ObvFieldTrendResult> ObvResults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double ObvSignalWeight { get; set; }

    // Market Climax (CLX) — standalone volume-breadth regime signal (sibling to the A/D Line).
    // Diagnostic-only in v1: we surface the latest net tally and the confirmation/divergence
    // verdict vs XIU. MarketClimax is sorted ascending by date; ClimaxRegime is the windowed
    // verdict computed in Delphi via MarketClimaxCalculator.ClassifyRegime.
    public IReadOnlyList<MarketClimaxEntry> MarketClimax { get; set; } = [];
    public ClimaxRegimeResult? ClimaxRegime { get; set; }
    public int ClimaxDivergenceWindow { get; set; }
    public int ClimaxDivergenceThreshold { get; set; }
    public int LoadedSymbols { get; set; }
    public int SkippedHistory { get; set; }
    public int SkippedStaleHistory { get; set; }
    public IReadOnlyList<HistoryFreshnessExclusion> StaleHistoryExclusions { get; set; } = [];
    public int SkippedPrice { get; set; }
    public int SkippedLowPrice { get; set; }
    public int SkippedLowVolume { get; set; }
    public int SkippedLeveragedEtp { get; set; }
    public decimal MinPriceFloor { get; set; }
    public long MinVolume20d { get; set; }
    public decimal DeployableCapital { get; set; }

    // ── RS coverage diagnostics (ADR-0010 follow-up) ──
    // Surfaces whether RS composite is actually being computed against real sector
    // data, or silently falling back to XIU (degenerate) or failing entirely due
    // to insufficient sector index history. Without these counters the leaderboard
    // can show `null` RS values with no explanation.
    public int RsFallbackToXiuCount { get; set; }       // symbols that used XIU because no usable sector series was loaded
    public IReadOnlyList<string> RsFallbackSymbols { get; set; } = [];
    public int RsCompositeNullCount { get; set; }       // symbols where CompositeScore came back null (insufficient bars)
    public int RsMinSectorBars { get; set; }            // min #bars across loaded sector indices
    public int RsMaxSectorBars { get; set; }            // max #bars across loaded sector indices
    public int RsBarsRequired { get; set; } = 80;       // max horizon (60) + zWindow (20)

    /// <summary>
    /// Builds the full diagnostic report (detailed, for Copilot/log analysis).
    /// </summary>
    public string BuildDiagnostic()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(new string('═', 80));
        sb.AppendLine("DELPHI DIAGNOSTIC REPORT");
        sb.AppendLine(new string('═', 80));

        sb.AppendLine("\n── Evaluation Dates ──");
        sb.AppendLine($"  Recommendation date: {RecommendationDate:yyyy-MM-dd}  (run date; database PickDate/EvalDate)");
        sb.AppendLine($"  Market data as of:    {MarketDataAsOf:yyyy-MM-dd}  (latest completed TSX session)");
        sb.AppendLine("  Continuation ranking: RScomp + OBV tilt; DirectionEdge and composite tiebreakers");
        sb.AppendLine("  Breakout ranking:     DirectionEdge + RScomp + OBV tilt; DirectionEdge and composite tiebreakers (journaled only)");

        // ── Market Regime ──
        sb.AppendLine("\n── Market Regime ──");
        if (Regime != null)
        {
            sb.AppendLine($"  XIU Uptrend (MA50>MA200): {Regime.IsBenchmarkUptrend}");
            sb.AppendLine($"  XIU 20d Return:           {Regime.BenchmarkReturn20d:P2}");
            sb.AppendLine($"  XIU Volatility Normal:    {Regime.IsVolatilityNormal}");
            sb.AppendLine($"  SPY Uptrend:              {Regime.IsSpyUptrend}");
            sb.AppendLine($"  SPY 20d Positive:         {Regime.IsSpy20dPositive}");
            sb.AppendLine($"  Any Benchmark Uptrend:    {Regime.IsAnyBenchmarkUptrend}");
            sb.AppendLine($"  Both Bearish:             {Regime.IsBothBearish}");
        }
        else
        {
            sb.AppendLine("  [No regime data]");
        }

        // ── A/D Line ──
        sb.AppendLine("\n── Advance-Decline Line ──");
        if (AdLine.Count > 0)
        {
            var latest = AdLine[^1];
            sb.AppendLine($"  Date:             {latest.Date:yyyy-MM-dd}");
            sb.AppendLine($"  Advancers:        {latest.Advancers}");
            sb.AppendLine($"  Decliners:        {latest.Decliners}");
            sb.AppendLine($"  Plurality:        {latest.DailyPlurality:+0;-0}");
            sb.AppendLine($"  Cumulative:       {latest.CumulativeDifferential:+#,0;-#,0;0}");
            sb.AppendLine($"  Breadth Score:    {BreadthScore:+0.00;-0.00}");
            sb.AppendLine($"  Slope (20d):      {AdvanceDeclineCalculator.Slope(AdLine):+0.0;-0.0}");
            sb.AppendLine($"  Above SMA(50):    {AdvanceDeclineCalculator.IsAboveSma(AdLine)}");
            sb.AppendLine($"  Bearish Diverg:   {BearishDivergence}");
        }

        // ── Sector Indices ──
        sb.AppendLine("\n── Sector Indices ──");
        if (SectorSnapshots.Count > 0)
        {
            sb.AppendLine($"  {"Sector",-28} {"Symbol",-8} {"Price",10} {"Change",8} {"%Chg",8}");
            sb.AppendLine($"  {new string('─', 64)}");
            foreach (var s in SectorSnapshots.OrderByDescending(s => s.PercentChange))
            {
                sb.AppendLine($"  {s.SectorName,-28} {s.Symbol,-8} {s.Price,10:F2} {s.PriceChange,8:+0.00;-0.00} {s.PercentChange,7:+0.00;-0.00}%");
            }
        }

        // ── US Confirming Indices (Genuity #17–#20) ──
        sb.AppendLine("\n── US Confirming Indices (Genuity) ──");
        if (UsIndexBars.Count > 0)
        {
            sb.AppendLine($"  {"Symbol",-8} {"LatestDate",-12} {"Close",10} {"1dRet",8} {"5dRet",8} {"Bars",5}");
            sb.AppendLine($"  {new string('─', 56)}");
            foreach (var kvp in UsIndexBars.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var bars = kvp.Value;
                if (bars.Count == 0) continue;
                var last = bars[^1];
                double r1 = bars.Count >= 2 && bars[^2].Close > 0
                    ? (last.Close / bars[^2].Close) - 1.0 : 0;
                double r5 = bars.Count >= 6 && bars[^6].Close > 0
                    ? (last.Close / bars[^6].Close) - 1.0 : 0;
                sb.AppendLine($"  {kvp.Key,-8} {last.Date,-12:yyyy-MM-dd} {last.Close,10:F2} {r1,7:+0.00%;-0.00%;0.00%} {r5,7:+0.00%;-0.00%;0.00%} {bars.Count,5}");
            }
        }
        else
        {
            sb.AppendLine("  [No US index data]");
        }

        // ── Market Tape (Light Volume #25–#28) ──
        sb.AppendLine("\n── Market Tape (Light Volume) ──");
        if (MarketTape is not null)
        {
            sb.AppendLine($"  Date:                 {MarketTape.Date:yyyy-MM-dd}");
            sb.AppendLine($"  XIU close:            {(MarketTape.XiuClose is decimal xc ? xc.ToString("F2") : "n/a")}");
            sb.AppendLine($"  XIU prev close:       {(MarketTape.XiuPrevClose is decimal xp ? xp.ToString("F2") : "n/a")}");
            sb.AppendLine($"  XIU return (1d):      {(MarketTape.XiuReturn1d is decimal r1 ? r1.ToString("+0.00%;-0.00%;0.00%") : "n/a")}");
            sb.AppendLine($"  XIU volume:           {(MarketTape.XiuVolume is long xv ? xv.ToString("N0") : "n/a")}");
            sb.AppendLine($"  XIU volume SMA20prior:{(MarketTape.XiuVolumeSma20Prior is decimal xs ? xs.ToString("N0") : "n/a")}");
            sb.AppendLine($"  XIU volume ratio20:   {(MarketTape.XiuVolumeRatio20 is decimal vr ? vr.ToString("F2") : "n/a")}");
            bool light = MarketTape.XiuVolumeRatio20 is decimal vr2 && vr2 < 0.85m;
            sb.AppendLine($"  Is light volume:      {light} (threshold 0.85)");
        }
        else
        {
            sb.AppendLine("  [No market tape data]");
        }

        // ── Granville ──
        if (Granville != null)
        {
            sb.AppendLine("\n── Granville Indicators ──");
            foreach (var r in Granville.Results)
            {
                sb.AppendLine($"  [{r.IndicatorNumber:D2}] {r.Name,-30} Signal={r.Signal,-14} Points={r.GranvillePoints:+0;-0}");
                sb.AppendLine($"       {r.Description}");
            }
            sb.AppendLine($"  Net Points: {Granville.NetPoints:+0;-0}  Bullish: {Granville.BullishCount}  Bearish: {Granville.BearishCount}  Adj: {Granville.CompositeAdjustment:+0.000;-0.000}");
        }

        // ── Weighting (Granville #15/#16) ──
        if (Weighting != null)
        {
            sb.AppendLine("\n── Weighting (Granville #15/#16) ──");
            if (Weighting.Degraded)
            {
                sb.AppendLine($"  Coverage:       {Weighting.ConstituentsObserved}/{Weighting.ConstituentsRequired} (degraded — no scoring)");
            }
            else
            {
                sb.AppendLine($"  Coverage:       {Weighting.ConstituentsObserved}/60");
                sb.AppendLine($"  XIU Return:     {Weighting.XiuReturn:+0.0000;-0.0000}");
                sb.AppendLine($"  ScoreB:         {Weighting.ScoreB:F3}  (threshold {WeightingCalculator.ScoreBThreshold:F2})");
                sb.AppendLine($"  ScoreC:         {Weighting.ScoreC:F3}  (threshold {WeightingCalculator.ScoreCThreshold:F2})");
                sb.AppendLine($"  Triggered:      {Weighting.Triggered}");
                if (Weighting.TopContributors.Count > 0)
                {
                    sb.AppendLine($"  Top contributors (with-index):");
                    foreach (var c in Weighting.TopContributors)
                    {
                        sb.AppendLine($"    {c.Symbol,-8} w={c.Weight:F4} ret={c.Return:+0.0000;-0.0000} contrib={c.Contribution:+0.000000;-0.000000}");
                    }
                }
            }
        }

        // ── Universe Stats ──
        sb.AppendLine("\n── Universe ──");
        sb.AppendLine($"  Loaded:                {LoadedSymbols}");
        sb.AppendLine($"  Skipped (history):     {SkippedHistory}");
        sb.AppendLine($"  Skipped (stale):       {SkippedStaleHistory}  (latest bar must match XIU session {MarketDataAsOf:yyyy-MM-dd}, ADR-0019)");
        if (StaleHistoryExclusions.Count > 0)
        {
            foreach (var exclusion in StaleHistoryExclusions
                .OrderBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"    {exclusion.Symbol,-8} latest={exclusion.LatestBarDate:yyyy-MM-dd}  sessions behind={exclusion.SessionsBehind}  {exclusion.Reason}");
            }
        }
        sb.AppendLine($"  Skipped (price ceiling):{SkippedPrice}");
        sb.AppendLine($"  Skipped (price floor): {SkippedLowPrice}  (< ${MinPriceFloor:N2})");
        sb.AppendLine($"  Skipped (low volume):  {SkippedLowVolume}  (20d avg < {MinVolume20d:N0})");
        sb.AppendLine($"  Skipped (lev/inv ETP): {SkippedLeveragedEtp}  (ShortName guard, ADR-0009)");
        sb.AppendLine($"  Liquidity floor:       price >= ${MinPriceFloor:N2} AND 20d vol >= {MinVolume20d:N0} (ADR-0007)");
        sb.AppendLine($"  RS computed:           {RsScores.Count}");

        // ── RS Coverage ──
        // Tells the truth about whether RS composite is meaningful. If sector
        // index history is shorter than RsBarsRequired (max horizon + Z window),
        // CompositeScore will be null for all sector-mapped symbols — making the
        // top-picks RS columns silently empty. This block makes that visible.
        sb.AppendLine("\n── RS Coverage ──");
        sb.AppendLine($"  Sector bars (min/max): {RsMinSectorBars} / {RsMaxSectorBars}  (required >= {RsBarsRequired} for full composite)");
        sb.AppendLine($"  Fallback to XIU:       {RsFallbackToXiuCount}  (no usable sector-index series; sector dimension degenerate)");
        if (RsFallbackSymbols.Count > 0)
            sb.AppendLine($"  Fallback symbols:      {string.Join(", ", RsFallbackSymbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))}");
        sb.AppendLine($"  Composite null:        {RsCompositeNullCount}  (insufficient bars for 10d/60d horizons or 20d Z window)");
        if (RsMinSectorBars > 0 && RsMinSectorBars < RsBarsRequired)
        {
            sb.AppendLine($"  ⚠ Sector index history is too short ({RsMinSectorBars} < {RsBarsRequired} bars). RS composite cannot be computed for sector-mapped symbols. Backfill TraderDB.dbo.SectorIndices.");
        }

        // ── Top Picks Detail ──
        sb.AppendLine("\n── Top Picks (diagnostic) ──");
        sb.AppendLine($"  {"#",-3} {"Symbol",-8} {"Dir",-5} {"Comp",6} {"P(Up)",6} {"P(Dn)",6} {"Edge",7} {"Brk",6} {"RScomp",10} {"CompZ",8} {"RS10d",9} {"OBV",-10} {"Gate",-20}");
        sb.AppendLine($"  {new string('─', 112)}");
        int rank = 1;
        foreach (var p in TopPicks)
        {
            double pUp = GetProb(p, "BinaryUp10");
            double pDn = GetProb(p, "BinaryDown10");
            double brk = GetProb(p, "BreakoutEnhanced");
            double edge = pUp - pDn;
            // RS composite is a raw 10d return-difference blend; values typically fall in ±0.005.
            // Display 4 decimals so sub-0.05% divergences are not silently rounded to 0.
            // CompZ (ADR-0010) is the volatility-normalized variant — typically lives in roughly ±2.
            RsScores.TryGetValue(p.Symbol, out var row);
            string rsCompStr = row?.CompositeScore is double rsC ? rsC.ToString("+0.0000;-0.0000;0.0000") : "null";
            string rsCompZStr = row?.CompositeScoreZ is double rsCz ? rsCz.ToString("+0.00;-0.00;0.00") : "null";
            string rs10dStr = row?.RS_StockVsMarket_10d is double rs10 ? rs10.ToString("+0.000;-0.000;0.000") : "null";
            string obvStr = ObvCell(p.Symbol);
            string gate = "Pass";
            if (p.GateTrace != null)
            {
                var blocked = p.GateTrace.FirstOrDefault(g => !g.Passed);
                if (blocked.Reason != null) gate = $"Fail:{blocked.GateName}";
            }
            sb.AppendLine($"  {rank,-3} {p.Symbol,-8} {p.Direction,-5} {p.CompositeScore,6:P0} {pUp,6:P0} {pDn,6:P0} {edge,7:+0.0%;-0.0%} {brk,6:P0} {rsCompStr,10} {rsCompZStr,8} {rs10dStr,9} {obvStr,-10} {gate,-20}");
            rank++;
        }

        // ── On-Balance Volume (OBV) Field Trend ──
        // Granville's per-symbol volume confirmation. Reporting-only here; the soft
        // ranking tilt (±ObvSignalWeight) is applied in the engine's PrimaryKey.
        sb.AppendLine("\n── On-Balance Volume (Field Trend) ──");
        if (ObvResults.Count > 0)
        {
            int rising = ObvResults.Values.Count(r => r.Trend == ObvFieldTrend.Rising);
            int falling = ObvResults.Values.Count(r => r.Trend == ObvFieldTrend.Falling);
            int doubtful = ObvResults.Values.Count(r => r.Trend == ObvFieldTrend.Doubtful);
            int indet = ObvResults.Values.Count(r => r.Trend == ObvFieldTrend.Indeterminate);
            sb.AppendLine($"  Window: {ObvResults.Values.Select(r => r.BreakoutWindow).FirstOrDefault()}  Tilt: ±{ObvSignalWeight:0.##}  (soft ranking signal, not a gate)");
            sb.AppendLine($"  Universe trend: {rising} rising, {falling} falling, {doubtful} doubtful, {indet} indeterminate");
            sb.AppendLine();
            sb.AppendLine($"  {"Symbol",-8} {"Trend",-13} {"Designation",-12} {"AsOf",-12} {"Pivots",6} {"Tilt",6}");
            sb.AppendLine($"  {new string('─', 62)}");
            foreach (var p in TopPicks)
            {
                if (!ObvResults.TryGetValue(p.Symbol, out var r)) continue;
                string desig = r.LatestDesignation == ObvDesignation.None
                    ? "—"
                    : $"{r.LatestDesignation}{(r.LatestDesignationDate is DateTime d ? $" {d:MM-dd}" : "")}";
                string tilt = r.Trend switch
                {
                    ObvFieldTrend.Rising => $"+{ObvSignalWeight:0.##}",
                    ObvFieldTrend.Falling => $"-{ObvSignalWeight:0.##}",
                    _ => "0"
                };
                sb.AppendLine($"  {p.Symbol,-8} {r.Trend,-13} {desig,-12} {r.AsOf,-12:yyyy-MM-dd} {r.PivotCount,6} {tilt,6}");
            }
        }
        else
        {
            sb.AppendLine("  [No OBV data] — run: dotnet run --project Sandbox -- obv-backfill");
        }

        // ── Market Climax (CLX) ──
        // Granville's market-wide net OBV-breakout tally across the XIU-60 leaders (sibling
        // to the A/D Line). Diagnostic-only: standing net is the signal, fresh flow is the
        // flow diagnostic, regime is the confirmation/divergence verdict vs XIU.
        sb.AppendLine("\n── Market Climax (CLX) ──");
        if (MarketClimax.Count > 0)
        {
            var clx = MarketClimax[^1];
            sb.AppendLine($"  Date:             {clx.Date:yyyy-MM-dd}");
            sb.AppendLine($"  CLX (net):        {clx.Clx:+0;-0;0}");
            sb.AppendLine($"  Up / Down:        {clx.UpBreakouts} / {clx.DownBreakouts}");
            sb.AppendLine($"  Covered:          {clx.Covered} / {clx.BasketSize} (basket)");
            sb.AppendLine($"  Fresh flow:       +{clx.FreshUp} up / -{clx.FreshDown} down (fired {clx.Date:MM-dd})");
            sb.AppendLine($"  XIU Close:        {(clx.XiuClose.HasValue ? clx.XiuClose.Value.ToString("F2") : "N/A")}");
            if (ClimaxRegime != null)
            {
                sb.AppendLine($"  Regime:           {ClimaxRegime.Regime}");
                sb.AppendLine($"  CLX Change:       {ClimaxRegime.ClxThen:+0;-0;0} → {ClimaxRegime.ClxNow:+0;-0;0} (Δ{ClimaxRegime.ClxChange:+0;-0;0})");
                sb.AppendLine($"  XIU Change:       {(ClimaxRegime.XiuChangePct.HasValue ? ClimaxRegime.XiuChangePct.Value.ToString("+0.0%;-0.0%") : "N/A")}");
                sb.AppendLine($"  Window/Thresh:    {ClimaxDivergenceWindow}d / {ClimaxDivergenceThreshold}");
                sb.AppendLine($"  Verdict:          {ClimaxRegime.Description}");
            }
        }
        else
        {
            sb.AppendLine("  [No CLX data] — run: dotnet run --project Sandbox -- climax-backfill");
        }

        // ── Best Pick All Signals ──
        if (BestPick != null)
        {
            sb.AppendLine($"\n── Best Pick Signals: {BestPick.Symbol} ──");
            sb.AppendLine("  Signal hints use ModelRegistry thresholds shown in each model's notes; trade eligibility uses StrategyVersion gate thresholds.");
            foreach (var s in BestPick.Signals)
                sb.AppendLine($"  [{s.Hint,-5}] {s.Name,-25} Score={s.Score:0.###} {s.Notes}");

            if (BestPick.GateTrace != null)
            {
                sb.AppendLine("\n  Gate Pipeline:");
                foreach (var g in BestPick.GateTrace)
                    sb.AppendLine($"    {(g.Passed ? "✓" : "✗")} {g.GateName,-18} {g.Reason ?? "Passed"}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the human-readable summary report.
    /// </summary>
    public string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(new string('═', 60));
        sb.AppendLine("DELPHI MARKET SUMMARY");
        sb.AppendLine(new string('═', 60));
        sb.AppendLine($"\nRecommendation date: {RecommendationDate:yyyy-MM-dd}  |  Market data as of: {MarketDataAsOf:yyyy-MM-dd}");

        // ── Regime ──
        if (Regime != null)
        {
            string regimeLabel = Regime.IsBothBearish ? "🔻 Bearish"
                : Regime.IsAnyBenchmarkUptrend ? "📈 Bullish"
                : "⚠️ Mixed";
            sb.AppendLine($"\nMarket Regime: {regimeLabel}");
            sb.AppendLine($"  XIU 20d return: {Regime.BenchmarkReturn20d:P2}");
        }

        // ── Universe / liquidity gate ──
        sb.AppendLine($"\nUniverse: {LoadedSymbols} loaded (liquidity floor: price >= ${MinPriceFloor:N2}, 20d vol >= {MinVolume20d:N0})");
        sb.AppendLine($"  Skipped: {SkippedHistory} history, {SkippedStaleHistory} stale, {SkippedPrice} too pricey, {SkippedLowPrice} sub-${MinPriceFloor:N2}, {SkippedLowVolume} thin (< {MinVolume20d:N0}), {SkippedLeveragedEtp} lev/inv ETP");
        if (SkippedStaleHistory > 0)
        {
            sb.AppendLine($"  ⚠ Freshness gate: {SkippedStaleHistory} symbol(s) excluded because their latest bar did not match XIU session {MarketDataAsOf:yyyy-MM-dd}.");
        }

        // ── RS coverage banner (only when there is something to report) ──
        if (RsMinSectorBars > 0 && RsMinSectorBars < RsBarsRequired)
        {
            sb.AppendLine($"  ⚠ RS coverage: sector index history {RsMinSectorBars} bars (< {RsBarsRequired}) — composite null for sector-mapped symbols. {RsCompositeNullCount} symbols affected.");
        }
        else if (RsFallbackToXiuCount > 0 || RsCompositeNullCount > 0)
        {
            sb.AppendLine($"  RS coverage: {RsFallbackToXiuCount} fallback-to-XIU, {RsCompositeNullCount} null composite.");
        }

        // ── A/D Breadth ──
        if (AdLine.Count > 0)
        {
            var latest = AdLine[^1];
            int adv = latest.Advancers;
            int dec = latest.Decliners;
            int plurality = latest.DailyPlurality;
            string breadthLabel = plurality > 100 ? "strongly bullish"
                : plurality > 0 ? "bullish"
                : plurality > -100 ? "bearish"
                : "strongly bearish";
            sb.AppendLine($"\nBreadth: {adv} advancing vs {dec} declining — {breadthLabel}");
            sb.AppendLine($"  Cumulative A/D: {latest.CumulativeDifferential:+#,0;-#,0;0}  Score: {BreadthScore:+0.00;-0.00}");
            if (BearishDivergence)
                sb.AppendLine("  ⚠️ Bearish divergence detected");
        }

        // ── Volume regime (CLX) ──
        // One-line confirmation/divergence read of the market-wide net OBV-breakout tally vs XIU.
        if (MarketClimax.Count > 0 && ClimaxRegime != null && ClimaxRegime.Regime != Core.Indicators.ClimaxRegime.Insufficient)
        {
            string clxTag = ClimaxRegime.Regime switch
            {
                Core.Indicators.ClimaxRegime.Confirming => "✅ confirming",
                Core.Indicators.ClimaxRegime.BearishDivergence => "⚠️ unconfirmed advance",
                Core.Indicators.ClimaxRegime.BullishDivergence => "📈 improving breadth",
                _ => "➖ neutral"
            };
            sb.AppendLine($"\nVolume regime: {clxTag} — CLX {ClimaxRegime.ClxThen:+0;-0;0}→{ClimaxRegime.ClxNow:+0;-0;0}" +
                $"{(ClimaxRegime.XiuChangePct.HasValue ? $", XIU {ClimaxRegime.XiuChangePct.Value:+0.0%;-0.0%}" : "")} over {ClimaxDivergenceWindow}d");
        }

        // ── Sectors ──
        if (SectorSnapshots.Count > 0)
        {
            var positive = SectorSnapshots.Count(s => s.PercentChange > 0);
            var negative = SectorSnapshots.Count - positive;
            sb.AppendLine($"\nSectors: {positive} of {SectorSnapshots.Count} positive");

            var leaders = SectorSnapshots.OrderByDescending(s => s.PercentChange).Take(3);
            sb.AppendLine($"  Leaders:  {string.Join(", ", leaders.Select(s => $"{s.SectorName} ({s.PercentChange:+0.00;-0.00}%)"))}");

            var laggards = SectorSnapshots.OrderBy(s => s.PercentChange).Take(2);
            sb.AppendLine($"  Laggards: {string.Join(", ", laggards.Select(s => $"{s.SectorName} ({s.PercentChange:+0.00;-0.00}%)"))}");
        }

        // ── Granville ──
        if (Granville != null)
        {
            string gLabel = Granville.NetPoints > 0 ? "📈 Bullish" : Granville.NetPoints < 0 ? "📉 Bearish" : "➖ Neutral";
            sb.AppendLine($"\nGranville: {gLabel} (net {Granville.NetPoints:+0;-0} pts, {Granville.BullishCount} bull / {Granville.BearishCount} bear)");

            // Genuity (#17–#20) line — cross-border (US) confirmation of XIU's move.
            var genuity = Granville.Results.Where(r => r.Category == Core.Indicators.Granville.IndicatorCategory.Genuity).ToList();
            if (genuity.Count > 0)
            {
                int gBull = genuity.Count(r => r.Signal is Core.Indicators.Granville.IndicatorSignal.Bullish or Core.Indicators.Granville.IndicatorSignal.StrongBullish);
                int gBear = genuity.Count(r => r.Signal is Core.Indicators.Granville.IndicatorSignal.Bearish or Core.Indicators.Granville.IndicatorSignal.StrongBearish);
                int gNeutral = genuity.Count - gBull - gBear;
                bool stale = genuity.Any(r => r.Name.Contains("Stale", StringComparison.OrdinalIgnoreCase));
                string label = stale ? "⚠️ stale US data" : (gBull > gBear ? "confirmed" : gBear > gBull ? "non-confirmation" : "mixed");
                sb.AppendLine($"  Genuity (US confirm): {label} ({gBull} confirm / {gBear} divergent / {gNeutral} neutral)");
            }

            // Light Volume (#25–#28) — tape-level conviction read on light-volume days.
            var lightVol = Granville.Results.Where(r => r.Category == Core.Indicators.Granville.IndicatorCategory.LightVolume
                                                       && r.IndicatorNumber > 0).ToList();
            if (lightVol.Count > 0)
            {
                var top = lightVol[0];
                string tag = top.Signal switch
                {
                    Core.Indicators.Granville.IndicatorSignal.StrongBullish => "💪 strong-bullish exhaustion",
                    Core.Indicators.Granville.IndicatorSignal.Bullish => "📈 light-vol leaders carrying",
                    Core.Indicators.Granville.IndicatorSignal.StrongBearish => "⚠️ strong-bearish",
                    Core.Indicators.Granville.IndicatorSignal.Bearish => "📉 no-conviction tape",
                    _ => "neutral"
                };
                sb.AppendLine($"  Light Volume (#{top.IndicatorNumber}): {tag}");
            }
            else if (MarketTape is { XiuVolumeRatio20: decimal vrSum } && vrSum < 0.85m)
            {
                sb.AppendLine($"  Light Volume: light tape (ratio {vrSum:F2}) but no #25–#28 fired");
            }
        }

        // ── Weighting summary line ──
        if (Weighting != null && !Weighting.Degraded)
        {
            if (Weighting.Triggered)
            {
                string topSyms = Weighting.TopContributors.Count > 0
                    ? string.Join(", ", Weighting.TopContributors.Select(c => c.Symbol))
                    : "—";
                sb.AppendLine($"  ⚠️ Narrow advance: ScoreB={Weighting.ScoreB:F2}, ScoreC={Weighting.ScoreC:F2}, top: {topSyms}");
            }
            else
            {
                sb.AppendLine($"  Weighting: ScoreB={Weighting.ScoreB:F2}, ScoreC={Weighting.ScoreC:F2} (no trigger)");
            }
        }

        // ── Recommendation ──
        sb.AppendLine($"\n{"─",-60}");
        if (BestPick != null && Size != null && Size.SuggestedSize > 0)
        {
            double pUp = GetProb(BestPick, "BinaryUp10");
            double pDn = GetProb(BestPick, "BinaryDown10");
            double edge = pUp - pDn;
            double brk = GetProb(BestPick, "BreakoutEnhanced");

            sb.AppendLine($"Recommendation: {BestPick.Direction.ToString().ToUpper()} {BestPick.Symbol}");
            sb.AppendLine($"  Composite: {BestPick.CompositeScore:P1}  Edge: {edge:+0.0%;-0.0%}  Breakout: {brk:P0}");
            sb.AppendLine($"  Allocate:  {Size.SuggestedSize:C2} ({Size.AllocationPercent:P1})");

            if (Granville != null)
                sb.AppendLine($"  Granville adj: {Granville.CompositeAdjustment:+0.000;-0.000}");

            // OBV confirmation — does Granville's volume field trend agree with the pick?
            if (ObvResults.TryGetValue(BestPick.Symbol, out var obv))
            {
                string obvLine = obv.Trend switch
                {
                    ObvFieldTrend.Rising => $"✅ confirms (rising field trend, tilt +{ObvSignalWeight:0.##})",
                    ObvFieldTrend.Falling => $"⚠️ contradicts (falling field trend, tilt -{ObvSignalWeight:0.##})",
                    ObvFieldTrend.Doubtful => "➖ neutral (doubtful — breakouts out of gear)",
                    _ => "➖ no read (insufficient OBV history)"
                };
                string desig = obv.LatestDesignation == ObvDesignation.None
                    ? ""
                    : $", last {obv.LatestDesignation}{(obv.LatestDesignationDate is DateTime dd ? $" {dd:MM-dd}" : "")}";
                sb.AppendLine($"  OBV: {obvLine}{desig}");
            }
        }
        else
        {
            sb.AppendLine($"Recommendation: NO TRADE — {Size?.Reason ?? "no qualifying candidates"}");
        }

        return sb.ToString();
    }

    private static double GetProb(RankedPick pick, string name) =>
        pick.Signals.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Score ?? 0;

    /// <summary>
    /// Compact OBV field-trend cell for the Top-Picks table: a trend arrow plus the
    /// latest UP/DOWN designation (e.g. "↑UP", "↓DN", "→··", or "·" when no data).
    /// </summary>
    private string ObvCell(string symbol)
    {
        if (!ObvResults.TryGetValue(symbol, out var r))
            return "·";

        string arrow = r.Trend switch
        {
            ObvFieldTrend.Rising => "↑",
            ObvFieldTrend.Falling => "↓",
            ObvFieldTrend.Doubtful => "→",
            _ => "·"
        };
        string desig = r.LatestDesignation switch
        {
            ObvDesignation.Up => "UP",
            ObvDesignation.Down => "DN",
            _ => "··"
        };
        return $"{arrow}{desig}";
    }
}
