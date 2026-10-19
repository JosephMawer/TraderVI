#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public sealed record BrokerRiskEvidence(ImmutableArray<BrokerRiskContinuationRequest> Continuations,
    BrokerCashCoverage? Coverage, ImmutableArray<string> Blocks);
public interface IBrokerRiskEvidenceSource
{
    Task<BrokerRiskEvidence> ReadAsync(BrokerImportReceipt import, BrokerAccountRiskState state, CancellationToken ct);
    Task<BrokerRiskEvidence> ReadAsync(BrokerImportReceipt import, BrokerAccountRiskState state,
        ImmutableArray<BrokerStrategyOrder> ownedOrders, CancellationToken ct) => ReadAsync(import, state, ct);
}
/// <summary>The captured broker feed does not prove cash-effective timing or matched boundary books.</summary>
public sealed class UnqualifiedBrokerRiskEvidenceSource : IBrokerRiskEvidenceSource
{
    public Task<BrokerRiskEvidence> ReadAsync(BrokerImportReceipt import, BrokerAccountRiskState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new BrokerRiskEvidence([], null,
            ["Native effective cash timing, complete event coverage and matched boundary books remain unqualified."]));
    }
}

/// <summary>Shared synchronization/continuation caller. It neither invents source qualification nor starts a scheduler.</summary>
public sealed class BrokerManagedAccountSource(BrokerReconciliationWorkflow reconciliation,
    Func<long> sessionRevision, WealthsimpleTfsaAccount brokerAccount, IBrokerRiskBaselineStore baselines,
    IBrokerAccountRiskContinuationStore continuationStore, ITsxSessionCalendar calendar,
    IBrokerRiskEvidenceSource riskEvidence, BrokerImportWorkflow imports,
    ManualMarketBuyWorkflow buys, ManualMarketSellWorkflow sells, IManualSellBroker broker,
    Func<string, CancellationToken, Task<string>> resolveSecurity, TimeProvider? time = null) : IBrokerManagedAccountSource
{
    public string ImplementationIdentity => TradingRecommendationStorage.ImplementationIdentity;
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly BrokerAccountRiskContinuationWorkflow continuation = new(continuationStore, calendar, time);
    public bool RequiresTrialOrderReview(ImmutableArray<BrokerStrategyOrder> orders) =>
        BrokerNativeTrialQualification.RequiresOrderReview(buys.ReadOrders(), sells.ReadOrders(), orders);
    public async Task<BrokerAccountProjection> SynchronizeAsync(ImmutableArray<BrokerStrategyOrder> orders, CancellationToken ct)
    {
        foreach (var order in orders.Where(o => !o.Reconciled))
        {
            if (order.Side == "BUY") await buys.CheckOrderAsync(order.RequestId, ct);
            else await sells.CheckOrderAsync(order.RequestId, ct);
        }
        long revision = sessionRevision();
        var synchronized = await new BrokerAccountSynchronizationWorkflow(imports).RefreshAsync(
            token => reconciliation.ReadAsync(brokerAccount, revision, token), ct);
        var saved = synchronized.Saved ?? throw new InvalidOperationException(
            "Use the reviewed permanent account link and resolve broker history before strategy synchronization.");
        var baselineState = await baselines.ReadAsync(ct);
        var baseline = baselineState.RiskHistory.ExistingBaseline ?? throw new InvalidOperationException("Establish the reviewed initial risk references first.");
        var state = await continuationStore.ReadAsync(baseline.BaselineId, ct);
        var evidence = await riskEvidence.ReadAsync(saved, state, orders, ct);
        foreach (var request in evidence.Continuations)
            state = (await continuation.ApplyAsync(request, ct)).State;
        var projection = BrokerAccountProjector.Project(baselineState.Import.Binding!, saved, state, evidence.Coverage, clock.GetUtcNow());
        return projection with { NewRiskBlocks = projection.NewRiskBlocks.AddRange(evidence.Blocks).Distinct().ToImmutableArray() };
    }
    public async Task<ImmutableArray<BrokerProtectionMark>> ReadProtectionMarksAsync(BrokerAccountProjection account, CancellationToken ct)
    {
        var result = ImmutableArray.CreateBuilder<BrokerProtectionMark>();
        foreach (var position in account.Positions)
        {
            var read = await broker.ReadManualSellContextAsync(brokerAccount, position.Symbol, ct);
            if (read.Status != WealthsimpleReadStatus.Success || read.Data is not { } context || context.Security.Id != position.SecurityId)
                throw new InvalidOperationException("A retained holding's current price could not be verified.");
            result.Add(new(position.PositionId, context.Quote.Bid, context.Quote.QuotedAt, BrokerAccountImport.Hash(context.Quote)));
        }
        return result.ToImmutable();
    }
    public async Task<TradingRecommendationInput> ReadStrategyInputAsync(BrokerManagedSession session, CancellationToken ct)
    {
        var input = await new TradingRecommendationRepository().ReadBrokerInputAsync(session, ct);
        if (session.Strategy.Strategy.Family == TradingStrategyFamily.DailyShadow &&
            (session.Strategy.Strategy.ReadShadowPolicy().UseProgressiveProfitProtection ||
             input.PreviousMemory.Symbols.Values.Any(s => s.ShadowProtection?.Progressive is not null)))
            input = input with { Quotes = await new TradingRecommendationQuoteSource().ReadAsync(input, ct) };
        return input;
    }
    public Task<ImmutableArray<TradingQuote>> ReadQuotesAsync(TradingRecommendationInput input, CancellationToken ct) =>
        new TradingRecommendationQuoteSource().ReadAsync(input, ct);
    public Task<string> ResolveSecurityAsync(string symbol, CancellationToken ct) => resolveSecurity(symbol, ct);
    public async Task PreserveRiskHoldsAsync(BrokerAccountProjection account, TradingRecommendationMemory memory, CancellationToken ct)
    {
        if ((!memory.DailyLossHold || account.Risk.DailyLossHold) && (!memory.CapitalReviewRequired || account.Risk.CapitalReviewRequired)) return;
        var at = clock.GetUtcNow();
        await continuation.ApplyAsync(new(Guid.NewGuid(), account.Risk.BaselineId, account.Risk.RevisionId,
            BrokerRiskContinuationKind.HoldObservation, null, null,
            new(Guid.NewGuid(), account.Risk.BindingId, account.Risk.TradingDate, at, memory.DailyLossHold, memory.CapitalReviewRequired)), ct);
    }
}
