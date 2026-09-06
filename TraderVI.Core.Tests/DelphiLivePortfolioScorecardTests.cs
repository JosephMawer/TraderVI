#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLivePortfolioScorecardTests
{
    private static readonly DateOnly Date = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
    private static readonly ReviewedTsxSessionCalendar Calendar = new(new("test-calendar", "synthetic only",
        Date, Date.AddDays(2), [Date, Date.AddDays(1), Date.AddDays(2)]));

    [Fact]
    public void WholeMissingSessionStaysInCheckpointCoverageAndBlocksPathStatistics()
    {
        var portfolio = Portfolio() with { Marks = Marks(Date, 1000m).Concat(Marks(Date.AddDays(2), 900m)).ToImmutableArray() };
        var report = DelphiLivePortfolioScorecard.Calculate(portfolio, Date.AddDays(2), Calendar);
        report.CheckpointCoverage.InvalidCount.ShouldBe(78);
        report.CheckpointCoverage.Readiness.ShouldBe(DelphiLiveCoverageReadiness.Blocked);
        report.MaximumCheckpointDrawdown.ShouldBeNull();
        report.MeanCheckpointExposure.ShouldBeNull();
        report.TotalReturn.ShouldBe(-.10m); // The exact ending NAV has a different input requirement.
    }

    [Fact]
    public void ReportNeverUsesLaterClosingMarkAndDoesNotDropEstimatedFills()
    {
        Guid position = Guid.NewGuid();
        var buy = Action(DelphiLiveActionSide.Buy, Open.AddMinutes(22), position, "StrongConfirmationCompleted", true);
        var sell = Action(DelphiLiveActionSide.Sell, Open.AddMinutes(32), position, "LiveWeakeningExit", true);
        var pending = Action(DelphiLiveActionSide.Buy, Open.AddMinutes(42), null, "StrongConfirmationCompleted", false);
        var fills = new[] { Fill(buy, position, 100m, DelphiLiveFillConfidence.EstimatedFill),
            Fill(sell, position, 110m, DelphiLiveFillConfidence.SideSpecific) };
        var marks = Marks(Date, 1010m).Select(m => m.BarEndUtc < Open.AddMinutes(30)
                ? m with { Nav = 1000m } : m).ToImmutableArray();
        var portfolio = Portfolio() with
        {
            Cash = 1010m, Actions = [buy, sell, pending], Fills = fills.ToImmutableArray(),
            Positions = [new(position, "AAA", 1, 100m, fills[0].FilledUtc, buy.Intent.ActionId, "{}",
                DelphiLiveProfitProtectionState.Open(position, 100m), fills[1].FilledUtc, sell.Intent.ActionId)],
            Marks = marks.AddRange(Marks(Date.AddDays(1), 1500m))
        };
        var report = DelphiLivePortfolioScorecard.Calculate(portfolio, Date, Calendar);
        report.TotalReturn.ShouldBe(.01m);
        report.CompletedTradeCount.ShouldBe(1);
        report.WinRate.ShouldBe(1m);
        report.RequestedActionCount.ShouldBe(3);
        report.NoFillCount.ShouldBe(1);
        report.PendingActionCount.ShouldBe(1);
        report.EstimatedFillCount.ShouldBe(1);
        report.EstimatedFillRate.ShouldBe(.5m);
        report.GrossTurnoverVsStartingCapital.ShouldBe(.21m);
        report.ExitCountsByReason["LiveWeakeningExit"].ShouldBe(1);
    }

    [Fact]
    public void CashOnlyGenerationHasObservedZeroReturnAndExposureWithoutInventingTradeRates()
    {
        var report = DelphiLivePortfolioScorecard.Calculate(Portfolio() with { Marks = Marks(Date, 1000m) }, Date, Calendar);
        report.TotalReturn.ShouldBe(0m);
        report.MeanCheckpointExposure.ShouldBe(0m);
        report.MaximumCheckpointDrawdown.ShouldBe(0m);
        report.WinRate.ShouldBeNull();
        report.NoFillRate.ShouldBeNull();
        report.EstimatedFillRate.ShouldBeNull();
    }

    private static DelphiLivePortfolioSnapshot Portfolio() => DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(),
        DelphiLivePolicyDefinition.Version1.PolicyVersionId, "OperationalChampion", null, 1000m, "CAD", Date, Open,
        Open.AddDays(-1), "test operator", "Synthetic capital"));
    private static ImmutableArray<DelphiLiveLedgerMark> Marks(DateOnly date, decimal nav) => Enumerable.Range(1, 78)
        .Select(n => new DelphiLiveLedgerMark(Guid.NewGuid(), date, n == 78 ? DelphiLivePortfolioMarkKind.Closing : DelphiLivePortfolioMarkKind.Checkpoint,
            Calendar.GetSessionBounds(date).OpenUtc.AddMinutes(5 * n), true, nav, [], "CompleteExactNav")).ToImmutableArray();
    private static DelphiLiveLedgerAction Action(DelphiLiveActionSide side, DateTime filled, Guid? position, string reason, bool complete)
    {
        var intent = new DelphiLiveActionIntent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "AAA", side,
            filled.AddSeconds(-2), filled.AddSeconds(-1), side == DelphiLiveActionSide.Buy ? Open.AddHours(6).AddMinutes(15) : null,
            side == DelphiLiveActionSide.Sell ? 1 : null, side == DelphiLiveActionSide.Buy ? 200m : null);
        return new(intent, position, Guid.NewGuid(), Date, Guid.NewGuid(), reason, "{}", complete ? "Filled" : "Pending",
            1, Guid.NewGuid(), complete ? filled : null, complete ? "Filled" : null, []);
    }
    private static DelphiLiveLedgerFill Fill(DelphiLiveLedgerAction action, Guid position, decimal price, DelphiLiveFillConfidence confidence) =>
        new(Guid.NewGuid(), action.Intent.ActionId, position, Guid.NewGuid(), "AAA", action.Intent.Side, 1, price,
            confidence == DelphiLiveFillConfidence.EstimatedFill ? DelphiLiveQuoteField.Price : DelphiLiveQuoteField.Bid,
            confidence, action.CompletedUtc!.Value, Date);
}
