using Core.Db;
using Core.Runtime;
using Core.Trader;
using System;
using System.Threading.Tasks;

namespace TraderVI;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry();

        var quoteRepo = new QuoteRepository();
        var symbols = await new SymbolsRepository().GetSymbols();

        foreach (var sym in symbols)
        {
            var bars = await quoteRepo.GetDailyBarsAsync(sym.Symbol);
            if (bars.Count < 30) continue;

            var decision = engine.Evaluate(bars);

            if (decision.Direction != TradeDirection.Hold)
                Console.WriteLine($"{sym.Symbol}: {decision.Direction}");
        }
    }
}