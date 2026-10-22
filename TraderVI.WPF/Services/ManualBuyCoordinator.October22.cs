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
    private static async Task<(BrokerStrategyReview Review, DateTimeOffset Updated)> ReadOctober22SourceAsync(
        Guid paperAccount, CancellationToken ct)
    {
        var snapshot = await new DelphiLiveLedgerRepository().LoadPortfolioAsync(paperAccount, ct)
            ?? throw new InvalidOperationException("Recover the existing Portfolio rules only source ledger.");
        var review = await new TradingPromotionRepository().ReviewBrokerAsync(paperAccount, TradingStrategyFamily.DelphiLive, ct);
        return (review, new(DateTime.SpecifyKind(snapshot.UpdatedUtc, DateTimeKind.Utc)));
    }

    private async Task<ContinuationPreparation> PrepareOctober22Async(WealthsimpleTfsaAccount account,
        CancellationToken ct, Action check, Action<string> stage)
    {
        check(); _ = GetExecutionHost();
        if (HostGrants().Read()?.Enabled == true) throw new InvalidOperationException("Stop execution host before reviewing October 22. Approval cannot inherit an old grant.");
        var now = DateTimeOffset.UtcNow;
        if (DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) != BrokerOctober22Continuation.ExecutionDate ||
            now >= executionCalendar!.GetSessionBounds(BrokerOctober22Continuation.ExecutionDate).CloseUtc)
            throw new InvalidOperationException("October 22 review is available only on October 22 before 4 PM, including pre-open. No automatic renewal.");
        stage("recovering the existing Portfolio rules only assignment and original risk history");
        var baseline = await new BrokerAccountRiskBaselineRepository().ReadAsync(ct); check();
        var original = baseline.RiskHistory.ExistingBaseline ?? throw new InvalidOperationException("Recover the original baseline; do not repeat initial setup.");
        var saved = await executionSessions.ReadAsync(original.BindingId, ct)
            ?? throw new InvalidOperationException("Recover the existing October 21 assignment."); check();
        _ = BrokerManagedSessionComposition.MatchAccount(saved, new[] { account });
        if (saved.October22 is not null) throw new InvalidOperationException("October 22 approval is already saved. Refresh readiness; enable its reviewed host separately.");
        var risks = new BrokerAccountRiskContinuationRepository(executionCalendar!);
        var actual = await risks.ReadAsync(original.BaselineId, ct); check();
        var recovered = await risks.FindAsync(BrokerOctober22Continuation.RequestId(original.BaselineId), ct); check();
        var continuation = recovered?.Request.October22 ?? new BrokerOctober22Continuation(saved.SessionId, saved.RevisionId,
            BrokerAccountImport.Hash(saved), now, actual with { TrialCarry = null, Continuation = null },
            saved.EffectiveReview.NoTransferScope ?? throw new InvalidOperationException("Recover the original October 21 scope."),
            now, Environment.UserName, true, BrokerOctober22Continuation.IsPreOpenReview(executionCalendar!, now));
        continuation.RequirePrior(saved, executionCalendar!);
        stage("checking the exact existing source, current build and immutable storage");
        var source = await ReadOctober22SourceAsync(saved.Strategy.PaperAccountId, ct); check();
        var storage = await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        var preOpenDaily = await new TradingPromotionRepository().HasPreOpenDailySourceAsync(source.Review.Strategy,
            executionCalendar!, DateTimeOffset.UtcNow, ct); check();
        var scope = continuation.Scope(executionCalendar!);
        var components = CreateManagedComponents(connection, connection, connection, journal, journal,
            orderGate, account, executionCalendar!, new BrokerNoTransferRiskEvidenceSource(scope, executionCalendar!, preOpenReviewOnly: true),
            previewRisk: new BrokerOctober22PreviewRiskStore(risks, continuation, executionCalendar!));
        stage("reconciling the current cash-only account without resetting financial references");
        var projected = await components.AccountSource.SynchronizeAsync(saved.Orders, ct); check();
        var review = BrokerOctober22ReviewPolicy.Prepare(saved, continuation, source.Review, source.Updated, projected,
            storage.BaselineInstalled && storage.ContinuationInstalled && storage.SessionInstalled && storage.ImmutableStorageVerified,
            BrokerManagedSessionComposition.ImplementationIdentity, executionCalendar!, DateTimeOffset.UtcNow, preOpenDaily);
        return new(review, source.Review, projected, null,
            new(new BrokerManagedSessionRepository(executionCalendar!), risks, components.AccountSource, HostGrants(), executionCalendar!));
    }
}
