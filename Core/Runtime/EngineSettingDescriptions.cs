#nullable enable
using System;

namespace Core.Runtime;

public static class EngineSettingDescriptions
{
    public static string Section(string key) => key switch
    {
        "SelectedRawMoveThreshold" or "SelectedExcessMoveThreshold" or "SelectedRulerSessions" or "DirectionalVolumeThreshold" or "StructureBufferUnits" or "MinimumStructureReferences" => "Entry",
        "InitialAllocationFraction" or "AddOnAllocationFraction" or "MaximumHoldings" or "EntryTargetNavFraction" or "MaximumSameSessionEntriesPerSymbol" or "MaximumSameDayEntriesPerSymbol" => "Sizing and entries",
        "DailyLossGuardFraction" or "CapitalReviewDrawdownFraction" or "DrawdownReviewFraction" => "Account risk",
        _ => "Exit"
    };
    public static string Help(string key) => key switch
    {
        "SelectedRawMoveThreshold" => "Minimum stock movement needed for entry, in volatility-ruler units (0.15, 0.25 or 0.35); higher requires a stronger move.",
        "SelectedExcessMoveThreshold" => "Minimum movement above the benchmark in relative volatility-ruler units (0.025, 0.05 or 0.10); higher demands more outperformance.",
        "SelectedRulerSessions" => "Number of sessions used to measure typical movement (10 or 14); this sets the scale for movement thresholds.",
        "DirectionalVolumeThreshold" => "Directional-volume balance needed to confirm an up or down price move; 0.10 means a 0.10 balance on the −1 to +1 scale. Higher widens the neutral range.",
        "StructureBufferUnits" => "Distance beyond reference structure required for confirmation, measured in volatility-ruler units; higher adds more buffer.",
        "MinimumStructureReferences" => "Minimum number of usable price-structure references required; a higher whole number demands more supporting evidence.",
        "HardLossFraction" => "Exit when the holding loses this fraction of its entry cost; 0.05 means 5%. Lower values trigger protection sooner.",
        "FastDownsideReturnFloor" => "Short-window return at or below this negative fraction triggers downside protection; values closer to zero trigger sooner.",
        "ProfitFloorActivationGainFraction" => "Gain above entry needed to arm the protected-profit floor; 0.05 means 5%. Lower arms it earlier.",
        "TrailingActivationGainFraction" => "Gain above entry needed to start trailing protection; lower values start the trail earlier.",
        "TrailingDistanceFraction" or "TrailingLossFraction" => "Allowed decline from the observed high once trailing protection is active; 0.05 means 5%. Lower keeps the trail tighter.",
        "MaximumHoldings" => "Maximum simultaneous holdings in this portfolio; a lower whole number concentrates capital in fewer positions.",
        "EntryTargetNavFraction" => "Target fraction of current portfolio value for a new holding, subject to available cash; 0.25 means 25%.",
        "MaximumSameSessionEntriesPerSymbol" or "MaximumSameDayEntriesPerSymbol" => "Maximum entries into the same symbol in one session, including re-entry; use a whole number.",
        "DailyLossGuardFraction" => "Session loss fraction that pauses new buys; 0.03 means 3%. Existing exits continue and changing rules does not erase the hold.",
        "CapitalReviewDrawdownFraction" or "DrawdownReviewFraction" => "Decline from the account high that requires an explicit capital review before new buys can resume; 0.10 means 10%.",
        "InitialAllocationFraction" => "Fraction of a position's target budget allocated to its initial purchase; 0.75 means 75%, not 75% of the whole account.",
        "AddOnAllocationFraction" => "Fraction of the position target reserved for an eligible add-on purchase; 0.25 means 25%.",
        "EntryFrictionRate" => "Simulated entry cost added to the observed buy price; 0.0025 means 0.25% and reduces shares affordable.",
        "ExitFrictionRate" => "Simulated exit cost deducted from the observed sell price; 0.0025 means 0.25% and reduces sale proceeds.",
        "EntryCostRate" => "Assumed entry cost used to calculate the tracked policy's break-even protection; 0.0025 means 0.25%. It does not rewrite actual entry fills.",
        "ExitCostRate" => "Assumed exit cost used to calculate break-even protection; 0.0025 means 0.25%. It does not change manually reported broker fills.",
        "ConditionalLossFraction" => "Loss from entry that normally triggers an exit; only qualifying fresh breakout evidence can permit the documented exception.",
        "AbsoluteLossFraction" => "Absolute loss from entry that triggers exit even when the strong-breakout exception qualifies; 0.20 means 20%.",
        "OrdinaryMaximumSessions" => "Normal maximum holding period in trading sessions before the time exit applies; qualifying breakout evidence can extend it.",
        "AbsoluteMaximumSessions" => "Maximum holding period in trading sessions even under the strong-breakout exception.",
        "StrongBreakoutProbability" => "Minimum breakout-model probability for the tracked-position holding exception; 0.60 means 60%.",
        "StrongDirectionEdge" => "Minimum up probability minus down probability for the holding exception; 0.10 means a ten-percentage-point edge.",
        "MaximumStrongDownProbability" => "Maximum down-model probability permitted for the holding exception; lower values require less downside risk.",
        _ => throw new ArgumentException($"Missing behavioral description for {key}.")
    };
}
