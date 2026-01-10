using Core.Db;
using Core.ML;
using Delphi.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

Console.WriteLine("The Oracle Of Delphi");

var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry();

const string symbol = "CEU";

// Load enough daily bars so Trend30 can evaluate (needs 30)
var history = await new QuoteRepository().GetDailyBarsAsync(symbol);

if (history.Count < 30)
{
    Console.WriteLine($"Not enough history for {symbol}. Bars loaded: {history.Count}");
    return;
}

var result = engine.Evaluate(history);

Console.WriteLine();
Console.WriteLine($"Ticker: {symbol}");
Console.WriteLine($"Final Direction: {result.Direction}");
Console.WriteLine("Signals:");

foreach (var s in result.Signals)
{
    Console.WriteLine($"- {s.Name}: Score={s.Score:0.###}, Hint={s.Hint}, Notes={s.Notes}");
}