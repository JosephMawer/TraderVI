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
// CONFIGURATION
// ═══════════════════════════════════════════════════════════════════
decimal availableCapital = 500.00m;  // Your trading capital
int maxPositions = 5;                // Maximum number of positions
int topSymbolsToEvaluate = 50;       // How many symbols to scan

Console.WriteLine($"Available Capital: ${availableCapital:N2}");
Console.WriteLine($"Max Positions: {maxPositions}");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
// BOOTSTRAP ENGINE
// ═══════════════════════════════════════════════════════════════════
var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry();

// Configure position sizer
engine.Sizer = new PositionSizer(availableCapital)
{
    MaxPositionPercent = 0.25m,    // Max 25% per position ($125)
    MinPositionSize = 25m,         // Min $25 per position
    MinExpectedReturn = 0.005,     // Min 0.5% expected return
    MinConfidence = 0.40,          // Min 40% confidence
    KellyFraction = 0.5            // Half-Kelly for safety
};

// ═══════════════════════════════════════════════════════════════════
// SINGLE SYMBOL EVALUATION (detailed)
// ═══════════════════════════════════════════════════════════════════
const string symbol = "CEU";

var history = await new QuoteRepository().GetDailyBarsAsync(symbol);

if (history.Count >= 35)
{
    var result = engine.Evaluate(history);

    Console.WriteLine(new string('─', 60));
    Console.WriteLine($"DETAILED ANALYSIS: {symbol}");
    Console.WriteLine(new string('─', 60));
    Console.WriteLine($"Direction:       {result.Direction}");
    Console.WriteLine($"Expected Return: {result.ExpectedReturn:P2}");
    Console.WriteLine($"Confidence:      {result.Confidence:P1}");

    if (result.PositionSize != null)
    {
        Console.WriteLine($"Position Size:   ${result.PositionSize.SuggestedSize:N2} ({result.PositionSize.AllocationPercent:P1})");
        Console.WriteLine($"Sizing Reason:   {result.PositionSize.Reason}");
    }

    Console.WriteLine("\nSignals:");
    foreach (var s in result.Signals)
    {
        Console.WriteLine($"  [{s.Hint,-5}] {s.Name,-20} Score={s.Score:0.###}");
    }
}
else
{
    Console.WriteLine($"Not enough history for {symbol}. Bars: {history.Count}");
}

// ═══════════════════════════════════════════════════════════════════
// MULTI-SYMBOL SCAN + POSITION SIZING
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine("\n" + new string('═', 60));
Console.WriteLine("PORTFOLIO RECOMMENDATIONS");
Console.WriteLine(new string('═', 60) + "\n");

var quoteRepo = new QuoteRepository();
var symbols = (await new SymbolsRepository().GetSymbols())
    .Select(s => s.Symbol)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Take(topSymbolsToEvaluate)
    .ToList();

Console.WriteLine($"Scanning {symbols.Count} symbols...\n");

var allBars = new Dictionary<string, IReadOnlyList<DailyBar>>();
foreach (var sym in symbols)
{
    var bars = await quoteRepo.GetDailyBarsAsync(sym);
    if (bars.Count >= 35)
        allBars[sym] = bars;
}

// Get sized portfolio recommendations
var portfolio = engine.EvaluateRankAndSize(allBars, availableCapital, maxPositions);

if (portfolio.Count == 0)
{
    Console.WriteLine("No positions meet the sizing criteria.");
}
else
{
    decimal totalAllocated = portfolio.Sum(p => p.PositionSize);
    decimal remainingCash = availableCapital - totalAllocated;

    Console.WriteLine($"{"#",-3} {"Symbol",-10} {"Action",-6} {"ExpRet",10} {"Conf",8} {"Size",12} {"Alloc",8}");
    Console.WriteLine(new string('─', 65));

    int rank = 1;
    foreach (var pick in portfolio)
    {
        Console.WriteLine($"{rank,-3} {pick.Symbol,-10} {pick.Direction,-6} {pick.ExpectedReturn,10:P2} {pick.Confidence,8:P1} {pick.PositionSize,12:C2} {pick.AllocationPercent,8:P1}");
        rank++;
    }

    Console.WriteLine(new string('─', 65));
    Console.WriteLine($"{"TOTAL",-20} {"",-6} {"",-10} {"",-8} {totalAllocated,12:C2} {(double)(totalAllocated / availableCapital),8:P1}");
    Console.WriteLine($"{"CASH REMAINING",-20} {"",-6} {"",-10} {"",-8} {remainingCash,12:C2}");
}

// ═══════════════════════════════════════════════════════════════════
// SUMMARY
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine("\n" + new string('═', 60));
Console.WriteLine("POSITION SIZING PARAMETERS");
Console.WriteLine(new string('═', 60));
Console.WriteLine($"  Max per position:    {engine.Sizer.MaxPositionPercent:P0} (${availableCapital * engine.Sizer.MaxPositionPercent:N2})");
Console.WriteLine($"  Min position size:   ${engine.Sizer.MinPositionSize:N2}");
Console.WriteLine($"  Min expected return: {engine.Sizer.MinExpectedReturn:P2}");
Console.WriteLine($"  Min confidence:      {engine.Sizer.MinConfidence:P0}");
Console.WriteLine($"  Kelly fraction:      {engine.Sizer.KellyFraction:P0}");

Console.WriteLine("\n=== Done ===");