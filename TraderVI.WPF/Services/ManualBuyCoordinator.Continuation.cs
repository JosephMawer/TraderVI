#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    private sealed record ContinuationPreparation(BrokerFirstExecutionReview Review, BrokerStrategyReview Exact, BrokerAccountProjection Account,
        BrokerContinuationWorkflow? Workflow, BrokerOctober22Workflow? October22Workflow = null);

    private async Task<ContinuationPreparation> PrepareContinuationAsync(WealthsimpleTfsaAccount account,
        CancellationToken ct, Action check, Action<string> stage)
    {
        check(); _ = GetExecutionHost();
        var now = DateTimeOffset.UtcNow;
        if (DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) == BrokerOctober22Continuation.ExecutionDate)
            return await PrepareOctober22Async(account, ct, check, stage);
        var bounds = executionCalendar!.GetSessionBounds(BrokerOctober21Continuation.ExecutionDate);
        if (DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) != BrokerOctober21Continuation.ExecutionDate || now >= bounds.CloseUtc)
            throw new InvalidOperationException("October 21 continuation can be reviewed on October 21 before its close, including pre-open. Buying has no later-day renewal.");
        if (HostGrants().Read()?.Enabled == true)
            throw new InvalidOperationException("Stop execution host first. Continuation approval cannot inherit an enabled old host.");
        stage("recovering the original baseline and expired assignment");
        var baseline = await new BrokerAccountRiskBaselineRepository().ReadAsync(ct); check();
        var original = baseline.RiskHistory.ExistingBaseline ?? throw new InvalidOperationException("Recover the original saved risk baseline; do not save another setup.");
        var saved = await executionSessions.ReadAsync(original.BindingId, ct)
            ?? throw new InvalidOperationException("The October 21 path requires the existing October 20 assignment."); check();
        _ = BrokerManagedSessionComposition.MatchAccount(saved, new[] { account });
        if (saved.Continuation is not null)
            throw new InvalidOperationException("October 21 continuation is already saved. Refresh readiness and separately enable its reviewed host; no new approval is needed.");
        var risks = new BrokerAccountRiskContinuationRepository(executionCalendar);
        var actual = await risks.ReadAsync(original.BaselineId, ct); check();
        // A partially saved approval recovers its original risk request. Historical verification
        // files remain untouched and are not dependencies of current-session permission.
        var recoveredRisk = await risks.FindAsync(BrokerOctober21Continuation.RequestId(original.BaselineId), ct); check();
        var continuation = recoveredRisk?.Request.Continuation ?? new BrokerOctober21Continuation(saved.SessionId, saved.RevisionId,
            BrokerAccountImport.Hash(saved), saved.SavedAt, actual with { TrialCarry = null },
            saved.EffectiveReview.NoTransferScope ?? throw new InvalidOperationException("Recover the original October 20 trial scope."),
            now, Environment.UserName, true) { PurchaseExpiresAt = bounds.CloseUtc,
                PreOpenApproved = BrokerOctober21Continuation.IsPreOpenReview(executionCalendar, now) };
        continuation.RequirePrior(saved, executionCalendar);
        var scope = continuation.Scope(executionCalendar);
        stage("checking current immutable account and permission storage");
        var storage = await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        var normal = await ReadNativeNormalAsync(ct, stage); check();
        BrokerOctober21Continuation.RequireSameDefinition(saved.Strategy, normal.Review.Strategy);
        var preOpenSource = await new TradingPromotionRepository().HasPreOpenDailySourceAsync(normal.Review.Strategy,
            executionCalendar, DateTimeOffset.UtcNow, ct); check();
        if (!BrokerOctober21Continuation.SourceReady(continuation, normal.Updated, executionCalendar, DateTimeOffset.UtcNow, preOpenSource))
            throw new InvalidOperationException("Refresh the current October 21 Normal source before continuation review.");
        var prospective = new BrokerContinuationPreviewRiskStore(risks, continuation, executionCalendar);
        var components = CreateManagedComponents(connection, connection, connection, journal, journal,
            orderGate, account, executionCalendar, new BrokerNoTransferRiskEvidenceSource(scope, executionCalendar, preOpenReviewOnly: true),
            previewRisk: prospective);
        stage("reconciling the current cash-only account against the prospective continuation");
        var projected = await components.AccountSource.SynchronizeAsync(saved.Orders, ct); check();
        BrokerTrialCarryForward.RequireUnchangedBook(continuation.PreviousScope.CashOnlyAnchor, projected.Import.Plan.Observation);
        var review = BrokerContinuationReviewPolicy.Prepare(saved, continuation, normal.Review, normal.Updated, projected,
            storage.BaselineInstalled && storage.ContinuationInstalled && storage.SessionInstalled && storage.ImmutableStorageVerified,
            BrokerManagedSessionComposition.ImplementationIdentity, executionCalendar, DateTimeOffset.UtcNow, preOpenSource);
        return new(review, normal.Review, projected,
            new(new BrokerManagedSessionRepository(executionCalendar), risks, components.AccountSource, HostGrants(), executionCalendar));
    }
}
