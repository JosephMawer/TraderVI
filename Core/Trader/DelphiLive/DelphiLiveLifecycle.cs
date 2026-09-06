#nullable enable

using System;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveDataConfidenceState
{
    Normal,
    Ambiguous,
    Degraded,
    MonitoringLost
}

public sealed record DelphiLiveDataConfidence(
    DelphiLiveDataConfidenceState State,
    int ConsecutiveMisses)
{
    public static DelphiLiveDataConfidence Normal { get; } =
        new(DelphiLiveDataConfidenceState.Normal, 0);

    public bool AllowsNewRisk => State == DelphiLiveDataConfidenceState.Normal;
}

public static class DelphiLiveDataConfidencePolicy
{
    public static DelphiLiveDataConfidence Advance(
        DelphiLiveDataConfidence current,
        bool isMarketObservationMiss,
        bool exactStockAndXiuBarsPersistedOnTime)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.ConsecutiveMisses < 0)
            throw new ArgumentOutOfRangeException(nameof(current));
        if (isMarketObservationMiss && exactStockAndXiuBarsPersistedOnTime)
        {
            throw new ArgumentException(
                "One cycle cannot be both a market-observation miss and a clean exact pair.");
        }

        if (exactStockAndXiuBarsPersistedOnTime)
            return DelphiLiveDataConfidence.Normal;

        // Legitimate immaturity, optional-diagnostic absence, quote failure, and a
        // missing daily run are neither a clean observation nor a ladder miss.
        if (!isMarketObservationMiss)
            return current;

        int misses = checked(current.ConsecutiveMisses + 1);
        DelphiLiveDataConfidenceState state = misses switch
        {
            1 => DelphiLiveDataConfidenceState.Ambiguous,
            2 => DelphiLiveDataConfidenceState.Degraded,
            _ => DelphiLiveDataConfidenceState.MonitoringLost
        };
        return new DelphiLiveDataConfidence(state, misses);
    }
}

public enum DelphiLiveRecommendationState
{
    WarmingUp,
    Watching,
    Emerging,
    EntryEligible,
    BuyPending,
    Held,
    ExitPending,
    Dismissed
}

public enum DelphiLivePresentationActivity
{
    Active,
    Quiet
}

public static class DelphiLiveLifecycleReasons
{
    public const string WarmingUp = "WarmingUp";
    public const string Watching = "Watching";
    public const string StrongConfirmationStarted = "StrongConfirmationStarted";
    public const string StrongConfirmationCompleted = "StrongConfirmationCompleted";
    public const string StrongConfirmationBroken = "StrongConfirmationBroken";
    public const string WeakeningConfirmationStarted = "WeakeningConfirmationStarted";
    public const string DismissedWeakeningConfirmed = "DismissedWeakeningConfirmed";
    public const string DismissedRecoveryStarted = "DismissedRecoveryStarted";
    public const string DismissedRecoveryCompleted = "DismissedRecoveryCompleted";
    public const string ObservationInvalid = "ObservationInvalid";
    public const string DataConfidenceNotNormal = "DataConfidenceNotNormal";
    public const string SafetyVetoActive = "SafetyVetoActive";
    public const string BuyPending = "BuyPending";
    public const string Held = "Held";
    public const string ExitPending = "ExitPending";
    public const string PositionClosedFreshEvidenceRequired = "PositionClosedFreshEvidenceRequired";
}

public sealed record DelphiLiveLifecycleSnapshot(
    DelphiLiveRecommendationState State,
    int ConsecutiveStrongObservations,
    int ConsecutiveStrongWeakeningObservations,
    DateTime? LastScheduledBarEndUtc,
    string ReasonCode)
{
    public static DelphiLiveLifecycleSnapshot NewSession(bool isCurrentSessionCandidate) =>
        new(
            isCurrentSessionCandidate
                ? DelphiLiveRecommendationState.WarmingUp
                : DelphiLiveRecommendationState.Watching,
            0,
            0,
            null,
            isCurrentSessionCandidate
                ? DelphiLiveLifecycleReasons.WarmingUp
                : DelphiLiveLifecycleReasons.Watching);
}

public sealed record DelphiLiveLifecycleInput(
    DateTime ScheduledBarEndUtc,
    bool FamiliesMature,
    bool ObservationIsValid,
    DelphiLiveDataConfidence DataConfidence,
    DelphiLiveMomentumJudgment Momentum,
    bool SafetyVetoActive,
    bool IsHeld,
    bool HasPendingBuy,
    bool HasPendingSell,
    bool IsCurrentSessionCandidate);

public sealed record DelphiLiveLifecycleDecision(
    DelphiLiveLifecycleSnapshot Snapshot,
    DelphiLivePresentationActivity PresentationActivity,
    bool HasProtectiveCollectionPriority,
    bool MayCreateBuyDecision);

public enum DelphiLivePendingBuyEndReason
{
    BuyQuoteUnavailableExpired,
    BuyCancelledSignal,
    BuyCancelledSafety,
    BuyCancelledDataConfidence,
    BuyCancelledPortfolio,
    BuyCutoffExpired,
    BuyRestartExpired
}

