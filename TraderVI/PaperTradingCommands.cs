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
    private const int PollIntervalMinutes = 15;
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
        if (args.Length > 1 ||
            (args.Length == 1 &&
             !string.Equals(args[0], "watch", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Usage: paper-monitor [watch]");
        }

        bool watch = args.Length == 1;
        do
        {
            await MonitorOnceAsync();
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

            DateTime nextPollLocal = NextPollLocal(localNow);
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

    private static async Task MonitorOnceAsync()
    {
        DateTime pollStartedUtc = DateTime.UtcNow;
        var positionRepository = new ActivePositionRepository();
        List<ActivePositionInfo> positions = (await positionRepository.GetActivePositions())
            .Where(position => position.OriginalPickId.HasValue)
            .OrderBy(position => position.Symbol)
            .ToList();
        if (positions.Count == 0)
        {
            Console.WriteLine("No active paper positions linked to a Delphi pick.");
            return;
        }

        using var tmx = new TmxClient();
        Console.WriteLine();
        Console.WriteLine(
            $"ADR-0028 paper monitor — {ToToronto(pollStartedUtc):yyyy-MM-dd HH:mm:ss} Toronto");
        Console.WriteLine("Advisory only: updates ghost position snapshots but never records a sell.");
        Console.WriteLine(
            $"{"Symbol",8} {"Entry",10} {"Observed",10} {"P/L",9} " +
            $"{"Last policy bar",20} {"Trail",10} {"Directive",34} {"Age",12}");
        Console.WriteLine(new string('-', 124));

        foreach (ActivePositionInfo position in positions)
        {
            try
            {
                TradeLogInfo entryTrade = await GetEntryTradeAsync(position.Symbol);
                DateTime entryUtc = DateTime.SpecifyKind(
                    entryTrade.CreatedUtc,
                    DateTimeKind.Utc);
                DateTime entryLocalDate = ToToronto(entryUtc).Date;
                DateTime requestStartUtc = ToUtc(
                    entryLocalDate.AddHours(9).AddMinutes(30));
                TmxIntradayBatch batch = pollStartedUtc - requestStartUtc > TimeSpan.FromDays(5)
                    ? await tmx.GetIntradayTimeSeriesChunkedAsync(
                        position.Symbol,
                        PollIntervalMinutes,
                        requestStartUtc,
                        pollStartedUtc)
                    : await tmx.GetIntradayTimeSeriesBatchAsync(
                        position.Symbol,
                        PollIntervalMinutes,
                        requestStartUtc,
                        pollStartedUtc);
                OhlcvBar observed = batch.Bars.LastOrDefault()
                    ?? throw new InvalidOperationException(
                        $"TMX returned no intraday evidence for {position.Symbol}.");
                IReadOnlyList<OhlcvBar> completedPolicyEvidence = batch.Bars
                    .Where(bar =>
                        bar.TimestampUtc.AddMinutes(PollIntervalMinutes) <= batch.ReceivedUtc)
                    .ToList()
                    .AsReadOnly();
                IReadOnlyList<DelayedIntradayBar> policyBars =
                    CompletedIntradayBarAggregator.BuildPolicyBars(
                        completedPolicyEvidence,
                        batch.ReceivedUtc,
                        entryUtc);

                IntradaySwingPositionState state =
                    IntradaySwingPositionState.Open(position.EntryPrice, entryUtc);
                IntradaySwingDecision? decision = null;
                foreach (DelayedIntradayBar bar in policyBars)
                {
                    decision = DelayedIntradaySwingExitPolicy.Evaluate(state, bar);
                    state = decision.State;
                    if (decision.Directive == IntradaySwingDirective.ExitAlert)
                        break;
                }

                decimal currentValue = decimal.Round(observed.Close * position.Shares, 2);
                decimal unrealized = decimal.Round(currentValue - position.CostBasis, 2);
                double unrealizedPercent = position.CostBasis == 0m
                    ? 0d
                    : (double)(unrealized / position.CostBasis);
                decimal highWater = state.HighestCompletedClose;
                double drawdown = highWater == 0m
                    ? 0d
                    : (double)(observed.Close / highWater - 1m);
                await positionRepository.UpdatePositionPrices(
                    position.PositionId,
                    observed.Close,
                    currentValue,
                    unrealized,
                    unrealizedPercent,
                    highWater,
                    drawdown,
                    state.LastTradingSessionOrdinal);

                string policyEvent = decision is null
                    ? "waiting"
                    : ToToronto(decision.State.LastProcessedBarEndUtc!.Value)
                        .ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string trail = state.TrailingStopPrice.HasValue
                    ? state.TrailingStopPrice.Value.ToString("C", CultureInfo.CurrentCulture)
                    : "—";
                string directive = decision is null
                    ? "Hold — awaiting first full bar"
                    : decision.Reason == IntradaySwingReason.None
                        ? "Hold — no exit signal"
                        : $"{decision.Directive} — {decision.Reason}";
                string age = decision is null ? "—" : decision.DataAge.ToString("g");
                Console.WriteLine(
                    $"{position.Symbol,8} {position.EntryPrice,10:C} {observed.Close,10:C} " +
                    $"{unrealizedPercent,9:P2} {policyEvent,20} {trail,10} " +
                    $"{directive,34} {age,12}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"{position.Symbol,8} {position.EntryPrice,10:C} {"—",10} " +
                    $"{"—",9} {"—",20} {"—",10} " +
                    $"{"Unavailable — retry next poll",34} {"—",12}");
                Console.Error.WriteLine(
                    $"[{position.Symbol}] Monitor error type: {ex.GetType().Name}.");
            }
        }
    }

    private static async Task<TradeLogInfo> GetEntryTradeAsync(string symbol)
    {
        List<TradeLogInfo> trades = await new TradeLogRepository()
            .GetTradesBySymbol(symbol);
        return trades
            .Where(trade =>
                string.Equals(trade.TradeType, "BUY", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(trade => trade.CreatedUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No BUY trade exists for active position {symbol}.");
    }

    private static DateTime NextPollLocal(DateTime localNow)
    {
        DateTime hour = new(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            localNow.Hour,
            0,
            0,
            DateTimeKind.Unspecified);
        int nextBoundaryMinute =
            ((localNow.Minute / PollIntervalMinutes) + 1) * PollIntervalMinutes;
        DateTime boundary = hour.AddMinutes(nextBoundaryMinute);
        return boundary.AddMinutes(2);
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
