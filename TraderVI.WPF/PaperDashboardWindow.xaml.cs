#nullable enable

using Core.Trader;
using System;
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
        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = DashboardRefreshInterval
        };
        timer.Tick += Timer_Tick;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
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
        timer.Start();
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        viewModel.RefreshClock();
        await RefreshSafelyAsync();

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
            PaperMonitorCycleResult cycle = await monitor.PollOnceAsync(
                viewModel.AutomaticGhostExitsEnabled,
                shutdown.Token);
            viewModel.ApplyCycle(cycle);
            await viewModel.RefreshAsync(shutdown.Token);
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

    private void Window_Closed(object? sender, EventArgs e)
    {
        timer.Stop();
        shutdown.Cancel();
    }
}
