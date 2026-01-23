using Core.Db;
using Core.ML;
using Core.Runtime;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== The Oracle Of Delphi ===\n");

var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry();

// ═══════════════════════════════════════════════════════════════════
// Single symbol evaluation
// ═══════════════════════════════════════════════════════════════════
const string symbol = "CEU";

var history = await new QuoteRepository().GetDailyBarsAsync(symbol);

if (history.Count < 35)
{
    Console.WriteLine($"Not enough history for {symbol}. Bars loaded: {history.Count}");
    return;
}

var result = engine.Evaluate(history);

Console.WriteLine($"Ticker: {symbol}");
Console.WriteLine($"Final Direction: {result.Direction}");
Console.WriteLine($"Expected Return: {result.ExpectedReturn:P2}");
Console.WriteLine($"Confidence: {result.Confidence:P1}");
Console.WriteLine("\nSignals:");

foreach (var s in result.Signals)
{
    Console.WriteLine($"  - {s.Name}: Score={s.Score:0.###}, Hint={s.Hint}, Notes={s.Notes}");
}

// ═══════════════════════════════════════════════════════════════════
// Multi-symbol ranking (top picks)
// ═══════════════════════════════════════════════════════════════════
Console.WriteLine("\n" + new string('═', 60));
Console.WriteLine("TOP RANKED PICKS");
Console.WriteLine(new string('═', 60) + "\n");

var quoteRepo = new QuoteRepository();
var symbols = (await new SymbolsRepository().GetSymbols())
    .Select(s => s.Symbol)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Take(50)
    .ToList();

var allBars = new Dictionary<string, IReadOnlyList<DailyBar>>();
foreach (var sym in symbols)
{
    var bars = await quoteRepo.GetDailyBarsAsync(sym);
    if (bars.Count >= 35)
        allBars[sym] = bars;
}

var topPicks = engine.EvaluateAndRank(allBars, topN: 10);

Console.WriteLine($"{"Rank",-5} {"Symbol",-10} {"Direction",-10} {"ExpReturn",12} {"Confidence",12}");
Console.WriteLine(new string('-', 55));

int rank = 1;
foreach (var pick in topPicks)
{
    Console.WriteLine($"{rank,-5} {pick.Symbol,-10} {pick.Direction,-10} {pick.ExpectedReturn,12:P2} {pick.Confidence,12:P1}");
    rank++;
}

Console.WriteLine("\n=== Done ===");