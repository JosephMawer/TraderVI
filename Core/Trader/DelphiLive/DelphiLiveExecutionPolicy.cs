#nullable enable

using System;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveActionSide
{
    Buy,
    Sell
}

public enum DelphiLiveQuoteField
{
    Ask,
    Bid,
    Price
}

public enum DelphiLiveFillConfidence
{
    SideSpecific,
    EstimatedFill
}

public enum DelphiLiveQuoteAttemptDisposition
{
    Filled,
    RetryWithinWindow,
    BuyQuoteUnavailableExpired,
    BuyCutoffExpired,
    SellRemainsPending
}

public sealed record DelphiLiveActionIntent(
    Guid ActionId,
    Guid DecisionId,
    Guid DecisionEvidenceId,
    string Symbol,
    DelphiLiveActionSide Side,
    DateTime DecisionUtc,
    DateTime DecisionPersistedUtc,
    DateTime? BuyCutoffUtc,
    int? RequestedQuantity,
    decimal? BuyBudget);

public sealed record DelphiLiveCausalQuoteObservation(
    Guid QuoteObservationId,
    Guid DecisionId,
    string Symbol,
    int AttemptNumber,
    DateTime RequestStartedUtc,
    DateTime ReceivedUtc,
    decimal? Price,
    decimal? Bid,
    decimal? Ask,
    string SourceContractVersion);

public sealed record DelphiLiveQuoteAttemptDecision(
    DelphiLiveQuoteAttemptDisposition Disposition,
    decimal? FillPrice,
    DelphiLiveQuoteField? SelectedField,
    DelphiLiveFillConfidence? Confidence,
    string ReasonCode)
{
    public bool HasFill => Disposition == DelphiLiveQuoteAttemptDisposition.Filled;
}

public static class DelphiLiveExecutionPolicy
{
    public static DelphiLiveQuoteAttemptDecision EvaluateQuoteAttempt(
        DelphiLiveActionIntent action,
        DelphiLiveCausalQuoteObservation quote,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(policy);
        DelphiLivePolicyValidator.Validate(policy);
        Validate(action, quote, policy);

        bool buy = action.Side == DelphiLiveActionSide.Buy;
        if (buy && action.BuyCutoffUtc is DateTime cutoff && quote.ReceivedUtc >= cutoff)
        {
            return new(
                DelphiLiveQuoteAttemptDisposition.BuyCutoffExpired,
                null,
                null,
                null,
                "BuyCutoffExpired");
        }
        if (buy && quote.ReceivedUtc - action.DecisionPersistedUtc > policy.QuoteAttemptWindow)
        {
            return new(
                DelphiLiveQuoteAttemptDisposition.BuyQuoteUnavailableExpired,
                null,
                null,
                null,
                "BuyQuoteUnavailableExpired");
        }

        decimal? sidePrice = buy ? Positive(quote.Ask) : Positive(quote.Bid);
        if (sidePrice.HasValue)
        {
            return new(
                DelphiLiveQuoteAttemptDisposition.Filled,
                sidePrice,
                buy ? DelphiLiveQuoteField.Ask : DelphiLiveQuoteField.Bid,
                DelphiLiveFillConfidence.SideSpecific,
                buy ? "AskFill" : "BidFill");
        }

        decimal? fallback = Positive(quote.Price);
        if (fallback.HasValue)
        {
            return new(
                DelphiLiveQuoteAttemptDisposition.Filled,
                fallback,
                DelphiLiveQuoteField.Price,
                DelphiLiveFillConfidence.EstimatedFill,
                "EstimatedFill");
        }

        bool exhausted = quote.AttemptNumber >= policy.QuoteAttemptCount ||
            quote.ReceivedUtc - action.DecisionPersistedUtc >= policy.QuoteAttemptWindow;
        if (!exhausted)
        {
            return new(
                DelphiLiveQuoteAttemptDisposition.RetryWithinWindow,
                null,
                null,
                null,
                "QuoteUnavailableRetry");
        }

        return buy
            ? new(
                DelphiLiveQuoteAttemptDisposition.BuyQuoteUnavailableExpired,
                null,
                null,
                null,
                "BuyQuoteUnavailableExpired")
            : new(
                DelphiLiveQuoteAttemptDisposition.SellRemainsPending,
                null,
                null,
                null,
                "ExitQuoteUnavailablePending");
    }

