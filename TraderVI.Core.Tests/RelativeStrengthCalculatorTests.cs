#nullable enable

using Core.RelativeStrength;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class RelativeStrengthCalculatorTests
{
    [Fact]
    public void Compute_AlignsUnequalHistoriesByCanonicalSessionDate()
    {
        DateOnly[] sessions = Sessions(100);
        RelativeStrengthPricePoint[] stock = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index * index))
            .ToArray();
        RelativeStrengthPricePoint[] market = ConstantSeries(sessions, 100d);
        RelativeStrengthPricePoint[] recentSector = ConstantSeries(sessions.Skip(39), 100d);

        RelativeStrengthCalculationResult result = Calculate(stock, recentSector, market, sessions[^1]);

        double expectedStockReturn = (stock[^1].Close - stock[^11].Close) / stock[^11].Close;
        result.Features.RS_StockVsMarket_10d.ShouldNotBeNull();
        result.Features.RS_StockVsMarket_10d!.Value.ShouldBe(expectedStockReturn, 0.0000001d);
        result.Coverage.RequiredCanonicalSessions.ShouldBe(61);
        result.Coverage.HasAlignmentGap.ShouldBeFalse();
        result.Coverage.HasFullCoverage.ShouldBeTrue();
    }

    [Fact]
    public void Compute_LeavesSectorMetricsNullWhenExactReturnEndpointIsMissing()
    {
        DateOnly[] sessions = Sessions(70);
        RelativeStrengthPricePoint[] stock = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index))
            .ToArray();
        RelativeStrengthPricePoint[] market = ConstantSeries(sessions, 100d);
        DateOnly missingEndpoint = sessions[^11];
        RelativeStrengthPricePoint[] sector = ConstantSeries(
            sessions.Where(date => date != missingEndpoint),
            100d);

        RelativeStrengthCalculationResult result = Calculate(stock, sector, market, sessions[^1]);

        result.Features.RS_StockVsMarket_10d.ShouldNotBeNull();
        result.Features.RS_StockVsSector_10d.ShouldBeNull();
        result.Features.CompositeScore.ShouldBeNull();
        result.Coverage.HasSectorGapAfterFirstObservation.ShouldBeTrue();
        result.Coverage.MissingSectorSessions.ShouldContain(missingEndpoint);
    }

    [Fact]
    public void Compute_LeavesZCompositeNullWhenAnInteriorZWindowSessionIsMissing()
    {
        DateOnly[] sessions = Sessions(70);
        RelativeStrengthPricePoint[] stock = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index * index))
            .ToArray();
        RelativeStrengthPricePoint[] market = ConstantSeries(sessions, 100d);
        DateOnly missingInteriorSession = sessions[^5];
        RelativeStrengthPricePoint[] sector = ConstantSeries(
            sessions.Where(date => date != missingInteriorSession),
            100d);

        RelativeStrengthCalculationResult result = Calculate(stock, sector, market, sessions[^1]);

        result.Features.CompositeScore.ShouldNotBeNull();
        result.Features.RS_Z_StockVsSector.ShouldBeNull();
        result.Features.CompositeScoreZ.ShouldBeNull();
        result.Coverage.HasAlignmentGap.ShouldBeTrue();
    }

    [Fact]
    public void Compute_TreatsDuplicateSecondarySessionAsAmbiguousCoverage()
    {
        DateOnly[] sessions = Sessions(70);
        RelativeStrengthPricePoint[] market = ConstantSeries(sessions, 100d);
        DateOnly duplicateSession = sessions[^1];
        RelativeStrengthPricePoint[] duplicateStock =
        [
            .. ConstantSeries(sessions, 100d),
            new RelativeStrengthPricePoint(duplicateSession, 101d)
        ];

        RelativeStrengthCalculationResult result = Calculate(
            duplicateStock,
            market,
            market,
            sessions[^1]);

        result.Features.RS_StockVsMarket_5d.ShouldBeNull();
        result.Coverage.MissingStockSessions.ShouldContain(duplicateSession);
        result.Coverage.DuplicateStockSessions.ShouldContain(duplicateSession);
        result.Coverage.HasAlignmentGap.ShouldBeTrue();
    }

    [Fact]
    public void Compute_TreatsInvalidSecondaryCloseAsMissingCoverage()
    {
        DateOnly[] sessions = Sessions(70);
        RelativeStrengthPricePoint[] stock = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index))
            .ToArray();
        RelativeStrengthPricePoint[] market = ConstantSeries(sessions, 100d);
        DateOnly invalidEndpoint = sessions[^11];
        RelativeStrengthPricePoint[] sector = sessions
            .Select(date => new RelativeStrengthPricePoint(date, date == invalidEndpoint ? 0d : 100d))
            .ToArray();

        RelativeStrengthCalculationResult result = Calculate(stock, sector, market, sessions[^1]);

        result.Features.RS_StockVsSector_10d.ShouldBeNull();
        result.Coverage.MissingSectorSessions.ShouldContain(invalidEndpoint);
        result.Coverage.HasAlignmentGap.ShouldBeTrue();
    }

    [Fact]
    public void Compute_IgnoresInvalidSecondaryClosesOutsideTheRelevantWindow()
    {
        DateOnly[] sessions = Sessions(70);
        RelativeStrengthPricePoint[] stock = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index * index))
            .ToArray();
        RelativeStrengthPricePoint[] market = ConstantSeries(sessions, 100d);
        RelativeStrengthPricePoint[] sector =
        [
            .. sessions.Select((date, index) => new RelativeStrengthPricePoint(date, index == 0 ? 0d : 100d)),
            new RelativeStrengthPricePoint(sessions[^1].AddDays(1), 0d)
        ];

        RelativeStrengthCalculationResult result = Calculate(stock, sector, market, sessions[^1]);

        result.Features.CompositeScore.ShouldNotBeNull();
        result.Coverage.HasAlignmentGap.ShouldBeFalse();
        result.Coverage.HasFullCoverage.ShouldBeTrue();
    }

    [Fact]
    public void Compute_ReportsInvalidTargetMarketCloseWithoutShiftingTheTarget()
    {
        DateOnly[] sessions = Sessions(70);
        RelativeStrengthPricePoint[] stock = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index))
            .ToArray();
        RelativeStrengthPricePoint[] sector = ConstantSeries(sessions, 100d);
        RelativeStrengthPricePoint[] market = sessions
            .Select(date => new RelativeStrengthPricePoint(date, date == sessions[^1] ? 0d : 100d))
            .ToArray();

        RelativeStrengthCalculationResult result = Calculate(stock, sector, market, sessions[^1]);

        result.Features.RS_StockVsMarket_10d.ShouldBeNull();
        result.Coverage.HasTargetMarketSession.ShouldBeTrue();
        result.Coverage.HasTargetMarketClose.ShouldBeFalse();
        result.Coverage.UnavailableMarketCloseSessions.ShouldContain(sessions[^1]);
    }

    [Fact]
    public void Compute_UsesExactSessionDepthBoundaries()
    {
        DateOnly[] sixtySessions = Sessions(60);
        RelativeStrengthPricePoint[] stock60 = sixtySessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index * index))
            .ToArray();
        RelativeStrengthPricePoint[] baseline60 = ConstantSeries(sixtySessions, 100d);

        RelativeStrengthCalculationResult shortResult = Calculate(
            stock60,
            baseline60,
            baseline60,
            sixtySessions[^1]);

        DateOnly[] sixtyOneSessions = Sessions(61);
        RelativeStrengthPricePoint[] stock61 = sixtyOneSessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index * index))
            .ToArray();
        RelativeStrengthPricePoint[] baseline61 = ConstantSeries(sixtyOneSessions, 100d);
        RelativeStrengthCalculationResult completeResult = Calculate(
            stock61,
            baseline61,
            baseline61,
            sixtyOneSessions[^1]);

        shortResult.Features.RS_StockVsMarket_60d.ShouldBeNull();
        completeResult.Features.RS_StockVsMarket_60d.ShouldNotBeNull();
    }

    [Fact]
    public void Compute_UsesExactZScoreDepthBoundary()
    {
        DateOnly[] twentyNineSessions = Sessions(29);
        RelativeStrengthPricePoint[] stock29 = twentyNineSessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index * index))
            .ToArray();
        RelativeStrengthPricePoint[] baseline29 = ConstantSeries(twentyNineSessions, 100d);
        RelativeStrengthCalculationResult shortResult = Calculate(
            stock29,
            baseline29,
            baseline29,
            twentyNineSessions[^1]);

        DateOnly[] thirtySessions = Sessions(30);
        RelativeStrengthPricePoint[] stock30 = thirtySessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index * index))
            .ToArray();
        RelativeStrengthPricePoint[] baseline30 = ConstantSeries(thirtySessions, 100d);
        RelativeStrengthCalculationResult completeResult = Calculate(
            stock30,
            baseline30,
            baseline30,
            thirtySessions[^1]);

        shortResult.Features.RS_Z_StockVsMarket.ShouldBeNull();
        completeResult.Features.RS_Z_StockVsMarket.ShouldNotBeNull();
    }

    [Fact]
    public void Compute_DoesNotShiftToAnEarlierMarketSessionWhenTheTargetIsMissing()
    {
        DateOnly[] sessions = Sessions(70);
        RelativeStrengthPricePoint[] stock = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index))
            .ToArray();
        RelativeStrengthPricePoint[] baseline = ConstantSeries(sessions, 100d);
        DateOnly missingTarget = sessions[^1].AddDays(1);

        RelativeStrengthCalculationResult result = Calculate(
            stock,
            baseline,
            baseline,
            missingTarget);

        result.Features.CompositeScore.ShouldBeNull();
        result.Features.RS_StockVsMarket_5d.ShouldBeNull();
        result.Coverage.HasTargetMarketSession.ShouldBeFalse();
        result.Coverage.HasTargetMarketClose.ShouldBeFalse();
        result.Coverage.HasFullCoverage.ShouldBeFalse();
    }

    [Fact]
    public void Compute_PreservesTheExplicitXiuSectorFallbackSemantics()
    {
        DateOnly[] sessions = Sessions(70);
        RelativeStrengthPricePoint[] stock = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 100d + index * index))
            .ToArray();
        RelativeStrengthPricePoint[] xiu = sessions
            .Select((date, index) => new RelativeStrengthPricePoint(date, 90d + index))
            .ToArray();

        RelativeStrengthCalculationResult result = Calculate(stock, xiu, xiu, sessions[^1]);

        result.Features.RS_StockVsSector_10d.ShouldBe(result.Features.RS_StockVsMarket_10d);
        result.Features.RS_SectorVsMarket_10d.ShouldBe(0d);
        result.Features.CompositeScore!.Value.ShouldBe(
            0.8d * result.Features.RS_StockVsMarket_10d!.Value,
            0.0000001d);
    }

    private static RelativeStrengthCalculationResult Calculate(
        IReadOnlyList<RelativeStrengthPricePoint> stock,
        IReadOnlyList<RelativeStrengthPricePoint> sector,
        IReadOnlyList<RelativeStrengthPricePoint> market,
        DateOnly targetDate) =>
        RelativeStrengthCalculator.Compute(
            stock,
            sector,
            market,
            symbol: "TEST",
            date: targetDate,
            sectorIndexSymbol: "^TEST");

    private static DateOnly[] Sessions(int count)
    {
        var start = new DateOnly(2026, 1, 1);
        return Enumerable.Range(0, count)
            .Select(offset => start.AddDays(offset))
            .ToArray();
    }

    private static RelativeStrengthPricePoint[] ConstantSeries(
        IEnumerable<DateOnly> sessions,
        double close) =>
        sessions.Select(date => new RelativeStrengthPricePoint(date, close)).ToArray();
}
