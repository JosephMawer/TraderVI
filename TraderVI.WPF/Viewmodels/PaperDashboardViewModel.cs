#nullable enable

using Core.Db;
using Core.TMX.Models.Domain;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TraderVI.WPF.Viewmodels;

public sealed class PaperDashboardViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<Guid, PaperPositionMonitorResult> latestResults = [];
    private int openPositionCount;
    private int closedPositionCount;
    private decimal realizedPnL;
    private decimal unrealizedPnL;
    private string marketStatus = "Loading…";
    private string nextPollText = "—";
    private string lastReceiptText = "No durable poll yet";
    private string monitorStatus = "Starting";
    private string latestEventText = "Ready · durable SQL history loaded by the dashboard";
    private bool automaticGhostExitsEnabled = true;

    public ObservableCollection<PaperPositionRow> Positions { get; } = [];
    public ObservableCollection<PaperTradeRow> Trades { get; } = [];
    public ObservableCollection<PaperPollRow> Polls { get; } = [];
    public ObservableCollection<PaperMonitorEventRow> Events { get; } = [];

    public int OpenPositionCount
    {
        get => openPositionCount;
        private set => Set(ref openPositionCount, value);
    }

    public int ClosedPositionCount
    {
        get => closedPositionCount;
        private set => Set(ref closedPositionCount, value);
    }

    public decimal RealizedPnL
    {
        get => realizedPnL;
        private set => Set(ref realizedPnL, value);
    }

    public decimal UnrealizedPnL
    {
        get => unrealizedPnL;
        private set => Set(ref unrealizedPnL, value);
    }

    public string MarketStatus
    {
        get => marketStatus;
        private set => Set(ref marketStatus, value);
    }

    public string NextPollText
    {
        get => nextPollText;
        private set => Set(ref nextPollText, value);
    }

    public string LastReceiptText
    {
        get => lastReceiptText;
        private set => Set(ref lastReceiptText, value);
    }

    public string MonitorStatus
    {
        get => monitorStatus;
        set => Set(ref monitorStatus, value);
    }

    public bool AutomaticGhostExitsEnabled
    {
        get => automaticGhostExitsEnabled;
        set => Set(ref automaticGhostExitsEnabled, value);
    }

    public string LatestEventText
    {
        get => latestEventText;
        private set => Set(ref latestEventText, value);
    }

    public bool CanPollNow => PaperTradingMonitor.IsAutomaticPollTime(
        PaperTradingMonitor.ToToronto(DateTime.UtcNow));

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        List<ActivePositionInfo> positions =
            (await new ActivePositionRepository().GetRecentPositions(250))
            .Where(position => position.OriginalPickId.HasValue)
            .OrderBy(position => position.EntryDate)
            .ThenBy(position => position.Symbol)
            .ToList();
        HashSet<Guid> paperPositionIds = positions
            .Select(position => position.PositionId)
            .ToHashSet();
        List<TradeLogInfo> trades = (await new TradeLogRepository().GetRecentTrades(250))
            .Where(trade =>
                (trade.PositionId.HasValue && paperPositionIds.Contains(trade.PositionId.Value)) ||
                (trade.Notes?.Contains("OfficialPaper", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (trade.Notes?.Contains("ADR-0028", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (trade.Notes?.Contains("ADR-0031", StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(trade => trade.CreatedUtc)
            .ToList();
        IReadOnlyList<IntradayPollObservationInfo> polls =
            await new IntradayEvidenceRepository().GetRecentObservationsAsync(
                250,
                cancellationToken);

        var exitsByPosition = trades
            .Where(trade =>
                trade.PositionId.HasValue &&
                string.Equals(trade.TradeType, "SELL", StringComparison.OrdinalIgnoreCase))
            .GroupBy(trade => trade.PositionId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(x => x.CreatedUtc).First());

        Replace(
            Positions,
            positions.Select(position => PaperPositionRow.Create(
                position,
                exitsByPosition.GetValueOrDefault(position.PositionId),
                latestResults.GetValueOrDefault(position.PositionId))));
        Replace(Trades, trades.Select(PaperTradeRow.Create));
        Replace(Polls, polls.Select(PaperPollRow.Create));

        OpenPositionCount = positions.Count(position => position.IsActive);
        ClosedPositionCount = positions.Count(position => !position.IsActive);
        RealizedPnL = trades
            .Where(trade => string.Equals(trade.TradeType, "SELL", StringComparison.OrdinalIgnoreCase))
            .Sum(trade => trade.RealizedPnL ?? 0m);
        UnrealizedPnL = positions
            .Where(position => position.IsActive)
            .Sum(position => position.UnrealizedPnL ?? 0m);

        IntradayPollObservationInfo? latestReceipt = polls
            .Where(poll => poll.ReceivedUtc.HasValue)
            .OrderByDescending(poll => poll.ReceivedUtc)
            .FirstOrDefault();
        LastReceiptText = latestReceipt?.ReceivedUtc is DateTime received
            ? $"{PaperTradingMonitor.ToToronto(DateTime.SpecifyKind(received, DateTimeKind.Utc)):MMM d · HH:mm:ss} · {latestReceipt.AuditState}"
            : "No durable poll yet";
        RefreshClock();
    }

    public void ApplyCycle(PaperMonitorCycleResult cycle)
    {
        foreach (PaperPositionMonitorResult result in cycle.Positions)
            latestResults[result.PositionId] = result;

        MonitorStatus = cycle.Positions.Any(position => position.ErrorCode is not null)
            ? "Completed with warnings"
            : $"Cycle complete · {cycle.Positions.Count} position(s)";
        AddEvent(
            cycle.CompletedUtc,
            cycle.Positions.Count == 0
                ? "Monitor cycle completed; no active Delphi-linked paper positions."
                : $"Monitor cycle {cycle.PollCycleId.ToString()[..8]} completed for {cycle.Positions.Count} position(s).",
            cycle.Positions.Any(position => position.ErrorCode is not null) ? "Warning" : "Info");

        foreach (PaperPositionMonitorResult exit in cycle.Positions.Where(x => x.ExitExecuted))
        {
            AddEvent(
                cycle.CompletedUtc,
                $"{exit.Symbol} ghost exit recorded at {exit.ExitPrice:C3} ({exit.Reason}).",
                "Exit");
        }
    }

    public void AddEvent(DateTime utc, string message, string level)
    {
        LatestEventText = $"{PaperTradingMonitor.ToToronto(DateTime.SpecifyKind(utc, DateTimeKind.Utc)):HH:mm:ss} · {level} · {message}";
        Events.Insert(0, new PaperMonitorEventRow(
            PaperTradingMonitor.ToToronto(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            level,
            message));
        while (Events.Count > 100)
            Events.RemoveAt(Events.Count - 1);
    }

    public void RefreshClock()
    {
        DateTime localNow = PaperTradingMonitor.ToToronto(DateTime.UtcNow);
        bool marketWindow = PaperTradingMonitor.IsAutomaticPollTime(localNow);
        MarketStatus = marketWindow ? "Market monitor active" : "Market closed · history only";
        DateTime next = PaperTradingMonitor.NextScheduledPollLocal(localNow);
        NextPollText = marketWindow && next.TimeOfDay <= new TimeSpan(16, 2, 0)
            ? next.ToString("HH:mm:ss")
            : "Next regular session";
        OnPropertyChanged(nameof(CanPollNow));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source)
            target.Add(item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record PaperPositionRow(
    string Symbol,
    string Status,
    decimal EntryPrice,
    decimal? CurrentOrExitPrice,
    decimal PnL,
    double PnLPct,
    decimal? HighWaterMark,
    decimal? TrailingStopPrice,
    string Directive,
    DateTime LastUpdatedLocal,
    Brush PnLBrush)
{
    public static PaperPositionRow Create(
        ActivePositionInfo position,
        TradeLogInfo? exit,
        PaperPositionMonitorResult? latest)
    {
        decimal pnl = position.IsActive
            ? position.UnrealizedPnL ?? 0m
            : exit?.RealizedPnL ?? position.UnrealizedPnL ?? 0m;
        double pnlPct = position.IsActive
            ? position.UnrealizedPnLPct ?? 0d
            : exit?.RealizedPnLPct ?? position.UnrealizedPnLPct ?? 0d;
        string directive = latest?.ErrorCode is not null
            ? $"Unavailable · {latest.ErrorCode}"
            : latest?.ExitExecuted == true
                ? $"Exited · {latest.Reason}"
                : latest?.Directive is not null
                    ? latest.Reason == IntradaySwingReason.None
                        ? "Hold"
                        : $"{latest.Directive} · {latest.Reason}"
                    : exit?.Reason ?? (position.IsActive ? "Awaiting next poll" : "Closed");
        return new PaperPositionRow(
            position.Symbol,
            position.IsActive ? "OPEN" : "CLOSED",
            position.EntryPrice,
            position.IsActive ? position.CurrentPrice : exit?.Price ?? position.CurrentPrice,
            pnl,
            pnlPct,
            position.HighWaterMark,
            latest?.TrailingStopPrice,
            directive,
            PaperTradingMonitor.ToToronto(
                DateTime.SpecifyKind(position.LastUpdatedUtc, DateTimeKind.Utc)),
            pnl < 0m ? Brushes.IndianRed : pnl > 0m ? Brushes.MediumSeaGreen : Brushes.Gainsboro);
    }
}

public sealed record PaperTradeRow(
    DateTime TimeLocal,
    string Symbol,
    string Side,
    int Shares,
    decimal Price,
    decimal? RealizedPnL,
    string Reason,
    Brush PnLBrush)
{
    public static PaperTradeRow Create(TradeLogInfo trade) =>
        new(
            PaperTradingMonitor.ToToronto(
                DateTime.SpecifyKind(trade.CreatedUtc, DateTimeKind.Utc)),
            trade.Symbol,
            trade.TradeType,
            trade.Shares,
            trade.Price,
            trade.RealizedPnL,
            trade.Reason ?? "—",
            trade.RealizedPnL < 0m
                ? Brushes.IndianRed
                : trade.RealizedPnL > 0m
                    ? Brushes.MediumSeaGreen
                    : Brushes.Gainsboro);
}

public sealed record PaperPollRow(
    DateTime TimeLocal,
    string Symbol,
    int IntervalMinutes,
    string AuditState,
    int CompletedBars,
    int NewBars,
    string LatestEvent)
{
    public static PaperPollRow Create(IntradayPollObservationInfo poll) =>
        new(
            PaperTradingMonitor.ToToronto(DateTime.SpecifyKind(
                poll.ReceivedUtc ?? poll.CreatedUtc,
                DateTimeKind.Utc)),
            poll.Symbol,
            poll.IntervalMinutes,
            poll.AuditCode is null ? poll.AuditState : $"{poll.AuditState} · {poll.AuditCode}",
            poll.CompletedBarCount,
            poll.PersistedNewBarCount,
            poll.LatestCompletedEventUtc.HasValue
                ? PaperTradingMonitor.ToToronto(DateTime.SpecifyKind(
                    poll.LatestCompletedEventUtc.Value,
                    DateTimeKind.Utc)).ToString("MMM d HH:mm")
                : "—");
}

public sealed record PaperMonitorEventRow(
    DateTime TimeLocal,
    string Level,
    string Message);
