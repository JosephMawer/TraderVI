using Core.Db;
using Core.Indicators;
using Core.ML;
using Core.Runtime;
using Core.Trader;
using Core.Trader.Gates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== The Oracle Of Delphi ===\n");

// ═══════════════════════════════════════════════════════════════════
// CONFIGURATION (aggressive single-position rotation)
// ═══════════════════════════════════════════════════════════════════
decimal availableCapital = 500.00m;
int minBarsRequired = 55;              // Increased for enhanced features
decimal reserveCashPercent = 0.02m;
double minExpectedReturn = 0.00;
int maxSymbolsToScan = 500;
int topPicksToSave = 10;
bool saveToDB = true;

Console.WriteLine($"Available Capital: ${availableCapital:N2}");
Console.WriteLine($"Reserve Cash:      {reserveCashPercent:P0}");
Console.WriteLine($"Ranking Mode:      DirectionEdge-based");
Console.WriteLine($"Save to DB:        {saveToDB}");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
// LOAD ACTIVE STRATEGY VERSION → DERIVE RUNTIME CONFIG
// ═══════════════════════════════════════════════════════════════════
var strategyRepo = new StrategyVersionRepository();
var activeStrategy = await strategyRepo.GetActiveVersion();

Guid? strategyVersionId = activeStrategy?.VersionId;
StrategyConfig config = activeStrategy?.ToConfig() ?? StrategyConfig.Default;

if (activeStrategy != null)
{
    Console.WriteLine($"Strategy Version:  {activeStrategy.VersionName}");
    Console.WriteLine($"Description:       {activeStrategy.Description}");
    Console.WriteLine($"  MinComposite:    {config.MinCompositeScore:P0}");
    Console.WriteLine($"  MinUpProb:       {config.MinUpProb:P0}");
    Console.WriteLine($"  MinBreakout:     {config.MinBreakoutProb:P0}");
    Console.WriteLine($"  MaxDownProb:     {config.MaxDownProb:P0}");
    Console.WriteLine($"  MinDirEdge:      {config.MinDirectionEdge:P0}");
    Console.WriteLine($"  BreadthVeto:     {config.BreadthVetoThreshold:0.00;-0.00}");
    Console.WriteLine($"  StopLoss:        {config.StopLossPercent:P0}");
    Console.WriteLine($"  MaxPositions:    {config.MaxPositions}");
    Console.WriteLine();
}
else
{
    Console.WriteLine("⚠️  No active strategy version found. Using defaults.\n");
}

// ═══════════════════════════════════════════════════════════════════
// BOOTSTRAP ENGINE (loads enabled models from registry + strategy config)
// ═══════════════════════════════════════════════════════════════════
var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry(config);
engine.RankingMode = RankingMode.Probability;

engine.Sizer = new PositionSizer(availableCapital)
{
    Strategy = AllocationStrategy.SinglePositionAllIn,
    ReserveCashPercent = reserveCashPercent,
    MinPositionSize = 25m,
    MinExpectedReturn = minExpectedReturn,
    MinConfidence = config.MinCompositeScore,
    RequireBothSignals = false
};

// ═══════════════════════════════════════════════════════════════════
// COMPUTE MARKET REGIME FROM XIU + SPY BENCHMARKS
// ═══════════════════════════════════════════════════════════════════
var quoteRepo = new QuoteRepository();
var xiuBars = await quoteRepo.GetDailyBarsAsync("XIU");
var spyBars = await quoteRepo.GetDailyBarsAsync("SPY");

MarketRegime? regime = null;
if (xiuBars.Count >= 200)
{
    regime = TradeDecisionEngine.ComputeRegime(xiuBars, spyBars.Count >= 200 ? spyBars : null);

    Console.WriteLine("Market Regime:");
    Console.WriteLine($"  XIU Uptrend (MA50>MA200): {(regime.IsBenchmarkUptrend ? "✓ Yes" : "✗ No")}");
    Console.WriteLine($"  XIU 20d Return:           {regime.BenchmarkReturn20d:P2} {(regime.IsBenchmark20dPositive ? "✓" : "✗")}");
    Console.WriteLine($"  XIU Volatility:           {(regime.IsVolatilityNormal ? "Normal" : "⚠️ Elevated")}");
    Console.WriteLine($"  SPY Uptrend (MA50>MA200): {(regime.IsSpyUptrend ? "✓ Yes" : "✗ No")}");
    Console.WriteLine($"  SPY 20d Positive:         {(regime.IsSpy20dPositive ? "✓ Yes" : "✗ No")}");
    Console.WriteLine($"  Any Benchmark Uptrend:    {(regime.IsAnyBenchmarkUptrend ? "✓ Yes" : "✗ No")}");
    Console.WriteLine();

    if (regime.IsBothBearish)
    {
        Console.WriteLine("⚠️  BEARISH REGIME: Both XIU and SPY are bearish. Long trades will be filtered out.\n");
    }
    else if (!regime.IsBenchmarkUptrend && !regime.IsBenchmark20dPositive)
    {
        Console.WriteLine("⚠️  XIU BEARISH but SPY positive — proceeding with caution.\n");
    }
}
else
{
    Console.WriteLine("⚠️  Insufficient XIU data for regime calculation.\n");
}

