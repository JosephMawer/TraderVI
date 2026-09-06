#nullable enable

using Core.Db;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed class PortfoliosViewModel : INotifyPropertyChanged
{
    private readonly SystemShadowRepository repository = new();
    private SystemShadowGenerationInfo? generation;
    private PortfolioOverviewRow? selectedPortfolio;
    private string totalTfsaValue = "";
    private string availableTfsaCash = "";
    private string status = "Loading portfolio ledger…";
    private string generationStatus = "Shadow off";
    private string portfolioDisplayName = "";
    private bool schemaInstalled;
    private bool busy;

    public ObservableCollection<PortfolioOverviewRow> Portfolios { get; } = [];
    public ObservableCollection<PortfolioCandidateRow> Candidates { get; } = [];
    public ObservableCollection<PortfolioHoldingRow> Holdings { get; } = [];
    public ObservableCollection<PortfolioEventRow> Events { get; } = [];

    public string TotalTfsaValue
    {
        get => totalTfsaValue;
        set => Set(ref totalTfsaValue, value);
    }

    public string AvailableTfsaCash
    {
        get => availableTfsaCash;
        set => Set(ref availableTfsaCash, value);
    }

    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public string GenerationStatus
    {
        get => generationStatus;
        private set => Set(ref generationStatus, value);
    }

    public PortfolioOverviewRow? SelectedPortfolio
    {
        get => selectedPortfolio;
        set
        {
            if (!Set(ref selectedPortfolio, value)) return;
            PortfolioDisplayName = value?.DisplayName ?? "";
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanRename));
        }
    }

    public string PortfolioDisplayName
    {
        get => portfolioDisplayName;
        set => Set(ref portfolioDisplayName, value);
    }

    public bool CanStart => schemaInstalled && !busy && generation is null;
    public bool CanPause => schemaInstalled && !busy && generation?.Status == SystemShadowGenerationStatus.Active;
    public bool CanResume => schemaInstalled && !busy &&
        (generation?.Status == SystemShadowGenerationStatus.Paused || SelectedPortfolio?.Status == "CapitalReviewRequired");
    public bool CanRecordSnapshot => schemaInstalled && !busy && generation is not null;
    public bool CanRename => schemaInstalled && !busy && SelectedPortfolio?.SystemPortfolioId is not null;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        string? selectedCode = SelectedPortfolio?.StableCode;
        schemaInstalled = await repository.HasSchemaAsync(cancellationToken);
        if (!schemaInstalled)
        {
            generation = null;
            GenerationStatus = "Shadow schema not installed";
            Status = $"Apply {SystemShadowRepository.MigrationFileName}; Shadow is safely off.";
            Replace(Portfolios, await BuildTrackedRowsAsync(null, null));
            ClearDetails();
            NotifyActions();
            return;
        }

        generation = await repository.GetLatestGenerationAsync(cancellationToken);
        SystemShadowAccountSnapshot? accountSnapshot = generation is null
            ? null
            : await repository.GetLatestAccountSnapshotAsync(generation.GenerationId, cancellationToken);
        if (accountSnapshot is not null)
        {
            TotalTfsaValue = accountSnapshot.TotalAccountValue.ToString("0.00", CultureInfo.CurrentCulture);
            AvailableTfsaCash = accountSnapshot.AvailableAccountCash.ToString("0.00", CultureInfo.CurrentCulture);
        }

        IReadOnlyList<SystemShadowPortfolioOverview> system = generation is null
            ? Array.Empty<SystemShadowPortfolioOverview>()
            : await repository.GetPortfolioOverviewsAsync(generation.GenerationId, cancellationToken);
        var rows = new List<PortfolioOverviewRow>();
        rows.AddRange(await BuildTrackedRowsAsync(generation, accountSnapshot));
        rows.AddRange(system.Select(PortfolioOverviewRow.FromSystem));
        Replace(Portfolios, rows);
        if (selectedCode is not null)
            SelectedPortfolio = Portfolios.FirstOrDefault(x => x.StableCode == selectedCode);
        GenerationStatus = generation is null
            ? "Shadow off · enter TFSA capital to begin"
            : $"{generation.PolicyVersion} · {generation.Status} · started {generation.ActivatedUtc?.ToLocalTime():MMM d HH:mm}";
        Status = generation is null
            ? "No system-selected trades can occur until you click Start Shadow V1."
            : "System portfolios use virtual cash only. No broker connection and no real order is sent.";
        await RefreshDetailsAsync(cancellationToken);
        NotifyActions();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        (decimal total, decimal cash) = ReadCapital();
        await BusyAsync(async () =>
        {
            generation = await repository.CreateAndActivateGenerationAsync(
                total,
                cash,
                DateTime.UtcNow,
                cancellationToken);
            Status = "Shadow V1 activated. Its first decision must wait for fresh completed five-minute evidence.";
        });
        await RefreshAsync(cancellationToken);
    }

    public async Task RecordAccountSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (generation is null)
            throw new InvalidOperationException("Start Shadow before adding comparison snapshots.");
        (decimal total, decimal cash) = ReadCapital();
        await BusyAsync(() => repository.RecordAccountSnapshotAsync(
            generation.GenerationId,
            total,
            cash,
            DateTime.UtcNow,
            cancellationToken));
        await RefreshAsync(cancellationToken);
        Status = "Real TFSA comparison snapshot recorded. Shadow cash and performance history were not overwritten.";
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (generation is null)
            return;
        await BusyAsync(() => repository.SetGenerationStatusAsync(
            generation.GenerationId,
            SystemShadowGenerationStatus.Paused,
            "Operator paused new Shadow risk from the Portfolios tab.",
            cancellationToken));
        await RefreshAsync(cancellationToken);
        Status = "New buys are paused. Existing positions remain monitored and risk exits remain active.";
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (generation is null)
            return;
        if (SelectedPortfolio is { Status: "CapitalReviewRequired", SystemPortfolioId: Guid portfolioId })
        {
            await BusyAsync(() => repository.ResumePortfolioAfterCapitalReviewAsync(
                portfolioId,
                "Operator reviewed the 10% drawdown and explicitly resumed new risk.",
                cancellationToken));
        }
        else
        {
            await BusyAsync(() => repository.SetGenerationStatusAsync(
                generation.GenerationId,
                SystemShadowGenerationStatus.Active,
                "Operator resumed Shadow from the Portfolios tab.",
                cancellationToken));
        }
        await RefreshAsync(cancellationToken);
        Status = "Shadow resumed. New risk still requires the normal evidence checks.";
    }

    public async Task RenameSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedPortfolio?.SystemPortfolioId is not Guid portfolioId)
            return;
        await BusyAsync(() => repository.UpdateDisplayNameAsync(
            portfolioId,
            PortfolioDisplayName,
            cancellationToken));
        await RefreshAsync(cancellationToken);
        Status = "Display name updated. The portfolio's stable identity did not change.";
    }

    public async Task RefreshDetailsAsync(CancellationToken cancellationToken = default)
    {
        ClearDetails();
        if (SelectedPortfolio?.SystemPortfolioId is not Guid portfolioId)
            return;

        Task<IReadOnlyList<SystemShadowCandidateMonitorInfo>> candidatesTask =
            repository.GetCandidateMonitorAsync(portfolioId, cancellationToken);
        Task<IReadOnlyList<SystemShadowPositionInfo>> positionsTask =
            repository.GetPositionsAsync(portfolioId, cancellationToken);
        Task<IReadOnlyList<SystemShadowEventInfo>> eventsTask =
            repository.GetRecentEventsAsync(portfolioId, 100, cancellationToken);
        await Task.WhenAll(candidatesTask, positionsTask, eventsTask);

        Replace(Candidates, candidatesTask.Result.Select(PortfolioCandidateRow.From));
        Replace(Holdings, positionsTask.Result.Select(PortfolioHoldingRow.From));
        Replace(Events, eventsTask.Result.Select(PortfolioEventRow.From));
    }

    public void ApplyPollResult(SystemShadowPollResult result)
    {
        Status = result.Warnings.Count == 0
            ? $"Shadow poll: {result.SymbolsPolled} symbol(s), {result.OrdersFilled} fill(s), {result.SignalsCreated} new signal(s)."
            : $"Shadow poll completed with {result.Warnings.Count} evidence warning(s); new risk was blocked where evidence was unsafe.";
    }

    private async Task BusyAsync(Func<Task> operation)
    {
        busy = true;
        NotifyActions();
        try
        {
            await operation();
        }
        finally
        {
            busy = false;
            NotifyActions();
        }
    }

    private (decimal Total, decimal Cash) ReadCapital()
    {
        if (!TryMoney(TotalTfsaValue, out decimal total) || total <= 0m)
            throw new InvalidOperationException("Enter a positive total TFSA value.");
        if (!TryMoney(AvailableTfsaCash, out decimal cash) || cash < 0m || cash > total)
            throw new InvalidOperationException("Available TFSA cash must be between zero and the total TFSA value.");
        return (total, cash);
    }

    private static bool TryMoney(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Currency, CultureInfo.CurrentCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static async Task<IReadOnlyList<PortfolioOverviewRow>> BuildTrackedRowsAsync(
        SystemShadowGenerationInfo? generation,
        SystemShadowAccountSnapshot? snapshot)
    {
        List<ActivePositionInfo> positions = (await new ActivePositionRepository()
            .GetRecentPositions(1000))
            .Where(x => x.IsActive && TrackedPositionScope.Includes(x))
            .ToList();
        List<TradeLogInfo> trades = await new TradeLogRepository()
            .GetRecentTrades(1000);
        var rows = new List<PortfolioOverviewRow>();
        decimal realValue = snapshot?.TotalAccountValue ?? generation?.TotalAccountValue ?? 0m;
        decimal realCash = snapshot?.AvailableAccountCash ?? generation?.AvailableAccountCash ?? 0m;
        decimal initial = generation?.TotalAccountValue ?? 0m;
        rows.Add(PortfolioOverviewRow.ForTracked(
            "Real", "Real — Wealthsimple TFSA", "Operator", "Real",
            realValue, realCash,
            positions.Count(x => x.ExecutionMode == TrackedExecutionMode.Real),
            trades.Where(x => x.ExecutionMode == TrackedExecutionMode.Real).Sum(x => x.RealizedPnL ?? 0m),
            positions.Where(x => x.ExecutionMode == TrackedExecutionMode.Real).Sum(x => x.UnrealizedPnL ?? 0m),
            initial > 0m ? realValue / initial - 1m : 0m,
            snapshot?.OccurredUtc,
            snapshot is null ? "Manual snapshot missing" : SnapshotStatus(snapshot.OccurredUtc)));

        List<ActivePositionInfo> ghost = positions.Where(x => x.ExecutionMode == TrackedExecutionMode.Ghost).ToList();
        rows.Add(PortfolioOverviewRow.ForTracked(
            "OperatorGhost", "Ghost — Operator Selected", "Operator", "Ghost",
            ghost.Sum(x => x.CurrentValue ?? x.CostBasis), 0m, ghost.Count,
            trades.Where(x => x.ExecutionMode == TrackedExecutionMode.Ghost).Sum(x => x.RealizedPnL ?? 0m),
            ghost.Sum(x => x.UnrealizedPnL ?? 0m), 0m,
            ghost.Count == 0 ? null : ghost.Max(x => x.LastUpdatedUtc),
            "Tracked positions only"));
        return rows;
    }

    private static string SnapshotStatus(DateTime occurredUtc) =>
        DateTime.UtcNow - occurredUtc > TimeSpan.FromDays(1) ? "Real snapshot stale" : "Real snapshot current";

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanRecordSnapshot));
        OnPropertyChanged(nameof(CanRename));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (T row in rows) target.Add(row);
    }

    private void ClearDetails()
    {
        Candidates.Clear();
        Holdings.Clear();
        Events.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record PortfolioOverviewRow(
    string StableCode,
    Guid? SystemPortfolioId,
    string DisplayName,
    string Selector,
    string Execution,
    string Status,
    decimal NetAssetValue,
    decimal Cash,
    int OpenPositions,
    decimal RealizedProfitLoss,
    decimal UnrealizedProfitLoss,
    decimal TotalReturn,
    decimal? DailyReturn,
    decimal Drawdown,
    DateTime? FreshnessUtc)
{
    public string FreshnessText => FreshnessUtc?.ToLocalTime().ToString("MMM d HH:mm") ?? "—";

    public static PortfolioOverviewRow FromSystem(SystemShadowPortfolioOverview x) =>
        new(x.PortfolioCode, x.PortfolioId, x.DisplayName, "System", "Ghost",
            x.Status != "Active" ? x.Status : x.SessionStatus ?? x.Status,
            x.NetAssetValue, x.Cash, x.OpenPositions, x.RealizedProfitLoss, x.UnrealizedProfitLoss,
            x.TotalReturn, x.DailyReturn, x.Drawdown,
            Latest(x.FreshestPriceEventUtc, x.LatestCandidateEvaluationUtc) ?? x.UpdatedUtc);

    public static PortfolioOverviewRow ForTracked(
        string code, string name, string selector, string execution, decimal nav, decimal cash,
        int open, decimal realized, decimal unrealized, decimal totalReturn, DateTime? freshness, string status) =>
        new(code, null, name, selector, execution, status, nav, cash, open, realized, unrealized,
            totalReturn, null, 0m, freshness.HasValue ? DateTime.SpecifyKind(freshness.Value, DateTimeKind.Utc) : null);

    private static DateTime? Latest(DateTime? first, DateTime? second)
    {
        if (!first.HasValue) return second;
        if (!second.HasValue) return first;
        return first.Value >= second.Value ? first : second;
    }
}

public sealed record PortfolioCandidateRow(
    int Rank,
    string Symbol,
    string State,
    decimal PreviousSessionClose,
    decimal? PreviousFiveMinuteClose,
    decimal? LatestFiveMinuteClose,
    DateTime? LatestFiveMinuteBarUtc,
    string? ReasonCode,
    DateTime? LastEvaluatedUtc)
{
    public decimal? PercentVsPreviousSession => LatestFiveMinuteClose.HasValue && PreviousSessionClose > 0m
        ? LatestFiveMinuteClose.Value / PreviousSessionClose - 1m
        : null;

    public string LatestBarText => LatestFiveMinuteBarUtc?.AddMinutes(5).ToLocalTime().ToString("HH:mm") ?? "Waiting";
    public string LastEvaluatedText => LastEvaluatedUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "Waiting";

    public string ReasonText => ReasonCode switch
    {
        "Qualified" => "Qualified — waiting for a later fill bar",
        "BelowPreviousSessionClose" => "Below yesterday's close",
        "FallingFromPreviousFiveMinuteClose" => "Falling versus the prior five-minute bar",
        "MissingEvidence" => "Waiting for complete five-minute evidence",
        "LateEvidence" => "Market evidence arrived too late",
        "ConflictingEvidence" => "Market evidence conflicts",
        "MarketClosed" => "Market closed without an entry",
        null or "" => "Not evaluated yet",
        _ => ReasonCode
    };

    public static PortfolioCandidateRow From(SystemShadowCandidateMonitorInfo x) =>
        new(x.Rank, x.Symbol, x.State, x.PreviousSessionClose, x.PreviousFiveMinuteClose,
            x.LatestFiveMinuteClose, x.LatestFiveMinuteBarUtc, x.ReasonCode, x.LastEvaluatedUtc);
}

public sealed record PortfolioHoldingRow(
    string Symbol,
    string Status,
    int Shares,
    decimal AverageCost,
    decimal LastPrice,
    decimal MarketValue,
    decimal ProfitLoss,
    decimal? TrailingStop,
    DateTime EntryLocal,
    string ExitReason)
{
    public static PortfolioHoldingRow From(SystemShadowPositionInfo x) =>
        new(x.Symbol, x.Status, x.Shares, x.AverageCost, x.LastPrice, x.Shares * x.LastPrice,
            x.Status == "Open" ? x.Shares * x.LastPrice - x.CostBasis : x.RealizedProfitLoss,
            x.TrailingStopPrice, x.EntryUtc.ToLocalTime(), x.ExitReasonCode ?? "—");
}

public sealed record PortfolioEventRow(
    DateTime TimeLocal,
    string EventType,
    string ReasonCode,
    string Details)
{
    public static PortfolioEventRow From(SystemShadowEventInfo x) =>
        new(x.OccurredUtc.ToLocalTime(), x.EventType, x.ReasonCode, x.DetailsJson);
}
