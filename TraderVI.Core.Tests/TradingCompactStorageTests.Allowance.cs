#nullable enable
using Core.Trader.Storage;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    [Fact]
    public async Task PartialCancellationReleases62ExactlyOnceAcrossRollbackLostReplyAndFreshObservation()
    {
        var (db, state) = await Started();
        await db.Store.SaveAsync(Change(state) with { Orders = [Order()] });
        state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, 0, 100);

        var partial = TradingFillApplication.Prepare(state.Orders.Single(), 4, 38).Order;
        await db.Store.SaveAsync(Change(state) with { Orders = [partial with { Status = "Submitted" }],
            Positions = [Position() with { Quantity = 4, CostBasis = 38 }] });
        state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, 38, 62);
        var open = Assert.Single(state.Orders);
        Assert.Equal(62m, open.ReservedCash);
        decimal beforeCancel = state.Settings.PurchaseCeiling - state.Account.CommittedPurchaseCost - state.Account.ReservedPurchaseCost;
        var cancelled = open with { Status = "Cancelled", NeedsWork = false, LastCheckedUtc = Now };
        var save = Change(state) with { Orders = [cancelled] };

        db.FailAfterWrites = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(save));
        db.FailAfterWrites = 0;
        state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, 38, 62);
        Assert.Equal("Submitted", state.Orders.Single().Status);

        db.LoseCommitReply = true;
        await Assert.ThrowsAsync<TimeoutException>(() => db.Store.SaveAsync(save));
        state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, 38, 0);
        decimal afterCancel = state.Settings.PurchaseCeiling - state.Account.CommittedPurchaseCost - state.Account.ReservedPurchaseCost;
        Assert.Equal(62m, afterCancel - beforeCancel);
        Assert.Empty(state.Orders);
        Assert.Equal(4, state.Positions.Single().Quantity);
        int writesAfterCancellation = db.CommittedWrites;
        long versionAfterCancellation = state.Account.Version;

        var replay = await db.Store.SaveAsync(save);
        Assert.True(replay.AlreadyCommitted);
        Assert.Equal(0, replay.WrittenRows);
        var repeat = await db.Store.SaveAsync(Change(state) with
            { Orders = [cancelled with { LastCheckedUtc = Now.AddMinutes(1) }] });
        Assert.Equal(0, repeat.WrittenRows);
        Assert.Equal(writesAfterCancellation, db.CommittedWrites);
        state = (await db.Store.ReadAsync(Binding))!;
        Assert.Equal(versionAfterCancellation, state.Account.Version);
        AssertAllowance(state, 38, 0);
        var receipt = Assert.IsType<TradingOrder>(db.Rows[(typeof(TradingOrder), Binding, open.RequestId)]);
        Assert.Equal("Cancelled", receipt.Status);
        Assert.Equal(0m, receipt.ReservedCash);
        output.WriteLine($"Available allowance: before cancellation {beforeCancel}, after {afterCancel}, after retries {state.Settings.PurchaseCeiling - state.Account.CommittedPurchaseCost - state.Account.ReservedPurchaseCost}. Extra rows written by retries: 0.");
    }

    [Theory]
    [InlineData("Filled", 10, 96)]
    [InlineData("Filled", 10, 103)]
    [InlineData("Rejected", 0, 0)]
    [InlineData("Cancelled", 0, 0)]
    public async Task TerminalBuyUsesActualCostAndPreservesOtherReservations(string status, int filled, int net)
    {
        var db = new Database();
        await db.Store.SaveAsync(Initial() with { Orders = [Order(), Order(2) with { Status = "Unknown", Quantity = 5 }] });
        var state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, 0, 150);
        var terminal = Order() with { Status = status, LatestFilledQuantity = filled, LatestNetValue = net };
        // A terminal observation may arrive before position accounting finishes.
        await db.Store.SaveAsync(Change(state) with { Orders = [terminal] });
        state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, 0, 50 + net);
        var applied = TradingFillApplication.Prepare(terminal, filled, net).Order with { NeedsWork = false };
        var sale = Order(3) with { Side = "SELL", Status = "Filled", LatestFilledQuantity = 10, AppliedFilledQuantity = 10,
            LatestNetValue = 120, AppliedNetValue = 120, NeedsWork = false };
        await db.Store.SaveAsync(Change(state) with { Orders = [sale, applied] });
        state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, net, 50);
        Assert.Equal(Order(2).RequestId, Assert.Single(state.Orders).RequestId);
    }

    [Fact]
    public async Task PartialCostAboveEstimatePreservesOriginalAllowanceUntilTerminal()
    {
        var (db, state) = await Started();
        var partial = TradingFillApplication.Prepare(Order(), 9, 101).Order;
        await db.Store.SaveAsync(Change(state) with { Orders = [partial] });
        state = (await db.Store.ReadAsync(Binding))!;
        AssertAllowance(state, 100, 0);
        Assert.Equal(101m, state.Orders.Single().AppliedNetValue);
        await db.Store.SaveAsync(Change(state) with { Orders = [partial with { Status = "Cancelled", NeedsWork = false }] });
        AssertAllowance((await db.Store.ReadAsync(Binding))!, 101, 0);
    }

    [Fact]
    public async Task AccountOnlyChangeCannotEraseOrDoubleApplyAllowance()
    {
        var db = new Database();
        await db.Store.SaveAsync(Initial() with { Orders = [Order()] });
        var state = (await db.Store.ReadAsync(Binding))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with
        { Account = state.Account with { ReservedPurchaseCost = 0 } }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with
        { Account = state.Account with { CommittedPurchaseCost = 100 } }));
        await db.Store.SaveAsync(Change(state) with { Account = state.Account with { TradingDate = Now.Date.AddDays(1) } });
        AssertAllowance((await db.Store.ReadAsync(Binding))!, 0, 100);
    }

    private static void AssertAllowance(TradingCurrentState state, decimal committed, decimal reserved)
    {
        Assert.Equal(committed, state.Account.CommittedPurchaseCost);
        Assert.Equal(reserved, state.Account.ReservedPurchaseCost);
    }
}
