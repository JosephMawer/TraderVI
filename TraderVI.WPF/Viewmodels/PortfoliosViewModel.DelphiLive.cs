#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
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
    private string liveAccountsStatus = "Delphi Live accounts not loaded";
    public string LiveAccountsStatus { get => liveAccountsStatus; private set { if (Set(ref liveAccountsStatus, value)) OnPropertyChanged(nameof(DisplayStatus)); } }
    public string DisplayStatus => IsLiveSelected ? LiveAccountsStatus : Status;
    public bool IsLiveSelected => SelectedPortfolio?.IsDelphiLive == true;
    public string CandidateHint => IsLiveSelected ? "Saved live states · live ranking and signals are in Delphi Live" :
        "Rechecked every daily Shadow poll · a blocked candidate may qualify later";
    public string ExecutionHint => IsLiveSelected ? "Delphi Live · separate paper account · saved bid/ask or tagged estimated fills" :
        "Daily Shadow · whole shares · 0.25% friction each side · no broker";
    public string SelectedAccountExplanation => SelectedPortfolio?.Explanation ?? "";

    private void RequireDailySelection()
    {
        if (IsLiveSelected) throw new InvalidOperationException("Use Delphi Live → Setup & advanced to manage that account. Daily Shadow controls apply only to the older daily portfolios.");
    }

    private async Task<IReadOnlyList<PortfolioOverviewRow>> ReadDelphiLiveRowsAsync(CancellationToken token)
    {
        try { return ApplyDelphiLiveAccounts(await liveReader.ReadAsync(token), DateTime.UtcNow); }
        catch (Exception error) when (error is SqlException or JsonException or InvalidOperationException or ArgumentException)
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
                Explanation = $"{account.DisplayName}. {account.StatusAt(nowUtc)}. Starts {p.EffectiveSession:MMM d, yyyy}; starting cash {p.StartingCapital:N2} {p.Currency}. " +
                    $"{value.Explanation} {p.Fills.Count(f => f.Confidence == DelphiLiveFillConfidence.EstimatedFill)} estimated fill(s). " +
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
        return rows;
    }

    private static PortfolioOverviewRow LivePlaceholder(string status, string explanation) =>
        new("DelphiLive:inactive", null, "Delphi Live — Main paper account", "Delphi Live", "Paper", status,
            null, null, null, null, null, null, null, null, null)
        { IsDelphiLive = true, Currency = "—", Explanation = explanation };

    private void ShowDelphiLiveDetails()
    {
        if (SelectedPortfolio?.DelphiLivePortfolioId is not Guid id || !liveAccounts.TryGetValue(id, out var account)) return;
        var p = account.Snapshot;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Toronto")));
        var valuation = DelphiLiveAccountValuation.From(p, today);
        Replace(Candidates, p.CandidateStates.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c =>
            new PortfolioCandidateRow(null, c.Key, Words(c.Value.Lifecycle.State.ToString()), null, null, null, null,
                Words(c.Value.Lifecycle.ReasonCode), c.Value.EvaluatedUtc)));
        Replace(Holdings, p.Positions.OrderByDescending(position => position.OpenedUtc).Select(position =>
        {
            var sale = p.Fills.SingleOrDefault(f => f.ActionId == position.ExitActionId && f.Side == DelphiLiveActionSide.Sell);
            decimal? price = sale?.Price ?? (valuation.PositionPrices.TryGetValue(position.PositionId, out var mark) ? mark : null);
            var exit = p.Actions.SingleOrDefault(a => a.Intent.ActionId == position.ExitActionId);
            return new PortfolioHoldingRow(position.Symbol, position.ClosedUtc.HasValue ? "Closed" : "Held", position.Quantity,
                position.AveragePurchasePrice, price, position.ClosedUtc.HasValue ? null : price * position.Quantity,
                price.HasValue ? (price - position.AveragePurchasePrice) * position.Quantity : null,
                position.Protection.FloorPrice, position.OpenedUtc.ToLocalTime(), exit is null ? "—" : Words(exit.PrimaryReason));
        }));
        var actions = p.Actions.Select(a => new PortfolioEventRow(a.Intent.DecisionUtc.ToLocalTime(), $"{a.Intent.Side} decision",
            Words(a.PrimaryReason), $"{a.Intent.Symbol} · {a.Status} · {a.AttemptCount} quote attempt(s) · {Words(a.TerminalReason ?? "Pending")}"));
        var fills = p.Fills.Select(f => new PortfolioEventRow(f.FilledUtc.ToLocalTime(), $"{f.Side} fill",
            Words(f.Confidence.ToString()), $"{f.Symbol} · {f.Quantity} shares at {f.Price:N4} {p.Currency} · {f.Field}"));
        Replace(Events, actions.Concat(fills).OrderByDescending(e => e.TimeLocal).Take(100));
    }

    private static string Words(string value) => Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ");
}
