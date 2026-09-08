using Core.Trader;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public class PortfolioComparisonTests
{
    private static readonly DateOnly A = new(2026, 6, 1), B = new(2026, 6, 2), C = new(2026, 6, 3);

    [Fact]
    public void SameDatesNormalizeDifferentCapitalAndMeasureClosingDeclines()
    {
        var result = PortfolioComparison.Calculate([new(A, 200m), new(B, 220m), new(C, 209m)], [A, B, C]);
        Assert.Equal(.045m, result.Return);
        Assert.Equal(-.05m, result.ObservedDrawdown);
        Assert.Equal(100m, result.Points[0].Index);
        Assert.Equal(104.5m, result.Points[2].Index);
    }

    [Fact]
    public void MissingStartDoesNotShiftAnAccountsComparisonWindow()
    {
        var result = PortfolioComparison.Calculate([new(B, 100m), new(C, 110m)], [A, B, C]);
        Assert.Null(result.Return);
        Assert.All(result.Points, p => Assert.Null(p.Index));
        Assert.Equal("Matching endpoints unavailable", result.Evidence);
    }

    [Fact]
    public void InteriorGapKeepsEndpointReturnButCannotClaimMaximumDrawdown()
    {
        var result = PortfolioComparison.Calculate([new(A, 100m), new(C, 110m)], [A, B, C]);
        Assert.Equal(.1m, result.Return);
        Assert.Null(result.ObservedDrawdown);
        Assert.Null(result.Points[1].Index);
        Assert.Contains("gaps", result.Evidence);
    }

    [Fact]
    public void DuplicateOrInvalidClosingEvidenceIsNotChosenByOrder()
    {
        var result = PortfolioComparison.Calculate([new(A, 100m), new(B, 110m), new(B, 110m), new(C, -1m)], [A, B, C]);
        Assert.Null(result.Return);
        Assert.Null(result.ObservedDrawdown);
        Assert.Equal(1, result.ValidCloses);
        Assert.Null(result.Points[1].Index);
    }

    [Fact]
    public void ZeroEndingValueIsARealTotalLoss()
    {
        var result = PortfolioComparison.Calculate([new(A, 100m), new(B, 0m)], [A, B]);
        Assert.Equal(-1m, result.Return);
        Assert.Equal(-1m, result.ObservedDrawdown);
        Assert.Equal(0m, result.Points[1].Index);
    }

    [Fact]
    public void ZeroStartingValueAndSingleDateCannotEstablishReturn()
    {
        Assert.Null(PortfolioComparison.Calculate([new(A, 0m), new(B, 100m)], [A, B]).Return);
        Assert.Null(PortfolioComparison.Calculate([new(A, 100m)], [A]).Return);
        Assert.Null(PortfolioComparison.Calculate([], []).Return);
    }

    [Fact]
    public void WindowUsesLatestSavedDateAndCalendarDaysAcrossAccounts()
    {
        var dates = PortfolioComparison.Window([new(A, 100m), new(B, 100m), new(C, 100m), new(C, null)], 1);
        Assert.Equal(new[] { B, C }, dates);
    }
}
