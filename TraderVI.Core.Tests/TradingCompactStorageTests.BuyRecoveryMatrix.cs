#nullable enable
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    [Theory]
    [InlineData("NEW", 0, "Submitted")]
    [InlineData("SUBMITTED", 0, "Submitted")]
    [InlineData("PLACED", 0, "Submitted")]
    [InlineData("CONTINGENT", 0, "Submitted")]
    [InlineData("PENDING_SUBMISSION", 0, "Submitted")]
    [InlineData("PENDING_FUND_TRANSFER", 0, "Submitted")]
    [InlineData("PENDING_REVIEW", 0, "Submitted")]
    [InlineData("CANCEL_PENDING", 0, "Submitted")]
    [InlineData("CANCEL_PENDING", 4, "Submitted")]
    [InlineData("CANCELED", 0, "Cancelled")]
    [InlineData("CANCELLED", 0, "Cancelled")]
    [InlineData("EXPIRED", 4, "Expired")]
    public async Task OpeningBuyStatusUsesConfirmedFillAndOriginalReservation(string status, int shares, string expected)
    {
        var db = new Database();
        await db.Store.SaveAsync(Initial() with { Orders = [InterruptedOrder] });
        var fact = Cancellation with { Status = status, FilledQuantity = shares, NetValue = shares == 0 ? null : 38m,
            GrossValue = shares == 0 ? null : 38m, LastFilledAt = shares == 0 ? null : Cancellation.LastFilledAt };
        int reads = 0;
        Task<TradingSaveResult?> Restart() => new InterruptedBuyRecovery(db.Store, (binding, id, _) =>
        {
            Assert.Equal(Binding, binding);
            Assert.Equal(fact.ExternalId, id);
            reads++;
            return Task.FromResult<BrokerOrderFact?>(fact);
        }, SavedOpeningTerms).RecoverAsync(Binding, InterruptedOrder.RequestId, RecoveryAt);
        await Restart();
        int writes = db.CommittedWrites;
        await Restart();
        Assert.Equal(writes, db.CommittedWrites);
        bool open = expected == "Submitted";
        Assert.Equal(open ? 2 : 1, reads);
        var state = (await db.Store.ReadAsync(Binding))!;
        decimal cost = shares == 0 ? 0 : 38;
        AssertAllowance(state, cost, open ? 100 - cost : 0);
        Assert.Equal(shares, state.Positions.Sum(p => p.Quantity));
        Assert.Equal(cost, state.Positions.Sum(p => p.CostBasis));
        var receipt = Assert.Single(db.Rows.Values.OfType<TradingOrder>());
        Assert.Equal(expected, receipt.Status);
        Assert.Equal(open, receipt.NeedsWork);
        Assert.Equal(InterruptedOrder.RequestId, receipt.RequestId);
    }

    [Fact]
    public async Task StaleBrokerReadCannotOverwriteControlsAndRetryOnlyResolvesOriginalOrder()
    {
        var db = new Database();
        var other = Order(2);
        await db.Store.SaveAsync(Initial() with { Orders = [InterruptedOrder, other] });
        int attempts = 0;
        var recovery = new InterruptedBuyRecovery(db.Store, async (binding, id, ct) =>
        {
            Assert.Equal(Cancellation.ExternalId, id);
            attempts++;
            if (attempts == 1) throw new TimeoutException("Simulated broker read timeout.");
            if (attempts == 2)
            {
                var current = (await db.Store.ReadAsync(binding, ct))!;
                await db.Store.SaveAsync(Change(current) with { Account = current.Account with { Phase = "Paused" } }, ct);
            }
            return Cancellation;
        }, SavedOpeningTerms);
        Task<TradingSaveResult?> Retry() => recovery.RecoverAsync(Binding, InterruptedOrder.RequestId, RecoveryAt);
        await Assert.ThrowsAsync<TimeoutException>(Retry);
        await Assert.ThrowsAsync<InvalidOperationException>(Retry);
        var afterRace = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(afterRace, 0, 200);
        Assert.Empty(afterRace.Positions);
        await Retry();
        var final = (await db.Store.ReadAsync(Binding))!;
        Assert.Equal("Paused", final.Account.Phase);
        AssertAllowance(final, 38, 100);
        Assert.Equal(other.RequestId, Assert.Single(final.Orders).RequestId);
        Assert.Equal(4, Assert.Single(final.Positions).Quantity);
        Assert.Null(await Retry());
        Assert.Equal(3, attempts);
    }
}