public static class DelphiLiveLifecyclePolicy
{
    private static readonly TimeSpan ScheduledInterval = TimeSpan.FromMinutes(5);

    public static DelphiLiveLifecycleDecision Advance(
        DelphiLiveLifecycleSnapshot current,
        DelphiLiveLifecycleInput input)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.DataConfidence);
        ArgumentNullException.ThrowIfNull(input.Momentum);
        RequireUtc(input.ScheduledBarEndUtc, nameof(input.ScheduledBarEndUtc));
        ValidateSnapshot(current);

        bool immediatelyConsecutive =
            current.LastScheduledBarEndUtc is DateTime prior &&
            input.ScheduledBarEndUtc - prior == ScheduledInterval;

        int strong = immediatelyConsecutive
            ? current.ConsecutiveStrongObservations
            : 0;
        int weakening = immediatelyConsecutive
            ? current.ConsecutiveStrongWeakeningObservations
            : 0;
        DelphiLiveRecommendationState nextState;
        string reason;

        bool ordinaryEvidencePermitted =
            input.FamiliesMature &&
            input.ObservationIsValid &&
            input.DataConfidence.State == DelphiLiveDataConfidenceState.Normal &&
            !input.SafetyVetoActive;

        if (!input.FamiliesMature)
        {
            strong = 0;
            weakening = 0;
            nextState = current.State == DelphiLiveRecommendationState.Dismissed
                ? DelphiLiveRecommendationState.Dismissed
                : input.IsCurrentSessionCandidate
                    ? DelphiLiveRecommendationState.WarmingUp
                    : DelphiLiveRecommendationState.Watching;
            reason = nextState == DelphiLiveRecommendationState.WarmingUp
                ? DelphiLiveLifecycleReasons.WarmingUp
                : DelphiLiveLifecycleReasons.Watching;
        }
        else if (!ordinaryEvidencePermitted)
        {
            strong = 0;
            weakening = 0;
            nextState = current.State == DelphiLiveRecommendationState.Dismissed
                ? DelphiLiveRecommendationState.Dismissed
                : DelphiLiveRecommendationState.Watching;
            reason = !input.ObservationIsValid
                ? DelphiLiveLifecycleReasons.ObservationInvalid
                : input.DataConfidence.State != DelphiLiveDataConfidenceState.Normal
                    ? DelphiLiveLifecycleReasons.DataConfidenceNotNormal
                    : DelphiLiveLifecycleReasons.SafetyVetoActive;
        }
        else if (input.Momentum.IsEntryEligibleStrong)
        {
            weakening = 0;
            bool completes = immediatelyConsecutive && strong >= 1;
            strong = completes ? 2 : 1;
            if (current.State == DelphiLiveRecommendationState.Dismissed)
            {
                nextState = completes
                    ? DelphiLiveRecommendationState.EntryEligible
                    : DelphiLiveRecommendationState.Dismissed;
                reason = completes
                    ? DelphiLiveLifecycleReasons.DismissedRecoveryCompleted
                    : DelphiLiveLifecycleReasons.DismissedRecoveryStarted;
            }
            else
            {
                nextState = completes
                    ? DelphiLiveRecommendationState.EntryEligible
                    : DelphiLiveRecommendationState.Emerging;
                reason = completes
                    ? DelphiLiveLifecycleReasons.StrongConfirmationCompleted
                    : DelphiLiveLifecycleReasons.StrongConfirmationStarted;
            }
        }
        else if (input.Momentum.IsStrongWeakening)
        {
            strong = 0;
            bool confirms = immediatelyConsecutive && weakening >= 1;
            weakening = confirms ? 2 : 1;
            nextState = current.State == DelphiLiveRecommendationState.Dismissed || confirms
                ? DelphiLiveRecommendationState.Dismissed
                : DelphiLiveRecommendationState.Watching;
            reason = confirms
                ? DelphiLiveLifecycleReasons.DismissedWeakeningConfirmed
                : DelphiLiveLifecycleReasons.WeakeningConfirmationStarted;
        }
        else
        {
            bool confirmationWasActive = strong > 0 ||
                current.State is DelphiLiveRecommendationState.Emerging or
                    DelphiLiveRecommendationState.EntryEligible;
            strong = 0;
            weakening = 0;
            nextState = current.State == DelphiLiveRecommendationState.Dismissed
                ? DelphiLiveRecommendationState.Dismissed
                : DelphiLiveRecommendationState.Watching;
            reason = confirmationWasActive
                ? DelphiLiveLifecycleReasons.StrongConfirmationBroken
                : DelphiLiveLifecycleReasons.Watching;
        }

        if (input.HasPendingSell)
        {
            nextState = DelphiLiveRecommendationState.ExitPending;
            reason = DelphiLiveLifecycleReasons.ExitPending;
        }
        else if (input.IsHeld)
        {
            nextState = DelphiLiveRecommendationState.Held;
            reason = DelphiLiveLifecycleReasons.Held;
        }
        else if (input.HasPendingBuy)
        {
            nextState = DelphiLiveRecommendationState.BuyPending;
            reason = DelphiLiveLifecycleReasons.BuyPending;
        }

        var snapshot = new DelphiLiveLifecycleSnapshot(
            nextState,
            strong,
            weakening,
            input.ScheduledBarEndUtc,
            reason);
        return Describe(snapshot, input);
    }

    public static DelphiLiveLifecycleSnapshot ApplyPendingBuyEnd(
        DelphiLiveLifecycleSnapshot current,
        DelphiLivePendingBuyEndReason reason)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateSnapshot(current);
        if (current.State != DelphiLiveRecommendationState.BuyPending)
            throw new InvalidOperationException("Only a pending Buy can end through this transition.");

        return reason switch
        {
            DelphiLivePendingBuyEndReason.BuyQuoteUnavailableExpired or
            DelphiLivePendingBuyEndReason.BuyCancelledPortfolio or
            DelphiLivePendingBuyEndReason.BuyCutoffExpired => current with
            {
                State = DelphiLiveRecommendationState.EntryEligible,
                ReasonCode = reason.ToString()
            },
            DelphiLivePendingBuyEndReason.BuyRestartExpired => current with
            {
                State = DelphiLiveRecommendationState.WarmingUp,
                ConsecutiveStrongObservations = 0,
                ConsecutiveStrongWeakeningObservations = 0,
                LastScheduledBarEndUtc = null,
                ReasonCode = reason.ToString()
            },
            _ => current with
            {
                State = DelphiLiveRecommendationState.Watching,
                ConsecutiveStrongObservations = 0,
                ConsecutiveStrongWeakeningObservations = 0,
                ReasonCode = reason.ToString()
            }
        };
    }

    public static DelphiLiveLifecycleSnapshot AfterCompletedExit(
        DelphiLiveLifecycleSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateSnapshot(current);
        return current with
        {
            State = DelphiLiveRecommendationState.Watching,
            ConsecutiveStrongObservations = 0,
            ConsecutiveStrongWeakeningObservations = 0,
            ReasonCode = DelphiLiveLifecycleReasons.PositionClosedFreshEvidenceRequired
        };
    }

    public static DelphiLiveLifecycleSnapshot AfterProcessContinuityGap(
        DelphiLiveLifecycleSnapshot current,
        bool isCurrentSessionCandidate)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateSnapshot(current);
        DelphiLiveRecommendationState state = current.State switch
        {
            DelphiLiveRecommendationState.ExitPending => DelphiLiveRecommendationState.ExitPending,
            DelphiLiveRecommendationState.Held => DelphiLiveRecommendationState.Held,
            DelphiLiveRecommendationState.Dismissed => DelphiLiveRecommendationState.Dismissed,
            _ => isCurrentSessionCandidate
                ? DelphiLiveRecommendationState.WarmingUp
                : DelphiLiveRecommendationState.Watching
        };
        return current with
        {
            State = state,
            ConsecutiveStrongObservations = 0,
            ConsecutiveStrongWeakeningObservations = 0,
            LastScheduledBarEndUtc = null,
            ReasonCode = state.ToString()
        };
    }

    private static DelphiLiveLifecycleDecision Describe(
        DelphiLiveLifecycleSnapshot snapshot,
        DelphiLiveLifecycleInput input)
    {
        bool protective = input.IsHeld || input.HasPendingSell;
        bool active = !protective && snapshot.State != DelphiLiveRecommendationState.Dismissed &&
            (snapshot.State is DelphiLiveRecommendationState.WarmingUp or
                DelphiLiveRecommendationState.Emerging or
                DelphiLiveRecommendationState.EntryEligible or
                DelphiLiveRecommendationState.BuyPending ||
             input.Momentum.State is DelphiLiveMomentumState.Strong or
                DelphiLiveMomentumState.StrongWithConflict or
                DelphiLiveMomentumState.PositiveNudge or
                DelphiLiveMomentumState.PositiveNudgeWithConflict);
        bool mayBuy = snapshot.State == DelphiLiveRecommendationState.EntryEligible &&
            input.DataConfidence.AllowsNewRisk &&
            !input.SafetyVetoActive &&
            !protective;
        return new(
            snapshot,
            active ? DelphiLivePresentationActivity.Active : DelphiLivePresentationActivity.Quiet,
            protective,
            mayBuy);
    }

    private static void ValidateSnapshot(DelphiLiveLifecycleSnapshot snapshot)
    {
        if (snapshot.ConsecutiveStrongObservations is < 0 or > 2 ||
            snapshot.ConsecutiveStrongWeakeningObservations is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        if (snapshot.LastScheduledBarEndUtc is DateTime value)
            RequireUtc(value, nameof(snapshot.LastScheduledBarEndUtc));
        if (string.IsNullOrWhiteSpace(snapshot.ReasonCode))
            throw new ArgumentException("Lifecycle reason is required.", nameof(snapshot));
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
