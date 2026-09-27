#nullable enable
using System;
using System.Collections.Immutable;
using Core.Trader.DelphiLive;

namespace Core.Trader.Risk;

/// <summary>
/// Adapter-supplied whole-portfolio evidence. This type does not qualify a price feed.
/// NoCapitalFlowsConfirmed covers the interval since the previous accepted valuation;
/// aggregate net-deposit totals are insufficient. Cash-flow adjustment integration is pending.
/// </summary>
public sealed record PortfolioRiskValuation(Guid PortfolioId, Guid EvidenceId, DateOnly TradingDate,
    DateTimeOffset At, DateTimeOffset ValidUntil, decimal Value, bool IsComplete, bool NoCapitalFlowsConfirmed);

public enum AiRiskRoundPhase { Active, Closing, AwaitingEntry, SequencePaused }

/// <summary>Immutable working state. AiPortfolioRiskJournal retains every accepted transition and prior round.</summary>
public sealed record AiPortfolioRiskState(Guid PortfolioId, Guid SequenceId, Guid RoundId,
    int SequenceNumber, int RoundNumber, decimal OriginalCapital, decimal RoundOpeningCapital,
    decimal RoundPeak, PortfolioRiskValuation LastValuation, AiRiskRoundPhase Phase,
    DateOnly? FailedSession, DateTimeOffset? ReconciledAt, Guid? ReconciliationId)
{
    public decimal LossLimit => AiPortfolioRiskPolicy.LimitFor(RoundNumber);
    public decimal ExitTrigger => RoundPeak * (1m - LossLimit);
    public decimal Drawdown => System.Math.Max(0m, (RoundPeak - LastValuation.Value) / RoundPeak);
    public int FailedAttempts => Phase == AiRiskRoundPhase.Active ? RoundNumber - 1 : RoundNumber;
    public override string ToString() => "AI portfolio risk state (financial data omitted)";
}

/// <summary>Confirmed execution evidence; acknowledgements and partial fills are not closure.</summary>
public sealed record AiRoundClosure(Guid PortfolioId, Guid RoundId, Guid EvidenceId, DateTimeOffset At,
    int RemainingManagedHoldings, int UnresolvedOrders, decimal ReconciledCash, bool IsReconciled);

/// <summary>Fresh entry/review evidence after closure, plus existing independent risk blocks.</summary>
public sealed record AiRoundEntry(Guid PreviousRoundId, PortfolioRiskValuation Valuation, DateTimeOffset SignalAt,
    DateTimeOffset ReviewAt, DateTimeOffset ValidUntil, ImmutableArray<string> IndependentBlocks);

