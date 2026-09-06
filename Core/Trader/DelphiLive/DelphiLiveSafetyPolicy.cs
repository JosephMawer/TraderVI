#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveProfitProtectionStage
{
    None,
    BreakEven,
    Trailing
}

public sealed record DelphiLiveProfitProtectionState(
    Guid PositionId,
    decimal AveragePurchasePrice,
    decimal? HighestCompletedFiveMinuteClose,
    DelphiLiveProfitProtectionStage Stage,
    decimal? FloorPrice,
    DateTime? LastProcessedBarEndUtc,
    DateTime? FloorPersistedUtc)
{
    public static DelphiLiveProfitProtectionState Open(
        Guid positionId,
        decimal averagePurchasePrice)
    {
        if (positionId == Guid.Empty)
            throw new ArgumentException("Position identity is required.", nameof(positionId));
        RequirePrice(averagePurchasePrice, nameof(averagePurchasePrice));
        return new(
            positionId,
            averagePurchasePrice,
            null,
            DelphiLiveProfitProtectionStage.None,
            null,
            null,
            null);
    }

    private static void RequirePrice(decimal value, string parameterName)
    {
        if (value <= 0m)
            throw new ArgumentOutOfRangeException(parameterName, "Price must be positive.");
    }
}

public sealed record DelphiLiveProtectionUpdate(
    DelphiLiveProfitProtectionState State,
    bool FloorChanged,
    decimal? PriorFloor,
    decimal? NewFloor);

public sealed record DelphiLiveSafetyInput(
    bool IsHeld,
    bool IsWarmingUp,
    decimal? AveragePurchasePrice,
    decimal? CurrentBid,
    DateTime? CurrentBidReceivedUtc,
    decimal? CompletedBarOpen,
    decimal? CompletedBarClose,
    bool SessionVwapReferenceAvailable,
    bool CloseBelowBufferedSessionVwap,
    bool PriorRangeReferenceAvailable,
    bool CloseBelowBufferedPriorTwentyMinuteLow,
    DelphiLiveFamilyJudgment VolumeSupport,
    DelphiLiveMomentumJudgment Momentum,
    bool PreviousValidMomentumWasStrongWeakening,
    DelphiLiveProfitProtectionState? ProfitProtection);

public sealed record DelphiLiveSafetyEvaluation(
    bool EntrySafetyVetoActive,
    DelphiLiveExitRule? PrimaryExitRule,
    IReadOnlyList<DelphiLiveExitRule> FiredExitRules,
    string? LiveWeakeningDetail,
    string? WarmupPhaseReason)
{
    public bool RequiresProtectiveSell => PrimaryExitRule.HasValue;
}

public static class DelphiLiveSafetyReasons
{
    public const string BroadImmediateWeakening = "BroadImmediateWeakening";
    public const string PersistentWeakeningConfirmed = "PersistentWeakeningConfirmed";
    public const string WarmupHardLoss5Pct = "WarmupHardLoss5Pct";
    public const string WarmupProfitFloorBreach = "WarmupProfitFloorBreach";
}

