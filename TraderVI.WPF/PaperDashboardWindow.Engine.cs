#nullable enable
using Core.Runtime;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace TraderVI.WPF;

public partial class PaperDashboardWindow
{
    private readonly EngineRpcClient engineControl = new(EnginePaths.PipeName);
    private bool engineCommand;
    private async Task RefreshEngineStatusAsync()
    {
        if (engineCommand || shutdown.IsCancellationRequested) return;
        try
        {
            var status = await engineControl.CallAsync<IndependentEngineStatus>(EngineOperation.Status, ct: shutdown.Token);
            EngineStatusText.Text = $"Engine: {status.State}. Closing this dashboard leaves it running.";
            StartEngineButton.IsEnabled = status.State is "Stopped" or "StopFailed";
            StopEngineButton.IsEnabled = status.State is not ("Stopped" or "Stopping");
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch
        {
            EngineStatusText.Text = "Engine status unavailable. Reconnect before assuming trading or protection is running.";
            StartEngineButton.IsEnabled = true; StopEngineButton.IsEnabled = true;
        }
    }
    private async void StartEngine_Click(object sender, RoutedEventArgs e)
    {
        if (engineCommand) return;
        engineCommand = true; StartEngineButton.IsEnabled = StopEngineButton.IsEnabled = false;
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
        EngineStatusText.Text = "Stopping engine after admitted work finishes. Automatic protection will stop too.";
        try
        {
            var status = await engineControl.CallAsync<IndependentEngineStatus>(EngineOperation.Stop, ct: shutdown.Token);
            EngineStatusText.Text = status.State == "Stopped"
                ? "Engine stopped. Automatic protection is off; original broker orders and history remain."
                : $"Engine: {status.State}. Stop has not been confirmed.";
        }
        catch { EngineStatusText.Text = "Engine stop has not been confirmed. Reconnect and check status; do not assume protection is running."; }
        finally { engineCommand = false; StartEngineButton.IsEnabled = StopEngineButton.IsEnabled = true; }
    }
}
