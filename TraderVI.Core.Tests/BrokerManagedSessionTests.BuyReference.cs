#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData(true, 10, false, false, true)]
    [InlineData(true, -1, false, false, true)]
    [InlineData(false, 10, false, false, false)]
    [InlineData(true, 10, true, false, false)]
    [InlineData(true, 10, false, true, false)]
    public async Task RestartUsesOnlyApprovedReconciledStrategyProceedsForTheNextBuy(
        bool reuse, int profit, bool limitedBuyingPower, bool outsideDeposit, bool expectBuy)
    {
        var x = await OngoingFixture();
        var saved = await x.Workflow().ApproveAsync(x.Review with { ReinvestSaleProceeds = reuse });
        var f = x.F; f.Strategy = saved.Strategy; f.Risk = saved.Account.Risk;
        f.Now = new(2026, 10, 23, 14, 1, 0, TimeSpan.Zero);
        var capital = saved.EffectiveReview.PurchaseCeiling;
        BrokerStrategyOrder Filled(string side, decimal net, DateTimeOffset at)
        {
            var o = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-ROT", "ROT", side,
                10, capital / 10, side == "BUY" ? "Initial" : "RiskExit", null, at, WealthsimpleSubmissionStatus.Acknowledged);
            var fact = f.Fact(o.ExternalId, o.Symbol, side, 10, net, at.AddSeconds(10));
            if (side == "SELL") fact = fact with { GrossValue = net + 1m, Fees = 1m };
            return o with { Reconciled = true, Latest = fact };
        }
        var buy = Filled("BUY", capital, f.Now.AddSeconds(-40));
        var sell = Filled("SELL", capital + profit, f.Now.AddSeconds(-20));
        var rotated = saved with { Orders = [buy, sell] };
        var waiting = rotated with { Orders = [buy, sell with { Reconciled = false }] };
        Assert.Equal(0m, waiting.RemainingPurchaseAllowance);
        Assert.Equal(reuse ? capital + profit : 0m, rotated.RemainingPurchaseAllowance);
        var anchor = saved.OngoingManagement!.CashAnchor;
        var observation = anchor with { SnapshotAt = f.Now, CheckedAt = f.Now, Cash = anchor.Cash + profit,
            BuyingPower = limitedBuyingPower ? 1m : anchor.Cash + profit,
            Orders = [.. anchor.Orders, buy.Latest!, sell.Latest!],
            Instruments = [.. anchor.Instruments ?? [], new("security-ROT", "ROT", "XTSE", "CAD", "EQUITY")] };
        Assert.Equal(2, BrokerOngoingRiskEvidenceSource.Reconcile(saved.OngoingManagement, observation, rotated.Orders).Length);
        Assert.Throws<InvalidOperationException>(() => BrokerOngoingRiskEvidenceSource.Reconcile(saved.OngoingManagement,
            observation with { Cash = observation.Cash + 100 }, rotated.Orders));
        if (outsideDeposit) observation = observation with { Cash = observation.Cash + 100, BuyingPower = observation.BuyingPower + 100 };
        f.TransformAccount = account => BrokerAccountProjector.Project(f.Binding,
            account.Import with { Plan = account.Import.Plan with { Observation = observation } }, f.Risk, f.Coverage(), f.Now);
        f.Store.State = rotated;
        f.Store.Restart();
        OfferContinuationSignal(x.Existing.Original.Existing);
        f.Transport.ReadReference = () => new(105.02m,105m,f.Now,"OPEN",
            new(f.Now.UtcDateTime.Date.AddHours(13.5),TimeSpan.Zero),new(f.Now.UtcDateTime.Date.AddHours(20),TimeSpan.Zero),f.Now);
        var result = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(f.Transport.Sent == (expectBuy ? 1 : 0), $"{result.Event}: {result.LatestDecision?.SelectionOutcome}; {string.Join(", ",result.Blocks)}");
        Assert.Equal(expectBuy ? 3 : 2, result.Orders.Length);
        if (expectBuy)
        {
            var reserved = result.Orders.Last();
            Assert.Equal(capital + profit - reserved.Quantity * reserved.ReferencePrice, result.RemainingPurchaseAllowance);
        }
        f.Store.Restart();
        Assert.Equal(result.RemainingPurchaseAllowance, f.Store.State!.RemainingPurchaseAllowance);
        await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(expectBuy ? 1 : 0, f.Transport.Sent);
    }

    [Fact]
    public async Task OngoingBuyReferenceSurvivesRejectionAndRestartWithoutChasingOrDuplicateOrders()
    {
        var x = await OngoingFixture(); var saved = await x.Workflow().ApproveAsync(x.Review);
        var f = x.F; f.Strategy = saved.Strategy; f.Risk = saved.Account.Risk;
        f.Now = new(2026,10,23,14,1,0,TimeSpan.Zero);
        f.TransformAccount = account =>
        {
            var observation = saved.Account.Import.Plan.Observation with { SnapshotAt = f.Now, CheckedAt = f.Now };
            var import = account.Import with { Plan = account.Import.Plan with { Observation = observation } };
            return BrokerAccountProjector.Project(f.Binding, import, f.Risk, f.Coverage(), f.Now);
        };
        OfferContinuationSignal(x.Existing.Original.Existing);
        f.Transport.ReadReference = () => new(105.02m,105m,f.Now,"OPEN",new DateTimeOffset(f.Now.UtcDateTime.Date.AddHours(13.5),TimeSpan.Zero),new DateTimeOffset(f.Now.UtcDateTime.Date.AddHours(20),TimeSpan.Zero),f.Now);
        f.Transport.BeforePrepare = () => throw new InvalidOperationException("Synthetic rejected attempt");
        var error = await Record.ExceptionAsync(() => f.Workflow().TickAsync(f.Binding.BindingId));
        Assert.True(error is InvalidOperationException, $"{f.Store.State!.Event}: {f.Store.State.LatestDecision?.Summary}; {string.Join(';', f.Store.State.Blocks)}");
        Assert.True(f.Store.State!.BuyReferences is not null, error?.ToString());
        var reference = Assert.Single(f.Store.State!.BuyReferences!);
        Assert.Empty(f.Store.State.Orders); Assert.Equal(1, f.Transport.ReferenceReads);
        f.Store.Restart(); f.Now = f.Now.AddSeconds(15);
        f.Transport.ReadReference = () => throw new InvalidOperationException("Must not replace the fixed ask");
        f.Transport.BeforePrepare = null;
        var accepted = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(accepted.Orders.Length == 1, $"{accepted.Event}: {accepted.LatestDecision?.Summary}; {string.Join(';', accepted.Blocks)}");
        var order = Assert.Single(accepted.Orders);
        Assert.Equal(reference, order.BuyReference); Assert.Equal(reference.MaximumAsk, order.ReferencePrice);
        Assert.True(order.Quantity * order.ReferencePrice <= order.PositionTarget);
        Assert.True(order.Quantity * order.ReferencePrice <= accepted.EffectiveReview.PurchaseCeiling);
        Assert.Equal(1, f.Transport.ReferenceReads); Assert.Equal(1, f.Transport.Sent);
        f.Store.Restart(); await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(1, f.Transport.Sent);
        Assert.Throws<InvalidOperationException>(() => BrokerBuyReference.ValidateAppend(accepted,
            accepted with { BuyReferences = [reference with { Quote = reference.Quote with { Ask = 110 } }] }));
    }
}
