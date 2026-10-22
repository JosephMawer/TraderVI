using Core.WSTrade.Authentication;
#nullable enable
using Core.Db;
using Core.WSTrade.Orders;
using Core.WSTrade.Accounts;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TraderVI.WPF.Viewmodels;
using TraderVI.WPF.Views;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator : IDisposable
{
    private readonly ManualBuyFileJournal journal;
    private readonly ManualMarketBuyWorkflow workflow;
    private readonly WealthsimpleConnection connection;
    private readonly BrokerReconciliationWorkflow reconciliation;
    private readonly SemaphoreSlim orderGate = new(1, 1);
    private readonly BrokerSetupCycleBarrier setupCycles;
    private bool reconciling;
    private ManualBuyWindow? window;
    private ManualSellWindow? sellWindow;
    public ManualSellViewModel Sells { get; }
    public ManualBuyCoordinator(WealthsimpleConnection connection, BrokerSetupCycleBarrier? setupCycles = null)
    {
        this.connection = connection;
        this.setupCycles = setupCycles ?? new();
        journal = new ManualBuyFileJournal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraderVI", "Wealthsimple", "ManualOrders"), new WindowsManualBuyProtector());
        workflow = new(connection, journal, new ManualBuySafetyRepository(), orderGate: orderGate);
        Sells = new(new ManualMarketSellWorkflow(connection, journal, orderGate: orderGate));
        reconciliation = new(connection, journal, journal, orderGate);
        Sells.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
    }
    private bool IsManualBusy => window?.IsSubmitting == true || Sells.IsBusy || reconciling;
    public bool IsSubmitting => IsManualBusy || executionPolling || executionEditing;
    public async Task<BrokerAccountReconciliation> ReconcileForTransitionAsync(WealthsimpleTfsaAccount account, long revision, CancellationToken ct)
    {
        if (IsSubmitting) throw new InvalidOperationException("Finish the current order or reconciliation first.");
        reconciling = true; CommandManager.InvalidateRequerySuggested();
        try { return await reconciliation.RefreshAsync(account, revision, ct); }
        finally { reconciling = false; CommandManager.InvalidateRequerySuggested(); }
    }
    public async Task ReconcileAsync(Window owner, WealthsimpleTfsaAccount account)
    {
        if (IsSubmitting) return;
        reconciling = true; CommandManager.InvalidateRequerySuggested();
        var revision = connection.SessionRevision;
        var dialog = new BrokerReconciliationWindow(owner, account.DisplayName);
        using var cancellation = new CancellationTokenSource();
        bool closed = false, reading = true;
        dialog.Closed += (_, _) => { closed = true; if (reading) cancellation.Cancel(); };
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (connection.SessionRevision != revision || !connection.IsConnected)
            { if (reading) cancellation.Cancel(); dialog.Message("Login changed", "Close this result and reconcile the current account again."); }
        }
        connection.PropertyChanged += Changed;
        dialog.Closed += (_, _) => connection.PropertyChanged -= Changed;
        try
        {
            dialog.Show();
            var result = await reconciliation.RefreshAsync(account, revision, cancellation.Token);
            if (!closed && connection.SessionRevision == revision && connection.IsConnected) dialog.Present(result);
        }
        catch (OperationCanceledException) { if (!closed) dialog.Message("Reconciliation stopped", "No completed result was applied. Reconcile the current account again."); }
        catch (InvalidOperationException error) { if (!closed) dialog.Message("Reconciliation could not complete", error.Message); }
        catch { if (!closed) dialog.Message("Reconciliation could not complete", "The account read or protected history needs attention. No partial result was saved. Check the connection and saved manual-order statuses, then try again."); }
        finally { reading = false; reconciling = false; CommandManager.InvalidateRequerySuggested(); }
    }
    public void Open(Window owner, ManualBuyOrigin? origin)
    {
        if (Sells.IsBusy) { sellWindow?.Activate(); return; }
        if (window != null) { window.Activate(); return; }
        window = new ManualBuyWindow(workflow, origin, connection) { Owner = owner };
        try { window.ShowDialog(); }
        finally { window = null; }
    }
    private void ShowSells(Window owner, Func<Task> refreshAccount)
    {
        if (sellWindow != null) { sellWindow.Activate(); return; }
        sellWindow = new(Sells) { Owner = owner };
        sellWindow.StatusChecked += async (_, _) => await refreshAccount();
        sellWindow.Closed += (_, _) => sellWindow = null;
        sellWindow.Show();
    }
    public async Task SellAllAsync(Window owner, ManualSellSelection selection, Func<Task> refreshAccount)
    {
        if (IsSubmitting) return;
        // Start only from the explicit command; opening/activating the results window never submits.
        var submit = Sells.SellAllAsync(selection);
        ShowSells(owner, refreshAccount);
        await submit;
        await refreshAccount();
    }
    public async Task OpenSellsAsync(Window owner, Func<Task> refreshAccount)
    {
        await Sells.LoadAsync(); ShowSells(owner, refreshAccount);
    }
    public void OpenLimit(Window owner, ManualSellSelection selection)
    {
        new Window { Owner = owner, Title = "Limit sell", Width = 540, Height = 630, MinWidth = 440, MinHeight = 460,
            MaxHeight = SystemParameters.WorkArea.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(26, 32, 41)),
            Content = new LimitSellTicketPanel { DataContext = new LimitSellTicketViewModel(selection) } }.ShowDialog();
    }
    public void Dispose() { executionDisposed = true; Sells.Dispose(); journal.Dispose(); }
}
