#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Trader.Promotion;

/// <summary>
/// Account-wide risk state only. Symbol protection remains with its current owner and is never copied
/// back from this snapshot. An initial baseline and every later revision remain independent records.
/// </summary>
public sealed record BrokerAccountRiskState(Guid BaselineId, Guid BindingId, string AccountLabel,
    Guid? RevisionId, DateOnly TradingDate, BrokerRiskReference SessionReference,
    BrokerRiskReference DrawdownReference, bool DailyLossHold, bool CapitalReviewRequired,
    DateOnly? LastCompletedSession, DateTimeOffset Through)
{
    public override string ToString() => "Continuing account risk state (financial data omitted)";
}

public enum BrokerRiskContinuationKind { Opening, Closing, HoldObservation, CashFlows }

/// <summary>
/// A successful cash-flow reconciliation of this interval is required. This contract supports ONLY
/// intervals with no external deposits or withdrawals; use CashFlows when confirmed flows exist.
/// It must never be asserted from equal aggregate net-deposit totals (opposing flows can cancel).
/// </summary>
public sealed record BrokerRiskNoFlowEvidence(Guid EvidenceId, Guid BindingId,
    DateTimeOffset From, DateTimeOffset Through, DateTimeOffset ObservedAt, string SourceFingerprint)
{
    public override string ToString() => "Reconciled no-capital-flow interval (financial data omitted)";
}

public sealed record BrokerRiskHoldObservation(Guid EvidenceId, Guid BindingId, DateOnly TradingDate,
    DateTimeOffset At, bool DailyLossHold, bool CapitalReviewRequired)
{
    public override string ToString() => "Observed account holds (financial data omitted)";
}

public sealed record BrokerRiskContinuationRequest(Guid RequestId, Guid BaselineId, Guid? PreviousRevisionId,
    BrokerRiskContinuationKind Kind, BrokerRiskBoundaryValue? Boundary,
    BrokerRiskNoFlowEvidence? NoFlows, BrokerRiskHoldObservation? Holds)
{
    // Omit absent evidence to preserve hashes of previously saved requests.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrokerRiskCashFlowInterval? CashFlows { get; init; }
    [JsonIgnore]
    public string RequestHash => EngineStrategySettings.Hash(JsonSerializer.Serialize(this));
    public override string ToString() => "Account risk continuation request (no order authority)";
}

public sealed record BrokerRiskContinuationReceipt(BrokerRiskContinuationRequest Request,
    BrokerAccountRiskState State, DateTimeOffset SavedAt)
{
    public override string ToString() => "Saved account risk continuation (financial data omitted)";
}

