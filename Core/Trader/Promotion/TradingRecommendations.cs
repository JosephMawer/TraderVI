#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public sealed record TradingPriceBar(Guid EvidenceId, DateTime StartUtc, DateTime ReceivedUtc,
    int Minutes, decimal Open, decimal High, decimal Low, decimal Close);
public sealed record TradingQuote(string Symbol, DateTime RequestedUtc, DateTime ReceivedUtc,
    decimal? Price, decimal? Bid, decimal? Ask, string SourceContract);
public sealed record TradingCandidate(Guid CandidateId, Guid RunId, string Symbol, int Rank, decimal PreviousClose);
/// <summary>Durable market/daily context only; paper evaluation results are not recommendation inputs.</summary>
public sealed record TradingLiveObservation(DelphiLiveEvaluationInput Input, int ContinuityEpoch)
{
    public bool EvidenceConflict { get; init; }
}
public sealed record TradingActualFill(Guid TradeId, Guid PositionId, string Symbol, string Side,
    DateTime FillUtc, DateOnly TradingDate, int Quantity, decimal Price, string? ExitReason,
    string? RecommendationKind, decimal? PositionTarget);
public sealed record TradingSymbolState(Guid? PositionId, SystemShadowTrailingState? ShadowProtection,
    DelphiLiveProfitProtectionState? LiveProtection, DelphiLiveEvaluationState? LiveEvaluation,
    Guid? LiveSessionId, DateTime? LastLiveBarUtc, int? LiveEpoch,
    DelphiLiveEvaluationResult? LastLiveResult = null, string? ExitReason = null);
public sealed record TradingRecommendationMemory(DateOnly? TradingDate, bool DailyLossHold,
    bool CapitalReviewRequired, decimal? HighestClosingValue,
    ImmutableDictionary<string, TradingSymbolState> Symbols)
{
    public DateOnly? ShadowOpeningReviewed { get; init; }
    public static TradingRecommendationMemory Empty { get; } = new(null, false, false, null,
        ImmutableDictionary<string, TradingSymbolState>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));

    public static TradingRecommendationMemory Reconcile(TradingRecommendationBatch? previous, ReportedTradingAccount? report)
    {
        var memory=previous?.Result.Memory??Empty;
        // Every report already carries forward prior holds unless a separate capital review explicitly released one.
        // Use the newest financial record so an older batch cannot resurrect a reviewed hold/high-water baseline.
        return report is not null && (previous is null || report.ReportedUtc>previous.EvaluatedUtc)
            ? memory with {TradingDate=report.TradingDate,DailyLossHold=report.DailyLossHold,
                CapitalReviewRequired=report.CapitalReviewRequired,HighestClosingValue=report.HighestClosingValue}
            : memory;
    }
}

public sealed record TradingRecommendation(string Symbol, string Action, int Quantity, decimal? ReferencePrice,
    string Reason, Guid? PositionId, Guid? CandidateId, Guid? EvidenceId, DateTime? EvidenceUtc,
    string Kind, decimal? PositionTarget = null);

public sealed record TradingProtectionSession(DateTime CloseUtc, int SessionsBeforeCurrent);

public sealed record TradingRecommendationInput(Guid BatchId, TradingStrategyAssignment Assignment,
    DateTime EvaluatedUtc, DateOnly TradingDate, DateTime SessionOpenUtc, DateTime SessionCloseUtc,
    Guid? CurrentDailyStrategyId, Guid? FrozenRunId, string ImplementationIdentity,
    ReportedTradingAccount? Account, string CurrentLedgerFingerprint, bool EntriesPaused,
    ImmutableArray<ReportedTradingHolding> CurrentHoldings, ImmutableArray<TradingActualFill> ActualFills,
    ImmutableArray<TradingCandidate> Candidates,
    ImmutableDictionary<string, ImmutableArray<TradingPriceBar>> Bars,
    ImmutableArray<TradingLiveObservation> LiveInputs,
    ImmutableDictionary<Guid, int> HoldingSessionOrdinals,
    TradingRecommendationMemory PreviousMemory, ImmutableArray<string> SourceWarnings)
{
    public ImmutableArray<TradingQuote> Quotes { get; init; } = [];
    public ImmutableDictionary<Guid, DateTime> ProtectionBoundaries { get; init; } = ImmutableDictionary<Guid, DateTime>.Empty;
    public ImmutableArray<TradingProtectionSession> PriorProtectionSessions { get; init; } = [];
    public DateTime? CandidateEffectiveUtc { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableDictionary<string, string>? BrokerMarketSymbols { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableDictionary<string, string>? BrokerHoldingSecurityIds { get; init; }
}

public sealed record TradingRecommendationResult(string Status, ImmutableArray<TradingRecommendation> Recommendations,
    ImmutableArray<string> Warnings, TradingRecommendationMemory Memory)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<TradingEntryDecision>? EntryDecisions { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<string>? EntryRiskBlocks { get; init; }
}
public sealed record TradingRecommendationBatch(Guid BatchId, string AccountLabel, Guid PromotionId,
    Guid? AccountReportId, Guid? PriorBatchId, DateTime EvaluatedUtc, string InputFingerprint,
    TradingRecommendationResult Result)
{
    public string? LedgerFingerprint { get; init; }
}

/// <summary>A session holds the account/assignment fence through publication; disposal rolls back failures.</summary>
public interface ITradingRecommendationSession : IAsyncDisposable
{
    TradingRecommendationInput Input { get; }
    Task<TradingRecommendationBatch> PublishAsync(TradingRecommendationInput input, TradingRecommendationResult result, CancellationToken token);
}
public interface ITradingRecommendationStore
{
    Task<ITradingRecommendationSession?> BeginAsync(string accountLabel, CancellationToken token);
}
public interface ITradingRecommendationQuoteSource
{
    Task<ImmutableArray<TradingQuote>> ReadAsync(TradingRecommendationInput input, CancellationToken token);
}
public sealed class TradingRecommendationWorkflow(ITradingRecommendationStore store, ITradingRecommendationQuoteSource? quotes = null)
{
    public async Task<TradingRecommendationBatch?> EvaluateAsync(string accountLabel, CancellationToken token = default)
    {
        await using var session = await store.BeginAsync(accountLabel, token);
        if (session is null) return null;
        var input = session.Input;
        if (quotes is not null && (input.Assignment.Strategy.Family == TradingStrategyFamily.DelphiLive ||
            input.Assignment.Strategy.ReadShadowPolicy().UseProgressiveProfitProtection ||
            input.PreviousMemory.Symbols.Values.Any(s => s.ShadowProtection?.Progressive is not null)) &&
            input.EvaluatedUtc >= input.SessionOpenUtc && input.EvaluatedUtc < input.SessionCloseUtc)
            input = input with { Quotes = await quotes.ReadAsync(input, token) };
        // Keep the source cutoff fixed. Quotes carry their own later causal receipt times.
        var result = TradingRecommendationEngine.Evaluate(input);
        return await session.PublishAsync(input, result, token);
    }
}
