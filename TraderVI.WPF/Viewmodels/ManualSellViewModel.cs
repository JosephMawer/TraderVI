#nullable enable
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed class ManualSellViewModel(IManualSellWorkflow workflow) : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private ManualSellOrderRow? selected;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsBusy { get; private set; }
    public bool CanSelect => !IsBusy;
    public bool CanCheck => !IsBusy && selected != null && workflow.IsConnected;
    public IReadOnlyList<ManualSellOrderRow> Orders { get; private set; } = [];
    public ManualSellOrderRow? SelectedOrder
    {
        get => selected;
        set { if (IsBusy || selected == value) return; selected = value; Notify(); }
    }
    public string Status { get; private set; } = "Saved manual sells. Checking status never sends another order.";
    public string Summary => selected == null ? "No saved sell selected." :
        $"{selected.Record.Intent.Symbol} · {selected.Record.Intent.Quantity:N0} shares requested\n{selected.Record.Intent.AccountName}\n{selected.Record.Status}";
    public string FillSummary => selected?.Record.Latest is not { } state
        ? "Actual filled shares, execution price and proceeds have not been confirmed."
        : $"Filled: {state.FilledQuantity?.ToString("0.########", CultureInfo.CurrentCulture) ?? "unknown"} of {state.SubmittedQuantity:N0}\n" +
          $"Average execution price: {state.AverageFillPrice?.ToString() ?? "unavailable"}\n" +
          $"Gross proceeds: {state.FilledValue?.ToString() ?? "unavailable"}\nNet proceeds: {state.FilledNetValue?.ToString() ?? "unavailable"}\n" +
          $"Fees: {state.Fees?.ToString() ?? "unavailable"} · Withholding: {state.TaxWithholding?.ToString() ?? "unavailable"}\n" +
          $"Observed {TradingAccountOverview.Local(state.RetrievedAt.UtcDateTime)}";

    public Task LoadAsync() => Run(() => LoadOrdersAsync());
    // Called by the explicit grid command, never by Loaded, a timer, refresh or recovery.
    public Task SellAllAsync(ManualSellSelection selection) => Run(async () =>
    {
        Status = $"Checking {selection.Symbol} before selling all {selection.Quantity:0.########} shares…"; Notify();
        var result = await workflow.SellAllAsync(selection, lifetime.Token);
        Status = result.Message; await LoadOrdersAsync(result.Order?.Intent.RequestId);
        if (result.Order?.Submission == WealthsimpleSubmissionStatus.Acknowledged) await CheckCoreAsync();
    });
    public Task CheckAsync() => Run(CheckCoreAsync);
    private async Task CheckCoreAsync()
    {
        if (selected == null) return;
        var id = selected.Record.Intent.RequestId;
        var result = await workflow.CheckOrderAsync(id, lifetime.Token);
        Status = result.Message; await LoadOrdersAsync(id);
    }
    private async Task LoadOrdersAsync(Guid? id = null)
    {
        id ??= selected?.Record.Intent.RequestId;
        Orders = (await workflow.ReadOrdersAsync(lifetime.Token)).Select(o => new ManualSellOrderRow(o)).ToArray();
        selected = Orders.FirstOrDefault(o => o.Record.Intent.RequestId == id) ?? Orders.FirstOrDefault();
    }
    private async Task Run(Func<Task> action)
    {
        if (disposed || IsBusy) return;
        IsBusy = true; Notify();
        try { await action(); }
        catch (OperationCanceledException) { Status = "The check was interrupted. Any saved sell remains available; check its status before another order."; }
        catch
        {
            Status = "Account checks or protected order storage are unavailable. Check saved sells before trying another order.";
            try { await LoadOrdersAsync(); } catch { }
        }
        finally { IsBusy = false; Notify(); }
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose() { disposed = true; lifetime.Cancel(); lifetime.Dispose(); }
}

public sealed class ManualSellOrderRow(ManualSellRecord record)
{
    public ManualSellRecord Record => record;
    public string Symbol => record.Intent.Symbol;
    public int Quantity => record.Intent.Quantity;
    public string Status => record.Status;
    public string Created => TradingAccountOverview.Local(record.Intent.CreatedAt.UtcDateTime);
}

/// <summary>A visible, non-submitting limit ticket until a captured limit contract is verified.</summary>
public sealed class LimitSellTicketViewModel(ManualSellSelection selection) : INotifyPropertyChanged
{
    private string quantity = selection.IsWholeHolding ? selection.Quantity.ToString("0", CultureInfo.CurrentCulture) : "";
    private string limit = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Symbol => selection.Symbol;
    public string Account => selection.Account.DisplayName;
    public string Held => selection.Quantity.ToString("0.########", CultureInfo.CurrentCulture);
    public string QuantityText { get => quantity; set { quantity = value; Notify(); } }
    public string LimitPriceText { get => limit; set { limit = value; Notify(); } }
    public bool CanSubmit => false;
    public string Estimate
    {
        get
        {
            if (!int.TryParse(quantity, NumberStyles.None, CultureInfo.CurrentCulture, out int shares) || shares < 1 || shares > selection.Quantity ||
                !decimal.TryParse(limit, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal price) || price <= 0)
                return "Enter whole shares within the holding and a positive limit price.";
            try { return (checked(shares * price)).ToString("N2", CultureInfo.CurrentCulture) + " CAD before fees, if all shares fill."; }
            catch (OverflowException) { return "The entered amount is too large."; }
        }
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
