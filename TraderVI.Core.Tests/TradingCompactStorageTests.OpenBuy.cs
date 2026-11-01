#nullable enable
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartPollsOneOpenBuyAndBooksOnlyAdditionalShares(bool loseFirstFillReply)
    {
        var db = new Database();
        var original = InterruptedOrder;
        await db.Store.SaveAsync(Initial() with { Orders = [original] });
        var lookups = new List<string>();
        BrokerOrderFact broker = Cancellation with { Status = "PENDING", FilledQuantity = 0,
            GrossValue = null, NetValue = null, LastFilledAt = null };
        Task<BrokerOrderFact?> Read(Guid binding, string externalId, CancellationToken ct)
        {
            Assert.Equal(Binding, binding);
            lookups.Add(externalId);
            return Task.FromResult<BrokerOrderFact?>(broker);
        }
        Task<TradingSaveResult?> Restart() => new InterruptedBuyRecovery(db.Store, Read, SavedOpeningTerms)
            .RecoverAsync(Binding, original.RequestId, RecoveryAt.AddMinutes(lookups.Count));

        async Task<TradingCurrentState> ReadOpen(int shares, decimal spent, decimal reserved)
        {
            var state = (await db.Store.ReadAsync(Binding))!;
            var receipt = Assert.Single(state.Orders);
            Assert.Equal(original.RequestId, receipt.RequestId);
            Assert.Equal(original.DecisionId, receipt.DecisionId);
            Assert.True(receipt.NeedsWork);
            Assert.Equal("Submitted", receipt.Status);
            Assert.Equal(shares, receipt.AppliedFilledQuantity);
            Assert.Equal(spent, receipt.AppliedNetValue);
            Assert.Equal(reserved, receipt.ReservedCash);
            AssertAllowance(state, spent, reserved);
            if (shares == 0) Assert.Empty(state.Positions);
            else
            {
                var held = Assert.Single(state.Positions);
                Assert.Equal(shares, held.Quantity);
                Assert.Equal(spent, held.CostBasis);
                Assert.Equal(1, held.EntryCount);
                Assert.Equal(9m, held.ProtectionFloor);
            }
            return state;
        }

        await Restart();
        await ReadOpen(0, 0m, 100m);
        broker = Cancellation with { Status = "PARTIALLY_FILLED" };
        db.LoseCommitReply = loseFirstFillReply;
        if (loseFirstFillReply) await Assert.ThrowsAsync<TimeoutException>(Restart);
        else await Restart();
        var first = await ReadOpen(4, 38m, 62m);

        int writes = db.CommittedWrites;
        var repeat = await Restart();
        Assert.Equal(0, repeat!.WrittenRows);
        Assert.Equal(writes, db.CommittedWrites);
        var repeated = await ReadOpen(4, 38m, 62m);
        Assert.Equal(first.Account.Version, repeated.Account.Version);

        broker = broker with { FilledQuantity = 7, NetValue = 67m, GrossValue = 67m, LastFilledAt = RecoveryAt };
        await Restart();
        await ReadOpen(7, 67m, 33m);
        Assert.Equal(4, lookups.Count);
        Assert.All(lookups, id => Assert.Equal(Cancellation.ExternalId, id));
        Assert.Single(db.Rows.Values.OfType<TradingOrder>());

        broker = broker with { Status = "CANCELLED" };
        await Restart();
        var closed = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(closed, 67m, 0);
        Assert.Equal(7, Assert.Single(closed.Positions).Quantity);
        Assert.Empty(closed.Orders);
        Assert.Null(await Restart());
        Assert.Equal(5, lookups.Count);
        Assert.Equal(original.RequestId, Assert.Single(db.Rows.Values.OfType<TradingOrder>()).RequestId);
    }

    [Fact]
    public async Task EarlierPendingReplyCannotUndoConfirmedPartialFill()
    {
        var db = new Database();
        await db.Store.SaveAsync(Initial() with { Orders = [InterruptedOrder] });
        var broker = Cancellation with { Status = "PARTIALLY_FILLED" };
        var recovery = new InterruptedBuyRecovery(db.Store, (_, _, _) => Task.FromResult<BrokerOrderFact?>(broker), SavedOpeningTerms);
        await recovery.RecoverAsync(Binding, InterruptedOrder.RequestId, RecoveryAt);
        int writes = db.CommittedWrites;
        broker = broker with { Status = "PENDING", FilledQuantity = 0, NetValue = null, GrossValue = null, LastFilledAt = null };
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.RecoverAsync(Binding, InterruptedOrder.RequestId, RecoveryAt.AddMinutes(1)));
        Assert.Equal(writes, db.CommittedWrites);
        var after = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(after, 38m, 62m);
        Assert.Equal(4, Assert.Single(after.Positions).Quantity);
        Assert.True(Assert.Single(after.Orders).NeedsWork);
    }
}