/// <summary>
/// Offline continuous-round state machine (ADR-0087). No AI, storage, price collection or order calls.
/// Existing engine guards are not replaced until their adapters and execution integration are ready.
/// Overall review holds and other controls remain external inputs and cannot be cleared here.
/// </summary>
public sealed class AiPortfolioRiskPolicy(ITsxSessionCalendar calendar)
{
    private readonly ITsxSessionCalendar calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));

    public static decimal LimitFor(int roundNumber) => roundNumber switch
    {
        1 => 0.05m,
        2 => 0.03m,
        3 => 0.01m,
        _ => throw new ArgumentOutOfRangeException(nameof(roundNumber))
    };

    public AiPortfolioRiskState Start(PortfolioRiskValuation opening, DateTimeOffset now)
    {
        ValidateValuation(opening, now);
        if (opening.Value <= 0m) throw new InvalidOperationException("A round requires positive opening capital.");
        return new(opening.PortfolioId, Guid.NewGuid(), Guid.NewGuid(), 1, 1, opening.Value,
            opening.Value, opening.Value, opening, AiRiskRoundPhase.Active, null, null, null);
    }

    public AiPortfolioRiskState Observe(AiPortfolioRiskState state, PortfolioRiskValuation value, DateTimeOffset now)
    {
        ValidateState(state);
        ValidateValuation(value, now);
        if (value.PortfolioId != state.PortfolioId)
            throw new InvalidOperationException("Valuation belongs to another portfolio.");
        if (value == state.LastValuation) return state;
        if (value.At <= state.LastValuation.At || value.EvidenceId == state.LastValuation.EvidenceId ||
            state.ReconciledAt is { } reconciled && value.At < reconciled)
            throw new InvalidOperationException("Valuation must advance the current evidence without changing an old observation.");

        decimal peak = state.Phase == AiRiskRoundPhase.Active ? System.Math.Max(state.RoundPeak, value.Value) : state.RoundPeak;
        bool hit = state.Phase == AiRiskRoundPhase.Active && value.Value <= peak * (1m - state.LossLimit);
        return state with
        {
            RoundPeak = peak,
            LastValuation = value,
            Phase = hit ? AiRiskRoundPhase.Closing : state.Phase,
            FailedSession = hit ? value.TradingDate : state.FailedSession
        };
    }

    public AiPortfolioRiskState ConfirmClosure(AiPortfolioRiskState state, AiRoundClosure closure, DateTimeOffset now)
    {
        ValidateState(state);
        ArgumentNullException.ThrowIfNull(closure);
        if (state.Phase != AiRiskRoundPhase.Closing || closure.PortfolioId != state.PortfolioId || closure.RoundId != state.RoundId ||
            closure.EvidenceId == Guid.Empty || !closure.IsReconciled || closure.RemainingManagedHoldings != 0 ||
            closure.UnresolvedOrders != 0 || closure.ReconciledCash < 0m || closure.At < state.LastValuation.At || closure.At > now)
            throw new InvalidOperationException("A failed round needs confirmed closure of managed holdings, orders and cash.");
        return state with
        {
            Phase = state.RoundNumber == 3 ? AiRiskRoundPhase.SequencePaused : AiRiskRoundPhase.AwaitingEntry,
            ReconciledAt = closure.At,
            ReconciliationId = closure.EvidenceId
        };
    }

    public AiPortfolioRiskState BeginNextRound(AiPortfolioRiskState state, AiRoundEntry entry, DateTimeOffset now)
    {
        ValidateState(state);
        ArgumentNullException.ThrowIfNull(entry);
        ValidateValuation(entry.Valuation, now);
        if (state.Phase is not (AiRiskRoundPhase.AwaitingEntry or AiRiskRoundPhase.SequencePaused) ||
            state.ReconciledAt is not { } reconciled || entry.PreviousRoundId != state.RoundId || entry.Valuation.PortfolioId != state.PortfolioId ||
            entry.Valuation.Value <= 0m || entry.Valuation.At <= reconciled ||
            entry.Valuation.At < state.LastValuation.At || entry.SignalAt <= reconciled ||
            entry.ReviewAt < entry.SignalAt || entry.ReviewAt < entry.Valuation.At || entry.ReviewAt > now ||
            entry.ValidUntil < now || entry.IndependentBlocks.IsDefault || !entry.IndependentBlocks.IsEmpty)
            throw new InvalidOperationException("Re-entry requires reconciled closure, fresh entry/review evidence and no independent blocks.");

        var bounds = calendar.GetSessionBounds(entry.Valuation.TradingDate);
        if (now.UtcDateTime < bounds.OpenUtc || now.UtcDateTime > bounds.CloseUtc)
            throw new InvalidOperationException("Re-entry waits for the regular market session.");
        bool newSequence = state.Phase == AiRiskRoundPhase.SequencePaused;
        if (newSequence && entry.Valuation.TradingDate < calendar.GetNextSession(state.FailedSession!.Value))
            throw new InvalidOperationException("An exhausted sequence cannot restart until the next trading session.");

        return state with
        {
            SequenceId = newSequence ? Guid.NewGuid() : state.SequenceId,
            SequenceNumber = newSequence ? checked(state.SequenceNumber + 1) : state.SequenceNumber,
            RoundId = Guid.NewGuid(),
            RoundNumber = newSequence ? 1 : state.RoundNumber + 1,
            RoundOpeningCapital = entry.Valuation.Value,
            RoundPeak = entry.Valuation.Value,
            LastValuation = entry.Valuation,
            Phase = AiRiskRoundPhase.Active,
            FailedSession = null,
            ReconciledAt = null,
            ReconciliationId = null
        };
    }

    public PortfolioRiskAssessment Assess(AiPortfolioRiskState state, DateTimeOffset now,
        bool hasFreshAiReview, ImmutableArray<string> independentBlocks)
    {
        ValidateState(state);
        if (independentBlocks.IsDefault) throw new ArgumentException("Independent risk checks must be supplied.", nameof(independentBlocks));
        var blocks = independentBlocks.ToBuilder();
        if (state.LastValuation.At > now || state.LastValuation.ValidUntil < now) blocks.Add("ValuationNotCurrent");
        if (!hasFreshAiReview) blocks.Add("FreshAiReviewRequired");
        if (state.Phase != AiRiskRoundPhase.Active) blocks.Add(state.Phase.ToString());
        decimal multiplier = blocks.Count == 0 ? System.Math.Clamp(1m - state.Drawdown / state.LossLimit, 0m, 1m) : 0m;
        return new(PortfolioRiskProfile.AiContinuousRounds, multiplier,
            state.Phase == AiRiskRoundPhase.Closing, blocks.ToImmutable());
    }

    private void ValidateValuation(PortfolioRiskValuation value, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.PortfolioId == Guid.Empty || value.EvidenceId == Guid.Empty || !value.IsComplete ||
            !value.NoCapitalFlowsConfirmed || value.Value < 0m || value.At == default ||
            value.At > now || value.ValidUntil < now || !calendar.IsRegularSession(value.TradingDate))
            throw new InvalidOperationException("A complete, current, identified valuation and reconciled no-flow interval are required.");
        var bounds = calendar.GetSessionBounds(value.TradingDate);
        if (value.At.UtcDateTime < bounds.OpenUtc || value.At.UtcDateTime > bounds.CloseUtc)
            throw new InvalidOperationException("Valuation time must match its market session.");
    }

    private static void ValidateState(AiPortfolioRiskState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool active = state.Phase == AiRiskRoundPhase.Active;
        bool reconciled = state.Phase is AiRiskRoundPhase.AwaitingEntry or AiRiskRoundPhase.SequencePaused;
        if (state.PortfolioId == Guid.Empty || state.SequenceId == Guid.Empty || state.RoundId == Guid.Empty ||
            state.SequenceNumber < 1 || state.RoundNumber is < 1 or > 3 || state.OriginalCapital <= 0m ||
            state.RoundOpeningCapital <= 0m || state.RoundPeak < state.RoundOpeningCapital ||
            state.LastValuation is null || state.LastValuation.PortfolioId != state.PortfolioId ||
            !state.LastValuation.IsComplete || !state.LastValuation.NoCapitalFlowsConfirmed ||
            state.LastValuation.Value < 0m || state.LastValuation.EvidenceId == Guid.Empty ||
            state.LastValuation.At == default || state.LastValuation.ValidUntil < state.LastValuation.At ||
            !Enum.IsDefined(state.Phase) || active == state.FailedSession.HasValue ||
            reconciled != state.ReconciledAt.HasValue || reconciled != state.ReconciliationId.HasValue ||
            state.ReconciliationId == Guid.Empty ||
            (state.Phase == AiRiskRoundPhase.SequencePaused && state.RoundNumber != 3) ||
            (state.Phase == AiRiskRoundPhase.AwaitingEntry && state.RoundNumber == 3) ||
            (active && (state.LastValuation.Value <= state.ExitTrigger || state.LastValuation.Value > state.RoundPeak)))
            throw new InvalidOperationException("Saved AI round state is inconsistent; reconcile before proceeding.");
    }
}
