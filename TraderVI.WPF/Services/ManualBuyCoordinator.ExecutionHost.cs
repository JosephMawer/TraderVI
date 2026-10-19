#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TraderVI.WPF.Services;

public static class BrokerExecutionHostCommands
{
    public static RoutedUICommand Open { get; } = new("Execution setup", "BrokerExecutionHost", typeof(BrokerExecutionHostCommands));
    public static RoutedUICommand Stop { get; } = new("Stop execution host", "StopBrokerExecutionHost", typeof(BrokerExecutionHostCommands));
}

public sealed partial class ManualBuyCoordinator
{
    private BrokerExecutionHost? executionHost;
    private ReviewedTsxSessionCalendar? executionCalendar;
    private Window? executionWindow;
    private bool executionPolling, executionEditing, executionDisposed;
    private readonly BrokerManagedSessionControls executionSessions = new(new BrokerManagedSessionRepository());
    private static string HostGrantPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TraderVI", "Wealthsimple", "ManualOrders", "execution-host.dat");
    private BrokerExecutionHostGrantFileStore HostGrants() => new(HostGrantPath, new WindowsManualBuyProtector());
    public event EventHandler? ExecutionHostChanged;
    public BrokerExecutionHostStatus ExecutionStatus { get; private set; } = new("Off", "Automatic trading off. Review execution setup before enabling a host.", default);

    private BrokerExecutionHost GetExecutionHost()
    {
        if (executionHost is not null) return executionHost;
        var calendarPath = Environment.GetEnvironmentVariable(DelphiLiveDesktopService.CalendarPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(calendarPath))
            throw new InvalidOperationException("A reviewed TSX session calendar must be configured before enabling the execution host.");
        executionCalendar = ReviewedTsxSessionCalendar.Load(calendarPath);
        executionHost = new(HostGrants(), executionSessions, TickReviewedExecutionAsync, executionCalendar,
            BrokerManagedSessionComposition.ImplementationIdentity);
        executionHost.StatusChanged += (_, _) => PresentExecution(executionHost.Status);
        return executionHost;
    }
    private void PresentExecution(BrokerExecutionHostStatus status)
    { ExecutionStatus = status; ExecutionHostChanged?.Invoke(this, EventArgs.Empty); }

