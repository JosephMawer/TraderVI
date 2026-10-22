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
    private readonly ComboBox scope = new() { ItemsSource = new[] { "Real account and required inputs", "Paper and research history", "All history" }, SelectedIndex = 0, Margin = new(0, 0, 0, 10) };
    private EngineIntegrityStatus? last;
    public EngineIntegrityWindow()
    {
        Title = "History checks and recovery"; Width = 760; Height = 610; MinWidth = 600; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(26, 32, 41)); Foreground = Brushes.White;
        var panel = new DockPanel { Margin = new(20) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(summary);
        top.Children.Add(new TextBlock { Text = "Real-account recovery runs separately from paper history. Ordinary archive checks run outside market hours; explicit recovery can run now. Incomplete coverage is never a pass, and recovery does not enable trading.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        top.Children.Add(scope);
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
        scope.SelectionChanged += async (_, _) => await RefreshAsync();
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
                $"{r.Plan.Kind} / {r.Plan.Scope}: {r.Status}; {r.Progress.VerifiedUnits:N0} records checked; {r.Progress.TargetIndex}/{r.Plan.Targets.Length} groups complete; boundary {r.Plan.BoundaryUtc:yyyy-MM-dd HH:mm} UTC."));
            clear.IsEnabled = !last.Incidents.IsEmpty && Recovery() is not null;
        }
        catch { last = null; clear.IsEnabled = false; summary.Text = "History-check status is unavailable. Reconnect before relying on it."; }
    }
    private EngineRecoveryScope SelectedScope => scope.SelectedIndex switch { 0 => EngineRecoveryScope.Real, 1 => EngineRecoveryScope.Paper, _ => EngineRecoveryScope.All };
    private EngineAuditRun? Recovery() => last?.Runs.Where(r => r.Plan.Kind == EngineAuditKind.Recovery && r.Status == "Complete" &&
        (r.Plan.Scope == SelectedScope || SelectedScope == EngineRecoveryScope.Paper && r.Plan.Scope == EngineRecoveryScope.All))
        .OrderByDescending(r => r.Plan.BoundaryUtc).FirstOrDefault();
    private async Task VerifyAsync()
    {
        if (MessageBox.Show(this, "Verify the selected recovery scope now? It holds affected activity while checking saved records. Other domains keep their own restrictions. This does not repair data or enable trading.",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        verify.IsEnabled = false;
        try { await engine.CallAsync<Guid>(SelectedScope switch { EngineRecoveryScope.Real => EngineOperation.VerifyRealRecovery,
            EngineRecoveryScope.Paper => EngineOperation.VerifyPaperRecovery, _ => EngineOperation.VerifyRecovery }); await RefreshAsync(); }
        catch { summary.Text = "Recovery start has not been confirmed. Refresh its status before trying again."; }
        finally { verify.IsEnabled = true; }
    }
    private async Task ClearAsync()
    {
        var run = Recovery();
        if (run is null || last is null || string.IsNullOrWhiteSpace(reason.Text))
        { summary.Text = "A completed recovery check and your recovery reason are required."; return; }
        var selectedScope = SelectedScope;
        var ids = last.Incidents.Where(i => selectedScope switch {
            EngineRecoveryScope.Real => i.Scope is EngineIntegrityScope.Global or EngineIntegrityScope.Broker,
            EngineRecoveryScope.Paper => i.Scope is EngineIntegrityScope.Paper or EngineIntegrityScope.Research,
            _ => true }).Select(i => i.IncidentId).ToImmutableArray();
        if (ids.IsEmpty) { summary.Text = "No holds apply to the selected recovery scope."; return; }
        if (MessageBox.Show(this, $"Resolve {ids.Length} findings for the selected scope using this completed check? Real-only recovery preserves paper restrictions. Unknown shared findings remain blocked. Existing buying permission, expiry and other risk holds still apply.",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        clear.IsEnabled = false;
        try { await engine.CallAsync<bool>(EngineOperation.ClearIntegrityHolds, new EngineClearIntegrityHolds(run.RunId, ids, reason.Text) { Scope = selectedScope }); await RefreshAsync(); }
        catch { summary.Text = "Holds could not be confirmed cleared. Refresh status; changed history or a newer incident requires another recovery check."; }
    }
}