public static class DelphiLiveSafetyPolicy
{
    public static DelphiLiveProtectionUpdate ApplyCompletedClose(
        DelphiLiveProfitProtectionState current,
        DateTime barEndUtc,
        decimal completedClose,
        DateTime persistedUtc,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(policy);
        DelphiLivePolicyValidator.Validate(policy);
        RequireUtc(barEndUtc, nameof(barEndUtc));
        RequireUtc(persistedUtc, nameof(persistedUtc));
        RequirePrice(completedClose, nameof(completedClose));
        if (persistedUtc < barEndUtc)
            throw new ArgumentException("A completed close cannot be persisted before its bar ends.");
        ValidateProtectionState(current);

        if (current.LastProcessedBarEndUtc is DateTime prior && barEndUtc <= prior)
            return new(current, false, current.FloorPrice, current.FloorPrice);

        decimal high = current.HighestCompletedFiveMinuteClose.HasValue
            ? System.Math.Max(current.HighestCompletedFiveMinuteClose.Value, completedClose)
            : completedClose;
        DelphiLiveProfitProtectionStage stage = current.Stage;
        decimal? floor = current.FloorPrice;

        if (stage == DelphiLiveProfitProtectionStage.Trailing ||
            completedClose >= current.AveragePurchasePrice *
                (1m + policy.TrailingActivationGainFraction))
        {
            stage = DelphiLiveProfitProtectionStage.Trailing;
            decimal candidate = high * (1m - policy.TrailingDistanceFraction);
            floor = floor.HasValue
                ? System.Math.Max(floor.Value, candidate)
                : System.Math.Max(current.AveragePurchasePrice, candidate);
        }
        else if (stage == DelphiLiveProfitProtectionStage.BreakEven ||
                 completedClose >= current.AveragePurchasePrice *
                    (1m + policy.ProfitFloorActivationGainFraction))
        {
            stage = DelphiLiveProfitProtectionStage.BreakEven;
            floor = floor.HasValue
                ? System.Math.Max(floor.Value, current.AveragePurchasePrice)
                : current.AveragePurchasePrice;
        }

        floor = floor.HasValue
            ? decimal.Round(floor.Value, 6, MidpointRounding.AwayFromZero)
            : null;
        bool changed = stage != current.Stage || floor != current.FloorPrice;
        DelphiLiveProfitProtectionState next = current with
        {
            HighestCompletedFiveMinuteClose = high,
            Stage = stage,
            FloorPrice = floor,
            LastProcessedBarEndUtc = barEndUtc,
            FloorPersistedUtc = changed ? persistedUtc : current.FloorPersistedUtc
        };
        return new(next, changed, current.FloorPrice, floor);
    }

    public static DelphiLiveSafetyEvaluation Evaluate(
        DelphiLiveSafetyInput input,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(input.VolumeSupport);
        ArgumentNullException.ThrowIfNull(input.Momentum);
        DelphiLivePolicyValidator.Validate(policy);
        ValidateSafetyInput(input);

        bool fastDownside = HasFastDownside(input, policy);
        bool supportFailure = !input.IsWarmingUp &&
            input.SessionVwapReferenceAvailable &&
            input.CloseBelowBufferedSessionVwap &&
            input.PriorRangeReferenceAvailable &&
            input.CloseBelowBufferedPriorTwentyMinuteLow &&
            input.VolumeSupport.Family == DelphiLiveSignalFamily.VolumeSupport &&
            input.VolumeSupport.State == DelphiLiveFamilyState.Weakening;

        bool hardLoss = input.IsHeld &&
            input.AveragePurchasePrice is decimal average &&
            input.CurrentBid is decimal bid &&
            bid <= average * (1m - policy.HardLossFraction);
        bool profitFloorBreach = input.IsHeld &&
            input.ProfitProtection?.FloorPrice is decimal floor &&
            input.ProfitProtection.FloorPersistedUtc is DateTime floorPersisted &&
            input.CurrentBid is decimal protectionBid &&
            input.CurrentBidReceivedUtc is DateTime bidReceived &&
            bidReceived > floorPersisted &&
            protectionBid <= floor;
        bool liveWeakening = input.IsHeld && !input.IsWarmingUp &&
            (input.Momentum.State == DelphiLiveMomentumState.VeryWeak ||
             input.PreviousValidMomentumWasStrongWeakening &&
             input.Momentum.IsStrongWeakening);

        var fired = new List<DelphiLiveExitRule>(5);
        if (input.IsHeld && hardLoss)
            fired.Add(DelphiLiveExitRule.HardLoss5Pct);
        if (input.IsHeld && fastDownside)
            fired.Add(DelphiLiveExitRule.FastDownside10Pct);
        if (input.IsHeld && profitFloorBreach)
            fired.Add(DelphiLiveExitRule.ProfitProtectionFloorBreach);
        if (input.IsHeld && supportFailure)
            fired.Add(DelphiLiveExitRule.ConfirmedSupportFailure);
        if (liveWeakening)
            fired.Add(DelphiLiveExitRule.LiveWeakeningExit);

        DelphiLiveExitRule? primary = policy.PrimaryExitReasonOrder
            .Cast<DelphiLiveExitRule?>()
            .FirstOrDefault(candidate => candidate.HasValue && fired.Contains(candidate.Value));
        string? weakeningDetail = liveWeakening
            ? input.Momentum.State == DelphiLiveMomentumState.VeryWeak
                ? DelphiLiveSafetyReasons.BroadImmediateWeakening
                : DelphiLiveSafetyReasons.PersistentWeakeningConfirmed
            : null;
        string? warmup = input.IsWarmingUp
            ? hardLoss
                ? DelphiLiveSafetyReasons.WarmupHardLoss5Pct
                : profitFloorBreach
                    ? DelphiLiveSafetyReasons.WarmupProfitFloorBreach
                    : null
            : null;

        return new(
            fastDownside || supportFailure || hardLoss || profitFloorBreach,
            primary,
            fired.AsReadOnly(),
            weakeningDetail,
            warmup);
    }