    public async Task StopExecutionHostAsync(CancellationToken ct = default)
    {
        if (IsSubmitting) return;
        executionEditing = true; CommandManager.InvalidateRequerySuggested();
        try { await StopExecutionCoreAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { PresentExecution(new("Blocked", "The host stop could not be confirmed. Reopen execution setup to recover its saved permission.", DateTimeOffset.UtcNow)); }
        finally { executionEditing = false; CommandManager.InvalidateRequerySuggested(); }
    }
    private async Task StopExecutionCoreAsync(CancellationToken ct)
    {
        if (executionHost is not null) await executionHost.DisableAsync(ct);
        else
        {
            var store = HostGrants(); var old = store.Read();
            if (old?.Enabled == true) store.Write(old, old with { Enabled = false });
            if (store.Read()?.Enabled == true) throw new InvalidOperationException("The host stop needs saved-permission recovery.");
            PresentExecution(new("Off", "Execution host stopped. Saved assignment and original orders remain preserved.", DateTimeOffset.UtcNow));
        }
    }

    public async Task PollExecutionHostAsync(CancellationToken ct = default)
    {
        if (executionDisposed || executionPolling || executionEditing) return;
        bool available = connection.IsConnected && !IsManualBusy;
        executionPolling = true; CommandManager.InvalidateRequerySuggested();
        try
        {
            // Default-off startup reads no SQL, broker or journal and creates no permission file.
            if (executionHost is null && HostGrants().Read()?.Enabled != true)
            { PresentExecution(new("Off", "Automatic trading off. No execution-host permission is enabled.", DateTimeOffset.UtcNow)); return; }
            await GetExecutionHost().PollAsync(available, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch
        { PresentExecution(new("Blocked", "Execution setup could not be recovered. Review its protected permission and calendar; no new cycle was started.", DateTimeOffset.UtcNow)); }
        finally { executionPolling = false; CommandManager.InvalidateRequerySuggested(); }
    }

    private async Task<BrokerManagedSession> TickReviewedExecutionAsync(BrokerManagedSession saved, CancellationToken ct)
    {
        long revision = connection.SessionRevision;
        var accounts = await connection.ReadTfsaAccountsAsync(ct);
        if (!connection.IsConnected || revision != connection.SessionRevision ||
            accounts.Status != WealthsimpleReadStatus.Success || accounts.Data is null)
            throw new InvalidOperationException("The current owned TFSA could not be verified.");
        var account = BrokerManagedSessionComposition.MatchAccount(saved, accounts.Data);
        IBrokerRiskEvidenceSource risk = saved.Approval.Review.NoTransferScope is { } scope
            ? new BrokerNoTransferRiskEvidenceSource(scope, executionCalendar!)
            : new UnqualifiedBrokerRiskEvidenceSource();
        var workflow = BrokerManagedSessionComposition.Create(connection, connection, connection, journal, journal,
            orderGate, account, executionCalendar!, risk);
        var result = await workflow.TickAsync(saved.BindingId, ct);
        BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public async Task OpenExecutionSetupAsync(Window owner, WealthsimpleTfsaAccount account, Func<bool> currentAccount)
    {
        if (executionWindow is not null) { executionWindow.Activate(); return; }
        if (IsSubmitting || !currentAccount()) return;
        var dialog = executionWindow = new Window { Owner = owner, Title = "Delphi Live Normal execution setup",
            Width = 800, Height = 650, MinWidth = 620, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "Delphi Live Normal", FontSize = 26 });
        body.Children.Add(new TextBlock { Text = "Review the installed host and approved assignment. Enabling the host is separate from strategy approval and account risk setup.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) });
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) }; body.Children.Add(summary);
        var refresh = new Button { Content = "Refresh readiness", Padding = new Thickness(12), Margin = new Thickness(0,0,0,8) };
        var enable = new Button { Content = "Enable this reviewed execution host", IsEnabled = false, Padding = new Thickness(12), Margin = new Thickness(0,0,0,8) };
        var stop = new Button { Content = "Stop execution host", Padding = new Thickness(12) };
        body.Children.Add(refresh); body.Children.Add(enable); body.Children.Add(stop);
        body.Children.Add(new TextBlock { Text = "Stop new buys remains a separate saved strategy control. Stop execution host stops all new strategy cycles after a running cycle finishes; original orders still require recovery.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,14,0,0) });
        dialog.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        using var lifetime = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed = false, busy = false; long revision = connection.SessionRevision;
        BrokerManagedSession? reviewed = null;
        void Check()
        {
            if (closed || !currentAccount() || !connection.IsConnected || connection.SessionRevision != revision)
                throw new InvalidOperationException("The account or sign-in changed. Reopen setup using the current account.");
        }
        async Task Run(Func<Task> action)
        {
            if (closed || busy || executionPolling || IsManualBusy) return;
            busy = executionEditing = true; refresh.IsEnabled = enable.IsEnabled = stop.IsEnabled = false;
            CommandManager.InvalidateRequerySuggested();
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException error) { if (!closed) summary.Text = error.Message; reviewed = null; }
            catch { if (!closed) summary.Text = "Execution readiness could not be verified. Saved permissions are unchanged; refresh to recover."; reviewed = null; }
            finally
            {
                busy = executionEditing = false;
                if (!closed) { refresh.IsEnabled = stop.IsEnabled = true; enable.IsEnabled = reviewed is not null; }
                CommandManager.InvalidateRequerySuggested();
            }
        }
        async Task Refresh()
        {
            Check(); reviewed = null;
            var storage = await BrokerManagedSessionRepository.ReadStorageAsync(lifetime.Token);
            var imports = await new BrokerAccountImportRepository().ReadAsync(lifetime.Token); Check();
            var lines = $"Storage: baseline {(storage.BaselineInstalled ? "installed" : "missing")}, continuation {(storage.ContinuationInstalled ? "installed" : "missing")}, session {(storage.SessionInstalled ? "installed" : "missing")}.\n";
            if (imports.Binding is null)
            { summary.Text = lines + "Link and reconcile the permanent TFSA through the existing account-history controls first."; return; }
            var saved = await executionSessions.ReadAsync(imports.Binding.BindingId, lifetime.Token); Check();
            if (saved is null)
            {
                var baselines = await new BrokerAccountRiskBaselineRepository().ReadAsync(lifetime.Token); Check();
                summary.Text = lines + "No strategy is approved to manage this account. Automatic trading remains off.\n\n" +
                    (baselines.RiskHistory.ExistingBaseline is null ? "Initial risk references have not been reviewed/saved.\n" : "Initial risk references exist; continuation still needs current qualification.\n") +
                    "First-session scope: both reviewed holdings must sell completely and usable cash must reconcile. Use the existing Sell all and Account risk setup controls; this window submits no opening sales.\n\n" +
                    "Before activation, complete the verified cash/risk references, broker order, strategy-input, restart-recovery and storage checks. The strategy execution review is still required. Return here after that review to enable the exact approved assignment.";
                return;
            }
            // Match using broker identity, not a display label or another signed-in account.
            _ = BrokerManagedSessionComposition.MatchAccount(saved, new[] { account });
            if (!storage.BaselineInstalled || !storage.ContinuationInstalled || !storage.SessionInstalled || saved.ReviewInvalidated ||
                saved.Strategy.Strategy.ImplementationIdentity != BrokerManagedSessionComposition.ImplementationIdentity ||
                saved.Strategy.Strategy.Family != TradingStrategyFamily.DelphiLive || saved.Strategy.GateProfile != Core.Trader.PaperGateProfile.NormalV1)
            { summary.Text = lines + "The saved assignment needs a fresh exact Normal/build/storage review before host activation."; return; }
            _ = GetExecutionHost(); reviewed = saved;
            summary.Text = lines + $"Approved strategy: {saved.Strategy.Strategy.Name}\nSaved mode: {(saved.NewBuysStopped ? "new buys stopped" : "buying subject to current risk checks")}\n" +
                $"Assignment state: {saved.Phase}\n\n{ExecutionStatus.Message}\n\n" +
                "Enable only this reviewed assignment. Every subsequent cycle rechecks account, original orders, risk permission, source and protection. Missing native cash coverage stops new buys.";
        }
        refresh.Click += async (_, _) => await Run(Refresh);
        enable.Click += async (_, _) => await Run(async () =>
        {
            Check(); var selected = reviewed ?? throw new InvalidOperationException("Refresh the current approved assignment first.");
            await GetExecutionHost().EnableAsync(selected.BindingId, selected.SessionId, selected.Strategy.Fingerprint,
                Environment.UserName, lifetime.Token); Check(); summary.Text = ExecutionStatus.Message;
        });
        stop.Click += async (_, _) => await Run(async () =>
        {
            await StopExecutionCoreAsync(lifetime.Token);
            summary.Text = ExecutionStatus.Message;
        });
        dialog.Closing += (_, e) => { if (busy) e.Cancel = true; };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); executionWindow = null; completion.TrySetResult(); };
        dialog.Show(); await Run(Refresh); await completion.Task;
    }
}
