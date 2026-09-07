#nullable enable

using Core.Db;
using Core.Trader;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF;

public partial class PaperDashboardWindow : Window
{
    private static readonly TimeSpan DashboardRefreshInterval = TimeSpan.FromSeconds(30);
    private readonly PaperTradingMonitor monitor = new();
    private readonly PaperDashboardViewModel viewModel = new();
    private readonly DispatcherTimer timer;
    private readonly SemaphoreSlim cycleGate = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();
    private DateTime nextAutomaticPollLocal;

    public PaperDashboardWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        DelphiTabView.PaperPositionOpened += DelphiTabView_PaperPositionOpened;
        SettingsTabView.PreferencesChanged = enabled => viewModel.AutomaticGhostExitsEnabled = enabled;
        SettingsTabView.InitializePreferences(viewModel.AutomaticGhostExitsEnabled);
        SettingsTabView.ReevaluateAsync = ReevaluateSettingsAsync;
        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = DashboardRefreshInterval
        };
        timer.Tick += Timer_Tick;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
    }

    public void OpenSettings(string section)
    {
        SettingsTabView.Navigate(section); MainTabs.SelectedItem = SettingsTab;
    }
    private void TradingSettings_Click(object sender, RoutedEventArgs e) => OpenSettings(Core.Runtime.EngineStrategySettings.Tracked);

    private async Task<string> ReevaluateSettingsAsync(string family)
    {
        if (family == "Daily")
        {
            using var report = new System.IO.StringWriter();
            var result = await Task.Run(() => new Core.Runtime.DelphiWorkflow().RunAsync(output: report, cancellationToken: shutdown.Token));
            await DelphiTabView.RefreshSettingsResultAsync();
            return result.Succeeded ? "Daily reevaluation completed." : "Assignment active; daily reevaluation needs attention. See Delphi's report and run again to retry.";
        }
        if (family == Core.Runtime.EngineStrategySettings.Live)
        {
            await DelphiLiveTabView.TickAsync(shutdown.Token);
            return "Protection review requested. Entry confirmation needs fresh eligible bars; outside the market session it waits for the next opening.";
        }
        DateTime local = PaperTradingMonitor.ToToronto(DateTime.UtcNow);
        if (!PaperTradingMonitor.IsAutomaticPollTime(local)) return "Market closed; the new rules are active and reevaluation awaits the next market window.";
        if (family == Core.Runtime.EngineStrategySettings.Shadow)
        {
            var result = await PortfoliosTabView.RunScheduledCycleAsync(shutdown.Token);
            return $"Reevaluated Shadow: {result.SignalsCreated} signals; {result.Warnings.Count} evidence warnings. See Portfolios.";
        }
        var cycle = await monitor.PollOnceAsync(viewModel.AutomaticGhostExitsEnabled, shutdown.Token);
        viewModel.ApplyCycle(cycle); await viewModel.RefreshAsync(shutdown.Token);
        return "Trading monitor reevaluated. See Trading for position results and evidence availability.";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DateTime localNow = PaperTradingMonitor.ToToronto(DateTime.UtcNow);
        nextAutomaticPollLocal = PaperTradingMonitor.IsAutomaticPollTime(localNow)
            ? localNow
            : PaperTradingMonitor.NextScheduledPollLocal(localNow);
        viewModel.MonitorStatus = PaperTradingMonitor.IsAutomaticPollTime(localNow)
            ? "Waiting for scheduled poll"
            : "History mode";
        await RefreshSafelyAsync();
        await DelphiLiveTabView.RefreshAsync(shutdown.Token);
        timer.Start();
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        viewModel.RefreshClock();
        await RefreshSafelyAsync();
        // The shared Core workflow owns its own exact schedule and durable
        // lease. Monitoring is independent of which tab the operator selected.
        await DelphiLiveTabView.TickAsync(shutdown.Token);

        DateTime localNow = PaperTradingMonitor.ToToronto(DateTime.UtcNow);
        if (PaperTradingMonitor.IsAutomaticPollTime(localNow) &&
            localNow >= nextAutomaticPollLocal)
        {
            nextAutomaticPollLocal =
                PaperTradingMonitor.NextScheduledPollLocal(localNow.AddSeconds(1));
            await RunMonitorCycleAsync("Scheduled");
        }
    }

    private async void PollNowButton_Click(object sender, RoutedEventArgs e) =>
        await RunMonitorCycleAsync("Manual");

    private async void MarkRealButton_Click(object sender, RoutedEventArgs e)
    {
        PaperPositionRow? selected = viewModel.SelectedPosition;
        if (selected is null)
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"Mark the tracked {selected.Symbol} position as a REAL holding in account '{viewModel.RealAccountLabel}'?\n\n" +
            "Confirm only if its recorded entry fill and share count match the holding at your broker. " +
            "Real positions receive monitoring signals but can never be closed automatically. TraderVI sends no order.",
            "Confirm real-position reconciliation",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        MarkRealButton.IsEnabled = false;
        try
        {
            PositionModeChangeResult result = await viewModel.MarkSelectedAsRealAsync(shutdown.Token);
            viewModel.AddEvent(
                DateTime.UtcNow,
                $"{result.Symbol} reconciled as REAL in {result.AccountLabel}; no broker order was sent.",
                "Reconcile");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Real reconciliation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            MarkRealButton.IsEnabled = viewModel.CanMarkSelectedAsReal;
        }
    }

    private async void RecordRealExitButton_Click(object sender, RoutedEventArgs e)
    {
        PaperPositionRow? selected = viewModel.SelectedPosition;
        if (selected is null)
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"Record the actual broker SELL fill '{viewModel.RealExitFillPrice}' for all {selected.Symbol} shares?\n\n" +
            "This closes only TraderVI's tracked Real position at the manually supplied fill. " +
            "It does not send, modify, or verify a broker order.\n\n" +
            "Continue only if this was one all-shares fill with zero commission. " +
            "Cancel for a partial fill or any fee; those cases require a policy decision before they can be recorded safely.",
            "Confirm real exit fill",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        RecordRealExitButton.IsEnabled = false;
        try
        {
            TrackedRealExitResult result = await viewModel.RecordSelectedRealExitAsync(
                DateTime.Now,
                shutdown.Token);
            viewModel.AddEvent(
                DateTime.UtcNow,
                result.WasAlreadyRecorded
                    ? $"{result.Symbol} REAL exit was already recorded at {result.Price:C3}; no duplicate was created."
                    : $"{result.Symbol} REAL exit recorded at {result.Price:C3}; TraderVI sent no order.",
                "Real fill");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Real exit recording failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RecordRealExitButton.IsEnabled = viewModel.CanRecordSelectedRealExit;
        }
    }

    private async Task RunMonitorCycleAsync(string source)
    {
        if (!await cycleGate.WaitAsync(0))
            return;

        try
        {
            DateTime localNow = PaperTradingMonitor.ToToronto(DateTime.UtcNow);
            if (!PaperTradingMonitor.IsAutomaticPollTime(localNow))
            {
                viewModel.MonitorStatus = "Market closed · poll skipped";
                return;
            }

            PollNowButton.IsEnabled = false;
            viewModel.MonitorStatus = $"{source} poll running…";
            try
            {
                PaperMonitorCycleResult cycle = await monitor.PollOnceAsync(
                    viewModel.AutomaticGhostExitsEnabled,
                    shutdown.Token);
                viewModel.ApplyCycle(cycle);
                await viewModel.RefreshAsync(shutdown.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                viewModel.AddEvent(DateTime.UtcNow, $"Tracked-position monitor: {ex.GetType().Name}: {ex.Message}", "Error");
            }

            try
            {
                SystemShadowPollResult shadowCycle = await PortfoliosTabView.RunScheduledCycleAsync(shutdown.Token);
                viewModel.AddEvent(
                    shadowCycle.CompletedUtc,
                    $"Shadow: {shadowCycle.OrdersFilled} fill(s), {shadowCycle.SignalsCreated} signal(s), {shadowCycle.Warnings.Count} warning(s).",
                    "Shadow");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                viewModel.AddEvent(DateTime.UtcNow, $"Shadow monitor: {ex.GetType().Name}: {ex.Message}", "Error");
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            viewModel.MonitorStatus = "Monitor error · see footer";
            viewModel.AddEvent(DateTime.UtcNow, $"{ex.GetType().Name}: {ex.Message}", "Error");
        }
        finally
        {
            PollNowButton.IsEnabled = viewModel.CanPollNow;
            cycleGate.Release();
        }
    }

    private async Task RefreshSafelyAsync()
    {
        try
        {
            await viewModel.RefreshAsync(shutdown.Token);
            await PortfoliosTabView.RefreshAsync(shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            viewModel.MonitorStatus = "SQL refresh unavailable";
            viewModel.AddEvent(DateTime.UtcNow, $"{ex.GetType().Name}: {ex.Message}", "Error");
        }
    }

    private async void DelphiTabView_PaperPositionOpened(object? sender, PaperTradeEntryResult result)
    {
        viewModel.AddEvent(
            DateTime.UtcNow,
            $"{result.Symbol} {result.ExecutionMode.ToStorageValue()} position opened: {result.Shares} share(s) at {result.FillPrice:C2} from {result.Lens}.",
            "Entry");
        await RefreshSafelyAsync();
        MainTabs.SelectedItem = PaperTradingTab;
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        timer.Stop();
        shutdown.Cancel();
        try { await DelphiLiveTabView.StopAsync(); }
        catch (Exception ex)
        {
            viewModel.AddEvent(DateTime.UtcNow, $"Delphi Live shutdown: {ex.GetType().Name}", "Error");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DelphiTabView.IsRunning || SettingsTabView.IsBusy)
        {
            e.Cancel = true;
            MessageBox.Show(
                "A Delphi run or settings change is still in progress. Keep TraderVI open until it finishes so its evidence and state changes are not interrupted.",
                "Work in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        base.OnClosing(e);
    }
}
