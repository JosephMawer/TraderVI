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
        BrokerContinuationWorkflow Workflow, string PacketFingerprint);

    private async Task<ContinuationPreparation?> PrepareContinuationAsync(WealthsimpleTfsaAccount account,
        long connectionRevision, CancellationToken ct, Action check, Action<string> stage, Action<string> present)
    {
        check(); _ = GetExecutionHost();
        var now = DateTimeOffset.UtcNow;
        var bounds = executionCalendar!.GetSessionBounds(BrokerOctober21Continuation.ExecutionDate);
        if (now < bounds.OpenUtc || now >= bounds.CloseUtc)
            throw new InvalidOperationException("October 21 continuation can be reviewed only during its regular session. Preserve the saved trades/references; buying has no later-day renewal.");
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
        var evidence = new BrokerExecutionQualificationFileStore(ContinuationQualificationPath, new WindowsManualBuyProtector()).Read(now);
        if (evidence is null)
        {
            var checkpoint = NativeTrialEvidence(true).ReadCheckpoint(BrokerOctober21Continuation.ExecutionDate);
            var continued = checkpoint?.ContinuationReview ?? new BrokerOctober21Continuation(saved.SessionId, saved.RevisionId,
                BrokerAccountImport.Hash(saved), saved.SavedAt, actual,
                saved.EffectiveReview.NoTransferScope ?? throw new InvalidOperationException("Recover the original October 20 trial scope."),
                now, Environment.UserName, true) { PurchaseExpiresAt = bounds.CloseUtc };
            continued.RequirePrior(saved, executionCalendar);
            var native = await ReadNativeTrialObservationAsync(account, connectionRevision, checkpoint, ct, stage, continued); check();
            BrokerOctober21Continuation.RequireSameDefinition(saved.Strategy, native.Strategy.Strategy);
            if (checkpoint is null)
            {
                NativeTrialEvidence(true).Capture(native);
                present("October 21 current-build checkpoint saved/read back using the original four order IDs and unchanged risk references. No continuation approval or order was saved. Close TraderVI normally, reopen this same Release build with --no-build, confirm no outside changes again and choose Review next session. Do not repeat trades, baseline setup or opening-sale selection.");
                return null;
            }
            stage("verifying current-build restart recovery and publishing October 21 evidence");
            evidence = NativeTrialEvidence(true).Complete(native, continued.Scope(executionCalendar), executionCalendar,
                Environment.UserName, DateTimeOffset.UtcNow);
        }
        var continuation = evidence.NoTransferScope?.Continuation
            ?? throw new InvalidOperationException("The October 21 packet has no linked continuation. Preserve it for recovery.");
        continuation.RequirePrior(saved, executionCalendar);
        stage("reading the four original verification order IDs for continuation review");
        await ReadOriginalTrialOrdersAsync(account, original.TradingDate, ct, check);
        var normal = await ReadNativeNormalAsync(ct, stage); check();
        BrokerOctober21Continuation.RequireSameDefinition(saved.Strategy, normal.Review.Strategy);
        if (DateTimeOffset.UtcNow - normal.Updated > TimeSpan.FromMinutes(2) || normal.Updated > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Refresh the current October 21 Normal source before continuation review.");
        var prospective = new BrokerContinuationPreviewRiskStore(risks, continuation, executionCalendar);
        var components = BrokerManagedSessionComposition.CreateComponents(connection, connection, connection, journal, journal,
            orderGate, account, executionCalendar, new BrokerNoTransferRiskEvidenceSource(evidence.NoTransferScope!, executionCalendar),
            previewRisk: prospective);
        stage("reconciling the current cash-only account against the prospective continuation");
        var projected = await components.AccountSource.SynchronizeAsync(saved.Orders, ct); check();
        BrokerTrialCarryForward.RequireUnchangedBook(continuation.PreviousScope.CashOnlyAnchor, projected.Import.Plan.Observation);
        var review = BrokerFirstExecutionReviewPolicy.Prepare(normal.Review, projected, evidence,
            BrokerManagedSessionComposition.ImplementationIdentity, DateTimeOffset.UtcNow);
        if (review.Activation is { } activation)
        {
            var ceiling = Math.Min(activation.PurchaseCeiling, saved.EffectiveReview.PurchaseCeiling - saved.CommittedPurchaseCost);
            review = review with { PurchaseCeiling = ceiling,
                Activation = activation with { RequestId = BrokerOctober21Continuation.RequestId(original.BaselineId), PurchaseCeiling = ceiling } };
        }
        return new(review, normal.Review, projected,
            new(new BrokerManagedSessionRepository(executionCalendar), risks, components.AccountSource, HostGrants(), executionCalendar),
            BrokerAccountImport.Hash(evidence));
    }
}
