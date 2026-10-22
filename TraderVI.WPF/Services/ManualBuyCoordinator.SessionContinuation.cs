#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    private async Task<ContinuationPreparation> PrepareSelectedSessionAsync(WealthsimpleTfsaAccount account,
        CancellationToken ct, Action check, Action<string> stage)
    {
        check(); _ = GetExecutionHost();
        if (HostGrants().Read()?.Enabled == true) throw new InvalidOperationException("Stop execution host before reviewing the next session. Approval cannot inherit an old grant.");
        var now = DateTimeOffset.UtcNow;
        var session = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (!executionCalendar!.IsRegularSession(session) || now >= executionCalendar.GetSessionBounds(session).CloseUtc)
            throw new InvalidOperationException("Review the next regular session on its date before close, including pre-open. Tomorrow needs fresh account and daily checks; no automatic renewal.");
        stage("recovering the existing Portfolio rules only assignment and original risk history");
        var baseline = await new BrokerAccountRiskBaselineRepository().ReadAsync(ct); check();
        var original = baseline.RiskHistory.ExistingBaseline ?? throw new InvalidOperationException("Recover the original baseline; do not repeat initial setup.");
        var saved = await executionSessions.ReadAsync(original.BindingId, ct)
            ?? throw new InvalidOperationException("Recover the existing real-account assignment."); check();
        _ = BrokerManagedSessionComposition.MatchAccount(saved, new[] { account });
        if (saved.EffectiveReview.Session == session) throw new InvalidOperationException("This session approval is already saved. Refresh readiness; enable its reviewed host separately.");
        var risks = new BrokerAccountRiskContinuationRepository(executionCalendar!);
        var recovered = await risks.FindAsync(BrokerSessionContinuation.RequestId(original.BaselineId, session), ct); check();
        var continuation = recovered?.Request.SessionContinuation ??
            BrokerSessionContinuation.Create(saved, session, now, Environment.UserName, true, executionCalendar!);
        continuation.RequirePrior(saved, executionCalendar!);
        stage("checking the exact existing source, current build and immutable storage");
        var source = await new TradingPromotionRepository().ReviewExistingBrokerAsync(saved, executionCalendar!, now, ct); check();
        var storage = await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        var preOpenDaily = await new TradingPromotionRepository().HasPreOpenDailySourceAsync(source.Review.Strategy,
            executionCalendar!, DateTimeOffset.UtcNow, ct); check();
        var scope = continuation.Scope(executionCalendar!);
        var components = CreateManagedComponents(connection, connection, connection, journal, journal,
            orderGate, account, executionCalendar!, new BrokerNoTransferRiskEvidenceSource(scope, executionCalendar!, preOpenReviewOnly: true),
            previewRisk: new BrokerSessionPreviewRiskStore(risks, continuation, executionCalendar!));
        stage("reconciling the current cash-only account without resetting financial references");
        var projected = await components.AccountSource.SynchronizeAsync(saved.Orders, ct); check();
        var review = BrokerSessionReviewPolicy.Prepare(saved, continuation, source.Review, source.Updated, projected,
            storage.BaselineInstalled && storage.ContinuationInstalled && storage.SessionInstalled && storage.ImmutableStorageVerified,
            BrokerManagedSessionComposition.ImplementationIdentity, executionCalendar!, DateTimeOffset.UtcNow, preOpenDaily);
        return new(review, source.Review, projected, null, null,
            new(new BrokerManagedSessionRepository(executionCalendar!, sharedStorage: true), risks, components.AccountSource, HostGrants(), executionCalendar!), saved);
    }
}
