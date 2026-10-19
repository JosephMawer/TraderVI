#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Linq;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class WealthsimpleAccountTests
{
    [Fact]
    public async Task TradingTodayNeverRelabelsAnOverallReturnOrInventsADailyBaseline()
    {
        var source = new Source();
        source.Read = a => Task.FromResult(new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Success,
            Snapshot(a) with { Return = new(25, "CAD"), ReturnRate = .25m, ReturnReferenceDate = "2026-01-01" }));
        using var vm = new WealthsimpleAccountViewModel(source); await vm.RefreshAsync();
        Assert.False(vm.ShowOverallPerformance); Assert.Equal("Unavailable", vm.PerformanceAmount); Assert.Empty(vm.PerformanceRate);
        vm.ShowOverallPerformance = true;
        Assert.Contains("25", vm.PerformanceAmount); Assert.Contains("25", vm.PerformanceRate);
        Assert.Contains("2026-01-01", vm.PerformanceContext); Assert.Contains("not supplied", vm.PerformanceContext);
        vm.ShowOverallPerformance = false; Assert.Equal("Unavailable", vm.PerformanceAmount);
    }

    [Fact]
    public async Task HoldingSelectionRefreshesItsOrderIdentityAndCannotSurviveLogout()
    {
        var source = new Source();
        var h = new WealthsimpleHolding("FIX", "Fixture", "EQUITY", 2, new(24, "CAD"), new(20, "CAD"), new(4, "CAD")) { SecurityId = "fixture-security" };
        source.Read = a => Task.FromResult(new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Success, Snapshot(a) with { Holdings = [h] }));
        using var vm = new WealthsimpleAccountViewModel(source); await vm.RefreshAsync();
        var first = vm.SelectedHolding!; Assert.Equal(2, first.Quantity); Assert.Contains("10", first.AverageCost);
        Assert.DoesNotContain("Hold", vm.SelectedDecision.Status); Assert.Equal("Await decision", vm.SelectedDecision.Status);
        await vm.RefreshAsync(); Assert.NotSame(first, vm.SelectedHolding);
        Assert.False(vm.IsCurrentSellSelection(first.SellSelection!)); Assert.True(vm.IsCurrentSellSelection(vm.SelectedHolding!.SellSelection!));
        source.Disconnect(); Assert.Null(vm.SelectedHolding); Assert.Empty(vm.Holdings);
    }

    [Fact]
    public void MissingCostOrZeroQuantityCannotManufactureAverageCost()
    {
        Assert.Equal("Unavailable", new WealthsimpleHoldingRow(new("FIX", "Fixture", "EQUITY", 0, null, new(10,"CAD"), null)).AverageCost);
        Assert.Equal("Unavailable", new WealthsimpleHoldingRow(new("FIX", "Fixture", "EQUITY", 2, null, null, null)).AverageCost);
    }
}

public sealed class TradingHoldingDecisionTests
{
    [Fact]
    public async Task RequiredExitExplainsTransitionWithoutPlacingAnOrder()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        var position = session.Account.Positions.Single();
        var account = new WealthsimpleTfsaAccount("fixture", "Fixture");
        var selection = new ManualSellSelection(account, position.SecurityId, position.Symbol, position.Quantity, f.Now, 1);
        var decision = TradingHoldingDecision.Describe(session, selection);
        Assert.Equal("Exit planned", decision.Status); Assert.Contains("not retained", decision.Reason);
        Assert.Contains("including at a loss", decision.NextStep); Assert.Contains("protection", decision.NextStep);
        Assert.Equal(0, f.Transport.Sent); Assert.Empty(session.Orders);
        Assert.Equal("Reconcile holding", TradingHoldingDecision.Describe(session, selection with { Quantity = position.Quantity + 1 }).Status);
        Assert.Equal("Reconcile holding", TradingHoldingDecision.Describe(session, selection with { SecurityId = "other" }).Status);
    }

    [Fact]
    public async Task RetainedPositionWithoutExplicitDecisionIsNotCalledHold()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval(retain: true));
        var p = session.Account.Positions.Single();
        var selection = new ManualSellSelection(new WealthsimpleTfsaAccount("fixture", "Fixture"), p.SecurityId, p.Symbol, p.Quantity, f.Now, 1);
        Assert.Equal("Await decision", TradingHoldingDecision.Describe(session, selection).Status);
        Assert.Equal("Await decision", TradingHoldingDecision.Describe(null, selection).Status);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task IntentAcknowledgementAndUncertainSaleRemainDistinct()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        var session = await f.Workflow().ApproveAsync(f.Approval());
        var p = session.Account.Positions.Single();
        var selection = new ManualSellSelection(new WealthsimpleTfsaAccount("fixture", "Fixture"), p.SecurityId, p.Symbol, p.Quantity, f.Now, 1);
        var intent = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), p.PositionId, p.SecurityId, p.Symbol,
            "SELL", p.Quantity, 10, "TransitionExit", null, f.Now);
        Assert.Equal("Sell queued", TradingHoldingDecision.Describe(session with { Orders = [intent] }, selection).Status);
        Assert.Equal("Sell pending", TradingHoldingDecision.Describe(session with { Orders = [intent with { Submission = WealthsimpleSubmissionStatus.Acknowledged }] }, selection).Status);
        var uncertain = TradingHoldingDecision.Describe(session with { Orders = [intent with { Submission = WealthsimpleSubmissionStatus.OutcomeUnknown }] }, selection);
        Assert.Equal("Check sale", uncertain.Status); Assert.Contains("duplicate", uncertain.NextStep);
        Assert.Equal(0, f.Transport.Sent);
    }
}
