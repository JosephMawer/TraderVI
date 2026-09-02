#nullable enable

using Core.TMX.Models.Dto;
using System;
using System.Collections.Generic;

namespace Core.Indicators.Granville;

/// <summary>
/// Pure policy for attaching an undated movers response to leadership data.
/// An observation may be used only for the current local market date and only
/// when the provider returns the complete requested basket.
/// </summary>
public static class LeadershipAcquisitionPolicy
{
    public const int RequiredMoverBasketSize = 50;

    /// <summary>
    /// Plans the first high/low date to recompute. Only an incomplete row for
    /// the current local market date is eligible for repair.
    /// </summary>
    public static LeadershipAcquisitionPlan CreatePlan(
        DateTime localMarketDate,
        DateTime? latestStoredDate,
        bool latestHasActiveBreadth,
        DateTime initialComputeFrom)
    {
        DateTime marketDate = localMarketDate.Date;
        DateTime? latestDate = latestStoredDate?.Date;
        bool isCurrentDateRetry = latestDate == marketDate && !latestHasActiveBreadth;

        DateTime computeFrom = isCurrentDateRetry
            ? marketDate
            : latestDate?.AddDays(1) ?? initialComputeFrom.Date;

        return new LeadershipAcquisitionPlan(computeFrom, isCurrentDateRetry);
    }

    /// <summary>
    /// Selects the sole date allowed to receive a live, undated response.
    /// The current date must exist in both the computed leadership rows and the
    /// dated XIU bars; prior dates are never inferred from response timing.
    /// </summary>
    public static DateTime? SelectLiveTargetDate(
        DateTime localMarketDate,
        IEnumerable<DateTime> computedDates,
        DateTime? xiuAnchorDate)
    {
        ArgumentNullException.ThrowIfNull(computedDates);

        DateTime marketDate = localMarketDate.Date;
        if (xiuAnchorDate?.Date != marketDate)
            return null;

        foreach (DateTime computedDate in computedDates)
        {
            if (computedDate.Date == marketDate)
                return marketDate;
        }

        return null;
    }

    /// <summary>
    /// Validates a movers payload and derives advancing/declining counts.
    /// Partial, anonymous, duplicate, null, or directionless rows make the
    /// entire basket unavailable rather than silently changing its meaning.
    /// </summary>
    public static ActiveMoverBasketEvaluation EvaluateMoverBasket(
        IReadOnlyCollection<TmxMarketMoverDto?>? movers,
        int requestedSize = RequiredMoverBasketSize)
    {
        if (requestedSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedSize), requestedSize, "Basket size must be positive.");

        if (movers is null)
            return ActiveMoverBasketEvaluation.Invalid("Market movers payload was unavailable.");

        if (movers.Count != requestedSize)
        {
            return ActiveMoverBasketEvaluation.Invalid(
                $"Expected exactly {requestedSize} movers but received {movers.Count}.");
        }

        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int advancers = 0;
        int decliners = 0;

        foreach (TmxMarketMoverDto? mover in movers)
        {
            if (mover is null)
                return ActiveMoverBasketEvaluation.Invalid("Market movers payload contained a null row.");

            string symbol = mover.symbol?.Trim() ?? string.Empty;
            if (symbol.Length == 0)
                return ActiveMoverBasketEvaluation.Invalid("Market movers payload contained a blank symbol.");

            if (!symbols.Add(symbol))
            {
                return ActiveMoverBasketEvaluation.Invalid(
                    $"Market movers payload contained duplicate symbol '{symbol}'.");
            }

            if (!mover.priceChange.HasValue)
            {
                return ActiveMoverBasketEvaluation.Invalid(
                    $"Market movers payload omitted priceChange for '{symbol}'.");
            }

            if (mover.priceChange.Value > 0m)
                advancers++;
            else if (mover.priceChange.Value < 0m)
                decliners++;
        }

        return ActiveMoverBasketEvaluation.Valid(
            new ActiveMoverBreadthObservation(advancers, decliners, requestedSize));
    }
}

public sealed record LeadershipAcquisitionPlan(
    DateTime ComputeFrom,
    bool IsCurrentDateRetry);

public sealed record ActiveMoverBreadthObservation(
    int Advancers,
    int Decliners,
    int BasketSize)
{
    public int Unchanged => BasketSize - Advancers - Decliners;
}

public sealed record ActiveMoverBasketEvaluation(
    ActiveMoverBreadthObservation? Observation,
    string Reason)
{
    public bool IsValid => Observation is not null;

    internal static ActiveMoverBasketEvaluation Valid(ActiveMoverBreadthObservation observation) =>
        new(observation, string.Empty);

    internal static ActiveMoverBasketEvaluation Invalid(string reason) =>
        new(null, reason);
}
