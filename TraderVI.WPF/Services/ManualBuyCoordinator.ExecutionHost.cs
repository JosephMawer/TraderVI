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
        if (connection is EngineBrokerClient remote)
        {
            await remote.Rpc.CallAsync<bool>(EngineOperation.DisableExecution, ct: ct);
            PresentExecution(await remote.Rpc.CallAsync<BrokerExecutionHostStatus>(EngineOperation.ExecutionStatus, ct: ct));
            return;
        }
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
            if (connection is EngineBrokerClient remote)
            {
                PresentExecution(await remote.Rpc.CallAsync<BrokerExecutionHostStatus>(EngineOperation.ExecutionStatus, ct: ct));
                return;
            }
            // Default-off startup reads no SQL, broker or journal and creates no permission file.
            if (HostGrants().Read()?.Enabled != true)
            { PresentExecution(new("Off", "Automatic trading off. No execution-host permission is enabled.", DateTimeOffset.UtcNow)); return; }
            using var quiet = setupCycles.BeginSetup();
            using (PaperCycleTimings.Start(PaperCycleStage.BrokerAdmission))
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
        if (saved.EffectiveReview.NoTransferScope?.PreOpenApproved == true && saved.Orders.IsEmpty)
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerSourceRead);
            var storage = await BrokerManagedSessionRepository.ReadStorageAsync(ct);
            var normal = saved.SessionContinuation is not null
                ? await new TradingPromotionRepository().ReviewExistingBrokerAsync(saved, executionCalendar!, DateTimeOffset.UtcNow, ct)
                : saved.October22 is not null ? await ReadOctober22SourceAsync(saved.Strategy.PaperAccountId, ct)
                : saved.SourceSwitch is null ? await ReadNativeNormalAsync(ct)
                : await ReadUngatedLiveAsync(ct, _ => { });
            if (!storage.ImmutableStorageVerified || !storage.BaselineInstalled || !storage.ContinuationInstalled || !storage.SessionInstalled ||
                normal.Review.Strategy.Fingerprint != saved.Strategy.Fingerprint || !normal.Review.Blocks.IsEmpty ||
                normal.Updated > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - normal.Updated > TimeSpan.FromMinutes(2))
                throw new InvalidOperationException("Execution is waiting for current immutable storage and the fresh exact approved Live source. No order was sent.");
        }
        long revision = connection.SessionRevision;
        var accounts = await connection.ReadTfsaAccountsAsync(ct);
        if (!connection.IsConnected || revision != connection.SessionRevision ||
            accounts.Status != WealthsimpleReadStatus.Success || accounts.Data is null)
            throw new InvalidOperationException("The current owned TFSA could not be verified.");
        var account = BrokerManagedSessionComposition.MatchAccount(saved, accounts.Data);
        IBrokerRiskEvidenceSource risk = saved.OngoingManagement is { } ongoing
            ? new BrokerOngoingRiskEvidenceSource(ongoing, executionCalendar!, new BrokerOngoingBoundaryRepository().ReadAsync)
            : saved.EffectiveReview.NoTransferScope is { } scope
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
        var dialog = executionWindow = new Window { Owner = owner, Title = "Delphi Live execution setup",
            Width = 800, Height = 800, MinWidth = 620, MinHeight = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new BrokerExecutionSetupPanel(); dialog.Content = panel;
        var summary = panel.Summary; var refresh = panel.Refresh; var first = panel.PrepareFirst;
        var checkpoint = panel.CaptureNativeCheckpoint; var lastDecision = panel.LastDecision;
        var previewCarry = panel.PreviewCarry; var saveCarry = panel.SaveCarry; var noOutside = panel.NoOutsideChanges;
        var approve = panel.ApproveFirst; var enable = panel.EnableHost; var stop = panel.StopHost;
        var nextSession = panel.ReviewNextSession; var approveContinuation = panel.ApproveContinuation;
        var reviewOngoing = panel.ReviewOngoing; var approveOngoing = panel.ApproveOngoing; var pauseBuying = panel.PauseBuying;
        var reviewSwitch = panel.ReviewSourceSwitch; var approveSwitch = panel.ApproveSourceSwitch;
        using var lifetime = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed = false, busy = false, waitingForPaper = false; long revision = connection.SessionRevision;
        BrokerManagedSession? reviewed = null;
        BrokerFirstExecutionReview? firstReview = null;
        BrokerFirstApprovalRunner? firstWorkflow = null;
        string? qualificationFingerprint = null;
        BrokerRiskContinuationRequest? carryReview = null;
        ContinuationPreparation? continuationReview = null;
        SourceSwitchPreparation? sourceSwitchReview = null;
        BuildReviewPreparation? buildReview = null;
        bool reviewingBuild = false;
        BrokerOngoingManagement? ongoingReview = null;
        BrokerOngoingManagementWorkflow? ongoingWorkflow = null;
        CancellationTokenSource? reviewExpiry = null;
        IDisposable? reviewAdmission = null;
        void ReleaseReviewAdmission()
        {
            reviewAdmission?.Dispose(); reviewAdmission = null;
            reviewExpiry?.Dispose(); reviewExpiry = null;
        }
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
            busy = executionEditing = true; lastDecision.IsEnabled = refresh.IsEnabled = checkpoint.IsEnabled = first.IsEnabled = approve.IsEnabled = enable.IsEnabled = stop.IsEnabled =
                previewCarry.IsEnabled = saveCarry.IsEnabled = noOutside.IsEnabled = nextSession.IsEnabled = approveContinuation.IsEnabled =
                reviewSwitch.IsEnabled = approveSwitch.IsEnabled = false;
            reviewOngoing.IsEnabled = approveOngoing.IsEnabled = pauseBuying.IsEnabled = false;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                using var quiet = waitForPaperCycles ? setupCycles.BeginSetup() : null;
                // Acquire the next action's admission before releasing the prepared review's hold.
                ReleaseReviewAdmission();
                if (quiet is not null)
                {
                    waitingForPaper = true;
                    try
                    {
                        await quiet.WaitForIdleWithProgressAsync(lifetime.Token, elapsed =>
                            Stage($"waiting for admitted desktop paper work to finish ({elapsed:mm\\:ss} elapsed). New paper cycles are deferred. Close this window to cancel waiting; account checks have not started"));
                    }
                    finally { waitingForPaper = false; }
                    Check();
                }
                if (journal is EngineOrderReadModel orders) await orders.RefreshAsync(lifetime.Token);
                await action();
            }
            catch (OperationCanceledException) { ongoingReview = null; ongoingWorkflow = null; }
            catch (InvalidOperationException error) { if (!closed) summary.Text = error.Message; reviewed = null; firstReview = null; firstWorkflow = null; carryReview = null; continuationReview = null; sourceSwitchReview = null; buildReview = null; ongoingReview = null; ongoingWorkflow = null; }
            catch (Exception error)
            {
                if (!closed) summary.Text = $"Execution readiness failed while {stage}.\nDiagnostic: {BrokerRiskSetupDiagnostic.Describe(error)}.\n" +
                    "No completed checkpoint or approval is confirmed by this result. Preserve original orders and saved references; report this diagnostic before retrying.";
                reviewed = null; firstReview = null; firstWorkflow = null; carryReview = null; continuationReview = null; sourceSwitchReview = null; buildReview = null;
                ongoingReview = null; ongoingWorkflow = null;
            }
            finally
            {
                busy = executionEditing = false;
                if (!closed) { lastDecision.IsEnabled = refresh.IsEnabled = checkpoint.IsEnabled = first.IsEnabled = stop.IsEnabled = previewCarry.IsEnabled = noOutside.IsEnabled = true;
                    saveCarry.IsEnabled = carryReview is not null && noOutside.IsChecked == true;
                    nextSession.IsEnabled = true;
                    reviewSwitch.IsEnabled = true;
                    approveSwitch.IsEnabled = (sourceSwitchReview is not null || buildReview is not null) && noOutside.IsChecked == true;
                    approveContinuation.IsEnabled = continuationReview?.Review.CanApprove == true && noOutside.IsChecked == true;
                    reviewOngoing.IsEnabled = true;
                    approveOngoing.IsEnabled = ongoingReview is not null && ongoingWorkflow is not null && noOutside.IsChecked == true;
                    pauseBuying.IsEnabled = reviewed?.OngoingManagement is not null;
                    enable.IsEnabled = reviewed is not null; approve.IsEnabled = firstReview?.CanApprove == true && firstWorkflow is not null; }
                CommandManager.InvalidateRequerySuggested();
            }
        }
        async Task Refresh()
        {
            ongoingReview = null; ongoingWorkflow = null;
            reviewOngoing.Visibility = approveOngoing.Visibility = pauseBuying.Visibility = Visibility.Collapsed;
            Check(); reviewed = null; firstReview = null; firstWorkflow = null; qualificationFingerprint = null; carryReview = null; continuationReview = null;
            sourceSwitchReview = null;
            buildReview = null; reviewingBuild = false;
            reviewSwitch.Content = "Review Portfolio rules only"; approveSwitch.Content = "Approve reviewed source switch";
            reviewSwitch.Visibility = approveSwitch.Visibility = Visibility.Collapsed;
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
                if (baselines.RiskHistory.ExistingBaseline is { } original && original.TradingDate == BrokerTrialCarryForward.VerificationDate)
                {
                    _ = GetExecutionHost();
                    var risk = await new BrokerAccountRiskContinuationRepository(executionCalendar!).ReadAsync(original.BaselineId, lifetime.Token); Check();
                    summary.Text = lines + "No strategy is approved. Automatic trading remains off.\n\n" +
                        $"Original initial references remain saved for {original.TradingDate:yyyy-MM-dd}. Do not repeat the completed trades or save another baseline.\n" +
                        (risk.TrialCarry is null
                            ? "Accepted October 20 path: during its regular session, confirm the no-outside-change condition, Preview October 20 carry, review unchanged original references/cash/orders, then explicitly Save reviewed October 20 carry.\n\n"
                            : $"Reviewed trial carry is saved for {risk.TrialCarry.Session:yyyy-MM-dd}; original reference values, source times and holds remain preserved.\n\n") +
                        "Then capture a native recovery checkpoint with a fresh Normal source, confirm successful save, close normally/reopen this exact copy, and Prepare first execution review. Select the two original liquidation sales, excluding the test-position sale. All nine checks remain required.\n\n" +
                        "New purchases expire at the reviewed October 20 regular close; no later-day renewal. Assignment approval and host enable remain separate. No order is submitted by preview, carry save or assignment approval.";
                    return;
                }
                summary.Text = lines + "No strategy is approved to manage this account. Automatic trading remains off.\n\n" +
                    (baselines.RiskHistory.ExistingBaseline is null ? "Initial risk references have not been reviewed/saved.\n" : "Initial risk references exist; continuation still needs current qualification.\n") +
                    "First-session scope: both reviewed holdings must sell completely and usable cash must reconcile. Use the existing Sell all and Account risk setup controls; this window submits no opening sales.\n\n" +
                    "First complete both full sales, one manual one-share buy/readback and its separate full sale. Reconcile back to cash and save Account risk setup. Then Capture native recovery checkpoint, close normally and reopen this exact copy. Prepare first execution review reads the original order IDs and baseline again and publishes only complete first-day native/scoped proof. Partial, cancelled or uncertain results stop automatic order progression for operator review. Purchases end at today's regular close; approval and host enable remain separate.";
                return;
            }
            // Match using broker identity, not a display label or another signed-in account.
            _ = BrokerManagedSessionComposition.MatchAccount(saved, new[] { account });
            if (saved.OngoingManagement is not null || saved.SessionContinuation is not null)
            {
                _ = GetExecutionHost();
                first.Visibility = approve.Visibility = previewCarry.Visibility = saveCarry.Visibility = checkpoint.Visibility =
                    nextSession.Visibility = approveContinuation.Visibility = reviewSwitch.Visibility = approveSwitch.Visibility = Visibility.Collapsed;
                ((TextBlock)noOutside.Content).Text = "I confirm no deposits, withdrawals, outside trades or other account changes since the original cash-only baseline. I will pause management before any outside deposit, withdrawal or trade. Matching balances cannot independently prove that no offsetting transfers occurred.";
                if (saved.OngoingManagement is { } agreement)
                {
                    reviewed = saved; noOutside.Visibility = Visibility.Collapsed;
                    pauseBuying.Visibility = Visibility.Visible;
                    pauseBuying.Content = saved.NewBuysStopped ? "Resume new buys after checks" : "Pause new buys · keep protection";
                    enable.Content = "Resume stopped execution host";
                    summary.Text = lines + $"Ongoing management approved {agreement.ApprovedAt:g}\nStrategy: {saved.Strategy.Strategy.Name}\n" +
                        (saved.NewBuysStopped ? "New buys paused · existing exit/protection rules active when checks pass.\n" : "New buying allowed after all current checks.\n") +
                        $"Remaining cumulative purchase allowance: {saved.EffectiveReview.PurchaseCeiling - saved.CommittedPurchaseCost:N2} CAD\n" +
                        "Daily account, source, models/configuration, calendar, integrity and risk checks are automatic. No daily approval is needed for this unchanged agreement.\n" +
                        "Pause before outside account changes. A changed build/strategy or unresolved safety issue requires review.\n\n" +
                        ExecutionStatus.Message + "\n" + string.Join("\n", saved.Blocks);
                }
                else
                {
                    reviewed = null; noOutside.Visibility = Visibility.Visible;
                    reviewOngoing.Visibility = approveOngoing.Visibility = Visibility.Visible;
                    summary.Text = lines + "Initial ongoing approval is required. The previous dated permission has not been extended.\n\n" +
                        "Confirm the no-transfer agreement, then Review ongoing management. This stops the execution host for the review and checks the exact strategy/build, account and original financial references.\n\n" +
                        "Approve and start ongoing management saves the agreement and starts its host in one action. Buying remains subject to daily checks and regular market hours. Existing pause settings, protection and cumulative allowance are retained.\n\n" +
                        "Signal confirmation remains five-minute until a separately reviewed minute-data successor is qualified.";
                }
                return;
            }
            if (saved.Approval.Review.NoTransferScope?.TrialCarry is not null)
            {
                _ = GetExecutionHost();
                nextSession.Visibility = approveContinuation.Visibility = Visibility.Visible;
                first.Visibility = approve.Visibility = previewCarry.Visibility = saveCarry.Visibility = checkpoint.Visibility = Visibility.Collapsed;
                var today = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
                bool october22 = today == BrokerOctober22Continuation.ExecutionDate;
                var target = today;
                if (!executionCalendar!.IsRegularSession(today) || DateTimeOffset.UtcNow >= executionCalendar.GetSessionBounds(today).CloseUtc)
                    target = executionCalendar.GetNextSession(today);
                nextSession.Content = $"Review {target:yyyy-MM-dd}";
                approveContinuation.Content = $"Approve {target:yyyy-MM-dd} continuation";
                ((TextBlock)noOutside.Content).Text = $"I confirm no deposits, withdrawals, outside trades or other account changes since the original cash-only baseline, including missed sessions, and will keep that scope through the reviewed {target:yyyy-MM-dd} close.";
                if (target > BrokerOctober22Continuation.ExecutionDate && saved.EffectiveReview.Session < target)
                {
                    summary.Text = lines + $"Buying permission for {target:yyyy-MM-dd} needs a fresh review on that date, including pre-open. Trading remains disabled until review, explicit approval and separate enable.\n\n" +
                        "The existing Portfolio rules only strategy, original risk references, financial history and unused allowance are preserved. Missed sessions remain gaps. Fresh account, exact definition/build, immutable storage and current daily or direct market inputs must pass; paper recovery is independent.";
                    return;
                }
                if (october22 && saved.October22 is null)
                {
                    summary.Text = lines + "October 21 permission is expired. The prepared October 22 path retains the existing Portfolio rules only assignment, original risk references, financial history, modes and unused cumulative allowance.\n\nStop execution host, confirm no outside changes and choose Review October 22. Fresh account, exact source/build, immutable storage and (before open) October 22 daily evidence must pass. Approve October 22 continuation, then separately Enable reviewed host. No order is sent by review/approval; no permission after October 22 at 4 PM.";
                    return;
                }
                if (saved.Continuation is null)
                {
                    summary.Text = lines + "The saved Normal assignment and original trades/risk references remain preserved. October 20 buying permission is expired; it does not renew itself.\n\n" +
                        "Stop execution host first, then on October 21 (including pre-open) confirm no outside account changes and choose Review next session. Completed broker verification remains historical; no new verification checkpoint, restart or test trade is required.\n\n" +
                        "Review the current cash-only account, preserved risk references/holds, current Normal/source/build, installed storage and October 21 close expiry. Approve October 21 continuation explicitly, then separately Enable reviewed host. No October 22 buying authority is granted.";
                    return;
                }
                if (today != saved.EffectiveReview.Session || saved.EffectiveReview.NoTransferScope?.PurchaseExpiresAt <= DateTimeOffset.UtcNow)
                { summary.Text = lines + "The saved dated permission is expired. New buying stays blocked; history and protection remain preserved. October 22 review is prepared for October 22 only, including pre-open; stop the host normally before that review."; return; }
                nextSession.Visibility = approveContinuation.Visibility = Visibility.Collapsed;
                if (saved.SourceSwitch is null)
                {
                    reviewSwitch.Visibility = approveSwitch.Visibility = Visibility.Visible;
                    summary.Text = lines + "Current assignment: Delphi Live Normal.\n\n" +
                        "Requested source: Delphi Live · Portfolio rules only. This build requires an explicit source-switch review; the old host grant cannot authorize the changed source/build.\n\n" +
                        "Stop execution host, confirm no outside account changes, then choose Review Portfolio rules only. Review the exact source, daily-gate bypass, preserved risk references and today's 4 PM cutoff. Approval and Enable reviewed host are separate; review/approval submit no order.";
                    return;
                }
                if (saved.Strategy.Strategy.ImplementationIdentity != BrokerManagedSessionComposition.ImplementationIdentity)
                {
                    reviewingBuild = true;
                    reviewSwitch.Content = "Review updated build"; approveSwitch.Content = "Approve reviewed build";
                    reviewSwitch.Visibility = approveSwitch.Visibility = Visibility.Visible;
                    summary.Text = lines + "The saved Portfolio rules only source uses an earlier build.\n\n" +
                        "Stop execution host, confirm no outside account changes, then choose Review updated build. " +
                        "Review the same source, preserved financial history and today's 4 PM cutoff. Approval and Enable reviewed host remain separate; no signal history or financial references are reset.";
                    return;
                }
            }
            if (!storage.BaselineInstalled || !storage.ContinuationInstalled || !storage.SessionInstalled || saved.ReviewInvalidated ||
                saved.Strategy.Strategy.ImplementationIdentity != BrokerManagedSessionComposition.ImplementationIdentity ||
                saved.Strategy.Strategy.Family != TradingStrategyFamily.DelphiLive ||
                saved.Strategy.GateProfile != Core.Trader.PaperGateProfile.NormalV1 &&
                    (saved.SourceSwitch is null || saved.Strategy.GateProfile != Core.Trader.PaperGateProfile.PortfolioRulesOnlyV1))
            { summary.Text = lines + "The saved assignment needs a fresh exact source/build/storage review before host activation."; return; }
            _ = GetExecutionHost(); reviewed = saved;
            summary.Text = lines + $"Approved strategy: {saved.Strategy.Strategy.Name}\nSaved mode: {(saved.NewBuysStopped ? "new buys stopped" : "buying subject to current risk checks")}\n" +
                $"Assignment state: {saved.Phase}\n\n{ExecutionStatus.Message}\n\n" +
                (saved.Continuation is not null ? $"Dated continuation saved. New purchases expire {saved.EffectiveReview.NoTransferScope?.PurchaseExpiresAt?.ToLocalTime():g}; no automatic renewal.\n\n" : "") +
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
            var normal = TfsaLiveStrategySelection.Rank(portfolios).Where(c => c.HasNormalLiveComparisonIdentity).ToArray();
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
                var candidates = BrokerSupervisedTrialPreparation.OpeningSaleCandidates(original.Plan.Observation, recordedSales, baseline.TradingDate);
                if (candidates.Length < 2)
                    throw new InvalidOperationException("Recover both original full-sale results and their verified empty inventories before the saved baseline. Trial preparation cannot invent missing sale records.");
                var selectedIds = BrokerOpeningSalesReview.Select(dialog, candidates); Check();
                if (selectedIds is null) { summary.Text = "Opening-sale selection cancelled. No trial scope, approval or order was created."; return; }
                var sales = BrokerSupervisedTrialPreparation.SelectOpeningSales(original.Plan.Observation,
                    savedCheckpoint.Sales, baseline.TradingDate, selectedIds.Value);
                var preparedScope = BrokerSupervisedTrialPreparation.Prepare(baseline, original, continued, sales,
                    Environment.UserName, executionCalendar!, DateTimeOffset.UtcNow) with { FullFillOnlyNativeTrial = true };
                var nativeAfter = await ReadNativeTrialObservationAsync(account, revision, savedCheckpoint, lifetime.Token, Stage); Check();
                exact = nativeAfter.Strategy;
                Stage("validating restart evidence and publishing its protected qualification packet");
                evidence = NativeTrialEvidence().Complete(nativeAfter, preparedScope, executionCalendar!, Environment.UserName, DateTimeOffset.UtcNow);
            }
            IBrokerRiskEvidenceSource risk = evidence?.NoTransferScope is { } scope
                ? new BrokerNoTransferRiskEvidenceSource(scope, executionCalendar!) : new UnqualifiedBrokerRiskEvidenceSource();
            var components = CreateManagedComponents(connection, connection, connection, journal,
                journal, orderGate, account, executionCalendar!, risk);
            Stage("reconciling the current account for execution review");
            var projected = await components.AccountSource.SynchronizeAsync([], lifetime.Token); Check();
            firstReview = BrokerFirstExecutionReviewPolicy.Prepare(exact, projected, evidence,
                BrokerManagedSessionComposition.ImplementationIdentity, DateTimeOffset.UtcNow);
            firstWorkflow = firstReview.CanApprove ? new(new BrokerManagedSessionRepository(), components.AccountSource) : null;
            qualificationFingerprint = evidence is null ? null : BrokerAccountImport.Hash(evidence);
            if (firstReview.CanApprove)
            {
                var activation = firstReview.Activation!;
                var remaining = activation.PreparedAt.AddMinutes(2) - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new InvalidOperationException("The prepared review expired before display. Prepare first execution review again.");
                reviewExpiry = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                reviewAdmission = setupCycles.HoldReview(reviewExpiry.Token);
                reviewExpiry.Token.Register(() => dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (closed || busy || !ReferenceEquals(firstReview?.Activation, activation)) return;
                    ReleaseReviewAdmission(); firstReview = null; firstWorkflow = null; approve.IsEnabled = false;
                    summary.Text = "The two-minute assignment review expired. No approval or order was saved. Paper scheduling has resumed. Prepare first execution review again before approval.";
                })));
                reviewExpiry.CancelAfter(remaining);
            }
            summary.Text = $"Exact strategy: {exact.Strategy.Strategy.Name}\nProfile: {exact.Strategy.GateProfile}\n" +
                $"Current Normal source checked: {normal[0].Snapshot.UpdatedUtc.ToLocalTime():g}\n" +
                $"Actual available purchase ceiling: {firstReview.PurchaseCeiling:N2} CAD\n" +
                (evidence?.NoTransferScope?.TrialCarry is { } carried
                    ? $"Original references carried from {carried.Baseline.TradingDate:yyyy-MM-dd}; no historical close/open is claimed. Original verification trades remain on that date.\n" : "") +
                (evidence?.NoTransferScope is { LimitedFirstSessionTrial: true } trial
                    ? $"Limited first-day trial: automatic purchases end {trial.PurchaseExpiresAt?.ToLocalTime():g}. Independent protection remains managed; no next-day buying permission.\n\n"
                    : "Duration: ongoing selection; first-day scope still expires at its reviewed close.\n\n") +
                (evidence?.NoTransferScope?.FullFillOnlyNativeTrial == true ? BrokerNativeTrialQualification.Limits + "\n\n" : "") +
                string.Join("\n", firstReview.Checks.Select(c => c.Label + (c.Verified ? ": reviewed evidence reference present." : ": unverified."))) +
                "\n\n" + (firstReview.CanApprove ? "Review the actual capital and evidence above, then explicitly approve within two minutes. New desktop paper cycles are deferred during this review. Approval submits no order and leaves the host off."
                    : "Approval blocked:\n" + string.Join("\n", firstReview.Blocks)) +
                "\n\nPaper performance limitations:\n" + string.Join("\n", exact.Evidence.Limitations);
        }
        // Saved-state display needs no paper-cycle drain. Review/approval still take admission.
        refresh.Click += async (_, _) => await Run(Refresh, waitForPaperCycles: false);
        lastDecision.Click += async (_, _) => await Run(async () =>
        {
            Check(); Stage("reading the last saved decision");
            var binding = (await new BrokerAccountImportRepository().ReadAsync(lifetime.Token)).Binding
                ?? throw new InvalidOperationException("Link the account before reading its saved decision.");
            var saved = connection is EngineBrokerClient remote
                ? await remote.Rpc.CallAsync<EngineBrokerDiagnostics>(EngineOperation.ExecutionDiagnostics, binding.BindingId, lifetime.Token)
                : await new BrokerManagedSessionRepository().ReadDiagnosticsAsync(binding.BindingId, lifetime.Token);
            Check();
            summary.Text = $"Saved revision {saved.Revision} · {saved.SavedAt:u}\n" +
                (saved.Decision is { } decision
                    ? $"Evaluated {decision.EvaluatedUtc:u}\n{decision.Summary}\n\n" +
                      string.Join("\n", decision.Entries.Select(e => $"{e.Symbol}: {e.Outcome} · signal {e.SignalBarUtc:u}")) +
                      "\n" + string.Join("\n", decision.NewRiskBlocks.Concat(decision.QuoteReadBlocks))
                    : "This revision contains no saved decision trace.");
        }, waitForPaperCycles: false);
        noOutside.Checked += (_, _) => { saveCarry.IsEnabled = !busy && carryReview is not null;
            approveContinuation.IsEnabled = !busy && continuationReview?.Review.CanApprove == true;
            approveSwitch.IsEnabled = !busy && (sourceSwitchReview is not null || buildReview is not null); };
        noOutside.Unchecked += (_, _) => { saveCarry.IsEnabled = approveContinuation.IsEnabled = approveSwitch.IsEnabled = false;
            carryReview = null; continuationReview = null; sourceSwitchReview = null; buildReview = null;
            ongoingReview = null; ongoingWorkflow = null; approveOngoing.IsEnabled = false; ReleaseReviewAdmission(); };
        reviewOngoing.Click += async (_, _) => await Run(async () =>
        {
            if (noOutside.IsChecked != true) throw new InvalidOperationException("Read and confirm the ongoing no-transfer agreement first.");
            var prepared = await PrepareOngoingAsync(account, lifetime.Token, Check, Stage);
            ongoingReview = prepared.Review; ongoingWorkflow = prepared.Workflow;
            summary.Text = $"Review ongoing management · {ongoingReview.Review.Strategy.Strategy.Name}\n" +
                $"Build: {ongoingReview.Review.Strategy.Strategy.ImplementationIdentity}\n" +
                $"Cumulative purchase ceiling: {ongoingReview.Review.PurchaseCeiling:N2} CAD. Day changes and sales do not refill it.\n\n" +
                "Until paused, with automatic daily checks and checked restart restoration. Original account loss references and history remain. Pause new buys leaves existing protection active; full host stop is separate.\n\n" +
                "No outside changes since the original baseline; pause before any outside deposit, withdrawal or trade. Unexplained differences block buying. Matching balances cannot detect offsetting transfers.\n\n" +
                "Execution-price policy: fixed fresh broker ask per confirmation episode; at most 0.25% upward movement and 0.50% spread (ask minus bid, divided by ask). Shares reserve against the upper price within existing allocation/cash/allowance. Rejected references never move up. Market-order fills can exceed the checked quote.\n\n" +
                "Five-minute signal confirmation remains in effect. Review this exact agreement within two minutes, then Approve and start ongoing management. This enables checked automatic execution; it does not guarantee a purchase or fill.";
        }, waitForPaperCycles: false);
        approveOngoing.Click += async (_, _) => await Run(async () =>
        {
            if (noOutside.IsChecked != true || ongoingReview is null || ongoingWorkflow is null)
                throw new InvalidOperationException("Prepare and confirm the exact ongoing review first.");
            Check(); var saved = await ongoingWorkflow.ApproveAsync(ongoingReview, lifetime.Token); Check();
            // Financial approval is durable before host arming. A failed arm remains visibly recoverable.
            reviewed = saved;
            await StartApprovedOngoingAsync(saved, lifetime.Token); Check();
            await Refresh(); BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
        }, waitForPaperCycles: false);
        pauseBuying.Click += async (_, _) => await Run(async () =>
        {
            Check(); var selected = reviewed ?? throw new InvalidOperationException("Refresh the saved assignment first.");
            await executionSessions.SetBuyingStoppedAsync(selected.BindingId, selected.SessionId, !selected.NewBuysStopped, lifetime.Token);
            Check(); await Refresh(); BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
        }, waitForPaperCycles: false);
        reviewSwitch.Click += async (_, _) => await Run(async () =>
        {
            Check(); reviewed = null; sourceSwitchReview = null; buildReview = null;
            if (noOutside.IsChecked != true) throw new InvalidOperationException("Confirm no outside account changes before reviewing the source switch.");
            if (reviewingBuild)
            {
                buildReview = await PrepareBuildReviewAsync(account, lifetime.Token, Check, Stage); Check();
                summary.Text = buildReview.Summary;
                reviewExpiry = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                reviewAdmission = setupCycles.HoldReview(reviewExpiry.Token);
                var heldBuild = buildReview;
                reviewExpiry.Token.Register(() => dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (closed || busy || !ReferenceEquals(buildReview, heldBuild)) return;
                    ReleaseReviewAdmission(); buildReview = null; approveSwitch.IsEnabled = false;
                    summary.Text = "The updated-build review expired. No approval or order was saved. Review updated build again.";
                })));
                var remainingBuild = heldBuild.Proposal.Review.PreparedAt.AddMinutes(2) - DateTimeOffset.UtcNow;
                reviewExpiry.CancelAfter(remainingBuild > TimeSpan.Zero ? remainingBuild : TimeSpan.Zero);
                return;
            }
            sourceSwitchReview = await PrepareSourceSwitchAsync(account, lifetime.Token, Check, Stage); Check();
            summary.Text = sourceSwitchReview.Summary;
            reviewExpiry = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            reviewAdmission = setupCycles.HoldReview(reviewExpiry.Token);
            var held = sourceSwitchReview;
            reviewExpiry.Token.Register(() => dialog.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closed || busy || !ReferenceEquals(sourceSwitchReview, held)) return;
                ReleaseReviewAdmission(); sourceSwitchReview = null; approveSwitch.IsEnabled = false;
                summary.Text = "The source-switch review expired. No switch or order was saved. Review Portfolio rules only again.";
            })));
            var remaining = held.Proposal.Review.PreparedAt.AddMinutes(2) - DateTimeOffset.UtcNow;
            reviewExpiry.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        });
        approveSwitch.Click += async (_, _) => await Run(async () =>
        {
            if (reviewingBuild)
            {
                Check(); var preparedBuild = buildReview ?? throw new InvalidOperationException("Review updated build first.");
                if (noOutside.IsChecked != true) throw new InvalidOperationException("The no-outside-change confirmation is required.");
                await ApproveBuildReviewAsync(preparedBuild, lifetime.Token, Check, Stage);
                await Refresh(); summary.Text += "\n\nUpdated build approval saved/read back. No order was sent. Enable reviewed host separately.";
                BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            Check(); var prepared = sourceSwitchReview ?? throw new InvalidOperationException("Review Portfolio rules only first.");
            if (noOutside.IsChecked != true) throw new InvalidOperationException("The no-outside-change confirmation is required.");
            await ApproveSourceSwitchAsync(prepared, lifetime.Token, Check, Stage);
            await Refresh(); summary.Text += "\n\nPortfolio rules only switch saved/read back. No order was sent. Enable reviewed host separately.";
            BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
        });
        nextSession.Click += async (_, _) => await Run(async () =>
        {
            Check(); reviewed = null; continuationReview = null;
            if (noOutside.IsChecked != true)
                throw new InvalidOperationException("Confirm no outside account changes before reviewing the dated continuation. Changed facts require separate recovery, never another setup baseline.");
            continuationReview = await PrepareContinuationAsync(account, lifetime.Token, Check, Stage);
            var prepared = continuationReview.Review;
            var facts = continuationReview.Account;
            var dateLabel = (prepared.Activation?.Session ?? DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow))).ToString("yyyy-MM-dd");
            summary.Text = $"{dateLabel} continuation review\nExact source: {prepared.Strategy.Strategy.Name}\nProfile: {prepared.Strategy.GateProfile}\n" +
                $"Policy: {prepared.Strategy.Strategy.PolicyVersionId:D}\nDaily definition: {prepared.Strategy.Strategy.Daily.StrategyVersionId:D}\n" +
                $"Current build review: {BrokerAccountImport.Hash(BrokerManagedSessionComposition.ImplementationIdentity)[..16]}\n" +
                $"Available purchase ceiling: {prepared.PurchaseCeiling:N2} CAD\nFresh cash: {facts.Import.Plan.Observation.Cash:N2} CAD · buying power: {facts.Import.Plan.Observation.BuyingPower:N2} CAD\n" +
                $"Original daily reference: {facts.Risk.SessionReference.Value:N2} CAD · {facts.Risk.SessionReference.Source} · evidence {facts.Risk.SessionReference.EvidenceAt.ToLocalTime():g}\n" +
                $"Original drawdown reference: {facts.Risk.DrawdownReference.Value:N2} CAD · {facts.Risk.DrawdownReference.Source} · evidence {facts.Risk.DrawdownReference.EvidenceAt.ToLocalTime():g}\n" +
                $"Daily hold: {facts.Risk.DailyLossHold} · capital review hold: {facts.Risk.CapitalReviewRequired}\n" +
                "Original trades, sale selection, memory, prior approval and all history remain preserved. Completed verification is retained in development records; approval checks current readiness.\n" +
                (prepared.Activation?.NoTransferScope?.PreOpenApproved == true
                    ? "Pre-open approval: enable the reviewed host now to arm it for 9:30 AM Toronto. Keep the independent engine running and signed in. No pre-open orders; fresh account, exact source and risk checks at the open can delay or block execution.\n" : "") +
                (continuationReview.October22Workflow is not null || continuationReview.SessionWorkflow is not null ? "Portfolio rules only bypasses the existing daily admission gates. Intraday confirmation, freshness, sizing/cash/account limits, stops, holds, reconciliation, full-fill-only restrictions and original risk reference dates remain. The existing 3:45 PM entry cutoff also remains. October 21 expiry and historical coverage gaps remain recorded.\n" : "") +
                $"New purchases end {prepared.Activation?.NoTransferScope?.PurchaseExpiresAt?.ToLocalTime():g}. No later-day buying permission.\n\n" +
                string.Join("\n", prepared.Checks.Select(c => c.Label + (c.Verified ? ": current check passed." : ": blocked — " + c.Detail))) +
                "\n\n" + BrokerNativeTrialQualification.Limits + "\n\n" +
                (prepared.CanApprove ? $"Review these facts, then Approve {dateLabel} continuation within two minutes. Approval submits no order and leaves the host off."
                    : "Approval blocked:\n" + string.Join("\n", prepared.Blocks)) +
                "\n\nPaper performance limitations:\n" + string.Join("\n", continuationReview.Exact.Evidence.Limitations);
            if (prepared.Activation is { } activation)
            {
                var held = continuationReview;
                reviewExpiry = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                reviewAdmission = setupCycles.HoldReview(reviewExpiry.Token);
                reviewExpiry.Token.Register(() => dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (closed || busy || !ReferenceEquals(continuationReview, held)) return;
                    ReleaseReviewAdmission(); continuationReview = null; approveContinuation.IsEnabled = false;
                    summary.Text = "The continuation review expired. No approval or order was saved. Choose Review next session again.";
                })));
                var remaining = activation.PreparedAt.AddMinutes(2) - DateTimeOffset.UtcNow;
                reviewExpiry.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }
        }, waitForPaperCycles: false);
        approveContinuation.Click += async (_, _) => await Run(async () =>
        {
            Check(); var prepared = continuationReview ?? throw new InvalidOperationException("Review next session before approval.");
            if (noOutside.IsChecked != true) throw new InvalidOperationException("The no-outside-change confirmation is required.");
            Stage("rechecking current immutable storage and exact strategy source");
            var storage = await BrokerManagedSessionRepository.ReadStorageAsync(lifetime.Token); Check();
            if (!storage.BaselineInstalled || !storage.ContinuationInstalled || !storage.SessionInstalled || !storage.ImmutableStorageVerified)
                throw new InvalidOperationException("Current immutable storage checks failed. Keep execution off and recover storage before approval.");
            var normal = prepared.SessionWorkflow is not null
                ? await new TradingPromotionRepository().ReviewExistingBrokerAsync(prepared.ReviewedSession!, executionCalendar!, DateTimeOffset.UtcNow, lifetime.Token)
                : prepared.October22Workflow is not null ? await ReadOctober22SourceAsync(prepared.Exact.Strategy.PaperAccountId, lifetime.Token)
                : await ReadNativeNormalAsync(lifetime.Token, Stage); Check();
            var scope = prepared.Review.Activation!.NoTransferScope!;
            var preOpenSource = await new TradingPromotionRepository().HasPreOpenDailySourceAsync(normal.Review.Strategy,
                executionCalendar!, DateTimeOffset.UtcNow, lifetime.Token); Check();
            if (normal.Review.Strategy.Fingerprint != prepared.Exact.Strategy.Fingerprint || !normal.Review.Blocks.IsEmpty ||
                !(scope.SessionContinuation is { } selectedSession
                    ? BrokerSessionContinuation.SourceReady(selectedSession, normal.Updated, executionCalendar!, DateTimeOffset.UtcNow, preOpenSource)
                    : scope.October22 is { } oct22
                    ? BrokerOctober22Continuation.SourceReady(oct22, normal.Updated, executionCalendar!, DateTimeOffset.UtcNow, preOpenSource)
                    : BrokerOctober21Continuation.SourceReady(scope.Continuation!, normal.Updated, executionCalendar!, DateTimeOffset.UtcNow, preOpenSource)))
                throw new InvalidOperationException("The exact source changed or expired. Review the dated continuation again.");
            Stage("saving or recovering the explicitly approved dated continuation");
            var approval = new BrokerExecutionApproval(prepared.Review.Activation!, Environment.UserName,
                $"Explicit {scope.Session:yyyy-MM-dd}-only supervised continuation; original trades/references preserved; no outside changes; no recurring authority.");
            if (prepared.SessionWorkflow is { } sessionWorkflow) await sessionWorkflow.ApproveAsync(approval, lifetime.Token);
            else if (prepared.October22Workflow is { } workflow22) await workflow22.ApproveAsync(approval, lifetime.Token);
            else await prepared.Workflow!.ApproveAsync(approval, lifetime.Token);
            Check(); await Refresh(); summary.Text += "\n\nDated continuation saved/read back. No order was sent. Enable reviewed host separately.";
            BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
        }, waitForPaperCycles: false);
        previewCarry.Click += async (_, _) => await Run(async () =>
        {
            Check(); carryReview = null; reviewed = null; firstReview = null; firstWorkflow = null;
            if (noOutside.IsChecked != true)
                throw new InvalidOperationException("Confirm the no-outside-change condition above before reviewing the October 20 carry. If any outside change occurred, preserve the original records for recovery.");
            var facts = await ReadTrialCarryInputsAsync(account, lifetime.Token, Check, Stage); Check();
            var repository = new BrokerAccountRiskContinuationRepository(executionCalendar!);
            var prior = await repository.FindAsync(BrokerTrialCarryForward.RequestId(facts.Baseline.BaselineId), lifetime.Token); Check();
            if (prior is not null)
            {
                if (prior.Request.TrialCarry?.Fingerprint != facts.Risk.TrialCarry?.Fingerprint ||
                    BrokerAccountImport.Hash(prior.State) != BrokerAccountImport.Hash(facts.Risk))
                    throw new InvalidOperationException("Saved carry or risk history changed. Preserve the original carry for reviewed recovery.");
                summary.Text = "The original reviewed October 20 carry is already saved and recovered. No new financial event was written. Original initial references remain unchanged. Next Capture native recovery checkpoint with a fresh Normal source; do not repeat trades or save another baseline.";
                return;
            }
            carryReview = BrokerTrialCarryForward.Prepare(facts.Baseline, facts.Original, facts.Risk, facts.Account,
                Environment.UserName, true, executionCalendar!, DateTimeOffset.UtcNow);
            var carry = carryReview.TrialCarry!;
            var originalOrders = journal.ReadAll().Where(b => BrokerNativeTrialQualification.MatchesAccount(account, b.Intent.AccountId) &&
                DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(b.Intent.CreatedAt.UtcDateTime)) == facts.Baseline.TradingDate)
                .Select(b => $"BUY {b.Intent.Symbol} · {b.Intent.Quantity} share(s) · {b.Intent.RequestId:D}")
                .Concat(journal.ReadSells().Where(s => BrokerNativeTrialQualification.MatchesAccount(account, s.Intent.AccountId) &&
                    DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(s.Intent.CreatedAt.UtcDateTime)) == facts.Baseline.TradingDate)
                    .Select(s => $"SELL {s.Intent.Symbol} · {s.Intent.Quantity} share(s) · {s.Intent.RequestId:D}"));
            summary.Text = $"Review October 20 trial carry\nOriginal baseline: {facts.Baseline.TradingDate:yyyy-MM-dd}, saved {facts.Baseline.EstablishedAt.ToLocalTime():g}\n" +
                $"Daily reference retained: {facts.Risk.SessionReference.Value:N2} CAD · {facts.Risk.SessionReference.Source} · original evidence {facts.Risk.SessionReference.EvidenceAt.ToLocalTime():g}\n" +
                $"Drawdown reference retained: {facts.Risk.DrawdownReference.Value:N2} CAD · {facts.Risk.DrawdownReference.Source} · original evidence {facts.Risk.DrawdownReference.EvidenceAt.ToLocalTime():g}\n" +
                $"Daily hold: {facts.Risk.DailyLossHold} · Capital review hold: {facts.Risk.CapitalReviewRequired}\n" +
                $"Fresh unchanged cash: {facts.Account.Cash:N2} CAD · Buying power: {facts.Account.BuyingPower:N2} CAD\nNo holdings or pending orders; all original order facts match.\n" +
                $"Execution date: {carry.Session:yyyy-MM-dd} · new purchases expire {executionCalendar!.GetSessionBounds(carry.Session).CloseUtc.ToLocalTime():g}\n\n" +
                "Original verification order IDs:\n" + string.Join("\n", originalOrders) +
                "\n\nNo historical close/open is reconstructed; original references and holds are preserved.\n\n" +
                "Review these facts, then explicitly Save reviewed October 20 carry. Saving appends only this trial carry after fresh checks. It submits no order and leaves assignment approval/host enable separate.";
        });
        saveCarry.Click += async (_, _) => await Run(async () =>
        {
            Check(); var preview = carryReview?.TrialCarry ?? throw new InvalidOperationException("Preview the exact October 20 carry before saving.");
            if (noOutside.IsChecked != true) throw new InvalidOperationException("The no-outside-change condition must still be confirmed.");
            var facts = await ReadTrialCarryInputsAsync(account, lifetime.Token, Check, Stage); Check();
            if (BrokerAccountImport.Hash(preview.Baseline) != BrokerAccountImport.Hash(facts.Baseline) ||
                BrokerAccountImport.Hash(preview.OriginalAnchor) != BrokerAccountImport.Hash(facts.Original.Plan.Observation))
                throw new InvalidOperationException("The original baseline/import changed. Preserve references and review the original records.");
            BrokerTrialCarryForward.RequireUnchangedBook(preview.ReviewedAccount, facts.Account);
            var repository = new BrokerAccountRiskContinuationRepository(executionCalendar!);
            var prior = await repository.FindAsync(BrokerTrialCarryForward.RequestId(facts.Baseline.BaselineId), lifetime.Token); Check();
            if (prior is not null)
            {
                if (prior.Request.TrialCarry?.Fingerprint != facts.Risk.TrialCarry?.Fingerprint ||
                    BrokerAccountImport.Hash(prior.State) != BrokerAccountImport.Hash(facts.Risk))
                    throw new InvalidOperationException("Recover the original carry and current risk history before retrying; no replacement event was saved.");
            }
            else
            {
                var request = BrokerTrialCarryForward.Prepare(facts.Baseline, facts.Original, facts.Risk, facts.Account,
                    Environment.UserName, true, executionCalendar!, DateTimeOffset.UtcNow);
                Stage("saving or recovering the explicitly reviewed October 20 trial carry");
                prior = await new BrokerAccountRiskContinuationWorkflow(repository, executionCalendar!).ApplyAsync(request, lifetime.Token); Check();
                var readback = await repository.ReadAsync(facts.Baseline.BaselineId, lifetime.Token); Check();
                if (BrokerAccountImport.Hash(readback) != BrokerAccountImport.Hash(prior.State))
                    throw new InvalidOperationException("Carry save needs recovery: readback changed. Preserve the original event; do not save another baseline.");
            }
            carryReview = null;
            summary.Text = "Reviewed October 20 trial carry saved/read back. Original initial reference records, values, source times and holds remain unchanged. No historical close/open was invented. No order, strategy approval or host permission was created.\n\nNext Capture native recovery checkpoint with a fresh Normal source. Continue to same-copy normal restart only after successful checkpoint save.";
        });
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
            if (connection is EngineBrokerClient remote)
            {
                await remote.Rpc.CallAsync<bool>(EngineOperation.EnableExecution,
                    new EngineEnableExecution(selected.BindingId, selected.SessionId, selected.Strategy.Fingerprint, Environment.UserName), lifetime.Token);
                PresentExecution(await remote.Rpc.CallAsync<BrokerExecutionHostStatus>(EngineOperation.ExecutionStatus, ct: lifetime.Token));
            }
            else await GetExecutionHost().EnableAsync(selected.BindingId, selected.SessionId, selected.Strategy.Fingerprint,
                Environment.UserName, lifetime.Token);
            Check(); summary.Text = ExecutionStatus.Message;
        }, waitForPaperCycles: false); // The independent engine owns subsequent execution and its input checks.
        stop.Click += async (_, _) => await Run(async () =>
        {
            Stage("stopping the execution host");
            await StopExecutionCoreAsync(lifetime.Token);
            summary.Text = ExecutionStatus.Message;
        }, waitForPaperCycles: false);
        dialog.Closing += (_, e) => { if (busy && !waitingForPaper) e.Cancel = true; };
        dialog.Closed += (_, _) => { closed = true; ReleaseReviewAdmission(); lifetime.Cancel(); executionWindow = null; completion.TrySetResult(); };
        dialog.Show(); await Run(Refresh, waitForPaperCycles: false); await completion.Task;
    }
}
