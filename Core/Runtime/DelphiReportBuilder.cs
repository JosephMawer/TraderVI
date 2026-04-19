using Core.Db;
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
    public MarketRegime? Regime { get; set; }
    public IReadOnlyList<ADLineEntry> AdLine { get; set; } = [];
    public double BreadthScore { get; set; }
    public bool BearishDivergence { get; set; }
    public GranvilleDailyForecast? Granville { get; set; }
    public IReadOnlyList<SectorIndexSnapshot> SectorSnapshots { get; set; } = [];
    public IReadOnlyList<RankedPick> TopPicks { get; set; } = [];
    public RankedPick? BestPick { get; set; }
    public PositionSizeResult? Size { get; set; }
    public Dictionary<string, Core.RelativeStrength.RelativeStrengthRow> RsScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IReadOnlyList<DailyBar>> AllBars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int LoadedSymbols { get; set; }
    public int SkippedHistory { get; set; }
    public int SkippedPrice { get; set; }
    public decimal DeployableCapital { get; set; }

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

        // ── Universe Stats ──
        sb.AppendLine("\n── Universe ──");
        sb.AppendLine($"  Loaded:           {LoadedSymbols}");
        sb.AppendLine($"  Skipped (history):{SkippedHistory}");
        sb.AppendLine($"  Skipped (price):  {SkippedPrice}");
        sb.AppendLine($"  RS computed:      {RsScores.Count}");

        // ── Top Picks Detail ──
        sb.AppendLine("\n── Top Picks (diagnostic) ──");
        sb.AppendLine($"  {"#",-3} {"Symbol",-8} {"Dir",-5} {"Comp",6} {"P(Up)",6} {"P(Dn)",6} {"Edge",7} {"Brk",6} {"RS",9} {"Gate",-20}");
        sb.AppendLine($"  {new string('─', 80)}");
        int rank = 1;
        foreach (var p in TopPicks)
        {
            double pUp = GetProb(p, "BinaryUp10");
            double pDn = GetProb(p, "BinaryDown10");
            double brk = GetProb(p, "BreakoutEnhanced");
            double edge = pUp - pDn;
            double rs = RsScores.TryGetValue(p.Symbol, out var row) && row.CompositeScore.HasValue ? row.CompositeScore.Value : 0;
            string gate = "Pass";
            if (p.GateTrace != null)
            {
                var blocked = p.GateTrace.FirstOrDefault(g => !g.Passed);
                if (blocked.Reason != null) gate = $"Fail:{blocked.GateName}";
            }
            sb.AppendLine($"  {rank,-3} {p.Symbol,-8} {p.Direction,-5} {p.CompositeScore,6:P0} {pUp,6:P0} {pDn,6:P0} {edge,7:+0.0%;-0.0%} {brk,6:P0} {rs,9:+0.000;-0.000} {gate,-20}");
            rank++;
        }

        // ── Best Pick All Signals ──
        if (BestPick != null)
        {
            sb.AppendLine($"\n── Best Pick Signals: {BestPick.Symbol} ──");
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

        // ── Regime ──
        if (Regime != null)
        {
            string regimeLabel = Regime.IsBothBearish ? "🔻 Bearish"
                : Regime.IsAnyBenchmarkUptrend ? "📈 Bullish"
                : "⚠️ Mixed";
            sb.AppendLine($"\nMarket Regime: {regimeLabel}");
            sb.AppendLine($"  XIU 20d return: {Regime.BenchmarkReturn20d:P2}");
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
        }
        else
        {
            sb.AppendLine($"Recommendation: NO TRADE — {Size?.Reason ?? "no qualifying candidates"}");
        }

        return sb.ToString();
    }

    private static double GetProb(RankedPick pick, string name) =>
        pick.Signals.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Score ?? 0;
}