#nullable enable
using Core.Db;
using Core.WSTrade.Orders;
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

public sealed class ManualBuyCoordinator : IDisposable
{
    private readonly ManualBuyFileJournal journal;
    private readonly ManualMarketBuyWorkflow workflow;
    private readonly WealthsimpleConnection connection;
    private ManualBuyWindow? window;
    private ManualSellWindow? sellWindow;
    public ManualSellViewModel Sells { get; }
    public ManualBuyCoordinator(WealthsimpleConnection connection)
    {
        this.connection = connection;
        journal = new ManualBuyFileJournal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraderVI", "Wealthsimple", "ManualOrders"), new WindowsManualBuyProtector());
        var orderGate = new SemaphoreSlim(1, 1);
        workflow = new(connection, journal, new ManualBuySafetyRepository(), orderGate: orderGate);
        Sells = new(new ManualMarketSellWorkflow(connection, journal, orderGate: orderGate));
        Sells.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
    }
    public bool IsSubmitting => window?.IsSubmitting == true || Sells.IsBusy;
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
    public void Dispose() { Sells.Dispose(); journal.Dispose(); }
}
