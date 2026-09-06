#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveGenerationRequest(
    Guid GenerationId, Guid PortfolioId, Guid PolicyVersionId, string Role,
    Guid? ExperimentId, decimal StartingCapital, string Currency,
    DateOnly EffectiveSession, DateTime EffectiveSessionOpenUtc,
    DateTime AuthorizedUtc, string AuthorizedBy, string Reason);

public sealed record DelphiLiveLedgerPosition(
    Guid PositionId, string Symbol, int Quantity, decimal AveragePurchasePrice,
    DateTime OpenedUtc, Guid EntryActionId, string OriginalEntryDossierJson,
    DelphiLiveProfitProtectionState Protection, DateTime? ClosedUtc = null,
    Guid? ExitActionId = null);

public sealed record DelphiLiveLedgerAction(
    DelphiLiveActionIntent Intent, Guid? PositionId, Guid? EvaluationId,
    DateOnly TradingDate, Guid CreatedCycleId, string PrimaryReason,
    string DossierJson, string Status, int AttemptCount,
    Guid? LastAttemptCycleId, DateTime? CompletedUtc,
    string? TerminalReason, ImmutableArray<string> SupportingReasons);

public sealed record DelphiLiveLedgerFill(
    Guid FillId, Guid ActionId, Guid PositionId, Guid QuoteObservationId,
    string Symbol, DelphiLiveActionSide Side, int Quantity, decimal Price,
    DelphiLiveQuoteField Field, DelphiLiveFillConfidence Confidence,
    DateTime FilledUtc, DateOnly TradingDate);

public sealed record DelphiLiveLedgerQuote(
    Guid QuoteId, Guid? ActionId, Guid? PositionId, string Purpose,
    Guid CycleId, DelphiLiveCausalQuoteObservation Observation,
    string Disposition, string DossierJson);

public sealed record DelphiLiveLedgerMark(
    Guid MarkId, DateOnly TradingDate, DelphiLivePortfolioMarkKind Kind,
    DateTime BarEndUtc, bool Complete, decimal? Nav,
    ImmutableArray<DelphiLivePositionMark> Positions, string Reason);

public sealed record DelphiLivePortfolioCandidateState(
    DelphiLiveLifecycleSnapshot Lifecycle, int ContinuityEpoch,
    Guid EvaluationId, DateTime EvaluatedUtc);

public sealed record DelphiLivePortfolioSnapshot(
    Guid PortfolioId, Guid GenerationId, Guid PolicyVersionId, string Role,
    Guid? ExperimentId, DateOnly EffectiveSession, decimal StartingCapital,
    string Currency, long Revision, decimal Cash, DateOnly? CurrentSession,
    decimal? OpeningNav, decimal? PreviousClosingNav,
    decimal SessionOpeningCash, ImmutableArray<DelphiLiveLedgerPosition> SessionOpeningPositions,
    DelphiLivePortfolioGuardState Guards,
    ImmutableArray<DelphiLiveLedgerPosition> Positions,
    ImmutableArray<DelphiLiveLedgerAction> Actions,
    ImmutableArray<DelphiLiveLedgerFill> Fills,
    ImmutableArray<DelphiLiveLedgerQuote> Quotes,
    ImmutableArray<DelphiLiveLedgerMark> Marks,
    Guid? LastCompletedCycleId, DateTime UpdatedUtc)
{
    public ImmutableDictionary<string, DelphiLivePortfolioCandidateState> CandidateStates { get; init; } =
        ImmutableDictionary<string, DelphiLivePortfolioCandidateState>.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<DelphiLiveLedgerPosition> OpenPositions => Positions.Where(p => p.ClosedUtc is null);
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<DelphiLiveLedgerAction> PendingActions => Actions.Where(a => a.Status is "Pending" or "ExitPendingOvernight");
}

public sealed record DelphiLiveLedgerEvent(Guid EventId, string Kind, DateTime RecordedUtc, string DataJson);

