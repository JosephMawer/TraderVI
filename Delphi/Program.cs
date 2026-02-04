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
int topPicksToSave = 10;              // Save top N picks to database
bool saveToDB = true;                 // Toggle DB persistence

Console.WriteLine($"Available Capital: ${availableCapital:N2}");
Console.WriteLine($"Reserve Cash:      {reserveCashPercent:P0}");
Console.WriteLine($"Min Composite:     {minConfidence:P0}");
Console.WriteLine($"Ranking Mode:      Probability-based");
Console.WriteLine($"Save to DB:        {saveToDB}");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
// LOAD ACTIVE STRATEGY VERSION (for parameters + tracking)
// ═══════════════════════════════════════════════════════════════════
var strategyRepo = new StrategyVersionRepository();
var activeStrategy = await strategyRepo.GetActiveVersion();

Guid? strategyVersionId = activeStrategy?.VersionId;
if (activeStrategy != null)
{
    Console.WriteLine($"Strategy Version:  {activeStrategy.VersionName}");
    Console.WriteLine($"Description:       {activeStrategy.Description}");
    Console.WriteLine();
}
else
{
    Console.WriteLine("⚠️  No active strategy version found. Using defaults.\n");
}

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
    RequireBothSignals = false
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
var top = engine.EvaluateAndRank(allBars, topN: topPicksToSave);

// Helper to fetch individual probabilities from a pick's signals
static double GetProb(RankedPick pick, string nameEquals) =>
    pick.Signals
        .FirstOrDefault(s => string.Equals(s.Name, nameEquals, StringComparison.OrdinalIgnoreCase))
        ?.Score ?? 0;

static double GetProbContains(RankedPick pick, string nameContains) =>
    pick.Signals
        .FirstOrDefault(s => s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
        ?.Score ?? 0;

static double GetBreakoutProb(RankedPick pick)
{
    double enhanced = GetProb(pick, "BreakoutEnhanced");
    if (enhanced > 0) return enhanced;

    return GetProb(pick, "BreakoutPriorHigh10");
}

static double GetDirectionProb(RankedPick pick)
{
    // Prefer BinaryUp10, else legacy 4pct/3pct
    double binaryUp = GetProb(pick, "BinaryUp10");
    if (binaryUp > 0) return binaryUp;

    double up4 = GetProbContains(pick, "4pct");
    if (up4 > 0) return up4;

    double up3 = GetProbContains(pick, "3pct");
    if (up3 > 0) return up3;

    return 0;
}

static double GetRiskAdjUpProb(RankedPick pick) => GetProb(pick, "RiskAdjUp10");
static double GetRiskAdjDownProb(RankedPick pick) => GetProb(pick, "RiskAdjDown10");

Console.WriteLine(
    $"{"#",-3} {"Symbol",-10} {"Action",-6} {"Comp",7} {"Break",7} {"Vol",6} {"Dir",6} {"P↑",6} {"P↓",6} {"Δ",6}");
Console.WriteLine(new string('─', 80));

int rank = 1;
foreach (var p in top)
{
    double breakout = GetBreakoutProb(p);
    double volExp = GetProbContains(p, "VolExpansion");
    double dirProb = GetDirectionProb(p);
    double pUp = GetRiskAdjUpProb(p);
    double pDown = GetRiskAdjDownProb(p);
    double spread = pUp - pDown;

    Console.WriteLine(
        $"{rank,-3} {p.Symbol,-10} {p.Direction,-6} {p.CompositeScore,7:P0} {breakout,7:P0} {volExp,6:P0} {dirProb,6:P0} {pUp,6:P0} {pDown,6:P0} {spread,6:P0}");
    rank++;
}

// ═══════════════════════════════════════════════════════════════════
// SAVE DAILY PICKS TO DATABASE
// ═══════════════════════════════════════════════════════════════════
if (saveToDB && top.Count > 0)
{
    var pickDate = DateTime.Today;
    var pickRepo = new DailyPickRepository();

    // Delete any existing picks for today (in case of re-run)
    await pickRepo.DeletePicksByDate(pickDate);

    Console.WriteLine($"\nSaving {top.Count} picks to database...");

    int savedRank = 1;
    foreach (var p in top)
    {
        double breakout = GetBreakoutProb(p);
        double volExp = GetProbContains(p, "VolExpansion");
        double dirProb = GetDirectionProb(p);
        double relStrength = GetProbContains(p, "RelStrength");

        await pickRepo.InsertPick(
            pickDate: pickDate,
            symbol: p.Symbol,
            rank: savedRank,
            direction: p.Direction.ToString(),
            compositeScore: p.CompositeScore,
            breakoutProb: breakout,
            directionProb: dirProb,
            volExpansionProb: volExp,
            relStrengthProb: relStrength > 0 ? relStrength : null,
            expectedReturn: p.ExpectedReturn,
            suggestedSize: savedRank == 1 && size != null ? size.SuggestedSize : null,
            allocationPercent: savedRank == 1 && size != null ? (double)size.AllocationPercent : null,
            strategyVersionId: strategyVersionId,
            notes: savedRank == 1 ? "Top pick" : null);

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
double bestVolExp = GetProbContains(bestPick, "VolExpansion");
double bestDirProb = GetDirectionProb(bestPick);
double bestRelStrength = GetProbContains(bestPick, "RelStrength");

Console.WriteLine($"\n{"═",-70}");
Console.WriteLine("RECOMMENDATION");
Console.WriteLine($"{"═",-70}");
Console.WriteLine($"Symbol:          {bestPick.Symbol}");
Console.WriteLine($"Direction:       {bestPick.Direction}");
Console.WriteLine($"Composite Score: {bestPick.CompositeScore:P1}");
Console.WriteLine($"Breakout Prob:   {bestBreakout:P1}");
Console.WriteLine($"Vol Exp Prob:    {bestVolExp:P1}");
Console.WriteLine($"Direction Prob:  {bestDirProb:P1}");
if (bestRelStrength > 0)
    Console.WriteLine($"Rel Strength:    {bestRelStrength:P1}");
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