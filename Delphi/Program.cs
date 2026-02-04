using Core.Db;
using Core.ML;
using Core.Runtime;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== The Oracle Of Delphi ===\n");

// ═══════════════════════════════════════════════════════════════════
// CONFIGURATION (aggressive single-position rotation)
// ═══════════════════════════════════════════════════════════════════
decimal availableCapital = 500.00m;
int minBarsRequired = 35;
decimal reserveCashPercent = 0.02m;
double minExpectedReturn = 0.00;      // Lowered: we now rank by probability
double minConfidence = 0.35;          // Minimum composite score
int maxSymbolsToScan = 500;

Console.WriteLine($"Available Capital: ${availableCapital:N2}");
Console.WriteLine($"Reserve Cash:      {reserveCashPercent:P0}");
Console.WriteLine($"Min Composite:     {minConfidence:P0}");
Console.WriteLine($"Ranking Mode:      Probability-based");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
// BOOTSTRAP ENGINE (loads enabled models from registry)
// ═══════════════════════════════════════════════════════════════════
var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry();

// Use probability-based ranking (new default)
engine.RankingMode = RankingMode.Probability;

// Configure aggressive sizing behavior (single-position)
engine.Sizer = new PositionSizer(availableCapital)
{
    Strategy = AllocationStrategy.SinglePositionAllIn,
    ReserveCashPercent = reserveCashPercent,
    MinPositionSize = 25m,
    MinExpectedReturn = minExpectedReturn,
    MinConfidence = minConfidence,
    RequireBothSignals = false  // Changed: rely on composite score
};

// ═══════════════════════════════════════════════════════════════════
// LOAD ALL SYMBOLS FROM DATABASE
// ═══════════════════════════════════════════════════════════════════
var db = new SymbolsRepository();
var constituents = await db.GetSymbols();

var symbols = constituents
    .Select(c => c.Symbol)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Take(maxSymbolsToScan)
    .ToList();

Console.WriteLine($"Scanning symbols: {symbols.Count:N0}\n");

var quoteRepo = new QuoteRepository();
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
// EVALUATE + PICK BEST SINGLE TRADE + SIZE IT (mostly/all-in)
// ═══════════════════════════════════════════════════════════════════
var (bestPick, size) = engine.EvaluateBestPickAllIn(allBars, availableCapital);

Console.WriteLine(new string('═', 70));
Console.WriteLine("BEST PICK (SINGLE-POSITION MODE) - PROBABILITY RANKING");
Console.WriteLine(new string('═', 70));

// Always show a short ranked list for transparency (even when no trade qualifies)
Console.WriteLine("\nTop Ranked Candidates:");
var top = engine.EvaluateAndRank(allBars, topN: 10);

// Helper to fetch individual probabilities from a pick's signals.
// NOTE: This mirrors the logic in TradeDecisionEngine's composite breakdown.
static double GetProb(RankedPick pick, string nameEquals) =>
    pick.Signals
        .FirstOrDefault(s => string.Equals(s.Name, nameEquals, StringComparison.OrdinalIgnoreCase))
        ?.Score ?? 0;

static double GetProbContains(RankedPick pick, string nameContains) =>
    pick.Signals
        .FirstOrDefault(s => s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
        ?.Score ?? 0;

static double GetDirectionProb(RankedPick pick)
{
    // Prefer 4pct, else 3pct, else 0
    double up4 = GetProbContains(pick, "4pct");
    if (up4 > 0) return up4;

    double up3 = GetProbContains(pick, "3pct");
    if (up3 > 0) return up3;

    return 0;
}

Console.WriteLine(
    $"{"#",-3} {"Symbol",-10} {"Action",-6} {"Composite",10} {"Break",8} {"Vol",8} {"Dir",8} {"ExpRet",10}");
Console.WriteLine(new string('─', 74));

int rank = 1;
foreach (var p in top)
{
    double breakout = GetProb(p, "BreakoutPriorHigh10");
    double volExp = GetProbContains(p, "VolExpansion");
    double dirProb = GetDirectionProb(p);

    Console.WriteLine(
        $"{rank,-3} {p.Symbol,-10} {p.Direction,-6} {p.CompositeScore,10:P1} {breakout,8:P1} {volExp,8:P1} {dirProb,8:P1} {p.ExpectedReturn,10:P2}");
    rank++;
}

if (bestPick == null || size == null || size.SuggestedSize <= 0)
{
    var reason = size?.Reason ?? "Unknown (size is null)";
    Console.WriteLine($"\nNo qualifying trade found. Reason: {reason}");
    return;
}

double bestBreakout = GetProb(bestPick, "BreakoutPriorHigh10");
double bestVolExp = GetProbContains(bestPick, "VolExpansion");
double bestDirProb = GetDirectionProb(bestPick);

Console.WriteLine($"\nSymbol:          {bestPick.Symbol}");
Console.WriteLine($"Direction:       {bestPick.Direction}");
Console.WriteLine($"Composite Score: {bestPick.CompositeScore:P1}");
Console.WriteLine($"Breakout Prob:   {bestBreakout:P1}");
Console.WriteLine($"Vol Exp Prob:    {bestVolExp:P1}");
Console.WriteLine($"Direction Prob:  {bestDirProb:P1}");
Console.WriteLine($"Expected Return: {bestPick.ExpectedReturn:P2}");
Console.WriteLine($"Allocate:        {size.SuggestedSize:C2} ({size.AllocationPercent:P1})");
Console.WriteLine($"Reason:          {size.Reason}");

// Detailed signals for the best pick
Console.WriteLine("\nSignals (best pick):");
foreach (var s in bestPick.Signals)
{
    Console.WriteLine($"  [{s.Hint,-5}] {s.Name,-25} Score={s.Score:0.###} {s.Notes}");
}

Console.WriteLine("\n=== Done ===");