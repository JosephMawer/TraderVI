#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public interface IWealthsimpleAccountSource : INotifyPropertyChanged
{
    bool IsConnected { get; }
    bool HasSession { get; }
    long SessionRevision { get; }
    Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount account, CancellationToken ct);
}

// Broker display only. There is deliberately no reference to a SQL repository, strategy or order workflow.
public sealed class WealthsimpleAccountViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IWealthsimpleAccountSource source;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource lifetime = new();
    private long revision;
    private bool attempted, disposed;
    private string message = "Sign in with Wealthsimple to load your self-directed TFSA.";
    public event PropertyChangedEventHandler? PropertyChanged;
    public WealthsimpleAccountViewModel(IWealthsimpleAccountSource source, TimeProvider? clock = null)
    {
        this.source = source; this.clock = clock ?? TimeProvider.System;
        revision = source.SessionRevision;
        source.PropertyChanged += ConnectionChanged;
    }
    public IReadOnlyList<WealthsimpleTfsaAccount> Accounts { get; private set; } = [];
    private WealthsimpleTfsaAccount? selected;
    public WealthsimpleTfsaAccount? SelectedAccount
    {
        get => selected;
        set
        {
            if (ReferenceEquals(selected, value) || IsBusy || (value != null && !Accounts.Contains(value))) return;
            selected = value; Snapshot = null; PendingOrders = null; message = value == null ? "Choose a self-directed TFSA." : "Ready to load the selected TFSA.";
            Notify();
            if (value != null) _ = RefreshAsync();
        }
    }
    public WealthsimpleTfsaSnapshot? Snapshot { get; private set; }
    public WealthsimplePendingOrders? PendingOrders { get; private set; }
    public IReadOnlyList<WealthsimplePendingOrder> Orders => PendingOrders?.Orders ?? [];
    public string OrdersStatus => PendingOrders == null ? "Pending orders have not been verified. Refresh before reviewing a transition."
        : $"{Orders.Count} pending orders · read {TradingAccountOverview.Local(PendingOrders.RetrievedAt.UtcDateTime)}. Submitted quantities include any partial fills; they are not remaining quantities.";
    public string ValueText => Snapshot?.Value?.ToString() ?? "—";
    public string CashText => Snapshot?.Cash?.ToString() ?? "—";
    public string BuyingPowerText => Snapshot?.BuyingPower?.ToString() ?? "—";
    public string NetDepositsText => Snapshot?.NetDeposits?.ToString() ?? "—";
    public string ReturnText => Snapshot?.Return?.ToString() ?? "Unavailable";
    private WealthsimpleTfsaSnapshot? rowSnapshot;
    private IReadOnlyList<WealthsimpleHoldingRow> holdingRows = [];
    public IReadOnlyList<WealthsimpleHoldingRow> Holdings
    {
        get
        {
            if (!ReferenceEquals(rowSnapshot, Snapshot))
            {
                rowSnapshot = Snapshot;
                holdingRows = Snapshot?.Holdings.Select(h => new WealthsimpleHoldingRow(h, Snapshot, source.SessionRevision)).ToArray() ?? [];
            }
            return holdingRows;
        }
    }
    public bool IsCurrentSellSelection(global::Core.WSTrade.Orders.ManualSellSelection selection) =>
        !disposed && !IsBusy && source.IsConnected && selection.SessionRevision == source.SessionRevision &&
        ReferenceEquals(selection.Account, SelectedAccount) && Snapshot?.RetrievedAt == selection.SnapshotAt &&
        Holdings.Any(h => h.SellSelection?.SelectionId == selection.SelectionId);
    public bool IsBusy { get; private set; }
    public bool CanRefresh => !IsBusy && source.IsConnected;
    public bool CanSelect => !IsBusy && Accounts.Count > 1;
    public string Status => message + (Snapshot == null ? "" : $" Last retrieved {TradingAccountOverview.Local(Snapshot.RetrievedAt.UtcDateTime)}.") +
        (Snapshot != null && clock.GetUtcNow() - Snapshot.RetrievedAt > TimeSpan.FromMinutes(5) ? " Snapshot is over five minutes old; refresh to update." : "");
    public string ReturnPeriod => Snapshot?.ReturnReferenceDate is { } date ? $"Account return since {date}" : "Account return · Wealthsimple period";
    public string ReturnAsOf => Snapshot?.ReturnAsOf is { } at ? $"Return as of {TradingAccountOverview.Local(at.UtcDateTime)}" : "Return valuation time was not supplied.";
    public string HoldingsStatus => Snapshot == null ? "Holdings have not been loaded." : Snapshot.Holdings.Count == 0 ? "No holdings in this TFSA." : $"{Snapshot.Holdings.Count} holdings · values include their currency.";

    public async Task RefreshAsync()
    {
        if (disposed || IsBusy || !source.IsConnected) return;
        var expected = source.SessionRevision;
        IsBusy = true; attempted = true; PendingOrders = null; rowSnapshot = null; holdingRows = [];
        message = "Reading your TFSA from Wealthsimple…"; Notify();
        try
        {
            if (Accounts.Count == 0)
            {
                var catalog = await source.ReadTfsaAccountsAsync(lifetime.Token);
                if (!IsCurrent(expected)) return;
                if (catalog.Status != WealthsimpleReadStatus.Success || catalog.Data == null) { Failure(catalog.Status, catalog.Stage); return; }
                Accounts = catalog.Data;
                selected = Accounts.Count == 1 ? Accounts[0] : null;
                if (selected == null) { message = Accounts.Count == 0 ? "No open self-directed TFSA was found." : "Choose which self-directed TFSA to load."; return; }
            }
            if (selected == null) { message = "Choose a self-directed TFSA."; return; }
            var result = await source.ReadTfsaSnapshotAsync(selected, lifetime.Token);
            if (!IsCurrent(expected)) return;
            if (result.Status == WealthsimpleReadStatus.Success && result.Data != null)
            {
                Snapshot = result.Data; message = "Loaded from Wealthsimple. Use Refresh to update.";
                var orders = await source.ReadTfsaPendingOrdersAsync(selected, lifetime.Token);
                if (!IsCurrent(expected)) return;
                if (orders.Status == WealthsimpleReadStatus.Success && orders.Data != null) PendingOrders = orders.Data;
                else if (orders.Status is WealthsimpleReadStatus.NotAuthenticated or WealthsimpleReadStatus.Forbidden or WealthsimpleReadStatus.AccountUnavailable)
                    Failure(orders.Status, orders.Stage);
                else message += " Pending orders could not be verified; transition review is blocked.";
            }
            else Failure(result.Status, result.Stage);
        }
        catch (OperationCanceledException) { if (IsCurrent(expected)) message = "Account refresh cancelled."; }
        catch { if (IsCurrent(expected)) message = "Account data could not be loaded. Try Refresh again."; }
        finally
        {
            IsBusy = false; Notify();
            // A replacement login may have completed while an older request was being discarded.
            if (!disposed && !attempted && source.IsConnected) _ = RefreshAsync();
        }
    }
    private bool IsCurrent(long expected) => !disposed && source.HasSession && expected == source.SessionRevision;
    private void Failure(WealthsimpleReadStatus status, string? stage)
    {
        message = status switch
        {
            WealthsimpleReadStatus.NotAuthenticated => "Sign in again to load your TFSA.",
            WealthsimpleReadStatus.Forbidden => "Wealthsimple did not allow this account read.",
            WealthsimpleReadStatus.RateLimited => "Wealthsimple has limited requests. Wait before using Refresh again.",
            WealthsimpleReadStatus.UnsupportedResponse => "The account response was not supported. No partial data was applied." + (stage == null ? "" : $" Step: {stage}."),
            WealthsimpleReadStatus.AccountUnavailable => "The selected account is no longer available. Use Refresh to discover accounts again.",
            _ => "Wealthsimple could not be reached. Use Refresh to try again."
        };
        if (status is WealthsimpleReadStatus.NotAuthenticated or WealthsimpleReadStatus.Forbidden or WealthsimpleReadStatus.AccountUnavailable)
        { Snapshot = null; PendingOrders = null; Accounts = []; selected = null; }
        else if (Snapshot != null) message += " Showing the previous snapshot.";
    }
    private void ConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed) return;
        if (revision != source.SessionRevision || !source.HasSession)
        {
            revision = source.SessionRevision; Accounts = []; selected = null; Snapshot = null; PendingOrders = null; attempted = false;
            message = "Sign in with Wealthsimple to load your self-directed TFSA.";
        }
        Notify();
        if (source.IsConnected && !attempted) _ = RefreshAsync();
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose() { disposed = true; source.PropertyChanged -= ConnectionChanged; lifetime.Cancel(); lifetime.Dispose(); }
}

public sealed class WealthsimpleHoldingRow(WealthsimpleHolding holding, WealthsimpleTfsaSnapshot? snapshot = null, long revision = 0)
{
    public global::Core.WSTrade.Orders.ManualSellSelection? SellSelection { get; } = snapshot == null ? null :
        global::Core.WSTrade.Orders.ManualSellSelection.FromHolding(holding, snapshot, revision);
    public string SellAllToolTip => SellSelection?.IsWholeHolding == true
        ? $"Immediately sell all {Quantity:0.########} {Symbol} shares at market after fresh checks. The execution price may differ from the displayed value."
        : "Market sell all requires a verified whole-share CAD equity holding. Use Wealthsimple for fractional shares.";
    public global::Core.WSTrade.Orders.ManualBuyOrigin? BuyOrigin => global::Core.WSTrade.Orders.ManualBuyOrigin.FromHolding(holding);
    public string Symbol => holding.Symbol;
    public string Name => holding.Name;
    public decimal Quantity => holding.Quantity;
    public string Value => holding.Value?.ToString() ?? "—";
    public string BookValue => holding.BookValue?.ToString() ?? "—";
    public string UnrealizedGain => holding.UnrealizedGain?.ToString() ?? "—";
}