    public static bool MustCancelPendingBuy(
        bool signalStillPermitted,
        bool safetyClear,
        DelphiLiveDataConfidence confidence,
        bool portfolioPermitsRisk,
        DateTime nowUtc,
        DateTime cutoffUtc,
        out DelphiLivePendingBuyEndReason? reason)
    {
        ArgumentNullException.ThrowIfNull(confidence);
        RequireUtc(nowUtc, nameof(nowUtc));
        RequireUtc(cutoffUtc, nameof(cutoffUtc));
        if (!signalStillPermitted)
            reason = DelphiLivePendingBuyEndReason.BuyCancelledSignal;
        else if (!safetyClear)
            reason = DelphiLivePendingBuyEndReason.BuyCancelledSafety;
        else if (!confidence.AllowsNewRisk)
            reason = DelphiLivePendingBuyEndReason.BuyCancelledDataConfidence;
        else if (!portfolioPermitsRisk)
            reason = DelphiLivePendingBuyEndReason.BuyCancelledPortfolio;
        else if (nowUtc >= cutoffUtc)
            reason = DelphiLivePendingBuyEndReason.BuyCutoffExpired;
        else
            reason = null;
        return reason.HasValue;
    }

    private static void Validate(
        DelphiLiveActionIntent action,
        DelphiLiveCausalQuoteObservation quote,
        DelphiLivePolicyDefinition policy)
    {
        if (action.ActionId == Guid.Empty || action.DecisionId == Guid.Empty ||
            action.DecisionEvidenceId == Guid.Empty)
            throw new ArgumentException("Action, decision, and trigger-evidence identities are required.", nameof(action));
        if (!Enum.IsDefined(action.Side))
            throw new ArgumentOutOfRangeException(nameof(action.Side));
        if (quote.QuoteObservationId == Guid.Empty || quote.DecisionId == Guid.Empty)
            throw new ArgumentException("Quote and decision identities are required.", nameof(quote));
        if (quote.QuoteObservationId == action.DecisionEvidenceId)
            throw new ArgumentException("Decision evidence cannot also be reused as fill evidence.", nameof(quote));
        if (quote.DecisionId != action.DecisionId)
            throw new ArgumentException("Quote does not belong to the action decision.", nameof(quote));
        if (string.IsNullOrWhiteSpace(action.Symbol) ||
            !string.Equals(action.Symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Quote symbol does not match the action.", nameof(quote));
        if (string.IsNullOrWhiteSpace(quote.SourceContractVersion) ||
            quote.SourceContractVersion.Length > 64)
            throw new ArgumentException("Quote source-contract version is required.", nameof(quote));
        RequireUtc(action.DecisionUtc, nameof(action.DecisionUtc));
        RequireUtc(action.DecisionPersistedUtc, nameof(action.DecisionPersistedUtc));
        RequireUtc(quote.RequestStartedUtc, nameof(quote.RequestStartedUtc));
        RequireUtc(quote.ReceivedUtc, nameof(quote.ReceivedUtc));
        if (action.DecisionPersistedUtc < action.DecisionUtc)
            throw new ArgumentException("A decision cannot be persisted before it exists.", nameof(action));
        if (quote.RequestStartedUtc < action.DecisionPersistedUtc)
            throw new ArgumentException("A fill quote must be requested after the decision is persisted.", nameof(quote));
        if (quote.ReceivedUtc < quote.RequestStartedUtc)
            throw new ArgumentException("Quote receipt cannot precede its request.", nameof(quote));
        if (quote.ReceivedUtc <= action.DecisionPersistedUtc)
            throw new ArgumentException("Fill evidence must first be received strictly after the decision is persisted.", nameof(quote));
        if (quote.AttemptNumber is < 1)
            throw new ArgumentOutOfRangeException(nameof(quote.AttemptNumber));
        if (action.Side == DelphiLiveActionSide.Buy)
        {
            if (action.BuyCutoffUtc is not DateTime cutoff)
                throw new ArgumentException("A Buy requires its regular-session cutoff.", nameof(action));
            RequireUtc(cutoff, nameof(action.BuyCutoffUtc));
            if (action.RequestedQuantity.HasValue && action.RequestedQuantity.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(action.RequestedQuantity));
            if (action.BuyBudget.HasValue && action.BuyBudget.Value <= 0m)
                throw new ArgumentOutOfRangeException(nameof(action.BuyBudget));
        }
        else if (action.BuyCutoffUtc.HasValue || action.BuyBudget.HasValue)
        {
            throw new ArgumentException("A protective Sell does not carry Buy budget or expiry state.", nameof(action));
        }

        // Later cycles make one new attempt against the same durable Sell. Attempt
        // number may therefore exceed the initial three; Buys never survive that window.
        if (action.Side == DelphiLiveActionSide.Buy && quote.AttemptNumber > policy.QuoteAttemptCount)
            throw new ArgumentOutOfRangeException(nameof(quote.AttemptNumber));
    }

    private static decimal? Positive(decimal? value) => value is > 0m ? value : null;

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
