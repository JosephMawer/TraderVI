#nullable enable

using System;
using System.Collections.Generic;

namespace Core.Trader;

public enum SystemShadowGenerationStatus
{
    Draft,
    Active,
    Paused,
    CapitalReviewRequired,
    Stopped
}

public sealed record SystemShadowPortfolioDefinition(
    string Code,
    string DefaultDisplayName,
    string Lens,
    int MaximumPositions)
{
    public static IReadOnlyList<SystemShadowPortfolioDefinition> Version1 { get; } =
    [
        new("ContinuationTop3", "System — Continuation Top 3", "Continuation", 3),
        new("ContinuationTop5", "System — Continuation Top 5", "Continuation", 5),
        new("BreakoutTop3", "System — Breakout Top 3", "Breakout", 3),
        new("BreakoutTop5", "System — Breakout Top 5", "Breakout", 5)
    ];
}

public sealed record SystemShadowGenerationInfo(
    Guid GenerationId,
    string PolicyVersion,
    SystemShadowGenerationStatus Status,
    decimal TotalAccountValue,
    decimal AvailableAccountCash,
    DateTime RealSnapshotUtc,
    DateTime? ActivatedUtc,
    DateTime UpdatedUtc);

public sealed record SystemShadowAccountSnapshot(
    DateTime OccurredUtc,
    decimal TotalAccountValue,
    decimal AvailableAccountCash);

public sealed record SystemShadowPortfolioOverview(
    Guid PortfolioId,
    Guid GenerationId,
    string PortfolioCode,
    string DisplayName,
    string Lens,
    int MaximumPositions,
    string Status,
    decimal Cash,
    decimal NetAssetValue,
    int OpenPositions,
    decimal RealizedProfitLoss,
    decimal UnrealizedProfitLoss,
    decimal TotalReturn,
    decimal? DailyReturn,
    decimal Drawdown,
    DateTime? FreshestPriceEventUtc,
    DateTime? LatestCandidateEvaluationUtc,
    DateTime UpdatedUtc,
    string? SessionStatus);

public sealed record SystemShadowCandidateMonitorInfo(
    Guid CandidateTrackingId,
    int Rank,
    string Symbol,
    string State,
    string? ReasonCode,
    decimal PreviousSessionClose,
    decimal? PreviousFiveMinuteClose,
    decimal? LatestFiveMinuteClose,
    DateTime? LatestFiveMinuteBarUtc,
    DateTime? LastEvaluatedUtc);

public sealed record SystemShadowPositionInfo(
    Guid PositionId,
    Guid PortfolioId,
    string Symbol,
    string Status,
    int Shares,
    decimal AverageCost,
    decimal CostBasis,
    decimal FullPositionTarget,
    DateTime EntryUtc,
    DateTime EntryTradingDate,
    int AddOnCount,
    int SameDayReentryCount,
    decimal? HighestFifteenClose,
    DateTime? LastFifteenMinuteBarUtc,
    decimal? TrailingStopPrice,
    bool ProfitProtectionArmed,
    decimal LastPrice,
    DateTime LastPriceEventUtc,
    decimal RealizedProfitLoss,
    DateTime? ExitUtc,
    string? ExitReasonCode);

public sealed record SystemShadowEventInfo(
    Guid EventId,
    Guid PortfolioId,
    DateTime OccurredUtc,
    string EventType,
    string ReasonCode,
    string DetailsJson);

public sealed record SystemShadowRuntimePortfolio(
    Guid PortfolioId,
    Guid GenerationId,
    string PortfolioCode,
    string Lens,
    int MaximumPositions,
    string Status,
    decimal CashBalance,
    decimal HighestClosingValue,
    DateTime ActivatedUtc);

public sealed record SystemShadowRuntimeSession(
    Guid SessionId,
    Guid PortfolioId,
    DateTime TradingDate,
    Guid? CalibrationRunId,
    string Status,
    DateTime? ActivationBaselineUtc,
    decimal OpeningValue,
    bool DailyLossGuardActive);

public sealed record SystemShadowRuntimeCandidate(
    Guid CandidateTrackingId,
    Guid SessionId,
    Guid CalibrationCandidateId,
    string Symbol,
    int Rank,
    decimal PreviousSessionClose,
    string State,
    string? ReasonCode,
    DateTime? LastEvaluatedUtc);

public sealed record SystemShadowPendingOrder(
    Guid OrderId,
    Guid PortfolioId,
    Guid? SessionId,
    Guid? PositionId,
    Guid? CandidateTrackingId,
    string Symbol,
    string Side,
    string OrderKind,
    DateTime SignalReceivedUtc,
    DateTime EarliestFillUtc,
    decimal? Budget,
    string ReasonCode);

public enum SystemShadowPendingBuyAction
{
    Wait,
    Fill,
    Requalify
}

public sealed record SystemShadowPollResult(
    Guid PollCycleId,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    int SymbolsPolled,
    int OrdersFilled,
    int SignalsCreated,
    int DecisionsBlocked,
    IReadOnlyList<string> Warnings);

public sealed record SystemShadowDelphiRun(
    Guid RunId,
    DateTime RecommendationDate,
    DateTime MarketDataAsOf,
    DateTime CreatedUtc);

public sealed record SystemShadowDelphiCandidate(
    Guid CandidateId,
    string Symbol,
    int Rank,
    decimal PreviousSessionClose);
