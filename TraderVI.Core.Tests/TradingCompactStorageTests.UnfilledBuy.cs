#nullable enable
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    [Theory]
    [InlineData("REJECTED", "Rejected", false, false)]
    [InlineData("REJECTED", "Rejected", false, true)]
    [InlineData("EXPIRED", "Expired", false, false)]
    [InlineData("EXPIRED", "Expired", true, true)]
    public async Task BeforeAnyFillTerminalBuyReleasesEntireReservationOnce(
        string brokerStatus, string receiptStatus, bool emptyExpiryReport, bool loseCommitReply)
    {
        var db = new Database();
        var order = InterruptedOrder;
        await db.Store.SaveAsync(Initial() with { Settings = [Settings with { PurchaseCeiling = 100 }],
            Orders = [order] });
        var before = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(before, 0, 100);
        Assert.Equal(100m, Assert.Single(before.Orders).ReservedCash);
        var fact = Cancellation with { Status = brokerStatus, FilledQuantity = 0, LastFilledAt = null,
            NetValue = null, GrossValue = null, Fees = null, Tax = null, FillQuantityWasNull = emptyExpiryReport };
        int lookups = 0;
        Task<BrokerOrderFact?> Lookup(Guid binding, string id, CancellationToken ct)
        {
            Assert.Equal(Binding, binding);
            Assert.Equal(fact.ExternalId, id);
            lookups++;
            return Task.FromResult<BrokerOrderFact?>(fact);
        }
        Task<TradingSaveResult?> Restart() => new InterruptedBuyRecovery(db.Store, Lookup,
            (_, _) => throw new InvalidOperationException("An unfilled buy cannot open a position."))
            .RecoverAsync(Binding, order.RequestId, RecoveryAt);

        db.LoseCommitReply = loseCommitReply;
        if (loseCommitReply) await Assert.ThrowsAsync<TimeoutException>(Restart);
        else Assert.NotNull(await Restart());
        var after = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(after, 0, 0);
        Assert.Equal(100m, after.Settings.PurchaseCeiling - after.Account.CommittedPurchaseCost - after.Account.ReservedPurchaseCost);
        Assert.Empty(after.Orders);
        Assert.Empty(after.Positions);
        int writes = db.CommittedWrites;
        Assert.Null(await Restart());
        Assert.Equal(1, lookups);
        Assert.Equal(writes, db.CommittedWrites);
        var again = (await db.Store.ReadAsync(Binding))!;
        Assert.Equal(after.Account, again.Account);
        Assert.Empty(again.Positions);
        var receipt = Assert.Single(db.Rows.Values.OfType<TradingOrder>());
        Assert.Equal(order.RequestId, receipt.RequestId);
        Assert.Equal(receiptStatus, receipt.Status);
        Assert.False(receipt.NeedsWork);
        Assert.Equal(0, receipt.AppliedFilledQuantity);
        Assert.Equal(0m, receipt.ReservedCash);
    }

    [Theory]
    [InlineData("REJECTED")]
    [InlineData("EXPIRED")]
    public async Task ZeroFillTerminalReportCannotEraseAnEarlierFill(string status)
    {
        var db = new Database();
        var order = InterruptedOrder;
        await db.Store.SaveAsync(Initial() with { Orders = [order] });
        var fact = Cancellation with { Status = "PARTIALLY_FILLED" };
        var recovery = new InterruptedBuyRecovery(db.Store, (_, _, _) => Task.FromResult<BrokerOrderFact?>(fact), SavedOpeningTerms);
        await recovery.RecoverAsync(Binding, order.RequestId, RecoveryAt);
        fact = fact with { Status = status, FilledQuantity = 0, NetValue = 0, GrossValue = 0, LastFilledAt = null };
        int writes = db.CommittedWrites;
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.RecoverAsync(Binding, order.RequestId, RecoveryAt.AddMinutes(1)));
        Assert.Equal(writes, db.CommittedWrites);
        var state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, 38, 62);
        Assert.Equal(4, Assert.Single(state.Positions).Quantity);
        Assert.True(Assert.Single(state.Orders).NeedsWork);
    }
}