    private static bool HasFastDownside(
        DelphiLiveSafetyInput input,
        DelphiLivePolicyDefinition policy)
    {
        if (!input.CompletedBarOpen.HasValue && !input.CompletedBarClose.HasValue)
            return false;
        if (!input.CompletedBarOpen.HasValue || !input.CompletedBarClose.HasValue)
            throw new ArgumentException("Fast-downside evidence requires both open and close.");
        RequirePrice(input.CompletedBarOpen.Value, nameof(input.CompletedBarOpen));
        RequirePrice(input.CompletedBarClose.Value, nameof(input.CompletedBarClose));
        decimal barReturn = input.CompletedBarClose.Value / input.CompletedBarOpen.Value - 1m;
        return barReturn <= policy.FastDownsideReturnFloor;
    }

    private static void ValidateSafetyInput(DelphiLiveSafetyInput input)
    {
        if (input.IsHeld && input.AveragePurchasePrice is null)
            throw new ArgumentException("A held position requires average purchase price.", nameof(input));
        if (input.AveragePurchasePrice is decimal average)
            RequirePrice(average, nameof(input.AveragePurchasePrice));
        if (input.CurrentBid is decimal bid)
            RequirePrice(bid, nameof(input.CurrentBid));
        if (input.CurrentBidReceivedUtc is DateTime received)
            RequireUtc(received, nameof(input.CurrentBidReceivedUtc));
        if (input.CurrentBid.HasValue != input.CurrentBidReceivedUtc.HasValue)
            throw new ArgumentException("Bid value and receipt time must be supplied together.", nameof(input));
        if (input.ProfitProtection is not null)
        {
            ValidateProtectionState(input.ProfitProtection);
            if (input.IsHeld && input.AveragePurchasePrice != input.ProfitProtection.AveragePurchasePrice)
                throw new ArgumentException("Protection state does not match the held cost basis.", nameof(input));
        }
    }

    private static void ValidateProtectionState(DelphiLiveProfitProtectionState state)
    {
        if (state.PositionId == Guid.Empty)
            throw new ArgumentException("Protection state requires a position identity.", nameof(state));
        RequirePrice(state.AveragePurchasePrice, nameof(state.AveragePurchasePrice));
        if (state.HighestCompletedFiveMinuteClose is decimal high)
            RequirePrice(high, nameof(state.HighestCompletedFiveMinuteClose));
        if (state.FloorPrice is decimal floor)
            RequirePrice(floor, nameof(state.FloorPrice));
        if (state.Stage == DelphiLiveProfitProtectionStage.None && state.FloorPrice.HasValue ||
            state.Stage != DelphiLiveProfitProtectionStage.None && !state.FloorPrice.HasValue ||
            state.FloorPrice.HasValue != state.FloorPersistedUtc.HasValue)
            throw new ArgumentException("Protection stage, floor, and persistence time are inconsistent.", nameof(state));
        if (state.LastProcessedBarEndUtc is DateTime end)
            RequireUtc(end, nameof(state.LastProcessedBarEndUtc));
        if (state.FloorPersistedUtc is DateTime persisted)
            RequireUtc(persisted, nameof(state.FloorPersistedUtc));
    }

    private static void RequirePrice(decimal value, string parameterName)
    {
        if (value <= 0m)
            throw new ArgumentOutOfRangeException(parameterName, "Price must be positive.");
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
