#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.DelphiLive;

public static class DelphiLivePortfolioReasons
{
    public const string Sized = "Sized";
    public const string PortfolioNavUnavailable = "PortfolioNavUnavailable";
    public const string PortfolioFull = "PortfolioFull";
    public const string ExistingPosition = "ExistingPosition";
    public const string InsufficientCashForOneShare = "InsufficientCashForOneShare";
    public const string DailyBuyingPaused = "DailyBuyingPaused";
    public const string CapitalReviewRequired = "CapitalReviewRequired";
    public const string CapitalChangeUnsupportedV1 = "CapitalChangeUnsupportedV1";
}

public enum DelphiLivePortfolioMarkKind
{
    Opening,
    Checkpoint,
    Closing
}

public sealed record DelphiLivePositionMark(
    Guid PositionId,
    string Symbol,
    int Quantity,
    decimal Price,
    DateTime BarEndUtc);

public sealed record DelphiLiveNavResult(
    bool IsComplete,
    decimal? NetAssetValue,
    IReadOnlyList<string> MissingSymbols,
    string ReasonCode);

public sealed record DelphiLiveBuySizingDecision(
    bool IsAllowed,
    int Quantity,
    decimal TargetNotional,
    decimal RequiredCash,
    string ReasonCode);

public sealed record DelphiLivePortfolioGuardState(
    bool DailyBuyingPaused,
    bool CapitalReviewRequired,
    decimal? DailyReturn,
    decimal? DrawdownFromHighestClosingNav,
    decimal HighestClosingNav);

public sealed record DelphiLivePortfolioActionCandidate(
    Guid ActionId,
    DelphiLiveActionSide Side,
    int LiveRank,
    string Symbol);

