#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TraderVI.WPF.Views;
using TraderVI.WPF.Viewmodels;

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
    private static string ExecutionQualificationPath => Path.Combine(Path.GetDirectoryName(HostGrantPath)!, "execution-qualification.dat");
    private BrokerExecutionQualificationBundle? ReadExecutionQualification(DateTimeOffset now) =>
        new BrokerExecutionQualificationFileStore(ExecutionQualificationPath,
            new WindowsManualBuyProtector()).Read(now);
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
            if (HostGrants().Read()?.Enabled != true)
            { PresentExecution(new("Off", "Automatic trading off. No execution-host permission is enabled.", DateTimeOffset.UtcNow)); return; }
            using var quiet = setupCycles.BeginSetup();
            await quiet.WaitForIdleAsync(ct);
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
        var panel = new BrokerExecutionSetupPanel(); dialog.Content = panel;
        var summary = panel.Summary; var refresh = panel.Refresh; var first = panel.PrepareFirst;
        var checkpoint = panel.CaptureNativeCheckpoint;
        var approve = panel.ApproveFirst; var enable = panel.EnableHost; var stop = panel.StopHost;
        using var lifetime = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed = false, busy = false; long revision = connection.SessionRevision;
        BrokerManagedSession? reviewed = null;
        BrokerFirstExecutionReview? firstReview = null;
        BrokerManagedSessionWorkflow? firstWorkflow = null;
        string? qualificationFingerprint = null;
        string stage = "checking execution setup";
        void Stage(string value)
        {
            stage = value;
            if (!closed) summary.Text = $"Checking: {value}…";
        }
        void Check()
        {
            if (closed || !currentAccount() || !connection.IsConnected || connection.SessionRevision != revision)
                throw new InvalidOperationException("The account or sign-in changed. Reopen setup using the current account.");
        }
        async Task Run(Func<Task> action, bool waitForPaperCycles = true)
        {
            if (closed || busy || executionPolling || IsManualBusy) return;
            busy = executionEditing = true; refresh.IsEnabled = checkpoint.IsEnabled = first.IsEnabled = approve.IsEnabled = enable.IsEnabled = stop.IsEnabled = false;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                using var quiet = waitForPaperCycles ? setupCycles.BeginSetup() : null;
                if (quiet is not null)
                {
                    Stage("waiting for current desktop paper cycles to finish before account checks");
                    await quiet.WaitForIdleAsync(lifetime.Token);
                }
                await action();
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException error) { if (!closed) summary.Text = error.Message; reviewed = null; firstReview = null; firstWorkflow = null; }
            catch (Exception error)
            {
                if (!closed) summary.Text = $"Execution readiness failed while {stage}.\nDiagnostic: {BrokerRiskSetupDiagnostic.Describe(error)}.\n" +
                    "No completed checkpoint or approval is confirmed by this result. Preserve original orders and saved references; report this diagnostic before retrying.";
                reviewed = null; firstReview = null; firstWorkflow = null;
            }
            finally
            {
                busy = executionEditing = false;
                if (!closed) { refresh.IsEnabled = checkpoint.IsEnabled = first.IsEnabled = stop.IsEnabled = true; enable.IsEnabled = reviewed is not null; approve.IsEnabled = firstReview?.CanApprove == true && firstWorkflow is not null; }
                CommandManager.InvalidateRequerySuggested();
            }
        }
        async Task Refresh()
        {
            Check(); reviewed = null; firstReview = null; firstWorkflow = null; qualificationFingerprint = null;
            Stage("reading installed storage readiness");
            var storage = await BrokerManagedSessionRepository.ReadStorageAsync(lifetime.Token);
            Stage("reading the saved account-history link");
            var imports = await new BrokerAccountImportRepository().ReadAsync(lifetime.Token); Check();
            var lines = $"Storage: baseline {(storage.BaselineInstalled ? "installed" : "missing")}, continuation {(storage.ContinuationInstalled ? "installed" : "missing")}, session {(storage.SessionInstalled ? "installed" : "missing")}.\n";
            if (imports.Binding is null)
            { summary.Text = lines + "Link and reconcile the permanent TFSA through the existing account-history controls first."; return; }
            Stage("recovering any saved strategy assignment");
            var saved = await executionSessions.ReadAsync(imports.Binding.BindingId, lifetime.Token); Check();
            if (saved is null)
            {
                Stage("reading saved initial risk references");
                var baselines = await new BrokerAccountRiskBaselineRepository().ReadAsync(lifetime.Token); Check();
                summary.Text = lines + "No strategy is approved to manage this account. Automatic trading remains off.\n\n" +
                    (baselines.RiskHistory.ExistingBaseline is null ? "Initial risk references have not been reviewed/saved.\n" : "Initial risk references exist; continuation still needs current qualification.\n") +
                    "First-session scope: both reviewed holdings must sell completely and usable cash must reconcile. Use the existing Sell all and Account risk setup controls; this window submits no opening sales.\n\n" +
                    "First complete both full sales, one manual one-share buy/readback and its separate full sale. Reconcile back to cash and save Account risk setup. Then Capture native recovery checkpoint, close normally and reopen this exact copy. Prepare first execution review reads the original order IDs and baseline again and publishes only complete first-day native/scoped proof. Partial, cancelled or uncertain results stop automatic order progression for operator review. Purchases end at today's regular close; approval and host enable remain separate.";
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
                (saved.TrialOrderReviewRequired ? "Native trial order review is required. Read/recover original IDs only; ordinary Resume buying cannot release this hold or allow a residual/replacement order.\n\n" : "") +
                "Enable only this reviewed assignment. Every subsequent cycle rechecks account, original orders, risk permission, source and protection. Missing native cash coverage stops new buys.";
        }
        async Task PrepareFirst()
        {
            Check(); reviewed = null; firstReview = null; firstWorkflow = null; qualificationFingerprint = null;
            Stage("reading initial references for the first execution review");
            var baselineState = await new BrokerAccountRiskBaselineRepository().ReadAsync(lifetime.Token); Check();
            if (baselineState.Import.Binding is null || baselineState.RiskHistory.ExistingBaseline is null)
            {
                summary.Text = "The first execution review is blocked: reconcile the permanent account and save its reviewed initial risk references first. Both opening holdings must be fully sold before this cash-start approval.\n\n" +
                    "The limited first-day trial still requires these four independent native checks, plus its scoped sale/reference/cash/recovery evidence:\n" +
                    string.Join("\n", BrokerActivationReview.LimitedTrialNativeChecks.Select(c => BrokerFirstExecutionReviewPolicy.Label(c) + ": native qualification still required."));
                return;
            }
            if (await executionSessions.ReadAsync(baselineState.Import.Binding.BindingId, lifetime.Token) is not null)
            { Check(); await Refresh(); return; }
            Stage("loading the reviewed calendar and current Normal definition");
            Check(); _ = GetExecutionHost();
            var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
            var portfolios = await new DelphiLiveLedgerRepository().GetPortfoliosForSessionAsync(date, lifetime.Token); Check();
            var normal = TfsaLiveStrategySelection.Rank(portfolios).Where(c => c.SupportsTransitionReview &&
                c.Snapshot.ComparisonAccount?.Profile == PaperGateProfile.NormalV1).ToArray();
            if (normal.Length != 1) throw new InvalidOperationException("The exact current Normal source is unavailable or ambiguous. Refresh its source before review.");
            var exact = await new TradingPromotionRepository().ReviewBrokerAsync(normal[0].Snapshot.PortfolioId,
                TradingStrategyFamily.DelphiLive, lifetime.Token); Check();
            var evidence = ReadExecutionQualification(DateTimeOffset.UtcNow);
            if (evidence?.NoTransferScope is null)
            {
                Stage("recovering the original baseline import, risk history and checkpoint");
                var baseline = baselineState.RiskHistory.ExistingBaseline!;
                var original = await new BrokerAccountImportRepository().FindAsync(baseline.ImportRequestId, lifetime.Token)
                    ?? throw new InvalidOperationException("Recover the exact import used by the saved initial risk baseline before trial preparation."); Check();
                var continued = await new BrokerAccountRiskContinuationRepository(executionCalendar!).ReadAsync(baseline.BaselineId, lifetime.Token); Check();
                var savedCheckpoint = NativeTrialEvidence().ReadCheckpoint(date)
                    ?? throw new InvalidOperationException("Capture the native recovery checkpoint after all manual trades and cash-only risk setup, then close normally and reopen this exact copy before preparing review.");
                // The immutable checkpoint retains original inventory-verification times before the baseline.
                // Post-restart native status reads below independently match these IDs/intents and financial facts.
                var recordedSales = savedCheckpoint.Sales;
                var candidates = BrokerSupervisedTrialPreparation.OpeningSaleCandidates(original.Plan.Observation, recordedSales, date);
                if (candidates.Length < 2)
                    throw new InvalidOperationException("Recover both original full-sale results and their verified empty inventories before the saved baseline. Trial preparation cannot invent missing sale records.");
                var selectedIds = BrokerOpeningSalesReview.Select(dialog, candidates); Check();
                if (selectedIds is null) { summary.Text = "Opening-sale selection cancelled. No trial scope, approval or order was created."; return; }
                var sales = BrokerSupervisedTrialPreparation.SelectOpeningSales(original.Plan.Observation,
                    savedCheckpoint.Sales, date, selectedIds.Value);
                var preparedScope = BrokerSupervisedTrialPreparation.Prepare(baseline, original, continued, sales,
                    Environment.UserName, executionCalendar!, DateTimeOffset.UtcNow) with { FullFillOnlyNativeTrial = true };
                var nativeAfter = await ReadNativeTrialObservationAsync(account, revision, savedCheckpoint, lifetime.Token, Stage); Check();
                exact = nativeAfter.Strategy;
                Stage("validating restart evidence and publishing its protected qualification packet");
                evidence = NativeTrialEvidence().Complete(nativeAfter, preparedScope, executionCalendar!, Environment.UserName, DateTimeOffset.UtcNow);
            }
            IBrokerRiskEvidenceSource risk = evidence?.NoTransferScope is { } scope
                ? new BrokerNoTransferRiskEvidenceSource(scope, executionCalendar!) : new UnqualifiedBrokerRiskEvidenceSource();
            var components = BrokerManagedSessionComposition.CreateComponents(connection, connection, connection, journal,
                journal, orderGate, account, executionCalendar!, risk);
            Stage("reconciling the current account for execution review");
            var projected = await components.AccountSource.SynchronizeAsync([], lifetime.Token); Check();
            firstReview = BrokerFirstExecutionReviewPolicy.Prepare(exact, projected, evidence,
                BrokerManagedSessionComposition.ImplementationIdentity, DateTimeOffset.UtcNow);
            firstWorkflow = firstReview.CanApprove ? components.Workflow : null;
            qualificationFingerprint = evidence is null ? null : BrokerAccountImport.Hash(evidence);
            summary.Text = $"Exact strategy: {exact.Strategy.Strategy.Name}\nProfile: {exact.Strategy.GateProfile}\n" +
                $"Current Normal source checked: {normal[0].Snapshot.UpdatedUtc.ToLocalTime():g}\n" +
                $"Actual available purchase ceiling: {firstReview.PurchaseCeiling:N2} CAD\n" +
                (evidence?.NoTransferScope is { LimitedFirstSessionTrial: true } trial
                    ? $"Limited first-day trial: automatic purchases end {trial.PurchaseExpiresAt?.ToLocalTime():g}. Independent protection remains managed; no next-day buying permission.\n\n"
                    : "Duration: ongoing selection; first-day scope still expires at its reviewed close.\n\n") +
                (evidence?.NoTransferScope?.FullFillOnlyNativeTrial == true ? BrokerNativeTrialQualification.Limits + "\n\n" : "") +
                string.Join("\n", firstReview.Checks.Select(c => c.Label + (c.Verified ? ": reviewed evidence reference present." : ": unverified."))) +
                "\n\n" + (firstReview.CanApprove ? "Review the actual capital and evidence above, then explicitly approve. Approval submits no order and leaves the host off."
                    : "Approval blocked:\n" + string.Join("\n", firstReview.Blocks)) +
                "\n\nPaper performance limitations:\n" + string.Join("\n", exact.Evidence.Limitations);
        }
        refresh.Click += async (_, _) => await Run(Refresh);
        checkpoint.Click += async (_, _) => await Run(async () =>
        {
            Check(); reviewed = null; firstReview = null; firstWorkflow = null; qualificationFingerprint = null;
            var native = await ReadNativeTrialObservationAsync(account, revision, null, lifetime.Token, Stage); Check();
            Stage("saving and reading back the protected native checkpoint");
            NativeTrialEvidence().Capture(native);
            summary.Text = "Native recovery checkpoint saved after completed manual verification and actual cash-only baseline readback. No order, financial approval or host permission was created. Close TraderVI normally, reopen this exact executable and choose Prepare first execution review. It will read the same original order IDs and references, check the native/scoped evidence and publish the packet only if complete. Approval and host enable remain separate.";
        });
        first.Click += async (_, _) => await Run(PrepareFirst);
        approve.Click += async (_, _) => await Run(async () =>
        {
            Stage("rechecking the exact reviewed assignment and qualification packet");
            Check(); var prepared = firstReview?.Activation ?? throw new InvalidOperationException("Prepare the complete current execution review first.");
            var currentEvidence = ReadExecutionQualification(DateTimeOffset.UtcNow);
            if (currentEvidence is null || BrokerAccountImport.Hash(currentEvidence) != qualificationFingerprint)
                throw new InvalidOperationException("Qualification evidence changed. Prepare the first execution review again.");
            var current = await new TradingPromotionRepository().ReviewBrokerAsync(prepared.Strategy.PaperAccountId,
                TradingStrategyFamily.DelphiLive, lifetime.Token); Check();
            if (current.Strategy.Fingerprint != prepared.Strategy.Fingerprint || !current.Blocks.IsEmpty)
                throw new InvalidOperationException("The exact strategy or its operational readiness changed. Prepare review again.");
            Stage("saving or recovering the explicitly reviewed execution assignment");
            await (firstWorkflow ?? throw new InvalidOperationException("No complete execution review is prepared.")).ApproveAsync(
                new(prepared, Environment.UserName, "First supervised Delphi Live Normal TFSA session; actual available capital; no outside transfers or trades."), lifetime.Token);
            Check(); await Refresh(); summary.Text += "\n\nExecution approval saved. No order was sent by approval; enable the reviewed host separately.";
            BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
        });
        enable.Click += async (_, _) => await Run(async () =>
        {
            Stage("enabling the exact operator-reviewed host assignment");
            Check(); var selected = reviewed ?? throw new InvalidOperationException("Refresh the current approved assignment first.");
            await GetExecutionHost().EnableAsync(selected.BindingId, selected.SessionId, selected.Strategy.Fingerprint,
                Environment.UserName, lifetime.Token); Check(); summary.Text = ExecutionStatus.Message;
        });
        stop.Click += async (_, _) => await Run(async () =>
        {
            Stage("stopping the execution host");
            await StopExecutionCoreAsync(lifetime.Token);
            summary.Text = ExecutionStatus.Message;
        }, waitForPaperCycles: false);
        dialog.Closing += (_, e) => { if (busy) e.Cancel = true; };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); executionWindow = null; completion.TrySetResult(); };
        dialog.Show(); await Run(Refresh); await completion.Task;
    }
}