public interface IDelphiLiveLedgerStore
{
    Task<DelphiLivePortfolioSnapshot?> LoadPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DelphiLivePortfolioSnapshot>> GetPortfoliosForSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default);
    Task<DelphiLivePortfolioSnapshot> CreateGenerationAsync(DelphiLiveGenerationRequest request, CancellationToken cancellationToken = default);
    // One atomic revision, including every event, requires both the expected
    // portfolio revision and the current durable host's fencing token.
    Task<DelphiLivePortfolioSnapshot> CommitAsync(
        long expectedRevision, DelphiLivePortfolioSnapshot next,
        IReadOnlyList<DelphiLiveLedgerEvent> events, DelphiLiveLease lease,
        CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveActionCandidate(
    string Symbol, Guid EvaluationId, Guid EvidenceId, int LiveRank,
    bool ConfirmedEntryEligible, DateTime? ConfirmationStartedBarEndUtc,
    DelphiLiveDataConfidence Confidence, DelphiLiveSafetyInput Safety,
    string DossierJson);

public sealed record DelphiLivePortfolioCycleInput(
    Guid PortfolioId, Guid CycleId, DateOnly TradingDate,
    DateTime SessionOpenUtc, DateTime SessionCloseUtc,
    DateTime CheckpointBarEndUtc, DateTime BuyCutoffUtc,
    IReadOnlyList<DelphiLivePositionMark> ExactCheckpointMarks,
    IReadOnlyList<DelphiLivePositionMark> ExactOpeningMarks,
    IReadOnlyList<DelphiLiveActionCandidate> Candidates,
    bool IsRestart = false, bool CorporateActionUnsupported = false)
{
    public Guid? SessionId { get; init; }
}

public sealed record DelphiLiveProtectionCycleInput(
    Guid PortfolioId, Guid CycleId, DateOnly TradingDate,
    DateTime SessionOpenUtc, DateTime SessionCloseUtc,
    bool IsWarmingUp, bool IsRestart = false)
{
    public Guid? SessionId { get; init; }
}

public static class DelphiLiveLedgerJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException("Stored Delphi Live ledger JSON is empty.");
}

public static class DelphiLiveLedgerIntegrity
{
    public static void ValidatePolicyPromotion(DelphiLivePortfolioSnapshot prior, DelphiLivePortfolioSnapshot next)
    {
        if (prior.Role != "OperationalChampion" || next.PolicyVersionId == Guid.Empty ||
            next.PolicyVersionId == prior.PolicyVersionId || next.Revision != prior.Revision + 1 ||
            next.UpdatedUtc.Kind != DateTimeKind.Utc || next.UpdatedUtc < prior.UpdatedUtc ||
            DelphiLiveLedgerJson.Serialize(next with
            {
                PolicyVersionId = prior.PolicyVersionId, Revision = prior.Revision, UpdatedUtc = prior.UpdatedUtc
            }) != DelphiLiveLedgerJson.Serialize(prior))
            throw new InvalidOperationException("A human-approved boundary promotion may change only current policy identity and its audited revision, preserving the operational portfolio exactly.");
    }

    public static DelphiLivePortfolioSnapshot Create(DelphiLiveGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.GenerationId == Guid.Empty || request.PortfolioId == Guid.Empty || request.PolicyVersionId == Guid.Empty ||
            request.StartingCapital <= 0m || decimal.Round(request.StartingCapital, 6) != request.StartingCapital ||
            request.Currency is null || request.Currency.Length != 3 || request.Currency.Any(c => c < 'A' || c > 'Z') ||
            string.IsNullOrWhiteSpace(request.AuthorizedBy) || string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Explicit positive simulation capital, currency, identity, and authorization are required.");
        if (request.AuthorizedUtc.Kind != DateTimeKind.Utc || request.EffectiveSessionOpenUtc.Kind != DateTimeKind.Utc ||
            request.AuthorizedUtc >= request.EffectiveSessionOpenUtc)
            throw new ArgumentException("Activation must be authorized before its next regular-session boundary.");
        if (request.Role is not ("OperationalChampion" or "ActiveShadowChallenger" or "ShadowBaseline" or "ChampionControl") ||
            (request.Role != "OperationalChampion" && request.ExperimentId is null))
            throw new ArgumentException("A non-operational portfolio requires an explicit experiment identity.");
        return new(request.PortfolioId, request.GenerationId, request.PolicyVersionId, request.Role, request.ExperimentId,
            request.EffectiveSession, request.StartingCapital, request.Currency, 0, request.StartingCapital,
            null, null, null, request.StartingCapital, [], new(false, false, null, null, request.StartingCapital), [], [], [], [], [], null, request.AuthorizedUtc);
    }

