#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed class ManualBuyTicketViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ManualMarketBuyWorkflow workflow;
    private readonly CancellationTokenSource lifetime = new();
    private PreparedManualBuy? review;
    private WealthsimpleTfsaAccount? account;
    private ManualBuyOrderRow? selectedOrder;
    private string quantity = "";
    private bool disposed;
    public ManualBuyTicketViewModel(ManualMarketBuyWorkflow workflow, ManualBuyOrigin? origin)
    { this.workflow = workflow; Origin = origin; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ManualBuyOrigin? Origin { get; }
    public bool HasBuy => Origin != null;
    public string Title => Origin == null ? "Manual orders" : "Buy " + Origin.Symbol;
    public string SourceText => Origin == null ? "" : Origin.Context + " · direction: " + Origin.Direction;
    public string ManualNotice => Origin == null ? "" : (string.Equals(Origin.Direction, "Buy", StringComparison.OrdinalIgnoreCase) || string.Equals(Origin.Direction, "Manual", StringComparison.OrdinalIgnoreCase))
        ? "Manual purchase · managed by you. No automatic selling is assigned."
        : "Manual purchase · managed by you. This is outside the saved Buy recommendation; no automatic selling is assigned.";
    public IReadOnlyList<WealthsimpleTfsaAccount> Accounts { get; private set; } = [];
    public WealthsimpleTfsaAccount? SelectedAccount
    {
        get => account;
        set { if (account == value || IsBusy || (value != null && !Accounts.Contains(value))) return; account = value; review = null; Status = "Refresh to check the selected TFSA."; Notify(); }
    }
    public string QuantityText { get => quantity; set { if (IsBusy) return; quantity = value; Notify(); } }
    private int? Quantity => int.TryParse(quantity.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null;
    public bool IsBusy { get; private set; }
    public bool IsSubmitting { get; private set; }
    public bool CanEdit => !IsBusy && selectedOrder == null;
    public bool CanEditOrders => !IsBusy;
    public bool CanSelectAccount => CanEdit && Accounts.Count > 1;
    public bool CanRefresh => !IsBusy && HasBuy && workflow.IsConnected && selectedOrder == null;
    public bool CanSubmit => !IsBusy && selectedOrder == null && workflow.ValidateReview(review, Quantity) == null;
    public bool CanCheckOrder => !IsBusy && selectedOrder != null && workflow.IsConnected;
    public bool CanStartNew => !IsBusy && HasBuy && selectedOrder != null && selectedOrder.Record.IsResolved;
    public bool HasSelectedOrder => selectedOrder != null;
    public string Validation => selectedOrder != null ? "This order is saved. Closing this window does not cancel it."
        : IsBusy ? "Checking…" : workflow.ValidateReview(review, Quantity) ?? "Ready · your next Buy click submits a real order.";
    public string SubmitText => Quantity.HasValue ? $"Buy {Quantity.Value:N0} {Origin?.Symbol} at market" : "Enter shares to buy";
    public string InstrumentText => review == null ? "Canadian equity · CAD · market order" : $"{review.Context.Security.Symbol} · {(review.Context.Security.Exchange == "XTSE" ? "TSX" : "TSX Venture")} · CAD · market order";
    public string AskText => review == null ? "—" : Cad(review.Context.Quote.Ask);
    public string EstimateText
    {
        get
        {
            if (review == null || !Quantity.HasValue) return "—";
            try { return Cad(checked(review.Context.Quote.Ask * Quantity.Value)); } catch (OverflowException) { return "—"; }
        }
    }
    public string BuyingPowerText => review?.Context.AvailableCash is { } cash ? Cad(cash) : "—";
    public string HeldQuantityText => review == null ? "—" : review.Context.HeldQuantity?.ToString("0.########", CultureInfo.CurrentCulture) ?? "Unavailable";
    public string QuoteTimeText => review == null ? "Refresh loads a broker quote and account checks."
        : $"Ask quoted {TradingAccountOverview.Local(review.Context.Quote.QuotedAt.UtcDateTime)} · account checked {TradingAccountOverview.Local(review.Context.Snapshot.RetrievedAt.UtcDateTime)}";
    public string Status { get; private set; } = "Loading saved orders…";
    public IReadOnlyList<ManualBuyOrderRow> Orders { get; private set; } = [];
    public ManualBuyOrderRow? SelectedOrder
    {
        get => selectedOrder;
        set { if (IsBusy || selectedOrder == value) return; selectedOrder = value; if (value != null) review = null; Notify(); }
    }
    public string OrderSummary => selectedOrder == null ? "Select a saved order to inspect its broker result."
        : $"{selectedOrder.Symbol} · {selectedOrder.Quantity:N0} shares requested · {selectedOrder.Record.Intent.AccountName}\n{selectedOrder.Record.Intent.Origin.Context}\n{selectedOrder.Status}";
    public string FillSummary
    {
        get
        {
            var state = selectedOrder?.Record.Latest;
            if (state == null) return "Filled shares, actual price and final cost have not yet been confirmed.";
            return $"Filled: {state.FilledQuantity?.ToString("0.########", CultureInfo.CurrentCulture) ?? "unknown"} of {state.SubmittedQuantity:N0}\n" +
                $"Average fill: {state.AverageFillPrice?.ToString() ?? "unavailable"}\n" +
                $"Gross value: {state.FilledValue?.ToString() ?? "unavailable"} · Net value: {state.FilledNetValue?.ToString() ?? "unavailable"}\n" +
                $"Fees: {state.Fees?.ToString() ?? "unavailable"} · Tax withholding: {state.TaxWithholding?.ToString() ?? "unavailable"}\n" +
                $"Observed {TradingAccountOverview.Local(state.RetrievedAt.UtcDateTime)}";
        }
    }
    public async Task InitializeAsync() => await Run(async () =>
    {
        LoadOrders();
        if (Origin == null) { selectedOrder = Orders.FirstOrDefault(); Status = "Saved on this Windows account. Select an order and Check status for a fresh broker result."; return; }
        var unresolved = Orders.FirstOrDefault(o => !o.Record.IsResolved);
        if (unresolved != null) { selectedOrder = unresolved; Status = "An earlier manual order needs a status check before another buy."; return; }
        await PrepareCoreAsync();
    });
    public Task RefreshAsync() => Run(PrepareCoreAsync);
    private async Task PrepareCoreAsync()
    {
        if (Origin == null || selectedOrder != null) return;
        review = null;
        if (!workflow.IsConnected) { Status = "Sign in with Wealthsimple using the connection button, then refresh."; return; }
        // Discover again after recovery/reauth; retain the selected verified account if still present.
        var accounts = await workflow.ReadAccountsAsync(lifetime.Token);
        if (accounts.Status != WealthsimpleReadStatus.Success || accounts.Data == null)
        { Accounts = []; account = null; Status = "The self-directed TFSA accounts could not be verified. Sign in and refresh."; return; }
        Accounts = accounts.Data;
        account = Accounts.Contains(account!) ? account : Accounts.Count == 1 ? Accounts[0] : null;
        if (account == null) { Status = Accounts.Count == 0 ? "No open self-directed TFSA was found." : "Choose the verified self-directed TFSA, then refresh."; return; }
        var result = await workflow.PrepareAsync(account, Origin, lifetime.Token);
        review = result.Prepared; Status = result.Message;
    }
    public void AddShares(int delta)
    {
        if (!CanEdit) return;
        long value = (long)(Quantity ?? 0) + delta;
        QuantityText = System.Math.Clamp(value, 1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
    }
    public async Task SubmitAsync()
    {
        if (!CanSubmit || review == null || !Quantity.HasValue) return;
        var reviewed = review; int shares = Quantity.Value;
        await Run(async () =>
        {
            IsSubmitting = true; Notify();
            try
            {
                var result = await workflow.SubmitAsync(reviewed, shares, lifetime.Token);
                Status = result.Message; review = result.UpdatedReview;
                if (result.Order != null)
                {
                    LoadOrders(result.Order.Intent.RequestId);
                    if (result.Order.Submission == WealthsimpleSubmissionStatus.Acknowledged)
                        await CheckOrderCoreAsync();
                }
            }
            finally { IsSubmitting = false; }
        });
    }
    public Task CheckOrderAsync() => Run(CheckOrderCoreAsync);
    private async Task CheckOrderCoreAsync()
    {
        if (selectedOrder == null) return;
        var id = selectedOrder.Record.Intent.RequestId;
        var result = await workflow.CheckOrderAsync(id, lifetime.Token);
        Status = result.Message; LoadOrders(id);
    }
    public void StartNew()
    {
        if (!CanStartNew) return;
        selectedOrder = null; quantity = ""; review = null;
        Status = "Enter the new order's quantity and refresh its checks."; Notify();
    }
    private void LoadOrders(Guid? selectedId = null)
    {
        selectedId ??= selectedOrder?.Record.Intent.RequestId;
        Orders = workflow.ReadOrders().Select(x => new ManualBuyOrderRow(x)).ToArray();
        selectedOrder = Orders.FirstOrDefault(o => o.Record.Intent.RequestId == selectedId);
    }
    private async Task Run(Func<Task> action)
    {
        if (disposed || IsBusy) return;
        IsBusy = true; Notify();
        try { await action(); }
        catch (OperationCanceledException) { Status = "The check was interrupted. Any saved order remains available for recovery."; review = null; }
        catch
        {
            Status = "Account checks or protected order storage are unavailable. No new submission is allowed; review saved orders before trying again."; review = null;
            try { LoadOrders(); selectedOrder ??= Orders.FirstOrDefault(o => !o.Record.IsResolved); } catch { }
        }
        finally { IsBusy = false; if (!disposed) Notify(); }
    }
    public void RefreshValidation() { if (!disposed) Notify(); }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private static string Cad(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture) + " CAD";
    public void Dispose() { disposed = true; lifetime.Cancel(); lifetime.Dispose(); }
}

public sealed class ManualBuyOrderRow(ManualBuyRecord record)
{
    public ManualBuyRecord Record => record;
    public string Symbol => record.Intent.Symbol;
    public int Quantity => record.Intent.Quantity;
    public string Status => record.Status;
    public string Created => TradingAccountOverview.Local(record.Intent.CreatedAt.UtcDateTime);
    public string Filled => record.Latest?.FilledQuantity?.ToString("0.########", CultureInfo.CurrentCulture) ?? "—";
}
