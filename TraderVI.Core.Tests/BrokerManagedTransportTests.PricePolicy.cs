#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedTransportTests
{
    [Theory]
    [InlineData("within", true)][InlineData("above", false)][InlineData("spread", false)]
    [InlineData("stale", false)][InlineData("cash", false)][InlineData("identity", false)]
    [InlineData("old-session", false)][InlineData("pending", false)]
    public async Task FixedBrokerAskPolicyPreservesBoundsAndAllManualChecks(string scenario, bool allowed)
    {
        var f = new Fixture(); var quote = await f.Transport.ReadBuyReferenceAsync("BBB", f.Order.SecurityId, f.Projection, default);
        var reference = new BrokerBuyReference(Guid.NewGuid(), "BBB", f.Order.SecurityId, quote, f.Clock.Now);
        var order = f.Order with { BuyReference = reference, ReferencePrice = reference.MaximumAsk };
        var current = quote with { Ask = scenario == "above" ? 10.026m : 10.025m, Bid = 10.01m };
        if (scenario == "spread") current = current with { Bid = 9.9m };
        if (scenario == "stale") current = current with { QuotedAt = f.Clock.Now.AddMinutes(-3) };
        if (scenario == "old-session") current = current with { SessionOpen = current.SessionOpen.AddDays(-1) };
        if (scenario == "cash") order = order with { Quantity = 91, PositionTarget = 2000 };
        if (scenario == "identity") order = order with { SecurityId = "changed" };
        f.Buys.Context = f.Buys.Context with { Quote = current };
        if (scenario == "pending") f.Buys.Context = f.Buys.Context with
            { PendingOrders = f.Buys.Context.PendingOrders with { Orders = [new("BBB", "BUY", "CANCEL_PENDING", "MARKET", 1, null, f.Clock.Now)] } };
        if (allowed)
        {
            await using var prepared = await f.Transport.PrepareAsync(order, f.Projection, default);
            await prepared.SubmitOnceAsync(default);
            Assert.Equal(1, f.Buys.Submissions);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Transport.PrepareAsync(order, f.Projection, default));
            Assert.Equal(0, f.Buys.Submissions); Assert.Empty(f.Journal.ReadAll());
        }
    }

    [Fact]
    public async Task QuoteCannotExpireBetweenPreflightAndSubmission()
    {
        var f = new Fixture(); var reference = new BrokerBuyReference(Guid.NewGuid(), "BBB", f.Order.SecurityId, f.Buys.Context.Quote, f.Clock.Now);
        await using var prepared = await f.Transport.PrepareAsync(f.Order with { BuyReference = reference, ReferencePrice = reference.MaximumAsk }, f.Projection, default);
        f.Clock.Now = f.Clock.Now.AddSeconds(31);
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.SubmitOnceAsync(default));
        Assert.Empty(f.Journal.ReadAll()); Assert.Equal(0, f.Buys.Submissions);
    }

    [Fact]
    public void SizingReservesMovementWithoutIncreasingAllocationSharesOrPurchaseCeiling()
    {
        var f = new Fixture(); var r = new BrokerBuyReference(Guid.NewGuid(), "BBB", f.Order.SecurityId, f.Buys.Context.Quote, f.Clock.Now);
        Assert.Equal(9, r.Size(10, 100, 900, 900));
        Assert.Equal(4, r.Size(10, 100, 50, 900));
        Assert.Equal(3, r.Size(10, 100, 900, 40));
        Assert.Equal(2, r.Size(2, 1000, 900, 900));
        Assert.Equal(0, r.Size(10, 100, 900, -1));
    }
}
