using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public class PortfolioDailyPerformanceTests
{
    private static readonly DateOnly Day = new(2026, 9, 9);
    private static readonly DateTime Checkpoint = new(2026, 9, 9, 19, 55, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(997.65, -2.35, -0.00235)]
    [InlineData(1004.08, 4.08, 0.00408)]
    [InlineData(1000, 0, 0)]
    [InlineData(0, -1000, -1)]
    public void FirstDayGainNeedsOnlyThatSessionsOpeningAndEndingValue(decimal value, decimal gain, decimal change)
    {
        var result = PortfolioDailyPerformance.Calculate([new(Day, 1000, value, Checkpoint, true, "Saved close")], Day);
        Assert.Equal(gain, result.Gain);
        Assert.Equal(change, result.Return);
        Assert.True(result.IsClosing);
        // The separate, shared-period metric still cannot claim an earlier starting date.
        Assert.Null(PortfolioComparison.Calculate([new(Day, value)], [Day.AddDays(-5), Day]).Return);
    }

    [Fact]
    public void HistoricalDayUsesItsOwnOpeningAndNeverAnotherDaysValue()
    {
        PortfolioDailyObservation[] days = [new(Day, 800, 840, Checkpoint, true, "Saved close"),
            new(Day.AddDays(1), 840, 900, Checkpoint.AddDays(1), true, "Saved close")];
        var result = PortfolioDailyPerformance.Calculate(days, Day);
        Assert.Equal(40m, result.Gain);
        Assert.Equal(.05m, result.Return);
        Assert.Null(PortfolioDailyPerformance.Calculate(days, Day.AddDays(-1)).Gain);
        Assert.Null(PortfolioDailyPerformance.Calculate(days, null).Gain);
    }

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(0, 1000)]
    [InlineData(-1, 1000)]
    [InlineData(1000, null)]
    [InlineData(1000, -1)]
    public void MissingOrInvalidValuationsAreNotZeroGains(int? opening, int? value)
    {
        var result = PortfolioDailyPerformance.Calculate([new(Day, opening, value, null, false, "Missing")], Day);
        Assert.Null(result.Gain);
        Assert.Null(result.Return);
    }

    [Fact]
    public void DuplicateSessionsStayUnavailable()
    {
        var day = new PortfolioDailyObservation(Day, 1000, 1050, Checkpoint, true, "Saved close");
        Assert.Null(PortfolioDailyPerformance.Calculate([day, day], Day).Gain);
    }

    [Fact]
    public void LivePartialDayShowsCheckpointGainAndItsTimestampWithoutClaimingAClose()
    {
        var history = PortfolioDailyPerformance.FromLiveMarks([
            Mark(DelphiLivePortfolioMarkKind.Opening, 1000), Mark(DelphiLivePortfolioMarkKind.Checkpoint, 995.145m)]);
        var result = PortfolioDailyPerformance.Calculate(history, Day);
        Assert.Equal(-4.855m, result.Gain);
        Assert.Equal(-.004855m, result.Return);
        Assert.Equal(Checkpoint, result.AsOfUtc);
        Assert.False(result.IsClosing);
        Assert.Contains("Partial day", result.Evidence);
    }

    [Fact]
    public void IncompleteLatestLiveMarkNeverFallsBackToEarlierCompleteCheckpoint()
    {
        var history = PortfolioDailyPerformance.FromLiveMarks([
            Mark(DelphiLivePortfolioMarkKind.Opening, 1000), Mark(DelphiLivePortfolioMarkKind.Checkpoint, 1020),
            Mark(DelphiLivePortfolioMarkKind.Closing, 1030) with { Complete = false, BarEndUtc = Checkpoint.AddMinutes(5) }]);
        var result = PortfolioDailyPerformance.Calculate(history, Day);
        Assert.Null(result.Gain);
        Assert.Contains("incomplete", result.Evidence);
    }

    [Fact]
    public void RetriedOpeningAndPostFillCheckpointUseLastPersistedEvidenceAtSameEndpoint()
    {
        var history = PortfolioDailyPerformance.FromLiveMarks([
            Mark(DelphiLivePortfolioMarkKind.Opening, null) with { Complete = false },
            Mark(DelphiLivePortfolioMarkKind.Opening, 1000),
            Mark(DelphiLivePortfolioMarkKind.Checkpoint, 1020), Mark(DelphiLivePortfolioMarkKind.Checkpoint, 1018)]);
        Assert.Equal(18m, PortfolioDailyPerformance.Calculate(history, Day).Gain);
    }

    [Fact]
    public void OpeningOnlyAndUnsupportedCorporateActionsDoNotProduceDailyGains()
    {
        var opening = Mark(DelphiLivePortfolioMarkKind.Opening, 1000);
        Assert.Null(PortfolioDailyPerformance.Calculate(PortfolioDailyPerformance.FromLiveMarks([opening]), Day).Gain);
        var unsupported = Mark(DelphiLivePortfolioMarkKind.Closing, 2000) with { Reason = "CorporateActionUnsupported" };
        Assert.Null(PortfolioDailyPerformance.Calculate(PortfolioDailyPerformance.FromLiveMarks([opening, unsupported]), Day).Gain);
        Assert.Null(PortfolioDailyPerformance.Calculate(PortfolioDailyPerformance.FromLiveMarks([
            opening with { Complete = false }, Mark(DelphiLivePortfolioMarkKind.Closing, 1010)]), Day).Gain);
    }

    [Fact]
    public void LiveCompletedDaysRemainSeparatedWhenLaterDayHasDifferentBaseline()
    {
        var history = PortfolioDailyPerformance.FromLiveMarks([
            Mark(DelphiLivePortfolioMarkKind.Opening, 1000), Mark(DelphiLivePortfolioMarkKind.Closing, 1020),
            Mark(DelphiLivePortfolioMarkKind.Opening, 1030) with { TradingDate = Day.AddDays(1) },
            Mark(DelphiLivePortfolioMarkKind.Checkpoint, 1040) with { TradingDate = Day.AddDays(1) }]);
        Assert.Equal(20m, PortfolioDailyPerformance.Calculate(history, Day).Gain);
        Assert.True(PortfolioDailyPerformance.Calculate(history, Day).IsClosing);
        Assert.Equal(10m, PortfolioDailyPerformance.Calculate(history, Day.AddDays(1)).Gain);
    }

    private static DelphiLiveLedgerMark Mark(DelphiLivePortfolioMarkKind kind, decimal? value) =>
        new(Guid.NewGuid(), Day, kind, Checkpoint, true, value, [], "Complete");
}
