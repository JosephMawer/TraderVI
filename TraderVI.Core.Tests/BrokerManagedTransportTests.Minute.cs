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
    [InlineData(false)][InlineData(true)]
    public async Task MinuteDeadlineCannotBeRenewedByFreshQuoteOrSlowJournal(bool expiresDuringJournal)
    {
        var f = new Fixture(); var expires = f.Clock.Now.AddSeconds(2);
        var account = f.Buys.Context.Account;
        var sells = new ManualMarketSellWorkflowTests.Broker(new(account, f.Buys.Context.Security,
            f.Buys.Context.Snapshot, f.Buys.Context.PendingOrders, f.Buys.Context.Quote));
        var journal = new CallbackJournal(f.Journal, () => { if (expiresDuringJournal) f.Clock.Now = expires.AddTicks(1); });
        var transport = new BrokerManagedOrderTransport(f.Buys, sells, journal, f.Safety, account, f.Gate, f.Clock);
        var order = f.Order with { SignalExpiresUtc = expires.UtcDateTime };
        await using var prepared = await transport.PrepareAsync(order, f.Projection, default);
        if (!expiresDuringJournal) f.Clock.Now = expires.AddTicks(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.SubmitOnceAsync(default));
        Assert.Equal(0, f.Buys.Submissions);
        Assert.Equal(expiresDuringJournal ? 1 : 0, f.Journal.ReadAll().Count);
        if (expiresDuringJournal) Assert.Equal(order.RequestId, Assert.Single(f.Journal.ReadAll()).Intent.RequestId);
    }
}
