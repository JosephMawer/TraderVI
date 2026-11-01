#nullable enable
using Core.Trader.Promotion;
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
    [InlineData("FILLED", false)]
    [InlineData("FILLED", true)]
    [InlineData("POSTED", false)]
    public async Task RestartBooksOnlyRemainderOfFullBuyAndKeepsProtection(string brokerStatus, bool loseSaveReply)
    {
        var db = new Database();
        var partial = TradingFillApplication.Prepare(InterruptedOrder, 4, 38m).Order with { Status = "Submitted" };
        var protectedPosition = Position() with
        {
            PositionId = BrokerManagedStrategy.StableId($"{Binding:D}:{partial.SecurityId}:{Cancellation.ExternalId}"),
            Quantity = 4, CostBasis = 38m, LastEntryUtc = Cancellation.LastFilledAt!.Value.UtcDateTime,
            ProtectionFloor = 9.25m, ExitDecisionId = Id(99), ExitReason = "FloorTriggered", ExitTriggeredUtc = Now
        };
        await db.Store.SaveAsync(Initial() with { Settings = [Settings with { PurchaseCeiling = 100m }],
            Orders = [partial], Positions = [protectedPosition] });
        var before = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(before, 38m, 62m);

        // The final broker observation includes the earlier four shares and includes a fee in net cost.
        var full = Cancellation with { Status = brokerStatus, FilledQuantity = 10,
            GrossValue = 95m, Fees = 1m, NetValue = 96m, LastFilledAt = RecoveryAt };
        int lookups = 0;
        Task<BrokerOrderFact?> Lookup(Guid binding, string externalId, CancellationToken ct)
        {
            Assert.Equal(Binding, binding);
            Assert.Equal(full.ExternalId, externalId);
            lookups++;
            return Task.FromResult<BrokerOrderFact?>(full);
        }
        Task<TradingSaveResult?> Restart() => new InterruptedBuyRecovery(db.Store, Lookup,
            (_, _) => throw new InvalidOperationException("Recovery must keep the saved position protection."))
            .RecoverAsync(Binding, partial.RequestId, RecoveryAt);

        db.LoseCommitReply = loseSaveReply;
        if (loseSaveReply) await Assert.ThrowsAsync<TimeoutException>(Restart);
        else Assert.NotNull(await Restart());

        var after = (await db.Store.ReadAsync(Binding))!;
        var held = Assert.Single(after.Positions);
        Assert.Equal(6, held.Quantity - before.Positions.Single().Quantity);
        Assert.Equal(58m, held.CostBasis - before.Positions.Single().CostBasis);
        Assert.Equal(protectedPosition with { Version = after.Account.Version, Quantity = 10,
            CostBasis = 96m, LastEntryUtc = RecoveryAt.UtcDateTime }, held);
        AssertAllowance(after, 96m, 0m);
        Assert.Equal(4m, after.Settings.PurchaseCeiling - after.Account.CommittedPurchaseCost - after.Account.ReservedPurchaseCost);
        Assert.Empty(after.Orders);

        int writes = db.CommittedWrites;
        Assert.Null(await Restart());
        Assert.Equal(1, lookups);
        Assert.Equal(writes, db.CommittedWrites);
        var reread = (await db.Store.ReadAsync(Binding))!;
        Assert.Equal(after.Account, reread.Account);
        Assert.Equal(held, Assert.Single(reread.Positions));
        var receipt = Assert.Single(db.Rows.Values.OfType<TradingOrder>());
        Assert.Equal(partial.RequestId, receipt.RequestId);
        Assert.Equal("Filled", receipt.Status);
        Assert.False(receipt.NeedsWork);
        Assert.Equal(10, receipt.AppliedFilledQuantity);
        Assert.Equal(96m, receipt.AppliedNetValue);
    }
}