/// <summary>
/// Deterministic reference continuation. Missing evidence leaves saved state intact; this policy has
/// no order, host, scheduler or recommendation dependency. No generic late-start buy pause is added.
/// </summary>
public static class BrokerAccountRiskContinuation
{
    public static BrokerAccountRiskState Start(BrokerAccountRiskBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.BaselineId == Guid.Empty || baseline.BindingId == Guid.Empty || baseline.AccountLabel != "TFSA" ||
            baseline.EstablishedAt == default || baseline.ObservationAt == default || baseline.ObservationAt > baseline.EstablishedAt ||
            baseline.SessionReference.Value <= 0m || baseline.DrawdownReference.Value <= 0m)
            throw new ArgumentException("Continuation requires the saved initial account baseline.");
        // A prior day's daily hold is not a same-day hold; capital holds have no ordinary expiry.
        bool dailyHold = baseline.CarriedMemory.TradingDate == baseline.TradingDate && baseline.CarriedMemory.DailyLossHold;
        return new(baseline.BaselineId, baseline.BindingId, baseline.AccountLabel, null, baseline.TradingDate,
            baseline.SessionReference, baseline.DrawdownReference, dailyHold, baseline.CarriedMemory.CapitalReviewRequired,
            null, baseline.ObservationAt);
    }

    public static BrokerAccountRiskState Apply(BrokerAccountRiskState state, BrokerRiskContinuationRequest request,
        ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(request);
        if (state.BaselineId != request.BaselineId || state.RevisionId != request.PreviousRevisionId)
            throw new InvalidOperationException("Risk history advanced or belongs to another baseline. Read the saved state again.");
        if (state.Through > now || state.SessionReference.Value <= 0m || state.DrawdownReference.Value <= 0m)
            throw new InvalidOperationException("Saved risk references are inconsistent.");

        if (request.Kind == BrokerRiskContinuationKind.HoldObservation)
        {
            var holds = request.Holds!;
            if (holds.EvidenceId == Guid.Empty || holds.BindingId != state.BindingId || holds.At < state.Through ||
                holds.At > now || holds.TradingDate != state.TradingDate)
                throw new InvalidOperationException("Hold evidence must belong to the current account and risk session.");
            // Do not advance the valuation/cash-flow boundary for a hold-only observation.
            return state with { RevisionId = request.RequestId, DailyLossHold = state.DailyLossHold || holds.DailyLossHold,
                CapitalReviewRequired = state.CapitalReviewRequired || holds.CapitalReviewRequired };
        }

        if (request.Kind == BrokerRiskContinuationKind.CashFlows)
            return BrokerAccountCashFlowContinuation.Apply(state, request, calendar, now);

        var boundary = request.Boundary!;
        var bounds = calendar.GetSessionBounds(boundary.TradingDate);
        bool opening = request.Kind == BrokerRiskContinuationKind.Opening;
        var expectedAt = opening ? bounds.OpenUtc : bounds.CloseUtc;
        if (boundary.EvidenceId == Guid.Empty || boundary.BindingId != state.BindingId || boundary.At != expectedAt ||
            boundary.ObservedAt < boundary.At || boundary.ObservedAt > now || boundary.Value <= 0m ||
            !CompleteTradingStrategy.IsHash(boundary.SourceFingerprint) ||
            boundary.Kind != (opening ? BrokerRiskBoundaryKind.Opening : BrokerRiskBoundaryKind.Closing) ||
            !SupportedContract(boundary))
            throw new InvalidOperationException("A complete, owned, strategy-defined session valuation is required.");

        if (opening && boundary.TradingDate == state.TradingDate)
        {
            // The saved first reference wins, including an explicitly labelled midday setup reference.
            // An exact retry is handled by request recovery; a later revision cannot repaint the open.
            throw new InvalidOperationException("This session already has a saved risk reference. Reuse it without resetting.");
        }
        if (opening && boundary.TradingDate != calendar.GetNextSession(state.TradingDate))
            throw new InvalidOperationException("Continue missing sessions in calendar order; a restart cannot erase their closing history.");
        if (!opening && (boundary.TradingDate != state.TradingDate || state.LastCompletedSession == state.TradingDate))
            throw new InvalidOperationException("Only the current, not-yet-completed risk session can close.");
        if (opening && calendar.IsRegularSession(state.TradingDate) &&
            state.Through <= calendar.GetSessionBounds(state.TradingDate).CloseUtc && state.LastCompletedSession != state.TradingDate)
            throw new InvalidOperationException("Recover the preceding session's closing evidence before advancing its risk history.");
        if (boundary.At < state.Through)
            throw new InvalidOperationException("A historical boundary cannot replace newer account risk evidence.");
        var flows = request.NoFlows!;
        if (flows.EvidenceId == Guid.Empty || flows.BindingId != state.BindingId || flows.From != state.Through ||
            flows.Through != boundary.At || flows.ObservedAt < flows.Through || flows.ObservedAt > now ||
            !CompleteTradingStrategy.IsHash(flows.SourceFingerprint))
            throw new InvalidOperationException("Reconcile all external cash movements across the valuation interval first.");

        if (opening)
            return state with { RevisionId = request.RequestId, TradingDate = boundary.TradingDate,
                SessionReference = Reference(boundary), DailyLossHold = false, Through = boundary.At };
        return state with { RevisionId = request.RequestId, LastCompletedSession = boundary.TradingDate,
            DrawdownReference = boundary.Value > state.DrawdownReference.Value ? Reference(boundary) : state.DrawdownReference,
            Through = boundary.At };
    }

    internal static void Validate(BrokerRiskContinuationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty || request.BaselineId == Guid.Empty ||
            request.PreviousRevisionId == Guid.Empty || request.PreviousRevisionId == request.RequestId ||
            !Enum.IsDefined(request.Kind) ||
            (request.Kind switch
            {
                BrokerRiskContinuationKind.HoldObservation => request.Holds is null || request.Boundary is not null ||
                    request.NoFlows is not null || request.CashFlows is not null,
                BrokerRiskContinuationKind.CashFlows => request.CashFlows is null || request.Holds is not null ||
                    request.Boundary is not null || request.NoFlows is not null,
                _ => request.Holds is not null || request.CashFlows is not null || request.Boundary is null || request.NoFlows is null
            }))
            throw new ArgumentException("Risk continuation needs an immutable request and exactly one supported event.");
    }

    private static bool SupportedContract(BrokerRiskBoundaryValue boundary) => boundary.Kind == BrokerRiskBoundaryKind.Opening
        ? boundary.ValuationContract is BrokerAccountSessionValuation.ShadowOpening or BrokerAccountSessionValuation.LiveOpening
        : boundary.ValuationContract is BrokerAccountSessionValuation.ShadowClosing or BrokerAccountSessionValuation.SessionClosing;

    private static BrokerRiskReference Reference(BrokerRiskBoundaryValue value) => new(value.Value,
        value.Kind == BrokerRiskBoundaryKind.Opening ? BrokerRiskReferenceSource.VerifiedSessionOpening : BrokerRiskReferenceSource.VerifiedSessionClosing,
        value.EvidenceId, value.At);
}
