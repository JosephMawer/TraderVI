#nullable enable

using Core.Indicators.Granville;
using Core.TMX.Models.Dto;
using Newtonsoft.Json;
using Shouldly;
using System;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class LeadershipAcquisitionPolicyTests
{
    private static readonly DateTime LocalMarketDate = new(2026, 9, 2);

    [Fact]
    public void SameDateIncompleteRow_IsEligibleForRetryAndLiveAttachment()
    {
        LeadershipAcquisitionPlan plan = LeadershipAcquisitionPolicy.CreatePlan(
            LocalMarketDate,
            latestStoredDate: LocalMarketDate,
            latestHasActiveBreadth: false,
            initialComputeFrom: LocalMarketDate.AddDays(-30));

        plan.IsCurrentDateRetry.ShouldBeTrue();
        plan.ComputeFrom.ShouldBe(LocalMarketDate);
        LeadershipAcquisitionPolicy.SelectLiveTargetDate(
                LocalMarketDate,
                [LocalMarketDate],
                xiuAnchorDate: LocalMarketDate)
            .ShouldBe(LocalMarketDate);
    }

    [Fact]
    public void IncompleteYesterdayRow_IsNotRetriedOrGivenUndatedMovers()
    {
        DateTime yesterday = LocalMarketDate.AddDays(-1);

        LeadershipAcquisitionPlan plan = LeadershipAcquisitionPolicy.CreatePlan(
            LocalMarketDate,
            latestStoredDate: yesterday,
            latestHasActiveBreadth: false,
            initialComputeFrom: LocalMarketDate.AddDays(-30));

        plan.IsCurrentDateRetry.ShouldBeFalse();
        plan.ComputeFrom.ShouldBe(LocalMarketDate);
        LeadershipAcquisitionPolicy.SelectLiveTargetDate(
                LocalMarketDate,
                [yesterday],
                xiuAnchorDate: yesterday)
            .ShouldBeNull();
    }

    [Fact]
    public void MultiDateCatchUp_AttachesLiveObservationOnlyToCurrentDate()
    {
        DateTime? target = LeadershipAcquisitionPolicy.SelectLiveTargetDate(
            LocalMarketDate,
            [LocalMarketDate.AddDays(-2), LocalMarketDate.AddDays(-1), LocalMarketDate],
            xiuAnchorDate: LocalMarketDate);

        target.ShouldBe(LocalMarketDate);
    }

    [Fact]
    public void CurrentComputedDateWithoutExactXiuAnchor_HasNoLiveTarget()
    {
        DateTime? missingAnchorTarget = LeadershipAcquisitionPolicy.SelectLiveTargetDate(
            LocalMarketDate,
            [LocalMarketDate],
            xiuAnchorDate: null);
        DateTime? staleAnchorTarget = LeadershipAcquisitionPolicy.SelectLiveTargetDate(
            LocalMarketDate,
            [LocalMarketDate],
            xiuAnchorDate: LocalMarketDate.AddDays(-1));

        missingAnchorTarget.ShouldBeNull();
        staleAnchorTarget.ShouldBeNull();
    }

    [Fact]
    public void CompleteDistinctFiftySymbolBasket_ProducesObservedBreadth()
    {
        TmxMarketMoverDto?[] movers = CompleteBasket();

        ActiveMoverBasketEvaluation result =
            LeadershipAcquisitionPolicy.EvaluateMoverBasket(movers);

        result.IsValid.ShouldBeTrue();
        result.Reason.ShouldBeEmpty();
        result.Observation.ShouldNotBeNull();
        result.Observation.Advancers.ShouldBe(20);
        result.Observation.Decliners.ShouldBe(20);
        result.Observation.Unchanged.ShouldBe(10);
        result.Observation.BasketSize.ShouldBe(50);
    }

    [Fact]
    public void PartialNullAnonymousDuplicateOrDirectionlessBasket_IsUnavailable()
    {
        TmxMarketMoverDto?[] partial = CompleteBasket().Take(49).ToArray();
        ShouldBeInvalid(partial, "exactly 50");

        TmxMarketMoverDto?[] nullRow = CompleteBasket();
        nullRow[12] = null;
        ShouldBeInvalid(nullRow, "null row");

        TmxMarketMoverDto?[] blankSymbol = CompleteBasket();
        blankSymbol[12]!.symbol = "  ";
        ShouldBeInvalid(blankSymbol, "blank symbol");

        TmxMarketMoverDto?[] duplicateSymbol = CompleteBasket();
        duplicateSymbol[12]!.symbol = duplicateSymbol[11]!.symbol.ToLowerInvariant();
        ShouldBeInvalid(duplicateSymbol, "duplicate symbol");

        TmxMarketMoverDto?[] missingDirection = CompleteBasket();
        missingDirection[12]!.priceChange = null;
        ShouldBeInvalid(missingDirection, "omitted priceChange");
    }

    [Fact]
    public void OmittedPriceChangeJson_RemainsMissing()
    {
        TmxMarketMoverDto? mover = JsonConvert.DeserializeObject<TmxMarketMoverDto>(
            "{\"symbol\":\"ABC\"}");

        mover.ShouldNotBeNull();
        mover.priceChange.ShouldBeNull();
    }

    private static TmxMarketMoverDto?[] CompleteBasket() =>
        Enumerable.Range(0, LeadershipAcquisitionPolicy.RequiredMoverBasketSize)
            .Select(index => new TmxMarketMoverDto
            {
                symbol = $"SYM{index:00}",
                priceChange = index < 20 ? 1m : index < 40 ? -1m : 0m
            })
            .Cast<TmxMarketMoverDto?>()
            .ToArray();

    private static void ShouldBeInvalid(
        TmxMarketMoverDto?[] movers,
        string expectedReason)
    {
        ActiveMoverBasketEvaluation result =
            LeadershipAcquisitionPolicy.EvaluateMoverBasket(movers);

        result.IsValid.ShouldBeFalse();
        result.Observation.ShouldBeNull();
        result.Reason.ShouldContain(expectedReason);
    }
}
