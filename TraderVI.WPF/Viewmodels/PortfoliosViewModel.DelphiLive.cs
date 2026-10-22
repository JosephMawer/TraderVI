#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class PortfoliosViewModel
{
    private readonly DelphiLivePortfolioReader liveReader = new();
    private readonly Dictionary<Guid, DelphiLivePortfolioAccount> liveAccounts = [];
    private int detailRequest;
    private sealed record HistoryView(Guid PortfolioId, DateTime AsOfUtc, PortfolioHoldingRow[] Holdings, PortfolioEventRow[] Events);
    private HistoryView? historyView;
    private int historyPage;
    private int historyRequest;
    private bool historyLoading;
    private CancellationTokenSource? historyCancellation;
    private bool ShowingHistory => IsLiveSelected && historyView?.PortfolioId == SelectedPortfolio?.DelphiLivePortfolioId;
    public bool CanLoadLiveHistory => IsLiveSelected && !historyLoading;
    public bool CanShowNewerLiveHistory => ShowingHistory && historyPage > 0;
    public bool CanShowOlderLiveHistory => ShowingHistory && (historyPage + 1) * 100 < Math.Max(historyView!.Holdings.Length, historyView.Events.Length);
    public string LiveActivityScope => !IsLiveSelected ? "Recent saved activity" : historyLoading ? "Loading saved history…" : ShowingHistory
        ? $"Saved history as of {historyView!.AsOfUtc.ToLocalTime():g} · page {historyPage + 1}. Return to current activity to manage holdings."
        : "Current session activity and latest closed positions. Earlier history is available on request.";
    private string liveAccountsStatus = "Delphi Live accounts not loaded";
    public string LiveAccountsStatus { get => liveAccountsStatus; private set { if (Set(ref liveAccountsStatus, value)) OnPropertyChanged(nameof(DisplayStatus)); } }
    public string DisplayStatus => IsLiveSelected ? LiveAccountsStatus : Status;
    public bool IsLiveSelected => SelectedPortfolio?.IsDelphiLive == true;
    public string CandidateHint => IsLiveSelected ? "Saved live states · live ranking and signals are in Delphi Live" :
        "Rechecked every daily Shadow poll · a blocked candidate may qualify later";
    public string ExecutionHint => IsLiveSelected ? "Delphi Live · separate paper account · saved bid/ask or tagged estimated fills" :
        "Daily Shadow · whole shares · friction follows the assigned strategy · no broker";
    public string SelectedAccountExplanation => SelectedPortfolio?.Explanation ?? "";

    private void RequireDailySelection()
    {
        if (IsLiveSelected) throw new InvalidOperationException("Use Delphi Live → Setup & advanced to manage that account. Daily Shadow controls apply only to the older daily portfolios.");
    }

    private async Task<IReadOnlyList<PortfolioOverviewRow>> ReadDelphiLiveRowsAsync(CancellationToken token)
    {
        try { return ApplyDelphiLiveAccounts(await Task.Run(() => liveReader.ReadAsync(token), token), DateTime.UtcNow); }
        catch (Exception error) when (error is SqlException or JsonException or InvalidOperationException or ArgumentException or IOException)
        {
            liveAccounts.Clear();
            LiveAccountsStatus = "Delphi Live accounts unavailable · refresh to retry";
            return [LivePlaceholder("Unavailable", "Saved Delphi Live accounts could not be read. This does not mean they are inactive.")];
        }
    }

    // Shared by the SQL refresh and the isolated presentation checks; contains no operational effects.
    public IReadOnlyList<PortfolioOverviewRow> ApplyDelphiLiveAccounts(DelphiLiveAccountsRead read, DateTime nowUtc)
    {
        liveAccounts.Clear();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.FindSystemTimeZoneById("America/Toronto")));
        foreach (var account in read.Accounts) liveAccounts.Add(account.Snapshot.PortfolioId, account);
        var rows = read.Accounts.Select(account =>
        {
            var p = account.Snapshot;
            var value = DelphiLiveAccountValuation.From(p, today);
            return new PortfolioOverviewRow($"DelphiLive:{p.PortfolioId:N}", null, account.DisplayName, "Delphi Live", "Paper",
                account.StatusAt(nowUtc), value.NetAssetValue, p.Cash, p.OpenPositions.Count(), value.RealizedProfitLoss,
                value.UnrealizedProfitLoss, value.TotalReturn, value.DailyReturn, value.Drawdown, value.MarkUtc ?? p.UpdatedUtc)
            {
                IsDelphiLive = true, DelphiLivePortfolioId = p.PortfolioId, Currency = p.Currency,
                StrategyName = $"Live policy {p.PolicyVersionId.ToString("N")[..8]}",
                Explanation = $"{account.DisplayName}. {account.StatusAt(nowUtc)}. Starts {p.EffectiveSession:MMM d, yyyy}; starting cash {p.StartingCapital:N2} {p.Currency}. " +
                    $"{value.Explanation} {DelphiLiveWorkingState.EstimatedFillCount(p)} estimated fill(s). " +
                    "Manage this account in Delphi Live → Setup & advanced. Historical replay accounts are viewed in Delphi Live → Replay account."
            };
        }).ToList();
        bool hasMain = read.Accounts.Any(a => a.Snapshot.Role == "OperationalChampion" && a.IsCurrentOrQueued(today));
        if (!hasMain) rows.Insert(0, LivePlaceholder(read.SchemaInstalled ? "Not activated" : "Not installed",
            read.SchemaInstalled ? "No Delphi Live main paper account has been activated. Use Delphi Live → Setup & advanced to review activation. The Friday replay is a separate historical simulation." :
            "The Delphi Live database schema is not installed. No account has been created by this view."));
        LiveAccountsStatus = !read.SchemaInstalled ? "Delphi Live schema not installed" : hasMain
            ? $"Delphi Live · {read.Accounts.Count} saved account(s), including history" : "Delphi Live · main paper account not activated";
        OnPropertyChanged(nameof(SelectedAccountExplanation));
        NotifyLiveHistory();
        return rows;
    }

    private static PortfolioOverviewRow LivePlaceholder(string status, string explanation) =>
        new("DelphiLive:inactive", null, "Delphi Live — Main paper account", "Delphi Live", "Paper", status,
            null, null, null, null, null, null, null, null, null)
        { IsDelphiLive = true, Currency = "—", Explanation = explanation, StrategyName = "No paper account" };

    private void ShowDelphiLiveDetails()
    {
        if (SelectedPortfolio?.DelphiLivePortfolioId is not Guid id || !liveAccounts.TryGetValue(id, out var account)) return;
        var p = account.Snapshot;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Toronto")));
        var valuation = DelphiLiveAccountValuation.From(p, today);
        Replace(Candidates, p.CandidateStates.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c =>
            new PortfolioCandidateRow(null, c.Key, Words(c.Value.Lifecycle.State.ToString()), null, null, null, null,
                Words(c.Value.Lifecycle.ReasonCode), c.Value.EvaluatedUtc)));
        if (ShowingHistory)
        {
            Replace(Holdings, historyView!.Holdings.Skip(historyPage * 100).Take(100));
            Replace(Events, historyView.Events.Skip(historyPage * 100).Take(100));
            NotifyLiveHistory();
            return;
        }
        Replace(Holdings, p.Positions.OrderByDescending(position => position.OpenedUtc).Select(position =>
        {
            var sale = p.Fills.SingleOrDefault(f => f.ActionId == position.ExitActionId && f.Side == DelphiLiveActionSide.Sell);
            decimal? price = sale?.Price ?? (valuation.PositionPrices.TryGetValue(position.PositionId, out var mark) ? mark : null);
            var exit = p.Actions.SingleOrDefault(a => a.Intent.ActionId == position.ExitActionId);
            return new PortfolioHoldingRow(position.Symbol, position.ClosedUtc.HasValue ? "Closed" : "Held", position.Quantity,
                position.AveragePurchasePrice, price, position.ClosedUtc.HasValue ? null : price * position.Quantity,
                price.HasValue ? (price - position.AveragePurchasePrice) * position.Quantity : null,
                position.Protection.FloorPrice, position.OpenedUtc.ToLocalTime(), exit is null ? "—" : Words(exit.PrimaryReason))
                { PositionId=position.PositionId, TargetId=p.PortfolioId, Family=Core.Runtime.EngineStrategySettings.Live };
        }));
        var actions = p.Actions.Select(a => new PortfolioEventRow(a.Intent.DecisionUtc.ToLocalTime(), $"{a.Intent.Side} decision",
            Words(a.PrimaryReason), $"{a.Intent.Symbol} · {a.Status} · {a.AttemptCount} quote attempt(s) · {Words(a.TerminalReason ?? "Pending")}"));
        var fills = p.Fills.Select(f => new PortfolioEventRow(f.FilledUtc.ToLocalTime(), $"{f.Side} fill",
            Words(f.Confidence.ToString()), $"{f.Symbol} · {f.Quantity} shares at {f.Price:N4} {p.Currency} · {f.Field}"));
        Replace(Events, actions.Concat(fills).OrderByDescending(e => e.TimeLocal).Take(100));
        NotifyLiveHistory();
    }

    public async Task LoadLiveHistoryAsync(CancellationToken ct = default)
    {
        if (!CanLoadLiveHistory || SelectedPortfolio?.DelphiLivePortfolioId is not Guid id) return;
        int request = ++historyRequest;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        historyCancellation = cancellation;
        var historyToken = cancellation.Token;
        historyLoading = true; NotifyLiveHistory();
        try
        {
            var view = await Task.Run(async () =>
            {
                var p = await new DelphiLiveLedgerRepository().LoadPortfolioAsync(id, historyToken)
                    ?? throw new InvalidOperationException("That account's saved history is unavailable.");
                var sales = p.Fills.Where(f => f.Side == DelphiLiveActionSide.Sell).ToDictionary(f => f.ActionId);
                var reasons = p.Actions.ToDictionary(a => a.Intent.ActionId, a => a.PrimaryReason);
                var rows = p.Positions.OrderByDescending(position => position.OpenedUtc).Select(position =>
                {
                    var sale = position.ExitActionId is Guid exit && sales.TryGetValue(exit, out var found) ? found : null;
                    string reason = position.ExitActionId is Guid action && reasons.TryGetValue(action, out var code) ? Words(code) : "—";
                    // Saved history is read-only. Current activity owns the holding action controls.
                    return new PortfolioHoldingRow(position.Symbol, position.ClosedUtc.HasValue ? "Closed" : "Held", position.Quantity,
                        position.AveragePurchasePrice, sale?.Price, null,
                        sale is null ? null : (sale.Price - position.AveragePurchasePrice) * sale.Quantity,
                        position.Protection.FloorPrice, position.OpenedUtc.ToLocalTime(), reason);
                }).ToArray();
                var actions = p.Actions.Select(a => new PortfolioEventRow(a.Intent.DecisionUtc.ToLocalTime(), $"{a.Intent.Side} decision",
                    Words(a.PrimaryReason), $"{a.Intent.Symbol} · {a.Status} · {a.AttemptCount} quote attempt(s) · {Words(a.TerminalReason ?? "Pending")}"));
                var fills = p.Fills.Select(f => new PortfolioEventRow(f.FilledUtc.ToLocalTime(), $"{f.Side} fill", Words(f.Confidence.ToString()),
                    $"{f.Symbol} · {f.Quantity} shares at {f.Price:N4} {p.Currency} · {f.Field}"));
                return new HistoryView(id, p.UpdatedUtc, rows, actions.Concat(fills).OrderByDescending(e => e.TimeLocal).ToArray());
            }, historyToken);
            if (request != historyRequest || SelectedPortfolio?.DelphiLivePortfolioId != id) return;
            historyView = view; historyPage = 0; ShowDelphiLiveDetails();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { historyCancellation = null; historyLoading = false; NotifyLiveHistory(); }
    }
    public void ShowEarlierLiveHistory() { if (CanShowOlderLiveHistory) { historyPage++; ShowDelphiLiveDetails(); } }
    public void ShowNewerLiveHistory() { if (CanShowNewerLiveHistory) { historyPage--; ShowDelphiLiveDetails(); } }
    public void ShowCurrentLiveActivity() { ResetLiveHistory(); ShowDelphiLiveDetails(); }
    private void ResetLiveHistory() { historyRequest++; historyCancellation?.Cancel(); historyView = null; historyPage = 0; }
    private void NotifyLiveHistory()
    {
        OnPropertyChanged(nameof(CanLoadLiveHistory)); OnPropertyChanged(nameof(CanShowNewerLiveHistory));
        OnPropertyChanged(nameof(CanShowOlderLiveHistory)); OnPropertyChanged(nameof(LiveActivityScope));
    }

    private static string Words(string value) => Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ");
}