// Inject regime into engine (thresholds already set via StrategyConfig)
engine.CurrentRegime = regime;


// ═══════════════════════════════════════════════════════════════════
// LOAD A/D LINE BREADTH — WIRE IN BEFORE EVALUATION
// ═══════════════════════════════════════════════════════════════════
var adRepo = new AdvanceDeclineRepository();
var adLine = await adRepo.GetRecentAsync(200);
double breadthScore = AdvanceDeclineCalculator.BreadthScore(adLine);
bool bearishDivergence = AdvanceDeclineCalculator.HasBearishDivergence(adLine);

// Inject breadth into engine BEFORE evaluation
engine.BreadthScore = breadthScore;

Console.WriteLine($"A/D Line Breadth Score: {breadthScore:+0.00;-0.00}");
Console.WriteLine($"  Slope (20d):         {AdvanceDeclineCalculator.Slope(adLine):+0.0;-0.0}");
Console.WriteLine($"  Above SMA(50):       {(AdvanceDeclineCalculator.IsAboveSma(adLine) ? "✓" : "✗")}");
Console.WriteLine($"  Bearish Divergence:  {(bearishDivergence ? "⚠️ YES" : "No")}");

if (breadthScore <= engine.BreadthVetoThreshold)
{
    Console.WriteLine($"  ⚠️  BREADTH VETO ACTIVE (score {breadthScore:+0.00} ≤ {engine.BreadthVetoThreshold:+0.00})");
}

Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
// LOAD ALL SYMBOLS FROM DATABASE
// ═══════════════════════════════════════════════════════════════════
var db = new SymbolsRepository();
var constituents = await db.GetEquitiesAsync();

var symbols = constituents
    .Select(c => c.Symbol)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Take(maxSymbolsToScan)
    .ToList();

Console.WriteLine($"Scanning symbols: {symbols.Count:N0} (equities only)\n");

var allBars = new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase);

int loaded = 0;
int skipped = 0;

foreach (var symbol in symbols)
{
    var bars = await quoteRepo.GetDailyBarsAsync(symbol);

    if (bars.Count >= minBarsRequired)
    {
        allBars[symbol] = bars;
        loaded++;
    }
    else
    {
        skipped++;
    }
}

Console.WriteLine($"Loaded: {loaded} symbols, Skipped: {skipped} (insufficient history)\n");

if (allBars.Count == 0)
{
    Console.WriteLine("No symbols with sufficient data to evaluate.");
    return;
}

// ═══════════════════════════════════════════════════════════════════
// EVALUATE + RANK (SINGLE PASS) + PICK BEST + SIZE IT
// ═══════════════════════════════════════════════════════════════════
var top = engine.EvaluateAndRank(allBars, topN: topPicksToSave);
var (bestPick, size) = engine.EvaluateBestPickAllIn(top, availableCapital);

Console.WriteLine(new string('═', 80));
Console.WriteLine("BEST PICK (SINGLE-POSITION MODE) - DIRECTION EDGE RANKING");
Console.WriteLine(new string('═', 80));

Console.WriteLine("\nTop Ranked Candidates:");

// ═══════════════════════════════════════════════════════════════════
// HELPER FUNCTIONS
// ═══════════════════════════════════════════════════════════════════
static double GetProb(RankedPick pick, string nameEquals) =>
    pick.Signals
        .FirstOrDefault(s => string.Equals(s.Name, nameEquals, StringComparison.OrdinalIgnoreCase))
        ?.Score ?? 0;

