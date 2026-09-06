#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveMeasurementTests
{
    private static readonly DateOnly SessionDate = new(2026, 9, 4);
    private static readonly DateTime SessionOpenUtc =
        new(2026, 9, 4, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DelphiLivePolicyDefinition Policy =
        DelphiLivePolicyDefinition.Version1;

    [Fact]
    public void CanonicalBarsRejectInvalidStructureTimingAndLateReceiptSemantics()
    {
        Should.Throw<ArgumentException>(() => new DelphiLiveFiveMinuteBar(
            Guid.NewGuid(),
            "ABC",
            SessionDate,
            SessionOpenUtc,
            SessionOpenUtc.AddMinutes(4),
            10m,
            11m,
            9m,
            10m,
            100,
            SessionOpenUtc.AddMinutes(6),
            "TMX",
            1,
            DelphiLiveEvidenceDisposition.OperationalOnTime));

        Should.Throw<ArgumentException>(() => Bar(
            "ABC",
            0,
            10m,
            12m,
            volume: 100,
            high: 11m,
            low: 9m));

        Should.Throw<ArgumentException>(() => new DelphiLiveFiveMinuteBar(
            Guid.NewGuid(),
            "ABC",
            SessionDate,
            SessionOpenUtc,
            SessionOpenUtc.AddMinutes(5),
            10m,
            11m,
            9m,
            10m,
            100,
            SessionOpenUtc.AddMinutes(5),
            "TMX",
            1,
            DelphiLiveEvidenceDisposition.OperationalOnTime));
    }

    [Fact]
    public void PersistenceUsesOpeningOpenThenExactContiguousCloses()
    {
        DelphiLiveFiveMinuteSeries stock = Series(
            "ABC",
            100m,
            new[] { 101m, 102m, 101m, 101m },
            new long[] { 1, 1, 1, 1 });
        DelphiLiveFiveMinuteSeries benchmark = Series(
            "XIU",
            100m,
            new[] { 100.5m, 101.6m, 101.2m, 101.2m },
            new long[] { 1, 1, 1, 1 });

        DelphiLivePersistenceMeasurements result =
            DelphiLiveMeasurements.CalculatePersistence(
                stock,
                benchmark,
                SessionOpenUtc.AddMinutes(20),
                Policy);

        result.Availability.ShouldBe(DelphiLiveMeasurementAvailability.Available);
        result.Intervals.Select(interval => interval.Contribution)
            .ShouldBe(new[] { 1, 0, -1, 0 });
        result.Score.ShouldBe(0);
        result.Intervals[0].StockReturn.ShouldBe(0.01m);
    }

    [Fact]
    public void PersistenceDoesNotTurnEqualOrFlatReturnsIntoVotes()
    {
        DelphiLiveFiveMinuteSeries stock = Series(
            "ABC",
            100m,
            new[] { 101m, 101m, 100m, 100m },
            new long[] { 1, 1, 1, 1 });
        DelphiLiveFiveMinuteSeries benchmark = Series(
            "XIU",
            100m,
            new[] { 101m, 101m, 100m, 100m },
            new long[] { 1, 1, 1, 1 });

        DelphiLivePersistenceMeasurements result =
            DelphiLiveMeasurements.CalculatePersistence(
                stock,
                benchmark,
                SessionOpenUtc.AddMinutes(20),
                Policy);

        result.Score.ShouldBe(0);
        result.Intervals.ShouldAllBe(interval => interval.Contribution == 0);
    }

    [Fact]
    public void RollingFamiliesNeedFiveFreshBarsAfterAContinuityReset()
    {
        DateTime firstFreshStartUtc = SessionOpenUtc.AddMinutes(30);
        DateTime firstFreshEndUtc = firstFreshStartUtc.AddMinutes(5);
        DelphiLiveFiveMinuteSeries fourStock = Series(
            "ABC",
            100m,
            new[] { 101m, 102m, 103m, 104m },
            new long[] { 10, 10, 10, 10 },
            firstFreshStartUtc,
            SessionOpenUtc,
            firstFreshEndUtc);
        DelphiLiveFiveMinuteSeries fourBenchmark = Series(
            "XIU",
            100m,
            new[] { 100.1m, 100.2m, 100.3m, 100.4m },
            new long[] { 10, 10, 10, 10 },
            firstFreshStartUtc,
            SessionOpenUtc,
            firstFreshEndUtc);

        DelphiLiveMeasurements.CalculatePersistence(
                fourStock,
                fourBenchmark,
                firstFreshEndUtc.AddMinutes(15),
                Policy)
            .Availability.ShouldBe(DelphiLiveMeasurementAvailability.NotMature);

        DelphiLiveFiveMinuteSeries fiveStock = Series(
            "ABC",
            100m,
            new[] { 101m, 102m, 103m, 104m, 105m },
            new long[] { 10, 10, 10, 10, 10 },
            firstFreshStartUtc,
            SessionOpenUtc,
            firstFreshEndUtc);
        DelphiLiveFiveMinuteSeries fiveBenchmark = Series(
            "XIU",
            100m,
            new[] { 100.1m, 100.2m, 100.3m, 100.4m, 100.5m },
            new long[] { 10, 10, 10, 10, 10 },
            firstFreshStartUtc,
            SessionOpenUtc,
            firstFreshEndUtc);

        DelphiLiveMeasurements.CalculatePersistence(
                fiveStock,
                fiveBenchmark,
                firstFreshEndUtc.AddMinutes(20),
                Policy)
            .Availability.ShouldBe(DelphiLiveMeasurementAvailability.Available);
        DelphiLiveMeasurements.CalculatePriorTwentyMinuteRange(
                fiveStock,
                firstFreshEndUtc.AddMinutes(20),
                Policy)
            .Availability.ShouldBe(DelphiLiveMeasurementAvailability.Available);
    }

    [Fact]
    public void LateResearchBarCannotRepairAnOperationalRollingWindow()
    {
        List<DelphiLiveFiveMinuteBar> stockBars = BuildBars(
            "ABC",
            100m,
            new[] { 101m, 102m, 103m, 104m },
            new long[] { 10, 10, 10, 10 });
        stockBars[2] = Bar(
            "ABC",
            2,
            stockBars[1].Close,
            stockBars[2].Close,
            10,
            disposition: DelphiLiveEvidenceDisposition.LateResearchOnly);
        var stock = new DelphiLiveFiveMinuteSeries(
            "ABC",
            SessionDate,
            SessionOpenUtc,
            SessionOpenUtc,
            stockBars);
        DelphiLiveFiveMinuteSeries benchmark = Series(
            "XIU",
            100m,
            new[] { 100.1m, 100.2m, 100.3m, 100.4m },
            new long[] { 10, 10, 10, 10 });

        DelphiLivePersistenceMeasurements result =
            DelphiLiveMeasurements.CalculatePersistence(
                stock,
                benchmark,
                SessionOpenUtc.AddMinutes(20),
                Policy);

        result.Availability.ShouldBe(DelphiLiveMeasurementAvailability.Unavailable);
        result.ReasonCode.ShouldBe(DelphiLiveReasonCodes.LateResearchOnly);
    }

    [Fact]
    public void DirectionalVolumeRollsFourIntervalsAndKeepsFlatVolumeInDenominator()
    {
        DelphiLiveFiveMinuteSeries stock = Series(
            "ABC",
            100m,
            new[] { 101m, 100m, 101m, 100m, 101m },
            new long[] { 1_000, 10, 20, 30, 40 });

        DelphiLiveDirectionalVolumeMeasurements openingWindow =
            DelphiLiveMeasurements.CalculateDirectionalVolume(
                stock,
                SessionOpenUtc.AddMinutes(20),
                Policy);
        DelphiLiveDirectionalVolumeMeasurements rolledWindow =
            DelphiLiveMeasurements.CalculateDirectionalVolume(
                stock,
                SessionOpenUtc.AddMinutes(25),
                Policy);

        openingWindow.Balance.RequireValue().ShouldBe(980m / 1_060m);
        rolledWindow.Balance.RequireValue().ShouldBe(0.20m);
        rolledWindow.TotalVolume.ShouldBe(100L);

        DelphiLiveFiveMinuteSeries flat = Series(
            "FLAT",
            100m,
            new[] { 100m, 100m, 100m, 100m },
            new long[] { 10, 20, 30, 40 });
        DelphiLiveMeasurements.CalculateDirectionalVolume(
                flat,
                SessionOpenUtc.AddMinutes(20),
                Policy)
            .Balance.RequireValue().ShouldBe(0m);
    }

    [Fact]
    public void ZeroTotalVolumeMakesDirectionalVolumeUnavailable()
    {
        DelphiLiveFiveMinuteSeries stock = Series(
            "ABC",
            100m,
            new[] { 101m, 102m, 103m, 104m },
            new long[] { 0, 0, 0, 0 });

        DelphiLiveDirectionalVolumeMeasurements result =
            DelphiLiveMeasurements.CalculateDirectionalVolume(
                stock,
                SessionOpenUtc.AddMinutes(20),
                Policy);

        result.Balance.Availability.ShouldBe(DelphiLiveMeasurementAvailability.Unavailable);
        result.Balance.ReasonCode.ShouldBe(DelphiLiveReasonCodes.ZeroTotalVolume);
        result.TwentyMinutePriceReturn.RequireValue().ShouldBe(0.04m);
    }

    [Fact]
    public void RollingReturnsUseTheOpeningPriceOnlyAtTheSessionOpenEndpoint()
    {
        DelphiLiveFiveMinuteSeries stock = Series(
            "ABC",
            100m,
            new[] { 101m, 102m, 103m, 104m, 110m },
            new long[] { 1, 1, 1, 1, 1 });
        DelphiLiveFiveMinuteSeries benchmark = Series(
            "XIU",
            100m,
            new[] { 100.5m, 101m, 101.5m, 102m, 102.5m },
            new long[] { 1, 1, 1, 1, 1 });

        DelphiLivePriceMovementMeasurements first =
            DelphiLiveMeasurements.CalculatePriceMovement(
                stock,
                benchmark,
                SessionOpenUtc.AddMinutes(20),
                99m,
                Policy);
        DelphiLivePriceMovementMeasurements next =
            DelphiLiveMeasurements.CalculatePriceMovement(
                stock,
                benchmark,
                SessionOpenUtc.AddMinutes(25),
                99m,
                Policy);

        first.TwentyMinute.StockReturn.RequireValue().ShouldBe(0.04m);
        next.TwentyMinute.StockReturn.RequireValue().ShouldBe(110m / 101m - 1m);
        first.OneHour.StockReturn.Availability
            .ShouldBe(DelphiLiveMeasurementAvailability.NotMature);
        first.PreviousCloseReturn.RequireValue().ShouldBe(104m / 99m - 1m);
    }

    [Fact]
    public void MedianTrueRangePct10UsesElevenExactAlignedDailyBars()
    {
        DateOnly firstDate = new(2026, 8, 14);
        DateOnly[] canonicalDates = Enumerable.Range(0, 21)
            .Select(offset => firstDate.AddDays(offset))
            .ToArray();
        var bars = new List<DelphiLiveDailyBar>();
        for (int index = 0; index < canonicalDates.Length; index++)
        {
            decimal rangeFraction = index == 0
                ? 0.01m
                : index <= 10
                    ? 0.02m
                    : (index - 10) / 100m;
            bars.Add(new DelphiLiveDailyBar(
                Guid.NewGuid(),
                "ABC",
                canonicalDates[index],
                100m,
                100m * (1m + rangeFraction),
                100m,
                100m,
                1_000));
        }

        DelphiLiveVolatilityRulerMeasurements result =
            DelphiLiveMeasurements.CalculateVolatilityRulers(
                bars,
                canonicalDates,
                liveSessionDate: canonicalDates[^1].AddDays(1),
                Policy);

        result.TenSession.MedianTrueRangePct.RequireValue().ShouldBe(0.055m);
        result.TenSession.SourceThroughSession.ShouldBe(canonicalDates[^1]);

        bars.RemoveAll(bar => bar.SessionDate == canonicalDates[15]);
        DelphiLiveMeasurements.CalculateVolatilityRulers(
                bars,
                canonicalDates,
                canonicalDates[^1].AddDays(1),
                Policy)
            .TenSession.MedianTrueRangePct.Availability
            .ShouldBe(DelphiLiveMeasurementAvailability.Unavailable);
    }

    [Fact]
    public void SessionVwapUsesEveryCompletedBarAndRejectsAGap()
    {
        DelphiLiveFiveMinuteBar first = Bar(
            "ABC",
            0,
            10m,
            10m,
            100,
            high: 12m,
            low: 8m);
        DelphiLiveFiveMinuteBar second = Bar(
            "ABC",
            1,
            10m,
            12m,
            300,
            high: 15m,
            low: 9m);
        var complete = new DelphiLiveFiveMinuteSeries(
            "ABC",
            SessionDate,
            SessionOpenUtc,
            SessionOpenUtc,
            new[] { first, second });

        DelphiLiveMeasurements.CalculateSessionVwap(
                complete,
                SessionOpenUtc.AddMinutes(10),
                Policy)
            .RequireValue().ShouldBe(11.5m);

        DelphiLiveFiveMinuteBar third = Bar(
            "ABC",
            2,
            12m,
            13m,
            100);
        var gapped = new DelphiLiveFiveMinuteSeries(
            "ABC",
            SessionDate,
            SessionOpenUtc,
            SessionOpenUtc,
            new[] { first, third });
        DelphiLiveMeasurements.CalculateSessionVwap(
                gapped,
                SessionOpenUtc.AddMinutes(15),
                Policy)
            .Availability.ShouldBe(DelphiLiveMeasurementAvailability.Unavailable);
    }

    [Fact]
    public void PriorRangeExcludesTheCurrentBarAndFirstMaturesOnTheFifthBar()
    {
        DelphiLiveFiveMinuteBar[] bars =
        {
            Bar("ABC", 0, 100m, 100m, 10, 101m, 99m),
            Bar("ABC", 1, 100m, 101m, 10, 104m, 98m),
            Bar("ABC", 2, 101m, 100m, 10, 103m, 97m),
            Bar("ABC", 3, 100m, 100m, 10, 102m, 96m),
            Bar("ABC", 4, 100m, 101m, 10, 200m, 95m)
        };
        var series = new DelphiLiveFiveMinuteSeries(
            "ABC",
            SessionDate,
            SessionOpenUtc,
            SessionOpenUtc,
            bars);

        DelphiLiveMeasurements.CalculatePriorTwentyMinuteRange(
                series,
                SessionOpenUtc.AddMinutes(20),
                Policy)
            .Availability.ShouldBe(DelphiLiveMeasurementAvailability.NotMature);

        DelphiLivePriorRangeMeasurements mature =
            DelphiLiveMeasurements.CalculatePriorTwentyMinuteRange(
                series,
                SessionOpenUtc.AddMinutes(25),
                Policy);
        mature.High.ShouldBe(104m);
        mature.Low.ShouldBe(96m);
    }

    private static DelphiLiveFiveMinuteSeries Series(
        string symbol,
        decimal openingPrice,
        decimal[] closes,
        long[] volumes,
        DateTime? firstBarStartUtc = null,
        DateTime? sessionOpenUtc = null,
        DateTime? operationalContinuityStartUtc = null)
    {
        DateTime firstStart = firstBarStartUtc ?? SessionOpenUtc;
        DateTime open = sessionOpenUtc ?? SessionOpenUtc;
        return new DelphiLiveFiveMinuteSeries(
            symbol,
            SessionDate,
            open,
            operationalContinuityStartUtc ?? open,
            BuildBars(symbol, openingPrice, closes, volumes, firstStart));
    }

    private static List<DelphiLiveFiveMinuteBar> BuildBars(
        string symbol,
        decimal openingPrice,
        decimal[] closes,
        long[] volumes,
        DateTime? firstBarStartUtc = null)
    {
        closes.Length.ShouldBe(volumes.Length);
        DateTime firstStart = firstBarStartUtc ?? SessionOpenUtc;
        var bars = new List<DelphiLiveFiveMinuteBar>(closes.Length);
        decimal open = openingPrice;
        for (int index = 0; index < closes.Length; index++)
        {
            bars.Add(Bar(
                symbol,
                index,
                open,
                closes[index],
                volumes[index],
                startBaseUtc: firstStart));
            open = closes[index];
        }
        return bars;
    }

    private static DelphiLiveFiveMinuteBar Bar(
        string symbol,
        int index,
        decimal open,
        decimal close,
        long volume,
        decimal? high = null,
        decimal? low = null,
        DelphiLiveEvidenceDisposition disposition = DelphiLiveEvidenceDisposition.OperationalOnTime,
        DateTime? startBaseUtc = null)
    {
        DateTime startUtc = (startBaseUtc ?? SessionOpenUtc).AddMinutes(index * 5);
        DateTime endUtc = startUtc.AddMinutes(5);
        return new DelphiLiveFiveMinuteBar(
            Guid.NewGuid(),
            symbol,
            SessionDate,
            startUtc,
            endUtc,
            open,
            high ?? System.Math.Max(open, close) + 0.01m,
            low ?? System.Math.Min(open, close) - 0.01m,
            close,
            volume,
            endUtc.AddMinutes(2),
            "TMX",
            1,
            disposition);
    }
}
