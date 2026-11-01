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
    private static readonly DateTimeOffset RecoveryAt = new(Now.AddMinutes(2));
    private static TradingOrder InterruptedOrder => Order() with { PositionId = null, Status = "Unknown" };
    private static BrokerOrderFact Cancellation => new($"order-{InterruptedOrder.RequestId:D}", "TEST-1", "TEST1", "BUY",
        "CANCELLED", "CAD", "EQUITY", 10, 4, 38, 38, 0, 0, Now, Now.AddMinutes(1));

    private static BuyOpeningTerms SavedOpeningTerms(TradingApprovedSettings settings, TradingOrder order) =>
        new(settings.BindingId, settings.PermissionId, order.RequestId, "fixture-owner", "Fixed", 9m, 120m);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshRecoveryAppliesUnrecordedBrokerFillAndCancellationOnce(bool commitReplyLost)
    {
        var db = new Database();
        var order = InterruptedOrder;
        var originalTerms = SavedOpeningTerms(Settings, order);
        await db.Store.SaveAsync(Initial() with { Orders = [order] });
        // Broker filled four and cancelled the rest while the engine was down. Only the
        // original intent and its $100 reservation have reached compact storage.
        var before = (await db.Store.ReadAsync(Binding))!;
        Assert.Empty(before.Positions);
        AssertAllowance(before, 0, 100);
        int lookups = 0;
        Task<BrokerOrderFact?> Read(Guid account, string id, CancellationToken ct)
        {
            Assert.Equal(Binding, account);
            Assert.Equal(Cancellation.ExternalId, id);
            lookups++;
            return Task.FromResult<BrokerOrderFact?>(Cancellation);
        }
        Task<TradingSaveResult?> Restart() => new InterruptedBuyRecovery(db.Store, Read, (_, _) => originalTerms)
            .RecoverAsync(Binding, order.RequestId, RecoveryAt);

        db.FailAfterWrites = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(Restart);
        Assert.Empty((await db.Store.ReadAsync(Binding))!.Positions);
        AssertAllowance((await db.Store.ReadAsync(Binding))!, 0, 100);
        db.FailAfterWrites = 0;
        db.LoseCommitReply = commitReplyLost;
        if (commitReplyLost) await Assert.ThrowsAsync<TimeoutException>(Restart);
        else Assert.NotNull(await Restart());

        int committedWrites = db.CommittedWrites;
        Assert.Null(await Restart());
        Assert.Equal(2, lookups);
        Assert.Equal(committedWrites, db.CommittedWrites);
        var after = (await db.Store.ReadAsync(Binding))!;
        var held = Assert.Single(after.Positions);
        Assert.Equal(4, held.Quantity);
        Assert.Equal(38m, held.CostBasis);
        Assert.Equal(1, held.EntryCount);
        Assert.Equal(9m, held.ProtectionFloor);
        Assert.Equal("fixture-owner", held.ProtectionOwner);
        Assert.True(after.Account.NewBuysStopped);
        AssertAllowance(after, 38, 0);
        Assert.Empty(after.Orders);
        var receipt = Assert.Single(db.Rows.Values.OfType<TradingOrder>());
        Assert.Equal(order.RequestId, receipt.RequestId);
        Assert.Equal("Cancelled", receipt.Status);
        Assert.Equal(4, receipt.AppliedFilledQuantity);
        Assert.Equal(38m, receipt.AppliedNetValue);
    }

    [Fact]
    public async Task RecoveryKeepsExistingProtectionAndAddsOnlyUnappliedShares()
    {
        var db = new Database();
        var order = TradingFillApplication.Prepare(InterruptedOrder, 2, 19).Order;
        var prior = Position() with { PositionId = BrokerManagedStrategy.StableId($"{Binding:D}:{order.SecurityId}:{Cancellation.ExternalId}"),
            Quantity = 2, CostBasis = 19, ProtectionFloor = 9.25m, ExitDecisionId = Id(99),
            ExitReason = "FloorTriggered", ExitTriggeredUtc = Now };
        await db.Store.SaveAsync(Initial() with { Orders = [order], Positions = [prior] });
        var recovery = new InterruptedBuyRecovery(db.Store, (_, _, _) => Task.FromResult<BrokerOrderFact?>(Cancellation),
            (_, _) => throw new InvalidOperationException("Existing protection must be preserved."));
        await recovery.RecoverAsync(Binding, order.RequestId, RecoveryAt);
        var after = (await db.Store.ReadAsync(Binding))!;
        var held = Assert.Single(after.Positions);
        Assert.Equal(prior with { Quantity = 4, CostBasis = 38, LastEntryUtc = Cancellation.LastFilledAt!.Value.UtcDateTime,
            Version = after.Account.Version }, held);
        AssertAllowance(after, 38, 0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong order")]
    [InlineData("wrong security")]
    [InlineData("pending")]
    [InlineData("wrong protection")]
    [InlineData("closed cycle")]
    public async Task InconclusiveRecoveryLeavesOriginalBuyUnfinished(string problem)
    {
        var db = new Database();
        var order = InterruptedOrder;
        var initial = Initial() with { Orders = [order] };
        if (problem == "closed cycle")
        {
            var closed = Position() with { PositionId = BrokerManagedStrategy.StableId($"{Binding:D}:{order.SecurityId}:{Cancellation.ExternalId}"),
                IsOpen = false, Quantity = 0, CostBasis = 0, FirstEntryUtc = Cancellation.LastFilledAt!.Value.UtcDateTime,
                LastEntryUtc = Cancellation.LastFilledAt.Value.UtcDateTime };
            initial = initial with { Positions = [closed] };
        }
        await db.Store.SaveAsync(initial);
        BrokerOrderFact? fact = problem switch { "missing" => null, "wrong order" => Cancellation with { ExternalId = "different" },
            "wrong security" => Cancellation with { SecurityId = "different" }, "pending" => Cancellation with { Status = "PENDING" }, _ => Cancellation };
        var recovery = new InterruptedBuyRecovery(db.Store, (_, _, _) => Task.FromResult(fact),
            (settings, buy) => SavedOpeningTerms(settings, buy) with { RequestId = problem == "wrong protection" ? Id(99) : buy.RequestId });
        int writes = db.CommittedWrites;
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.RecoverAsync(Binding, order.RequestId, RecoveryAt));
        Assert.Equal(writes, db.CommittedWrites);
        var state = (await db.Store.ReadAsync(Binding))!;
        Assert.Equal(order.RequestId, Assert.Single(state.Orders).RequestId);
        Assert.Empty(state.Positions);
        AssertAllowance(state, 0, 100);
    }
}
