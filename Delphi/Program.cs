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
decimal availableCapital = 500.00m;   // total capital available
int minBarsRequired = 35;             // must satisfy max lookback (e.g., 35 for MA crossover lookback)
decimal reserveCashPercent = 0.02m;   // keep 2% cash; set 0.00m for true all-in
double minExpectedReturn = 0.01;      // require >= 1% expected return
double minConfidence = 0.55;          // require >= 55% confidence
int maxSymbolsToScan = 500;           // safety limit

Console.WriteLine($"Available Capital: ${availableCapital:N2}");
Console.WriteLine($"Reserve Cash:      {reserveCashPercent:P0}");
Console.WriteLine($"Min Exp Return:    {minExpectedReturn:P2}");
Console.WriteLine($"Min Confidence:    {minConfidence:P0}");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
// BOOTSTRAP ENGINE (loads enabled models from registry)
// ═══════════════════════════════════════════════════════════════════
var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry();

// Configure aggressive sizing behavior (single-position)
engine.Sizer = new PositionSizer(availableCapital)
{
    Strategy = AllocationStrategy.SinglePositionAllIn,
    ReserveCashPercent = reserveCashPercent,
    MinPositionSize = 25m,
    MinExpectedReturn = minExpectedReturn,
    MinConfidence = minConfidence,
    RequireBothSignals = true
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
Console.WriteLine("BEST PICK (SINGLE-POSITION MODE)");
Console.WriteLine(new string('═', 70));

if (bestPick == null || size == null || size.SuggestedSize <= 0)
{
    Console.WriteLine("No qualifying trade found (did not pass expected return / confidence gates).");
    return;
}

Console.WriteLine($"Symbol:          {bestPick.Symbol}");
Console.WriteLine($"Direction:       {bestPick.Direction}");
Console.WriteLine($"Expected Return: {bestPick.ExpectedReturn:P2}");
Console.WriteLine($"Confidence:      {bestPick.Confidence:P1}");
Console.WriteLine($"Allocate:        {size.SuggestedSize:C2} ({size.AllocationPercent:P1})");
Console.WriteLine($"Reason:          {size.Reason}");

// Optional: show a short ranked list for transparency
Console.WriteLine("\nTop Ranked Candidates:");
var top = engine.EvaluateAndRank(allBars, topN: 10);

Console.WriteLine($"{"#",-3} {"Symbol",-10} {"Action",-6} {"ExpRet",10} {"Conf",8}");
Console.WriteLine(new string('─', 45));

int rank = 1;
foreach (var p in top)
{
    Console.WriteLine($"{rank,-3} {p.Symbol,-10} {p.Direction,-6} {p.ExpectedReturn,10:P2} {p.Confidence,8:P1}");
    rank++;
}

// Detailed signals for the best pick
Console.WriteLine("\nSignals (best pick):");
foreach (var s in bestPick.Signals)
{
    Console.WriteLine($"  [{s.Hint,-5}] {s.Name,-20} Score={s.Score:0.###} {s.Notes}");
}

Console.WriteLine("\n=== Done ===");