public static class DelphiLivePortfolioPolicy
{
    public static DelphiLiveNavResult CalculateExactNav(
        decimal cash,
        IReadOnlyCollection<(Guid PositionId, string Symbol, int Quantity)> positions,
        IReadOnlyCollection<DelphiLivePositionMark> exactMarks,
        DateTime checkpointBarEndUtc)
    {
        if (cash < 0m)
            throw new ArgumentOutOfRangeException(nameof(cash));
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(exactMarks);
        RequireUtc(checkpointBarEndUtc, nameof(checkpointBarEndUtc));

        var marksByPosition = new Dictionary<Guid, DelphiLivePositionMark>();
        foreach (DelphiLivePositionMark mark in exactMarks)
        {
            ValidateMark(mark);
            if (!marksByPosition.TryAdd(mark.PositionId, mark))
                throw new ArgumentException("A position has duplicate checkpoint marks.", nameof(exactMarks));
        }

        decimal nav = cash;
        var missing = new List<string>();
        foreach ((Guid positionId, string symbol, int quantity) in positions)
        {
            if (positionId == Guid.Empty || string.IsNullOrWhiteSpace(symbol) || quantity < 1)
                throw new ArgumentException("Open-position identity, symbol, and quantity are required.", nameof(positions));
            if (!marksByPosition.TryGetValue(positionId, out DelphiLivePositionMark? mark) ||
                mark.BarEndUtc != checkpointBarEndUtc ||
                !string.Equals(mark.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            {
                missing.Add(symbol);
                continue;
            }
            nav += quantity * mark.Price;
        }

        if (missing.Count > 0)
        {
            missing.Sort(StringComparer.OrdinalIgnoreCase);
            return new(
                false,
                null,
                missing.AsReadOnly(),
                DelphiLivePortfolioReasons.PortfolioNavUnavailable);
        }

        return new(true, nav, Array.Empty<string>(), "CompleteExactNav");
    }

    public static DelphiLiveBuySizingDecision SizeWholeShareEntry(
        DelphiLiveNavResult currentNav,
        decimal availableCash,
        decimal fillPrice,
        int openDistinctHoldings,
        bool symbolAlreadyHeld,
        DelphiLivePortfolioGuardState guards,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(currentNav);
        ArgumentNullException.ThrowIfNull(guards);
        ArgumentNullException.ThrowIfNull(policy);
        DelphiLivePolicyValidator.Validate(policy);
        if (availableCash < 0m)
            throw new ArgumentOutOfRangeException(nameof(availableCash));
        RequirePrice(fillPrice, nameof(fillPrice));
        if (openDistinctHoldings < 0)
            throw new ArgumentOutOfRangeException(nameof(openDistinctHoldings));

        if (guards.CapitalReviewRequired)
            return Blocked(DelphiLivePortfolioReasons.CapitalReviewRequired);
        if (guards.DailyBuyingPaused)
            return Blocked(DelphiLivePortfolioReasons.DailyBuyingPaused);
        if (!guards.DailyReturn.HasValue || !currentNav.IsComplete ||
            currentNav.NetAssetValue is not decimal nav || nav <= 0m)
            return Blocked(DelphiLivePortfolioReasons.PortfolioNavUnavailable);
        if (symbolAlreadyHeld)
            return Blocked(DelphiLivePortfolioReasons.ExistingPosition);
        if (openDistinctHoldings >= policy.MaximumHoldings)
            return Blocked(DelphiLivePortfolioReasons.PortfolioFull);

        decimal target = System.Math.Min(nav * policy.EntryTargetNavFraction, availableCash);
        int quantity = decimal.ToInt32(decimal.Floor(target / fillPrice));
        if (quantity < 1)
        {
            return new(
                false,
                0,
                decimal.Round(target, 6, MidpointRounding.ToEven),
                0m,
                DelphiLivePortfolioReasons.InsufficientCashForOneShare);
        }

        decimal required = quantity * fillPrice;
        if (required > availableCash)
            throw new InvalidOperationException("Whole-share sizing attempted to overspend available cash.");
        return new(
            true,
            quantity,
            decimal.Round(target, 6, MidpointRounding.ToEven),
            decimal.Round(required, 6, MidpointRounding.AwayFromZero),
            DelphiLivePortfolioReasons.Sized);
    }

    public static DelphiLivePortfolioGuardState EvaluateGuards(
        decimal checkpointNav,
        decimal? openingNav,
        decimal highestCompletedSessionClosingNav,
        bool dailyBuyingAlreadyPaused,
        bool capitalReviewAlreadyRequired,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        DelphiLivePolicyValidator.Validate(policy);
        RequirePrice(checkpointNav, nameof(checkpointNav));
        RequirePrice(highestCompletedSessionClosingNav, nameof(highestCompletedSessionClosingNav));
        if (openingNav is decimal open)
            RequirePrice(open, nameof(openingNav));

        decimal? dailyReturn = openingNav.HasValue
            ? checkpointNav / openingNav.Value - 1m
            : null;
        decimal drawdown = checkpointNav / highestCompletedSessionClosingNav - 1m;
        bool dailyPaused = dailyBuyingAlreadyPaused ||
            dailyReturn <= -policy.DailyLossGuardFraction;
        bool capitalReview = capitalReviewAlreadyRequired ||
            drawdown <= -policy.CapitalReviewDrawdownFraction;
        return new(
            dailyPaused,
            capitalReview,
            dailyReturn,
            drawdown,
            highestCompletedSessionClosingNav);
    }

    public static DelphiLivePortfolioGuardState ResumeAfterCapitalReview(
        DelphiLivePortfolioGuardState current,
        decimal reviewedCurrentNav,
        bool nextSession)
    {
        ArgumentNullException.ThrowIfNull(current);
        RequirePrice(reviewedCurrentNav, nameof(reviewedCurrentNav));
        if (!current.CapitalReviewRequired)
            throw new InvalidOperationException("Capital review is not active.");
        return new(
            nextSession ? false : current.DailyBuyingPaused,
            false,
            nextSession ? null : current.DailyReturn,
            0m,
            reviewedCurrentNav);
    }

    public static IReadOnlyList<DelphiLivePortfolioActionCandidate> OrderCapitalFirst(
        IEnumerable<DelphiLivePortfolioActionCandidate> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        List<DelphiLivePortfolioActionCandidate> ordered = actions.ToList();
        foreach (DelphiLivePortfolioActionCandidate action in ordered)
        {
            if (action.ActionId == Guid.Empty || string.IsNullOrWhiteSpace(action.Symbol) ||
                action.LiveRank < 0)
                throw new ArgumentException("Action identity, symbol, and nonnegative live rank are required.", nameof(actions));
        }
        ordered.Sort((left, right) =>
        {
            int side = left.Side == right.Side
                ? 0
                : left.Side == DelphiLiveActionSide.Sell ? -1 : 1;
            if (side != 0)
                return side;
            int rank = left.LiveRank.CompareTo(right.LiveRank);
            if (rank != 0)
                return rank;
            return StringComparer.OrdinalIgnoreCase.Compare(left.Symbol, right.Symbol);
        });
        return ordered.AsReadOnly();
    }

    public static string RejectCapitalChange(decimal amount)
    {
        if (amount == 0m)
            throw new ArgumentException("A zero amount is not a capital change.", nameof(amount));
        return DelphiLivePortfolioReasons.CapitalChangeUnsupportedV1;
    }

    private static DelphiLiveBuySizingDecision Blocked(string reason) =>
        new(false, 0, 0m, 0m, reason);

    private static void ValidateMark(DelphiLivePositionMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (mark.PositionId == Guid.Empty || string.IsNullOrWhiteSpace(mark.Symbol) || mark.Quantity < 1)
            throw new ArgumentException("Position mark identity, symbol, and quantity are required.", nameof(mark));
        RequirePrice(mark.Price, nameof(mark.Price));
        RequireUtc(mark.BarEndUtc, nameof(mark.BarEndUtc));
    }

    private static void RequirePrice(decimal value, string parameterName)
    {
        if (value <= 0m)
            throw new ArgumentOutOfRangeException(parameterName, "Price or NAV must be positive.");
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
