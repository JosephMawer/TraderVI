#nullable enable
using Core.Runtime;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF;

public partial class PaperDashboardWindow
{
    private readonly EngineRpcClient engineControl = new(EnginePaths.PipeName);
    private bool engineCommand;
    private IndependentEngineStatus? latestEngineStatus;
    private readonly DispatcherTimer engineDisplayTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool readingLocalStatus;
    private DateTimeOffset nextPrepCheck;
    private async Task RefreshLocalStatusAsync()
    {
        if (readingLocalStatus || shutdown.IsCancellationRequested) return;
        readingLocalStatus = true;
        try
        {
            var pathFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TraderVI", "Collector", "dashboard-status-path.txt");
            var display = await Task.Run(() => DashboardCollectorOverview.Read(pathFile, TimeProvider.System));
            if (shutdown.IsCancellationRequested) return;
            CollectorOverviewText.Text = display.State;
            CollectorOverviewText.Foreground = (Brush)new BrushConverter().ConvertFromString(display.Color)!;
            CollectorOverviewText.ToolTip = display.Detail;
            if (DateTimeOffset.UtcNow >= nextPrepCheck)
            {
                var prep = await Task.Run(DashboardDailyPrep.Read);
                if (shutdown.IsCancellationRequested) return;
                DailyPrepText.Text = prep.Label;
                DailyPrepText.Foreground = (Brush)new BrushConverter().ConvertFromString(prep.Color)!;
                DailyPrepText.ToolTip = prep.Detail;
                nextPrepCheck = DateTimeOffset.UtcNow.AddSeconds(30);
            }
        }
        finally { readingLocalStatus = false; }
    }
    private void OpenEngine_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = EngineTab;
    private async void ReviewEngineSwitch_Click(object sender, RoutedEventArgs e) => await TradingRecommendationsTabView.ReviewBrokerStrategyAsync(null);
    private void RenderEngineOverview()
    {
        if (engineCommand) return;
        var display = DashboardEngineOverview.From(latestEngineStatus, DateTimeOffset.UtcNow);
        EngineOverviewText.Text = display.State;
        EngineOverviewText.Foreground = (Brush)new BrushConverter().ConvertFromString(display.StateColor)!;
        EngineOverviewText.ToolTip = display.Detail + "\n" + display.Checked;
        EngineStatusText.Text = display.Detail + "\n" + display.Checked;
        HistoryOverviewText.Text = display.History;
        HistoryOverviewText.ToolTip = display.HistoryDetail;
        HistoryOverviewText.Foreground = (Brush)new BrushConverter().ConvertFromString(display.HistoryColor)!;
        HistoryDetailText.Text = display.HistoryDetail;
    }
    private void HistoryChecks_Click(object sender, RoutedEventArgs e) => new Views.EngineIntegrityWindow { Owner = this }.Show();
    private async Task RefreshEngineStatusAsync()
    {
        if (engineCommand || shutdown.IsCancellationRequested) return;
        try
        {
            var status = await engineControl.CallAsync<IndependentEngineStatus>(EngineOperation.Status, ct: shutdown.Token);
            latestEngineStatus = status;
            RenderEngineOverview();
            StartEngineButton.IsEnabled = status.State is "Stopped" or "StopFailed";
            StopEngineButton.IsEnabled = status.State is not ("Stopped" or "Stopping");
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch
        {
            latestEngineStatus = null;
            RenderEngineOverview();
            StartEngineButton.IsEnabled = true; StopEngineButton.IsEnabled = true;
        }
    }
    private async void StartEngine_Click(object sender, RoutedEventArgs e)
    {
        if (engineCommand) return;
        engineCommand = true; StartEngineButton.IsEnabled = StopEngineButton.IsEnabled = false;
        EngineOverviewText.Text = "Starting…";
        EngineOverviewText.Foreground = (Brush)FindResource("WarningBrush");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "TraderVI.Engine.exe");
            if (!File.Exists(path)) throw new FileNotFoundException();
            using var process = Process.Start(new ProcessStartInfo(path, "--run")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory });
            if (process is null) throw new InvalidOperationException();
            EngineStatusText.Text = "Starting engine. Trading still requires its exact reviewed assignment and permission.";
            await wealthsimple.StartAsync(shutdown.Token);
        }
        catch { EngineStatusText.Text = "Engine start could not be confirmed. Check the installed build and reviewed calendar."; }
        finally { engineCommand = false; await RefreshEngineStatusAsync(); }
    }
    private async void StopEngine_Click(object sender, RoutedEventArgs e)
    {
        if (engineCommand) return;
        engineCommand = true; StartEngineButton.IsEnabled = StopEngineButton.IsEnabled = false;
        EngineOverviewText.Text = "Stopping…";
        EngineOverviewText.Foreground = (Brush)FindResource("WarningBrush");
        EngineStatusText.Text = "Stopping engine after admitted work finishes. Automatic protection will stop too.";
        try
        {
            var status = await engineControl.CallAsync<IndependentEngineStatus>(EngineOperation.Stop, ct: shutdown.Token);
            latestEngineStatus = status;
            EngineOverviewText.Text = status.State;
            EngineOverviewText.Foreground = (Brush)new BrushConverter().ConvertFromString(DashboardEngineOverview.From(status, DateTimeOffset.UtcNow).StateColor)!;
            EngineStatusText.Text = status.State == "Stopped"
                ? "Engine stopped. Automatic protection is off; original broker orders and history remain."
                : $"Engine: {status.State}. Stop has not been confirmed.";
        }
        catch { latestEngineStatus = null; EngineOverviewText.Text = "Stop unconfirmed"; EngineOverviewText.Foreground = Brushes.LightCoral; EngineStatusText.Text = "Engine stop has not been confirmed. Reconnect and check status; do not assume protection is running."; }
        finally { engineCommand = false; StartEngineButton.IsEnabled = StopEngineButton.IsEnabled = true; }
    }
}
