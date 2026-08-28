using Core.Db;
using Core.Runtime;
using Core.Trader;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace TraderVI;

internal class Program
{
    // Ghost mode: log + simulate Wealthsimple, never send a live order. Default for now.
    private const bool Ghost = true;

    private static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        var verb = args[0].ToLowerInvariant();
        try
        {
            switch (verb)
            {
                case "buy":
                    await BuyCommand(args);
                    break;
                case "sell":
                    await SellCommand(args);
                    break;
                case "list":
                    await ListCommand();
                    break;
                case "pnl":
                    await PnlCommand();
                    break;
                case "scan":
                    await ScanCommand();
                    break;
                case "paper-enter":
                    await PaperTradingCommands.EnterAsync(args.Skip(1).ToArray());
                    break;
                case "paper-add":
                    await PaperTradingCommands.AddSavedPickAsync(args.Skip(1).ToArray());
                    break;
                case "paper-monitor":
                    await PaperTradingCommands.MonitorAsync(args.Skip(1).ToArray());
                    break;
                default:
                    Console.WriteLine($"Unknown command: '{verb}'");
                    PrintUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] {ex.Message}");
        }
    }

    private static async Task BuyCommand(string[] args)
    {
        // buy SYMBOL SHARES PRICE [notes...]
        if (args.Length < 4
            || !int.TryParse(args[2], out var shares)
            || !decimal.TryParse(args[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
        {
            Console.WriteLine("Usage: buy SYMBOL SHARES PRICE [notes]");
            return;
        }

        var notes = args.Length > 4 ? string.Join(' ', args.Skip(4)) : null;
        var manager = new TradeManager(Ghost);
        await manager.Buy(args[1], shares, price, notes);
    }

    private static async Task SellCommand(string[] args)
    {
        // sell SYMBOL PRICE [notes...]
        if (args.Length < 3
            || !decimal.TryParse(args[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
        {
            Console.WriteLine("Usage: sell SYMBOL PRICE [notes]");
            return;
        }

        var notes = args.Length > 3 ? string.Join(' ', args.Skip(3)) : null;
        var manager = new TradeManager(Ghost);
        await manager.Sell(args[1], price, notes);
    }

    private static async Task ListCommand()
    {
        var positions = await new ActivePositionRepository().GetActivePositions();
        if (positions.Count == 0)
        {
            Console.WriteLine("No open positions.");
            return;
        }

        Console.WriteLine("Open positions:");
        Console.WriteLine($"{"Symbol",-8}{"Shares",8}{"Entry",12}{"CostBasis",14}{"Stop",12}{"EntryDate",14}");
        Console.WriteLine(new string('-', 68));
        foreach (var p in positions)
        {
            Console.WriteLine($"{p.Symbol,-8}{p.Shares,8}{p.EntryPrice,12:C}{p.CostBasis,14:C}{p.StopLossPrice,12:C}{p.EntryDate,14:yyyy-MM-dd}");
        }
    }

    private static async Task PnlCommand()
    {
        var repo = new TradeLogRepository();
        var (totalPnL, wins, losses) = await repo.GetPnLSummary();
        var closed = wins + losses;
        var winRate = closed == 0 ? 0d : (double)wins / closed;

        Console.WriteLine("Realized P&L summary:");
        Console.WriteLine($"  Total realized P&L: {totalPnL:C}");
        Console.WriteLine($"  Wins / Losses:      {wins} / {losses} ({winRate:P0} win rate)");

        var recent = await repo.GetRecentTrades(10);
        if (recent.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Recent trades:");
        Console.WriteLine($"{"Date",-12}{"Type",-6}{"Symbol",-8}{"Shares",8}{"Price",12}{"RealizedPnL",14}");
        Console.WriteLine(new string('-', 60));
        foreach (var t in recent)
        {
            var pnl = t.RealizedPnL.HasValue ? t.RealizedPnL.Value.ToString("C", CultureInfo.CurrentCulture) : "-";
            Console.WriteLine($"{t.TradeDate,-12:yyyy-MM-dd}{t.TradeType,-6}{t.Symbol,-8}{t.Shares,8}{t.Price,12:C}{pnl,14}");
        }
    }

    private static async Task ScanCommand()
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

    private static void PrintUsage()
    {
        Console.WriteLine("TraderVI - trade logging (ghost mode: trades are logged + simulated, no live orders)");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  buy  SYMBOL SHARES PRICE [notes]   Log a buy and open a position with a -10% stop");
        Console.WriteLine("  sell SYMBOL PRICE [notes]          Log a sell, realize P&L, and close the position");
        Console.WriteLine("  list                               Show open positions");
        Console.WriteLine("  pnl                                Show realized P&L summary and recent trades");
        Console.WriteLine("  scan                               Run Delphi evaluation and print non-Hold directions");
        Console.WriteLine("  paper-enter [--dry-run] SYMBOL...  Link today's Continuation picks to one-share ghost entries");
        Console.WriteLine("  paper-add SYMBOL LENS SHARES PRICE  Open a saved Delphi pick at an operator fill");
        Console.WriteLine("  paper-monitor [watch] [--advisory-only]  Persist evidence and auto-close ghost exits");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  TraderVI buy CS 46 15.01 \"breakout lens #1\"");
        Console.WriteLine("  TraderVI buy BLDP 80 8.68");
        Console.WriteLine("  TraderVI sell CS 15.85");
        Console.WriteLine("  TraderVI paper-enter --dry-run NDM CMG ALK EDR OGI");
        Console.WriteLine("  TraderVI paper-monitor watch");
        Console.WriteLine("  TraderVI paper-monitor --advisory-only");
    }
}
