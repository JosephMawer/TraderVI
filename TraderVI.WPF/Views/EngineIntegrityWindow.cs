#nullable enable
using Core.Runtime;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TraderVI.WPF.Views;

/// <summary>Operator recovery controls are explicit and separate from engine start or buying permission.</summary>
public sealed class EngineIntegrityWindow : Window
{
    private readonly EngineRpcClient engine = new(EnginePaths.PipeName);
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
    private readonly TextBox details = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 200 };
    private readonly TextBox reason = new() { MinHeight = 40, TextWrapping = TextWrapping.Wrap };
    private readonly Button refresh = new() { Content = "Refresh", Margin = new(0, 10, 8, 0) };
    private readonly Button verify = new() { Content = "Verify recovery", Margin = new(0, 10, 8, 0) };
    private readonly Button clear = new() { Content = "Clear verified holds", Margin = new(0, 10, 0, 0), IsEnabled = false };
    private EngineIntegrityStatus? last;
    public EngineIntegrityWindow()
    {
        Title = "History checks and recovery"; Width = 760; Height = 610; MinWidth = 600; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(26, 32, 41)); Foreground = Brushes.White;
        var panel = new DockPanel { Margin = new(20) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(summary);
        top.Children.Add(new TextBlock { Text = "Checks resume in bounded batches outside market hours. Incomplete or overdue coverage does not count as a clean pass.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        bottom.Children.Add(new TextBlock { Text = "Recovery reason — describe the cause resolved and the verification performed:", Margin = new(0, 12, 0, 6) });
        bottom.Children.Add(reason);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(refresh); buttons.Children.Add(verify); buttons.Children.Add(clear); bottom.Children.Add(buttons);
        panel.Children.Add(details);
        Loaded += async (_, _) => await RefreshAsync();
        refresh.Click += async (_, _) => await RefreshAsync();
        verify.Click += async (_, _) => await VerifyAsync();
        clear.Click += async (_, _) => await ClearAsync();
    }
    private async Task RefreshAsync()
    {
        try
        {
            last = await engine.CallAsync<EngineIntegrityStatus>(EngineOperation.IntegrityStatus);
            summary.Text = last.Summary(DateTime.UtcNow);
            details.Text = string.Join("\n\n", last.Incidents.Select(i =>
                $"{i.Scope}: {i.FailureCode}\nFound {i.DiscoveredUtc:yyyy-MM-dd HH:mm} UTC. New exposure is held." +
                (i.ProtectionUntrusted ? " Affected automatic protection is unavailable." : " Protection still requires independently verified account and price evidence."))) +
                "\n\n" + string.Join("\n", last.Runs.OrderByDescending(r => r.Plan.BoundaryUtc).Select(r =>
                $"{r.Plan.Kind}: {r.Status}; {r.Progress.VerifiedUnits:N0} records checked; {r.Progress.TargetIndex}/{r.Plan.Targets.Length} groups complete; boundary {r.Plan.BoundaryUtc:yyyy-MM-dd HH:mm} UTC."));
            clear.IsEnabled = !last.Incidents.IsEmpty && Recovery() is not null;
        }
        catch { last = null; clear.IsEnabled = false; summary.Text = "History-check status is unavailable. Reconnect before relying on it."; }
    }
    private EngineAuditRun? Recovery() => last?.Runs.Where(r => r.Plan.Kind == EngineAuditKind.Recovery && r.Status == "Complete")
        .OrderByDescending(r => r.Plan.BoundaryUtc).FirstOrDefault();
    private async Task VerifyAsync()
    {
        if (MessageBox.Show(this, "Recovery verification adds a hold on new exposure and automatic protection while saved history is checked. It runs outside market hours and does not repair data. Continue?",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        verify.IsEnabled = false;
        try { await engine.CallAsync<Guid>(EngineOperation.VerifyRecovery); await RefreshAsync(); }
        catch { summary.Text = "Recovery start has not been confirmed. Refresh its status before trying again."; }
        finally { verify.IsEnabled = true; }
    }
    private async Task ClearAsync()
    {
        var run = Recovery();
        if (run is null || last is null || string.IsNullOrWhiteSpace(reason.Text))
        { summary.Text = "A completed recovery check and your recovery reason are required."; return; }
        var ids = last.Incidents.Select(i => i.IncidentId).ToImmutableArray();
        if (MessageBox.Show(this, $"Clear the {ids.Length} displayed holds using this completed recovery check? Existing buying permission, expiry and other risk holds still apply.",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        clear.IsEnabled = false;
        try { await engine.CallAsync<bool>(EngineOperation.ClearIntegrityHolds, new EngineClearIntegrityHolds(run.RunId, ids, reason.Text)); await RefreshAsync(); }
        catch { summary.Text = "Holds could not be confirmed cleared. Refresh status; changed history or a newer incident requires another recovery check."; }
    }
}
