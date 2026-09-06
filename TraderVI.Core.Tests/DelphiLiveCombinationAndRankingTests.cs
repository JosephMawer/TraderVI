#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveCombinationAndRankingTests
{
    public static IEnumerable<object[]> ExhaustiveVoteCountTable()
    {
        yield return new object[] { 4, 0, DelphiLiveMomentumState.Strong, DelphiLiveStrongTier.FourOfFour, DelphiLiveNeutralDetail.None };
        yield return new object[] { 3, 0, DelphiLiveMomentumState.Strong, DelphiLiveStrongTier.CleanThree, DelphiLiveNeutralDetail.None };
        yield return new object[] { 3, 1, DelphiLiveMomentumState.StrongWithConflict, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 2, 0, DelphiLiveMomentumState.PositiveNudge, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 2, 1, DelphiLiveMomentumState.PositiveNudgeWithConflict, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 2, 2, DelphiLiveMomentumState.MixedConflict, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 1, 0, DelphiLiveMomentumState.Neutral, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.SupportTilt };
        yield return new object[] { 0, 0, DelphiLiveMomentumState.Neutral, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 1, 1, DelphiLiveMomentumState.Neutral, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.Conflict };
        yield return new object[] { 0, 1, DelphiLiveMomentumState.Neutral, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.WeakTilt };
        yield return new object[] { 1, 2, DelphiLiveMomentumState.NegativeNudgeWithConflict, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 0, 2, DelphiLiveMomentumState.NegativeNudge, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 1, 3, DelphiLiveMomentumState.WeakWithConflict, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 0, 3, DelphiLiveMomentumState.Weak, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
        yield return new object[] { 0, 4, DelphiLiveMomentumState.VeryWeak, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None };
    }

    [Theory]
    [MemberData(nameof(ExhaustiveVoteCountTable))]
    public void FamilyCombinationImplementsEveryPossibleFullVoteCount(
        int supportive,
        int weakening,
        DelphiLiveMomentumState expectedState,
        DelphiLiveStrongTier expectedTier,
        DelphiLiveNeutralDetail expectedDetail)
    {
        DelphiLiveMomentumJudgment result = Combine(supportive, weakening);

        result.State.ShouldBe(expectedState);
        result.StrongTier.ShouldBe(expectedTier);
        result.NeutralDetail.ShouldBe(expectedDetail);
        result.SupportiveVotes.ShouldBe(supportive);
        result.WeakeningVotes.ShouldBe(weakening);
    }

    [Theory]
    [InlineData(DelphiLiveFamilyState.NotMature)]
    [InlineData(DelphiLiveFamilyState.Unavailable)]
    [InlineData(DelphiLiveFamilyState.PositiveLeaning)]
    [InlineData(DelphiLiveFamilyState.Neutral)]
    [InlineData(DelphiLiveFamilyState.NeutralConflict)]
    [InlineData(DelphiLiveFamilyState.NegativeLeaning)]
    public void EveryNonFullFamilyStateCastsNoVote(
        DelphiLiveFamilyState noVoteState)
    {
        DelphiLiveFamilyJudgment[] families = Enum.GetValues<DelphiLiveSignalFamily>()
            .Select(family => new DelphiLiveFamilyJudgment(
                family,
                noVoteState,
                noVoteState.ToString()))
            .ToArray();

        DelphiLiveMomentumJudgment result = DelphiLiveFamilyCombiner.Combine(families);

        result.SupportiveVotes.ShouldBe(0);
        result.WeakeningVotes.ShouldBe(0);
        result.State.ShouldBe(DelphiLiveMomentumState.Neutral);
    }

    [Fact]
    public void ThreeSupportiveVotesRemainCleanStrongWhenTheFourthIsUnavailable()
    {
        DelphiLiveFamilyJudgment[] families =
        {
            Family(DelphiLiveSignalFamily.Persistence, DelphiLiveFamilyState.Supportive),
            Family(DelphiLiveSignalFamily.PriceMovement, DelphiLiveFamilyState.Supportive),
            Family(DelphiLiveSignalFamily.VolumeSupport, DelphiLiveFamilyState.Supportive),
            Family(DelphiLiveSignalFamily.PriceStructure, DelphiLiveFamilyState.Unavailable)
        };

        DelphiLiveMomentumJudgment result = DelphiLiveFamilyCombiner.Combine(families);

        result.State.ShouldBe(DelphiLiveMomentumState.Strong);
        result.StrongTier.ShouldBe(DelphiLiveStrongTier.CleanThree);
        result.IsEntryEligibleStrong.ShouldBeTrue();
    }

    [Fact]
    public void DuplicateOrMissingNamedFamiliesFailInsteadOfShrinkingTheDenominator()
    {
        DelphiLiveFamilyJudgment[] duplicate =
        {
            Family(DelphiLiveSignalFamily.Persistence, DelphiLiveFamilyState.Neutral),
            Family(DelphiLiveSignalFamily.Persistence, DelphiLiveFamilyState.Neutral),
            Family(DelphiLiveSignalFamily.VolumeSupport, DelphiLiveFamilyState.Neutral),
            Family(DelphiLiveSignalFamily.PriceStructure, DelphiLiveFamilyState.Neutral)
        };

        Should.Throw<ArgumentException>(() => DelphiLiveFamilyCombiner.Combine(duplicate));
        Should.Throw<ArgumentException>(() => DelphiLiveFamilyCombiner.Combine(duplicate.Take(3).ToArray()));
    }

    [Fact]
    public void RankingUsesTheExactFifteenBucketOrderBeforeEveryTieBreak()
    {
        DelphiLiveMomentumJudgment[] buckets =
        {
            Combine(4, 0),
            Combine(3, 0),
            Combine(3, 1),
            Combine(2, 0),
            Combine(2, 1),
            Combine(1, 0),
            Combine(0, 0),
            Combine(1, 1),
            Combine(0, 1),
            Combine(2, 2),
            Combine(1, 2),
            Combine(0, 2),
            Combine(1, 3),
            Combine(0, 3),
            Combine(0, 4)
        };

        for (int index = 0; index < buckets.Length - 1; index++)
        {
            DelphiLiveRankCandidate stronger = Candidate(
                $"A{index:D2}",
                buckets[index],
                persistenceScore: -4,
                rank: 25,
                composite: -100m);
            DelphiLiveRankCandidate weaker = Candidate(
                $"B{index:D2}",
                buckets[index + 1],
                persistenceScore: 4,
                rank: 1,
                composite: 100m);

            DelphiLiveRankingComparer.Instance.Compare(stronger, weaker).ShouldBeLessThan(0);
        }
    }

    [Fact]
    public void RankingAppliesEveryLiveThenDailyTieBreakInOrder()
    {
        DelphiLiveMomentumJudgment morePositive = Combine(
            supportive: 0,
            weakening: 0,
            positiveLeaning: 2,
            negativeLeaning: 0);
        DelphiLiveMomentumJudgment lessPositive = Combine(
            supportive: 0,
            weakening: 0,
            positiveLeaning: 1,
            negativeLeaning: 0);
        Compare(
            Candidate("AAA", morePositive, -4, 25, -100m),
            Candidate("BBB", lessPositive, 4, 1, 100m)).ShouldBeLessThan(0);

        DelphiLiveMomentumJudgment fewerNegative = Combine(0, 0, 0, 1);
        DelphiLiveMomentumJudgment moreNegative = Combine(0, 0, 0, 2);
        Compare(
            Candidate("AAA", fewerNegative, -4, 25, -100m),
            Candidate("BBB", moreNegative, 4, 1, 100m)).ShouldBeLessThan(0);

        DelphiLiveMomentumJudgment tied = Combine(0, 0);
        Compare(
            Candidate("AAA", tied, 4, 25, -100m),
            Candidate("BBB", tied, 3, 1, 100m)).ShouldBeLessThan(0);
        Compare(
            Candidate("AAA", tied, 0, 2, -100m),
            Candidate("BBB", tied, 0, 3, 100m)).ShouldBeLessThan(0);
        Compare(
            Candidate("AAA", tied, 0, 2, 10m),
            Candidate("BBB", tied, 0, 2, 9m)).ShouldBeLessThan(0);
        Compare(
            Candidate("AAA", tied, 0, 2, 10m),
            Candidate("BBB", tied, 0, 2, 10m)).ShouldBeLessThan(0);
    }

    [Fact]
    public void ExactLiveTiePlacesCurrentCandidateBeforeCarryThenOrdersCarriesByTicker()
    {
        DelphiLiveMomentumJudgment tied = Combine(0, 0);
        DelphiLiveRankCandidate current = Candidate("ZZZ", tied, 0, 25, -100m);
        DelphiLiveRankCandidate carryA = Carry("AAA", tied, 0);
        DelphiLiveRankCandidate carryB = Carry("BBB", tied, 0);

        Compare(current, carryA).ShouldBeLessThan(0);
        Compare(carryA, carryB).ShouldBeLessThan(0);
    }

    [Fact]
    public void DualSelectionUsesBestRankAndReceivesNoSeparateBonus()
    {
        DelphiLiveMomentumJudgment tied = Combine(0, 0);
        DelphiLiveDailySetupQuality dual = DailySetup(
            commonComposite: 10m,
            Lens(DelphiLiveSourceLens.Continuation, rank: 8),
            Lens(DelphiLiveSourceLens.Breakout, rank: 2));
        DelphiLiveDailySetupQuality singleSameBest = DailySetup(
            commonComposite: 10m,
            Lens(DelphiLiveSourceLens.Continuation, rank: 2));
        var dualCandidate = new DelphiLiveRankCandidate("ZZZ", tied, 0, dual, false);
        var singleCandidate = new DelphiLiveRankCandidate("AAA", tied, 0, singleSameBest, false);

        dual.BestSelectedSourceRank.ShouldBe(2);
        Compare(singleCandidate, dualCandidate).ShouldBeLessThan(0);
    }

    [Fact]
    public void LensSpecificOrderUsesOnlyThatLensRankAndExcludesCarries()
    {
        DelphiLiveMomentumJudgment tied = Combine(0, 0);
        DelphiLiveDailySetupQuality dual = DailySetup(
            commonComposite: 10m,
            Lens(DelphiLiveSourceLens.Continuation, rank: 10),
            Lens(DelphiLiveSourceLens.Breakout, rank: 1));
        DelphiLiveDailySetupQuality continuationOnly = DailySetup(
            commonComposite: 10m,
            Lens(DelphiLiveSourceLens.Continuation, rank: 2));
        var dualCandidate = new DelphiLiveRankCandidate("AAA", tied, 0, dual, false);
        var continuationCandidate = new DelphiLiveRankCandidate(
            "BBB",
            tied,
            0,
            continuationOnly,
            false);
        DelphiLiveRankCandidate carry = Carry("CCC", tied, 0);

        DelphiLiveRanking.OrderForLens(
                new[] { dualCandidate, continuationCandidate, carry },
                DelphiLiveSourceLens.Continuation)
            .Select(candidate => candidate.Symbol)
            .ShouldBe(new[] { "BBB", "AAA" });
    }

    [Fact]
    public void MissingPersistenceScoreSortsAfterAnAvailableScoreWithoutInventingZero()
    {
        DelphiLiveMomentumJudgment tied = Combine(0, 0);
        DelphiLiveRankCandidate available = Candidate("ZZZ", tied, -4, 25, -100m);
        DelphiLiveRankCandidate unavailable = Candidate("AAA", tied, null, 1, 100m);

        Compare(available, unavailable).ShouldBeLessThan(0);
    }

    [Fact]
    public void RankingIsInputOrderIndependentAndComparatorIsTransitive()
    {
        DelphiLiveRankCandidate first = Candidate("CCC", Combine(3, 0), 0, 3, 1m);
        DelphiLiveRankCandidate second = Candidate("BBB", Combine(2, 0), 4, 1, 100m);
        DelphiLiveRankCandidate third = Candidate("AAA", Combine(0, 4), 4, 1, 100m);

        DelphiLiveRanking.Order(new[] { third, first, second })
            .Select(candidate => candidate.Symbol)
            .ShouldBe(new[] { "CCC", "BBB", "AAA" });
        Compare(first, second).ShouldBeLessThan(0);
        Compare(second, third).ShouldBeLessThan(0);
        Compare(first, third).ShouldBeLessThan(0);
        Compare(second, first).ShouldBeGreaterThan(0);
    }

    private static int Compare(DelphiLiveRankCandidate first, DelphiLiveRankCandidate second) =>
        DelphiLiveRankingComparer.Instance.Compare(first, second);

    private static DelphiLiveMomentumJudgment Combine(
        int supportive,
        int weakening,
        int positiveLeaning = 0,
        int negativeLeaning = 0)
    {
        if (supportive + weakening + positiveLeaning + negativeLeaning > 4)
            throw new ArgumentOutOfRangeException(nameof(supportive));

        DelphiLiveSignalFamily[] names = Enum.GetValues<DelphiLiveSignalFamily>();
        var families = new List<DelphiLiveFamilyJudgment>(4);
        int index = 0;
        for (int count = 0; count < supportive; count++)
            families.Add(Family(names[index++], DelphiLiveFamilyState.Supportive));
        for (int count = 0; count < weakening; count++)
            families.Add(Family(names[index++], DelphiLiveFamilyState.Weakening));
        for (int count = 0; count < positiveLeaning; count++)
            families.Add(Family(names[index++], DelphiLiveFamilyState.PositiveLeaning));
        for (int count = 0; count < negativeLeaning; count++)
            families.Add(Family(names[index++], DelphiLiveFamilyState.NegativeLeaning));
        while (index < names.Length)
            families.Add(Family(names[index++], DelphiLiveFamilyState.Neutral));
        return DelphiLiveFamilyCombiner.Combine(families);
    }

    private static DelphiLiveFamilyJudgment Family(
        DelphiLiveSignalFamily family,
        DelphiLiveFamilyState state) =>
        new(family, state, state.ToString());

    private static DelphiLiveRankCandidate Candidate(
        string symbol,
        DelphiLiveMomentumJudgment momentum,
        int? persistenceScore,
        int rank,
        decimal composite) =>
        new(
            symbol,
            momentum,
            persistenceScore,
            DailySetup(composite, Lens(DelphiLiveSourceLens.Continuation, rank)),
            false);

    private static DelphiLiveRankCandidate Carry(
        string symbol,
        DelphiLiveMomentumJudgment momentum,
        int? persistenceScore) =>
        new(symbol, momentum, persistenceScore, null, true);

    private static DelphiLiveSourceLensQuality Lens(
        DelphiLiveSourceLens lens,
        int rank) =>
        new(
            lens,
            isEligible: true,
            isPublished: true,
            rank,
            rankingKey: rank,
            reasonEvidence: "FrozenReason",
            gateEvidence: "FrozenGate");

    private static DelphiLiveDailySetupQuality DailySetup(
        decimal commonComposite,
        params DelphiLiveSourceLensQuality[] lenses) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            commonComposite,
            lenses.ToImmutableArray());
}