static double GetProbContains(RankedPick pick, string nameContains) =>
    pick.Signals
        .FirstOrDefault(s => s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
        ?.Score ?? 0;

static double GetBreakoutProb(RankedPick pick) => GetProb(pick, "BreakoutEnhanced");
static double GetUpProb(RankedPick pick) => GetProb(pick, "BinaryUp10");
static double GetDownProb(RankedPick pick) => GetProb(pick, "BinaryDown10");

// ═══════════════════════════════════════════════════════════════════
// DISPLAY RANKED CANDIDATES
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine(
    $"{"#",-3} {"Symbol",-8} {"Action",-6} {"Comp",6} {"Break",6} {"P↑",5} {"P↓",5} {"Edge",6} {"Vol",5} {"RelS",5} {"Gate",12}");
Console.WriteLine(new string('─', 90));

int rank = 1;
foreach (var p in top)
{
    double breakout = GetBreakoutProb(p);
    double pUp = GetUpProb(p);
    double pDown = GetDownProb(p);
    double edge = pUp - pDown;
    double volExp = GetProbContains(p, "VolExpansion");
    double relStr = GetProbContains(p, "RelStrength");

    string edgeStr = edge >= 0 ? $"+{edge:P0}" : $"{edge:P0}";

    // Show which gate blocked (if any)
    string gateStatus = "✓ All";
    if (p.GateTrace != null)
    {
        var blocked = p.GateTrace.FirstOrDefault(g => !g.Passed);
        if (blocked.Reason != null)
            gateStatus = $"✗ {blocked.GateName}";
    }

    Console.WriteLine(
        $"{rank,-3} {p.Symbol,-8} {p.Direction,-6} {p.CompositeScore,6:P0} {breakout,6:P0} {pUp,5:P0} {pDown,5:P0} {edgeStr,6} {volExp,5:P0} {relStr,5:P0} {gateStatus,12}");
    rank++;
}

// ═══════════════════════════════════════════════════════════════════
// SAVE DAILY PICKS TO DATABASE
// ═══════════════════════════════════════════════════════════════════
if (saveToDB && top.Count > 0)
{
    var pickDate = DateTime.Today;
    var pickRepo = new DailyPickRepository();

    await pickRepo.DeletePicksByDate(pickDate);

    Console.WriteLine($"\nSaving {top.Count} picks to database...");

    int savedRank = 1;
    foreach (var p in top)
    {
        double breakout = GetBreakoutProb(p);
        double pUp = GetUpProb(p);
        double pDown = GetDownProb(p);
        double volExp = GetProbContains(p, "VolExpansion");
        double relStrength = GetProbContains(p, "RelStrength");

        await pickRepo.InsertPick(
            pickDate: pickDate,
            symbol: p.Symbol,
            rank: savedRank,
            direction: p.Direction.ToString(),
            compositeScore: p.CompositeScore,
            breakoutProb: breakout,
            directionProb: pUp,
            volExpansionProb: volExp,
            relStrengthProb: relStrength > 0 ? relStrength : null,
            expectedReturn: p.ExpectedReturn,
            suggestedSize: savedRank == 1 && size != null ? size.SuggestedSize : null,
            allocationPercent: savedRank == 1 && size != null ? (double)size.AllocationPercent : null,
            strategyVersionId: strategyVersionId,
            notes: savedRank == 1 ? $"Top pick. P↓={pDown:P0}, Edge={pUp - pDown:P0}" : null);

        savedRank++;
    }

    Console.WriteLine($"✓ Saved {top.Count} picks to [dbo].[DailyPick] for {pickDate:yyyy-MM-dd}");
}

// ═══════════════════════════════════════════════════════════════════
// DISPLAY BEST PICK DETAILS
// ═══════════════════════════════════════════════════════════════════
if (bestPick == null || size == null || size.SuggestedSize <= 0)
{
    var reason = size?.Reason ?? "Unknown (size is null)";
    Console.WriteLine($"\nNo qualifying trade found. Reason: {reason}");
    return;
}

double bestBreakout = GetBreakoutProb(bestPick);
double bestUp = GetUpProb(bestPick);
double bestDown = GetDownProb(bestPick);
double bestEdge = bestUp - bestDown;
double bestVolExp = GetProbContains(bestPick, "VolExpansion");
double bestRelStrength = GetProbContains(bestPick, "RelStrength");

Console.WriteLine($"\n{"═",-80}");
Console.WriteLine("RECOMMENDATION");
Console.WriteLine($"{"═",-80}");
Console.WriteLine($"Symbol:          {bestPick.Symbol}");
Console.WriteLine($"Direction:       {bestPick.Direction}");
Console.WriteLine($"Composite Score: {bestPick.CompositeScore:P1}");
Console.WriteLine();
Console.WriteLine($"Setup Signal:");
Console.WriteLine($"  Breakout Prob: {bestBreakout:P1}");
Console.WriteLine();
Console.WriteLine($"Direction Signals:");
Console.WriteLine($"  P(Up +4%):     {bestUp:P1}");
Console.WriteLine($"  P(Down -4%):   {bestDown:P1}");
Console.WriteLine($"  Direction Edge:{bestEdge:+0.0%;-0.0%} {(bestEdge > 0 ? "✓ Bullish" : "✗ Bearish")}");
Console.WriteLine();
Console.WriteLine($"Confirmation Signals:");
Console.WriteLine($"  Vol Expansion: {bestVolExp:P1}");
if (bestRelStrength > 0)
    Console.WriteLine($"  Rel Strength:  {bestRelStrength:P1}");
Console.WriteLine();
Console.WriteLine($"Position:");
Console.WriteLine($"  Allocate:      {size.SuggestedSize:C2} ({size.AllocationPercent:P1})");
Console.WriteLine($"  Reason:        {size.Reason}");

Console.WriteLine("\nGate Pipeline (best pick):");
if (bestPick.GateTrace != null)
{
    foreach (var g in bestPick.GateTrace)
    {
        string icon = g.Passed ? "✓" : "✗";
        string reason = g.Reason ?? "Passed";
        Console.WriteLine($"  {icon} {g.GateName,-18} {reason}");
    }
}

Console.WriteLine("\nAll Signals (best pick):");
foreach (var s in bestPick.Signals)
{
    Console.WriteLine($"  [{s.Hint,-5}] {s.Name,-25} Score={s.Score:0.###} {s.Notes}");
}