using System;
using System.Collections.Generic;
using System.Linq;
using Core.Db;
using Core.Indicators;
using Core.Indicators.Granville;
using Shouldly;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class LeadershipMissingnessTests
{
    private static readonly DateTime FirstDay = new(2026, 8, 3);

    [Fact]
    public void ActiveBreadthRaw_DistinguishesUnavailableSourceFromGenuineNeutralBreadth()
    {
        var unavailable = Snapshot(0, newHighs: 20, activeAdvancers: null, activeDecliners: null, activeN: null);
        var tiedBasket = Snapshot(1, newHighs: 20, activeAdvancers: 25, activeDecliners: 25, activeN: 50);

        unavailable.HasActiveBreadth.ShouldBeFalse();
        unavailable.ActiveBreadthRaw.ShouldBeNull();

        tiedBasket.HasActiveBreadth.ShouldBeTrue();
        tiedBasket.ActiveBreadthRaw.ShouldBe(0d);
    }

    [Fact]
    public void RepositoryValidation_AcceptsAtomicMissingOrValidMoverObservations()
    {
        var entries = new[]
        {
            Snapshot(0, newHighs: 20, activeAdvancers: null, activeDecliners: null, activeN: null),
            Snapshot(1, newHighs: 21, activeAdvancers: 25, activeDecliners: 25, activeN: 50)
        };

        Should.NotThrow(() => LeadershipRepository.Validate(entries));
    }

    [Theory]
    [MemberData(nameof(InvalidMoverObservations))]
    public void RepositoryValidation_RejectsPartialOrInvalidMoverObservations(
        int? advancers,
        int? decliners,
        int? basketSize)
    {
        var entry = Snapshot(0, newHighs: 20, advancers, decliners, basketSize);

        entry.HasActiveBreadth.ShouldBeFalse();
        entry.ActiveBreadthRaw.ShouldBeNull();
        Should.Throw<ArgumentException>(() => LeadershipRepository.Validate([entry]));
    }

    public static IEnumerable<object[]> InvalidMoverObservations()
    {
        yield return [10, null, 50];
        yield return [null, 10, 50];
        yield return [10, 10, null];
        yield return [0, 0, 0];
        yield return [-1, 10, 50];
        yield return [30, 25, 50];
    }

    [Fact]
    public void ComputeQuality_RequiresAContiguousObservedMoverSuffix()
    {
        var calculator = new LeadershipCalculator();
        var history = ImprovingHistory(20);
        history[^1] = history[^1] with
        {
            ActiveAdvancers = null,
            ActiveDecliners = null,
            ActiveN = null
        };

        calculator.CountTrailingActiveBreadthDays(history).ShouldBe(0);
        calculator.ComputeQuality(history).ShouldBe(LeadershipQuality.Indeterminate);
    }

    [Fact]
    public void ComputeQuality_RecoversAfterTwelveNewContiguousMoverObservations()
    {
        var calculator = new LeadershipCalculator();
        var history = ImprovingHistory(13);
        history[0] = history[0] with
        {
            ActiveAdvancers = null,
            ActiveDecliners = null,
            ActiveN = null
        };

        calculator.RequiredActiveBreadthDays.ShouldBe(12);
        calculator.CountTrailingActiveBreadthDays(history).ShouldBe(12);
        calculator.ComputeQuality(history).ShouldBe(LeadershipQuality.Improving);
    }

    [Fact]
    public void XiuExpectedSession_AbsentFromAdAndLeadershipBreaksCoverage()
    {
        var calculator = new LeadershipCalculator();
        var completeHistory = ImprovingHistory(18);
        DateTime[] expectedSessions = completeHistory.Select(snapshot => snapshot.Date).ToArray();
        DateTime absentSession = expectedSessions[^12];
        var storedHistory = completeHistory
            .Where(snapshot => snapshot.Date != absentSession)
            .ToList();

        // Row-only callers cannot infer an absent session; the canonical calendar can.
        calculator.CountTrailingActiveBreadthDays(storedHistory).ShouldBe(17);
        calculator.CountTrailingActiveBreadthDays(storedHistory, expectedSessions).ShouldBe(11);
        calculator.Compute(storedHistory, expectedSessions).ShouldBe(LeadershipState.Indeterminate);
        calculator.ComputeQuality(storedHistory, expectedSessions).ShouldBe(LeadershipQuality.Indeterminate);

        GranvilleMarketContext leadershipContext = Context(
            storedHistory,
            expectedSessions: expectedSessions);
        leadershipContext.RecentHistory
            .Any(entry => entry.Date == absentSession)
            .ShouldBeFalse();
        GranvilleResult leadership = new LeadershipIndicators()
            .Evaluate(leadershipContext)
            .Single();
        GranvilleResult lightVolume = new LightVolumeIndicators()
            .Evaluate(Context(storedHistory, LightRisingTape(), expectedSessions))
            .Single();

        leadership.Name.ShouldBe("Leadership: No Active-Breadth Data");
        leadership.Signal.ShouldBe(IndicatorSignal.Neutral);
        leadership.Description.ShouldContain("11/12 contiguous days");
        lightVolume.Name.ShouldBe("Light Volume: No Leadership Movers");
        lightVolume.Signal.ShouldBe(IndicatorSignal.Neutral);
        lightVolume.Description.ShouldContain("11/12 contiguous days");
    }

    [Fact]
    public void CanonicalSessions_IgnorePostCalendarNhnlOnlyRows()
    {
        var canonicalHistory = ImprovingHistory(12);
        DateTime[] expectedSessions = canonicalHistory.Select(snapshot => snapshot.Date).ToArray();
        var postCalendarRow = Snapshot(
            day: 12,
            newHighs: 0,
            activeAdvancers: null,
            activeDecliners: null,
            activeN: null);
        var storedHistory = canonicalHistory.Append(postCalendarRow).ToList();
        var calculator = new LeadershipCalculator();

        calculator.CountTrailingActiveBreadthDays(storedHistory).ShouldBe(0);
        calculator.CountTrailingActiveBreadthDays(storedHistory, expectedSessions).ShouldBe(12);
        calculator.Compute(storedHistory, expectedSessions).ShouldBe(LeadershipState.Upswing);
        calculator.ComputeQuality(storedHistory, expectedSessions).ShouldBe(LeadershipQuality.Improving);

        GranvilleResult result = new LeadershipIndicators()
            .Evaluate(Context(storedHistory, expectedSessions: expectedSessions))
            .Single();
        result.IndicatorNumber.ShouldBe(10);
        result.Signal.ShouldBe(IndicatorSignal.StrongBullish);
    }

    [Fact]
    public void LeadershipIndicators_RequireDedicatedBenchmarkSessionCalendar()
    {
        var history = ImprovingHistory(12);
        GranvilleMarketContext context = Context(history, expectedSessions: Array.Empty<DateTime>());

        GranvilleResult leadership = new LeadershipIndicators().Evaluate(context).Single();
        GranvilleResult lightVolume = new LightVolumeIndicators()
            .Evaluate(Context(history, LightRisingTape(), Array.Empty<DateTime>()))
            .Single();

        leadership.Name.ShouldBe("Leadership: No Session Calendar");
        leadership.Signal.ShouldBe(IndicatorSignal.Neutral);
        lightVolume.Name.ShouldBe("Light Volume: No Leadership Calendar");
        lightVolume.Signal.ShouldBe(IndicatorSignal.Neutral);
    }

    [Fact]
    public void Compute_UnavailableOrFlatLayersDoNotVoteAsFalling()
    {
        var calculator = new LeadershipCalculator();
        var unavailableHistory = new List<LeadershipSnapshot>
        {
            Snapshot(0, newHighs: 19, activeAdvancers: null, activeDecliners: null, activeN: null),
            Snapshot(1, newHighs: 18, activeAdvancers: null, activeDecliners: null, activeN: null)
        };
        var flatMoverHistory = Enumerable.Range(0, 12)
            .Select(day => Snapshot(day, newHighs: 20 - day, activeAdvancers: 25, activeDecliners: 25, activeN: 50))
            .ToList();

        calculator.Compute(unavailableHistory).ShouldBe(LeadershipState.Indeterminate);
        calculator.Compute(flatMoverHistory).ShouldBe(LeadershipState.Indeterminate);
    }

    [Fact]
    public void Compute_CompleteImprovingAndDeterioratingHistoryPreservesDirectionalBehavior()
    {
        var calculator = new LeadershipCalculator();

        calculator.Compute(ImprovingHistory(12)).ShouldBe(LeadershipState.Upswing);
        calculator.ComputeQuality(ImprovingHistory(12)).ShouldBe(LeadershipQuality.Improving);

        calculator.Compute(DeterioratingHistory(12)).ShouldBe(LeadershipState.Downswing);
        calculator.ComputeQuality(DeterioratingHistory(12)).ShouldBe(LeadershipQuality.Deteriorating);
    }

    [Fact]
    public void Compute_CanonicalMoverRecoveryPreservesIndependentLargeCapHistory()
    {
        var history = Enumerable.Range(0, 30)
            .Select(day => Snapshot(
                day,
                newHighs: 20,
                activeAdvancers: 10 + day,
                activeDecliners: 10,
                activeN: 50) with
            {
                Tsx60Close = day switch
                {
                    28 => 120m,
                    29 => 130m,
                    _ => 100m
                },
                EqualWeightClose = 100m
            })
            .ToList();
        history[^13] = history[^13] with
        {
            ActiveAdvancers = null,
            ActiveDecliners = null,
            ActiveN = null
        };
        DateTime[] expectedSessions = history.Select(snapshot => snapshot.Date).ToArray();
        var calculator = new LeadershipCalculator();

        calculator.CountTrailingActiveBreadthDays(history, expectedSessions).ShouldBe(12);
        calculator.Compute(history, expectedSessions).ShouldBe(LeadershipState.Upswing);
    }

    [Fact]
    public void LeadershipIndicators_MissingMoversProduceExplicitNoDataNeutral()
    {
        var history = ImprovingHistory(20);
        history[^1] = history[^1] with
        {
            ActiveAdvancers = null,
            ActiveDecliners = null,
            ActiveN = null
        };

        GranvilleResult result = new LeadershipIndicators().Evaluate(Context(history)).Single();

        result.IndicatorNumber.ShouldBe(0);
        result.Signal.ShouldBe(IndicatorSignal.Neutral);
        result.GranvillePoints.ShouldBe(0);
        result.Name.ShouldBe("Leadership: No Active-Breadth Data");
        result.Description.ShouldContain("0/12 contiguous days");
    }

    [Fact]
    public void LightVolumeIndicators_MissingMoversProduceExplicitNoDataNeutral()
    {
        var history = ImprovingHistory(20);
        history[^1] = history[^1] with
        {
            ActiveAdvancers = null,
            ActiveDecliners = null,
            ActiveN = null
        };

        GranvilleResult result = new LightVolumeIndicators().Evaluate(Context(history, LightRisingTape())).Single();

        result.IndicatorNumber.ShouldBe(0);
        result.Signal.ShouldBe(IndicatorSignal.Neutral);
        result.GranvillePoints.ShouldBe(0);
        result.Name.ShouldBe("Light Volume: No Leadership Movers");
        result.Description.ShouldContain("0/12 contiguous days");
    }

    [Fact]
    public void CompleteMoverHistoryStillFiresLeadershipAndLightVolumeSignals()
    {
        var history = ImprovingHistory(12);

        GranvilleResult leadership = new LeadershipIndicators().Evaluate(Context(history)).Single();
        GranvilleResult lightVolume = new LightVolumeIndicators().Evaluate(Context(history, LightRisingTape())).Single();

        leadership.IndicatorNumber.ShouldBe(10);
        leadership.Signal.ShouldBe(IndicatorSignal.StrongBullish);
        lightVolume.IndicatorNumber.ShouldBe(26);
        lightVolume.Signal.ShouldBe(IndicatorSignal.Bullish);
    }

    private static List<LeadershipSnapshot> ImprovingHistory(int count) =>
        Enumerable.Range(0, count)
            .Select(day => Snapshot(
                day,
                newHighs: 10 + day,
                activeAdvancers: 20 + day,
                activeDecliners: 25 - day,
                activeN: 50))
            .ToList();

    private static List<LeadershipSnapshot> DeterioratingHistory(int count) =>
        Enumerable.Range(0, count)
            .Select(day => Snapshot(
                day,
                newHighs: 20 - day,
                activeAdvancers: 25 - day,
                activeDecliners: 10 + day,
                activeN: 50))
            .ToList();

    private static LeadershipSnapshot Snapshot(
        int day,
        int newHighs,
        int? activeAdvancers,
        int? activeDecliners,
        int? activeN) => new()
    {
        Date = FirstDay.AddDays(day),
        NewHighs = newHighs,
        NewLows = 20,
        IssuesTraded = 100,
        ActiveAdvancers = activeAdvancers,
        ActiveDecliners = activeDecliners,
        ActiveN = activeN
    };

    private static GranvilleMarketContext Context(
        IReadOnlyList<LeadershipSnapshot> history,
        MarketTapeContext tape = null,
        IReadOnlyList<DateTime> expectedSessions = null,
        IReadOnlyList<DateTime> adSessions = null)
    {
        expectedSessions ??= history.Select(snapshot => snapshot.Date).ToArray();
        adSessions ??= history.Select(snapshot => snapshot.Date).ToArray();
        var recentHistory = adSessions
            .Select(date => new ADLineEntry { Date = date })
            .ToArray();
        var today = recentHistory[^1];
        var yesterday = recentHistory.Length >= 2
            ? recentHistory[^2]
            : new ADLineEntry { Date = today.Date.AddDays(-1) };
        return new GranvilleMarketContext
        {
            Today = today,
            Yesterday = yesterday,
            RecentHistory = recentHistory,
            LeadershipHistory = history,
            LeadershipExpectedSessions = expectedSessions,
            MarketTape = tape
        };
    }

    private static MarketTapeContext LightRisingTape() => new()
    {
        Date = FirstDay,
        XiuVolume = 80,
        XiuVolumeSma20Prior = 100m,
        XiuClose = 101m,
        XiuPrevClose = 100m
    };
}