    public static void ValidateTransition(DelphiLivePortfolioSnapshot prior, DelphiLivePortfolioSnapshot next)
    {
        if (next.PortfolioId != prior.PortfolioId || next.GenerationId != prior.GenerationId ||
            next.PolicyVersionId != prior.PolicyVersionId || next.StartingCapital != prior.StartingCapital ||
            next.Currency != prior.Currency || next.Role != prior.Role || next.ExperimentId != prior.ExperimentId ||
            next.EffectiveSession != prior.EffectiveSession || next.Revision != prior.Revision + 1 ||
            next.UpdatedUtc.Kind != DateTimeKind.Utc || next.UpdatedUtc < prior.UpdatedUtc)
            throw new InvalidOperationException("Portfolio identity, capital, or revision cannot be rewritten.");
        PreservePrefix(prior.Fills, next.Fills, "fills");
        PreservePrefix(prior.Quotes, next.Quotes, "quotes");
        PreservePrefix(prior.Marks, next.Marks, "marks");
        if (next.Cash < 0m || next.Cash != next.StartingCapital + next.Fills.Sum(f =>
                (f.Side == DelphiLiveActionSide.Sell ? 1m : -1m) * f.Price * f.Quantity))
            throw new InvalidOperationException("Cash must reconcile exactly to persisted whole-share fills; capital changes are unsupported.");
        if (next.OpenPositions.GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            next.Positions.Select(p => p.PositionId).Distinct().Count() != next.Positions.Length ||
            next.Actions.Select(a => a.Intent.ActionId).Distinct().Count() != next.Actions.Length ||
            next.Fills.GroupBy(f => f.ActionId).Any(g => g.Count() > 1) ||
            next.PendingActions.Where(a => a.Intent.Side == DelphiLiveActionSide.Sell).GroupBy(a => a.PositionId).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Duplicate positions, actions, or fills violate the isolated ledger.");
        foreach (var fill in next.Fills.Skip(prior.Fills.Length))
        {
            var intent = prior.PendingActions.SingleOrDefault(a => a.Intent.ActionId == fill.ActionId)
                ?? throw new InvalidOperationException("A fill requires an already-persisted pending decision.");
            var quote = prior.Quotes.SingleOrDefault(q => q.QuoteId == fill.QuoteObservationId && q.ActionId == fill.ActionId)
                ?? throw new InvalidOperationException("A fill requires its already-persisted post-decision quote.");
            if (fill.Quantity < 1 || fill.Price <= 0m || fill.Symbol != intent.Intent.Symbol || fill.Side != intent.Intent.Side ||
                fill.FilledUtc != quote.Observation.ReceivedUtc || quote.Observation.RequestStartedUtc < intent.Intent.DecisionPersistedUtc ||
                quote.Observation.ReceivedUtc <= intent.Intent.DecisionPersistedUtc || quote.QuoteId == intent.Intent.DecisionEvidenceId ||
                next.Actions.Single(a => a.Intent.ActionId == fill.ActionId).Status != "Filled")
                throw new InvalidOperationException("The fill does not match its causal decision, quote, or atomic terminal action.");
            decimal? observed = fill.Field switch
            {
                DelphiLiveQuoteField.Ask when fill.Side == DelphiLiveActionSide.Buy => quote.Observation.Ask,
                DelphiLiveQuoteField.Bid when fill.Side == DelphiLiveActionSide.Sell => quote.Observation.Bid,
                DelphiLiveQuoteField.Price => quote.Observation.Price,
                _ => null
            };
            if (observed != fill.Price || (fill.Field == DelphiLiveQuoteField.Price) != (fill.Confidence == DelphiLiveFillConfidence.EstimatedFill))
                throw new InvalidOperationException("Fill price and confidence must match the preserved quote field.");
            var resultingPosition = next.Positions.SingleOrDefault(p => p.PositionId == fill.PositionId)
                ?? throw new InvalidOperationException("The fill requires its atomic position record.");
            if (fill.Side == DelphiLiveActionSide.Buy)
            {
                if (prior.Positions.Any(p => p.PositionId == fill.PositionId) || resultingPosition.EntryActionId != fill.ActionId ||
                    resultingPosition.Quantity != fill.Quantity || resultingPosition.AveragePurchasePrice != fill.Price ||
                    resultingPosition.OpenedUtc != fill.FilledUtc || resultingPosition.ClosedUtc is not null ||
                    intent.Intent.BuyBudget is not decimal budget || fill.Quantity * fill.Price > budget)
                    throw new InvalidOperationException("The Buy must atomically create a whole-share position within its recorded cash budget.");
            }
            else
            {
                var held = prior.OpenPositions.SingleOrDefault(p => p.PositionId == fill.PositionId)
                    ?? throw new InvalidOperationException("A Sell may close only this portfolio's open position.");
                if (held.Quantity != fill.Quantity || intent.PositionId != held.PositionId ||
                    resultingPosition.ClosedUtc != fill.FilledUtc || resultingPosition.ExitActionId != fill.ActionId)
                    throw new InvalidOperationException("A protective Sell must atomically close the full owned position.");
            }
        }
        foreach (var position in next.Positions.Where(p => prior.Positions.All(old => old.PositionId != p.PositionId)))
            if (!next.Fills.Skip(prior.Fills.Length).Any(f => f.PositionId == position.PositionId && f.Side == DelphiLiveActionSide.Buy))
                throw new InvalidOperationException("A position cannot be introduced without its causal Buy fill.");
        foreach (var position in prior.Positions)
        {
            var changed = next.Positions.SingleOrDefault(p => p.PositionId == position.PositionId)
                ?? throw new InvalidOperationException("A historical position was removed.");
            if (changed.Symbol != position.Symbol || changed.Quantity != position.Quantity ||
                changed.AveragePurchasePrice != position.AveragePurchasePrice || changed.OpenedUtc != position.OpenedUtc ||
                changed.EntryActionId != position.EntryActionId || changed.OriginalEntryDossierJson != position.OriginalEntryDossierJson ||
                (position.ClosedUtc is not null && changed != position) ||
                (position.Protection.FloorPrice is decimal oldFloor && (changed.Protection.FloorPrice is not decimal floor || floor < oldFloor)))
                throw new InvalidOperationException("Position attribution, completed history, and protection floors are immutable or monotone.");
        }
        foreach (var action in prior.Actions)
        {
            var changed = next.Actions.SingleOrDefault(a => a.Intent.ActionId == action.Intent.ActionId)
                ?? throw new InvalidOperationException("A historical action was removed.");
            if (changed.Intent != action.Intent || changed.PositionId != action.PositionId || changed.EvaluationId != action.EvaluationId ||
                changed.TradingDate != action.TradingDate || changed.CreatedCycleId != action.CreatedCycleId ||
                changed.PrimaryReason != action.PrimaryReason || changed.DossierJson != action.DossierJson ||
                changed.AttemptCount < action.AttemptCount || (action.Status is not ("Pending" or "ExitPendingOvernight") &&
                    DelphiLiveLedgerJson.Serialize(changed) != DelphiLiveLedgerJson.Serialize(action)))
                throw new InvalidOperationException("Action identity, original reason, and completed history cannot be rewritten.");
        }
    }

    private static void PreservePrefix<T>(ImmutableArray<T> prior, ImmutableArray<T> next, string name)
    {
        if (next.Length < prior.Length || prior.Where((item, index) =>
                DelphiLiveLedgerJson.Serialize(item) != DelphiLiveLedgerJson.Serialize(next[index])).Any())
            throw new InvalidOperationException($"Persisted {name} are append-only.");
    }
}
