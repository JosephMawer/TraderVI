// Replace empty while loop with actual decision engine integration:
using Core.Db;
using Core.Trader;
using System;
using System.Threading.Tasks;

static async Task Main(string[] args)
{
    // 1. Bootstrap Delphi with trained models from registry
    var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry();

    // 2. Get latest data
    var quoteRepo = new QuoteRepository();
    var symbols = await new SymbolsRepository().GetSymbols();

    foreach (var sym in symbols)
    {
        var bars = await quoteRepo.GetDailyBarsAsync(sym.Symbol);
        if (bars.Count < 30) continue;

        // 3. Evaluate with all loaded models
        var decision = engine.Evaluate(bars);

        if (decision.Direction != TradeDirection.Hold)
        {
            Console.WriteLine($"{sym.Symbol}: {decision.Direction}");
            // Execute trade via TradeManager...
        }
    }
}