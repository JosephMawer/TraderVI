#nullable enable

using Core.Db;
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace TraderVI;

internal static class PaperTradingCommands
{
    private const string Lens = "Continuation";
    private const int SharesPerSymbol = 1;
    private const int SourceIntervalMinutes = 5;
    private static readonly TimeZoneInfo TorontoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public static async Task EnterAsync(string[] args)
    {
        bool dryRun = args.Any(arg =>
            string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
        string[] symbols = args
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .Select(arg => arg.Trim().ToUpperInvariant())
            .Where(arg => arg.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (symbols.Length == 0)
            throw new ArgumentException(
                "Usage: paper-enter [--dry-run] SYMBOL [SYMBOL ...]");

        DateTime startedUtc = DateTime.UtcNow;
        DateTime localNow = ToToronto(startedUtc);
        EnsureRegularSession(localNow);
        DateTime marketOpenUtc = ToUtc(localNow.Date.AddHours(9).AddMinutes(30));

        var positionRepository = new ActivePositionRepository();
        List<ActivePositionInfo> active =
            await positionRepository.GetActivePositions();
        string[] conflicts = active
            .Where(position => symbols.Contains(
                position.Symbol,
                StringComparer.OrdinalIgnoreCase))
            .Select(position => position.Symbol)
            .OrderBy(symbol => symbol)
            .ToArray();
        if (conflicts.Length > 0)
            throw new InvalidOperationException(
                $"Active positions already exist for: {string.Join(", ", conflicts)}.");

        var pickRepository = new DailyPickRepository();
        var picks = new List<DailyPickInfo>();
        foreach (string symbol in symbols)
        {
            DailyPickInfo? pick = await pickRepository.GetPickByDateAndSymbol(
                localNow.Date,
                symbol,
                Lens);
            if (pick is null)
                throw new InvalidOperationException(
                    $"No persisted {Lens} pick exists for {symbol} on {localNow:yyyy-MM-dd}.");
            if (!string.Equals(pick.Direction, "Buy", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Persisted {Lens} pick {symbol} is {pick.Direction}, not Buy.");
            picks.Add(pick);
        }

        using var tmx = new TmxClient();
        var candidates = new List<PaperEntryCandidate>();
        foreach (DailyPickInfo pick in picks.OrderBy(pick => pick.Rank))
        {
            TmxIntradayBatch batch = await tmx.GetIntradayTimeSeriesBatchAsync(
                pick.Symbol,
                SourceIntervalMinutes,
                marketOpenUtc,
                startedUtc);
            OhlcvBar observed = batch.Bars.LastOrDefault()
                ?? throw new InvalidOperationException(
                    $"TMX returned no current-session five-minute evidence for {pick.Symbol}.");
            candidates.Add(new PaperEntryCandidate(
                pick,
                observed.Close,
                observed.TimestampUtc,
                batch.ReceivedUtc,
                observed.TimestampUtc.AddMinutes(SourceIntervalMinutes) > batch.ReceivedUtc));
        }

        Console.WriteLine(dryRun
            ? "Paper-entry preflight — NO WRITES"
            : "Paper-entry cohort — GHOST DATABASE WRITES ONLY; NO BROKER ORDER");
        Console.WriteLine(
            $"{"Rank",6} {"Symbol",8} {"Shares",8} {"Observed",12} " +
            $"{"Bar event",20} {"Received",20} {"State",10} {"PickId",38}");
        Console.WriteLine(new string('-', 132));
        foreach (PaperEntryCandidate candidate in candidates)
        {
            Console.WriteLine(
                $"{candidate.Pick.Rank,6} {candidate.Pick.Symbol,8} " +
                $"{SharesPerSymbol,8} {candidate.Price,12:C} " +
                $"{ToToronto(candidate.EventUtc),20:MM-dd HH:mm:ss} " +
                $"{ToToronto(candidate.ReceivedUtc),20:MM-dd HH:mm:ss} " +
                $"{(candidate.WasForming ? "forming" : "complete"),10} " +
                $"{candidate.Pick.PickId,38}");
        }

        if (dryRun)
        {
            Console.WriteLine("Result: preflight passed; no position or trade row was written.");
            return;
        }

        var manager = new TradeManager(ghost: true);
        foreach (PaperEntryCandidate candidate in candidates)
        {
            string notes =
                $"OfficialPaper {Lens} rank={candidate.Pick.Rank}; " +
                $"TMX5 eventUtc={candidate.EventUtc:O}; " +
                $"receivedUtc={candidate.ReceivedUtc:O}; " +
                $"sourceState={(candidate.WasForming ? "forming" : "complete")}; " +
                "one-share ADR-0029 pilot";
            bool inserted = await manager.Buy(
                candidate.Pick.Symbol,
                SharesPerSymbol,
                candidate.Price,
                notes,
                candidate.Pick.PickId,
                candidate.Pick.CompositeScore,
                "OfficialPaper intraday entry");
            if (!inserted)
                throw new InvalidOperationException(
                    $"Paper cohort stopped because {candidate.Pick.Symbol} could not be opened.");
        }

        Console.WriteLine("Result: paper cohort opened. Run 'paper-monitor watch' for advisory monitoring.");
    }

    public static async Task MonitorAsync(string[] args)
    {
        bool hasUnknownArgument = args.Any(arg =>
            !string.Equals(arg, "watch", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(arg, "--advisory-only", StringComparison.OrdinalIgnoreCase));
        if (args.Length > 2 || hasUnknownArgument)
        {
            throw new ArgumentException(
                "Usage: paper-monitor [watch] [--advisory-only]");
        }

        bool watch = args.Any(arg =>
            string.Equals(arg, "watch", StringComparison.OrdinalIgnoreCase));
        bool executeGhostExits = !args.Any(arg =>
            string.Equals(arg, "--advisory-only", StringComparison.OrdinalIgnoreCase));
        var monitor = new PaperTradingMonitor();
        do
        {
            PaperMonitorCycleResult cycle =
                await monitor.PollOnceAsync(executeGhostExits);
            PrintCycle(cycle);
            if (!watch)
                return;

            DateTime localNow = ToToronto(DateTime.UtcNow);
            DateTime finalPollLocal = localNow.Date.AddHours(16).AddMinutes(2);
            if (localNow >= finalPollLocal ||
                localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                Console.WriteLine("Monitor complete for the regular TSX session.");
                return;
            }

            DateTime nextPollLocal =
                PaperTradingMonitor.NextScheduledPollLocal(localNow);
            TimeSpan delay = ToUtc(nextPollLocal) - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                Console.WriteLine(
                    $"Next advisory poll: {nextPollLocal:HH:mm:ss} Toronto " +
                    $"({delay:g}). Press Ctrl+C to stop.");
                await Task.Delay(delay);
            }
        }
        while (true);
    }

    public static async Task AddSavedPickAsync(string[] args)
    {
        if (args.Length != 4 ||
            !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shares) ||
            !decimal.TryParse(args[3], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fillPrice))
        {
            throw new ArgumentException(
                "Usage: paper-add SYMBOL LENS SHARES FILL_PRICE");
        }

        string symbol = args[0].Trim().ToUpperInvariant();
        string lens = args[1].Trim();
        if (!string.Equals(lens, "Continuation", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(lens, "Breakout", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("LENS must be Continuation or Breakout.");
        }

        var repository = new DailyPickRepository();
        DateTime pickDate = await repository.GetLatestPickDate()
            ?? throw new InvalidOperationException("No saved Delphi picks exist.");
        DailyPickInfo pick = await repository.GetPickByDateAndSymbol(pickDate, symbol, lens)
            ?? throw new InvalidOperationException(
                $"No saved {lens} pick exists for {symbol} on {pickDate:yyyy-MM-dd}.");

        PaperTradeEntryResult result = await new PaperTradeEntryWorkflow()
            .OpenAsync(pick.PickId, shares, fillPrice);
        Console.WriteLine(result.Message);
    }

    private static void PrintCycle(PaperMonitorCycleResult cycle)
    {
        if (cycle.Positions.Count == 0)
        {
            Console.WriteLine("No active paper positions linked to a Delphi pick.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            $"ADR-0031 durable paper monitor — " +
            $"{ToToronto(cycle.StartedUtc):yyyy-MM-dd HH:mm:ss} Toronto");
        Console.WriteLine(cycle.AutomaticGhostExitsEnabled
            ? "Ghost policy exits enabled; no broker orders can be sent."
            : "Advisory-only override; no ghost exit will be recorded.");
        Console.WriteLine(
            $"{"Symbol",8} {"Entry",10} {"Observed",10} {"P/L",9} " +
            $"{"Last policy bar",20} {"Trail",10} {"Directive",36} {"Audit",11}");
        Console.WriteLine(new string('-', 128));

        foreach (PaperPositionMonitorResult position in cycle.Positions)
        {
            string observed = position.ObservedPrice?.ToString("C") ?? "—";
            string pnl = position.UnrealizedPnLPct?.ToString("P2") ?? "—";
            string policyEvent = position.LastPolicyBarEndUtc.HasValue
                ? ToToronto(position.LastPolicyBarEndUtc.Value)
                    .ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : "waiting";
            string trail = position.TrailingStopPrice?.ToString("C") ?? "—";
            string directive = position.ErrorCode is not null
                ? $"Unavailable — {position.ErrorCode}"
                : position.ExitExecuted
                    ? $"Ghost exit — {position.Reason} @ {position.ExitPrice:C}"
                    : position.Directive is null
                        ? "Hold — awaiting first full bar"
                        : position.Reason == IntradaySwingReason.None
                            ? "Hold — no exit signal"
                            : $"{position.Directive} — {position.Reason}";
            if (position.WarningCode is not null)
                directive += $" — {position.WarningCode}";
            string audit =
                $"{position.FifteenMinuteAuditState?.ToString() ?? "—"}/" +
                $"{position.FiveMinuteAuditState?.ToString() ?? "—"}";
            Console.WriteLine(
                $"{position.Symbol,8} {position.EntryPrice,10:C} {observed,10} " +
                $"{pnl,9} {policyEvent,20} {trail,10} " +
                $"{directive,36} {audit,11}");
        }
    }

    private static void EnsureRegularSession(DateTime localNow)
    {
        if (localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
            localNow.TimeOfDay < new TimeSpan(9, 30, 0) ||
            localNow.TimeOfDay >= new TimeSpan(16, 0, 0))
        {
            throw new InvalidOperationException(
                "Paper entry is allowed only during the regular 9:30-16:00 Toronto session.");
        }
    }

    private static DateTime ToToronto(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TorontoTimeZone);

    private static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            TorontoTimeZone);

    private sealed record PaperEntryCandidate(
        DailyPickInfo Pick,
        decimal Price,
        DateTime EventUtc,
        DateTime ReceivedUtc,
        bool WasForming);
}
