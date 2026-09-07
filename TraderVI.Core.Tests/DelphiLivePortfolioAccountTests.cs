using Core.Trader.DelphiLive;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public class DelphiLivePortfolioAccountTests
{
    private static readonly DateOnly Date = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Checkpoint = Open.AddMinutes(30);

    [Fact]
    public void QueuedAccount_IsVisibleBeforeEffectiveOpen_AndEndsOnExclusiveBoundary()
    {
        var account = new DelphiLivePortfolioAccount(CashOnly(), Open, Date.AddDays(2), null);
        account.StatusAt(Open.AddMinutes(-1)).ShouldStartWith("Queued · ");
        account.IsCurrentOrQueued(Date.AddDays(-2)).ShouldBeTrue();
        account.StatusAt(Open).ShouldBe("Awaiting first session");
        account.StatusAt(Open.AddDays(2)).ShouldBe("Ended");
        account.IsCurrentOrQueued(Date.AddDays(2)).ShouldBeFalse();
        (account with { CancelledUtc = Open.AddHours(-1) }).StatusAt(Open).ShouldBe("Cancelled");
    }

    [Fact]
    public void CashOnlyAccount_UsesActualCashWithoutInventingPrices()
    {
        var value = DelphiLiveAccountValuation.From(CashOnly(), Date);
        value.NetAssetValue.ShouldBe(700m);
        value.UnrealizedProfitLoss.ShouldBe(0m);
        value.TotalReturn.ShouldBe(0m);
        value.DailyReturn.ShouldBeNull();
        value.PositionPrices.ShouldBeEmpty();
        value.MarkUtc.ShouldBeNull();
    }

    [Fact]
    public void CompleteMatchingCheckpoint_ReconcilesCashHoldingsAndReturns()
    {
        var p = Invested();
        var value = DelphiLiveAccountValuation.From(p, Date);
        value.NetAssetValue.ShouldBe(720m);
        value.UnrealizedProfitLoss.ShouldBe(20m);
        value.TotalReturn.ShouldBe(720m / 700m - 1m);
        value.DailyReturn.ShouldBe(value.TotalReturn);
        value.Drawdown.ShouldBe(-0.04m);
        value.PositionPrices[p.Positions[0].PositionId].ShouldBe(110m);
        value.MarkUtc.ShouldBe(Checkpoint);
    }

    [Fact]
    public void LatestIncompleteCheckpoint_DoesNotReuseOlderCompleteValue()
    {
        var p = Invested();
        p = p with { Marks = p.Marks.Add(p.Marks[0] with { BarEndUtc = Checkpoint.AddMinutes(5), Complete = false, Nav = null, Positions = [] }) };
        var value = DelphiLiveAccountValuation.From(p, Date);
        value.NetAssetValue.ShouldBeNull();
        value.UnrealizedProfitLoss.ShouldBeNull();
        value.TotalReturn.ShouldBeNull();
        value.Drawdown.ShouldBeNull();
        value.PositionPrices.ShouldBeEmpty();
    }

    [Fact]
    public void ChangedCashOrPositionIdentity_CannotUseSupersededPortfolioMark()
    {
        var p = Invested();
        DelphiLiveAccountValuation.From(p with { Cash = 499m }, Date).NetAssetValue.ShouldBeNull();
        var changed = p with { Positions = [p.Positions[0] with { PositionId = Guid.NewGuid() }] };
        DelphiLiveAccountValuation.From(changed, Date).PositionPrices.ShouldBeEmpty();
    }

    [Fact]
    public void ClosedEstimatedFill_IsIncludedInActualPaperCashAndRealizedProfit()
    {
        var p = Invested();
        var sellId = Guid.NewGuid();
        var position = p.Positions[0] with { ClosedUtc = Checkpoint.AddMinutes(2), ExitActionId = sellId };
        p = p with { Cash = 716m, Positions = [position], Fills = p.Fills.Add(new(Guid.NewGuid(), sellId,
            position.PositionId, Guid.NewGuid(), position.Symbol, DelphiLiveActionSide.Sell, 2, 108m,
            DelphiLiveQuoteField.Price, DelphiLiveFillConfidence.EstimatedFill, position.ClosedUtc.Value, Date)) };
        var value = DelphiLiveAccountValuation.From(p, Date);
        value.NetAssetValue.ShouldBe(716m);
        value.RealizedProfitLoss.ShouldBe(16m);
        value.UnrealizedProfitLoss.ShouldBe(0m);
        value.PositionPrices.ShouldBeEmpty();
    }

    [Fact]
    public void PriorSessionReturn_IsNotPresentedAsTodaysReturn()
    {
        DelphiLiveAccountValuation.From(Invested(), Date.AddDays(1)).DailyReturn.ShouldBeNull();
        var p = Invested() with { Guards = new(true, false, -0.03m, 0m, 750m) };
        var account = new DelphiLivePortfolioAccount(p, Open, null, null);
        account.StatusAt(Checkpoint).ShouldBe("New buys paused");
        account.StatusAt(Checkpoint.AddDays(1)).ShouldBe("Enabled");
    }

    [Fact]
    public void CorporateActionAudit_DoesNotBecomePerformanceEvidence()
    {
        var p = Invested();
        p = p with { Marks = [p.Marks[0] with { Reason = "CorporateActionUnsupported" }] };
        DelphiLiveAccountValuation.From(p, Date).NetAssetValue.ShouldBeNull();
    }

    private static DelphiLivePortfolioSnapshot CashOnly() => DelphiLiveLedgerIntegrity.Create(new(
        Guid.NewGuid(), Guid.NewGuid(), DelphiLivePolicyDefinition.Version1.PolicyVersionId, "OperationalChampion", null,
        700m, "CAD", Date, Open, Open.AddDays(-2), "Test", "Fixture only"));

    private static DelphiLivePortfolioSnapshot Invested()
    {
        var id = Guid.NewGuid();
        var action = Guid.NewGuid();
        return CashOnly() with
        {
            Cash = 500m, CurrentSession = Date, OpeningNav = 700m, Guards = new(false, false, null, null, 750m),
            Positions = [new(id, "ABC", 2, 100m, Open.AddMinutes(10), action, "{}", DelphiLiveProfitProtectionState.Open(id, 100m))],
            Fills = [new(Guid.NewGuid(), action, id, Guid.NewGuid(), "ABC", DelphiLiveActionSide.Buy, 2, 100m,
                DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.SideSpecific, Open.AddMinutes(10), Date)],
            Marks = [new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, Checkpoint, true, 720m,
                [new(id, "ABC", 2, 110m, Checkpoint)], "Complete")]
        };
    }
}
