#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader;

/// <summary>Shadow ledger persistence boundary. SQL and isolated practice stores share the controller.</summary>
public interface ISystemShadowStore
{
    Task<bool> HasSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemShadowRuntimePortfolio>> GetRunnablePortfoliosAsync(
        CancellationToken cancellationToken = default);

    Task<SystemShadowRuntimeSession> EnsureSessionAsync(
        SystemShadowRuntimePortfolio portfolio,
        DateTime tradingDate,
        SystemShadowDelphiRun? run,
        IReadOnlyList<SystemShadowDelphiCandidate> candidates,
        DateTime? activationBaselineUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemShadowRuntimeCandidate>> GetSessionCandidatesAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemShadowPositionInfo>> GetPositionsAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemShadowPendingOrder>> GetPendingOrdersAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    Task<bool> TryCreateOrderAsync(
        Guid portfolioId,
        Guid? sessionId,
        Guid? positionId,
        Guid? candidateTrackingId,
        string symbol,
        string side,
        string orderKind,
        DateTime signalReceivedUtc,
        decimal? budget,
        string reasonCode,
        CancellationToken cancellationToken = default, SystemShadowPolicyConfig? config = null, Guid? strategyVersionId = null, Guid? operatorRequestId = null);

    Task<bool> FillPendingOrderAsync(
        SystemShadowPendingOrder order,
        decimal rawFillPrice,
        DateTime fillUtc,
        DateTime tradingDate,
        int sameDayReentryCount,
        CancellationToken cancellationToken = default, SystemShadowPolicyConfig? config = null);

    Task<bool> CancelPendingOrderAsync(
        SystemShadowPendingOrder order,
        string reason,
        DateTime occurredUtc,
        CancellationToken cancellationToken = default);

    Task UpdateCandidateDecisionAsync(
        Guid candidateTrackingId,
        string state,
        string reasonCode,
        DateTime evaluatedUtc,
        CancellationToken cancellationToken = default);

    Task UpdatePositionEvidenceAsync(
        Guid positionId,
        decimal lastPrice,
        DateTime lastPriceEventUtc,
        SystemShadowTrailingState trailingState,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemShadowPortfolioOverview>> GetPortfolioOverviewsAsync(
        Guid generationId,
        CancellationToken cancellationToken = default);

    Task SetRiskGuardAsync(
        Guid portfolioId,
        Guid sessionId,
        bool dailyLossGuard,
        bool capitalReviewRequired,
        CancellationToken cancellationToken = default);

    Task CompleteSessionAsync(
        Guid sessionId,
        decimal closingValue,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<int> GetPositionSessionOrdinalAsync(
        Guid portfolioId,
        DateTime entryTradingDate,
        DateTime currentTradingDate,
        CancellationToken cancellationToken = default);

    Task ExpirePendingBuysAndMarkNoEntryAsync(
        Guid sessionId,
        DateTime occurredUtc,
        CancellationToken cancellationToken = default);

    Task ExpirePendingBuysBeforeDateAsync(
        DateTime currentTradingDate,
        CancellationToken cancellationToken = default);
}
