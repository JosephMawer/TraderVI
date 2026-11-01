#nullable enable
using Core.Trader.Promotion;
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
    private static TradingPositionCurrent Triggered(int id = 2) => Position(id) with
    { ProtectionOwner = Strategy.ToString("D"), ExitDecisionId = Id(5000 + id), ExitReason = "FloorTriggered",
      ExitTriggeredUtc = Now, LastProtectionUtc = Now };

    private static TradingStateWrite ActiveBook() => Initial() with
    {
        Account = Account with { ExecutionEnabled = true },
        Positions = [Triggered()], Orders = [InterruptedOrder],
        Symbols = [new() { BindingId = Binding, Symbol = "TEST2", StrategyId = Strategy, SessionId = Id(10),
            TradingDate = Now.Date, EntriesToday = 1, IsRelevant = true }],
        Restrictions = [new() { BindingId = Binding, RestrictionId = Id(77), ScopeKey = "Account",
            ActionCode = "Buy", ReasonCode = "StopBuying", Explanation = "Operator stopped new buys" }]
    };

    private sealed class RecoveryClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => RecoveryAt;
    }

    private sealed class SimulatedAccountBroker(Database db)
    {
        internal readonly List<TradingOrder> Sent = [];
        internal readonly List<string> Calls = [];
        internal Func<string, BrokerOrderFact?> Status = _ => null;
        internal Func<WealthsimpleTfsaSnapshot> AccountBook = () => throw new TimeoutException();
        internal Guid SnapshotBinding = Binding;
        internal Func<Task>? AfterSend;
        internal Func<CancellationToken, Task<IRestartSellLease>> AcquireSellLease = _ =>
            Task.FromResult<IRestartSellLease>(new SimulatedLease());
        internal OfflineAccountRestart Restart() => new(db.Store, Lookup, (id, _) =>
        {
            Calls.Add("account");
            Assert.Equal(Binding, id);
            return Task.FromResult((SnapshotBinding, AccountBook()));
        }, Submit, AcquireSellLease, SavedOpeningTerms, new RecoveryClock());

        private sealed class SimulatedLease : IRestartSellLease
        {
            public void Verify(TradingCurrentState state, TradingPositionCurrent position, Guid requestId, DateTimeOffset now, bool final) { }
            public void Dispose() { }
        }

        private Task<BrokerOrderFact?> Lookup(Guid binding, string externalId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(Binding, binding);
            Calls.Add(externalId);
            return Task.FromResult(Status(externalId));
        }

        private async Task Submit(TradingOrder order, CancellationToken ct)
        {
            var state = (await db.Store.ReadAsync(Binding, ct))!;
            var receipt = Assert.Single(state.Orders.Where(o => o.RequestId == order.RequestId));
            Assert.Equal("SELL", receipt.Side);
            Assert.Equal(receipt.Quantity, receipt.ReservedQuantity);
            Assert.Equal(receipt.Quantity, state.Positions.Single(p => p.PositionId == order.PositionId).ReservedSellQuantity);
            Sent.Add(order); // Count every invocation, even a duplicate or a lost reply.
            Calls.Add("send");
            if (AfterSend is not null) await AfterSend();
        }
    }

    private static BrokerOrderFact SellFact(TradingOrder sale, string status, int filled) =>
        new($"order-{sale.RequestId:D}", sale.SecurityId, sale.Symbol, "SELL", status, "CAD", "EQUITY",
            sale.Quantity, filled, filled * 11m, filled * 11m, 0, 0, sale.CreatedUtc,
            filled == 0 ? null : RecoveryAt);

    private static WealthsimpleTfsaSnapshot BrokerBook(DateTimeOffset at, params (TradingPositionCurrent Position, int Shares)[] holdings) =>
        new(new WealthsimpleTfsaAccount("synthetic", "Offline"), at, null, new(962, "CAD"), new(950, "CAD"),
            null, null, null, null, null, holdings.Select(h => new WealthsimpleHolding(h.Position.Symbol, "Synthetic",
                "EQUITY", h.Shares, null, null, null) { SecurityId = h.Position.SecurityId }).ToArray());

    [Fact]
    public async Task WholeAccountRestoresControlsResolvesIndependentWorkAndSellsTriggeredHoldingOnce()
    {
        var db = new Database();
        var otherBuy = Order(3) with { PositionId = null };
        await db.Store.SaveAsync(ActiveBook() with { Orders = [InterruptedOrder, otherBuy] });
        var before = (await db.Store.ReadAsync(Binding))!;
        var broker = new SimulatedAccountBroker(db);
        broker.AccountBook = () => BrokerBook(RecoveryAt, (Position(3), 4));
        broker.Status = id =>
        {
            if (id == $"order-{InterruptedOrder.RequestId:D}") throw new TimeoutException();
            if (id == $"order-{otherBuy.RequestId:D}") return Cancellation with { ExternalId = id,
                SecurityId = otherBuy.SecurityId, Symbol = otherBuy.Symbol };
            var sale = Assert.Single(broker.Sent);
            Assert.Equal($"order-{sale.RequestId:D}", id);
            return SellFact(sale, "FILLED", 10);
        };
        var result = await broker.Restart().RunAsync(Binding);
        Assert.Equal("send", broker.Calls.First());
        var sale = Assert.Single(broker.Sent);
        Assert.Equal(Triggered().ExitDecisionId, sale.DecisionId);
        Assert.Equal(10, sale.Quantity);
        Assert.Equal("TEST2", sale.Symbol);
        Assert.Equal(InterruptedOrder.RequestId, Assert.Single(result.Current.Orders).RequestId);
        AssertAllowance(result.Current, 38, 100);
        Assert.Equal(before.Account with { Version = result.Current.Account.Version, ReservedPurchaseCost = 100,
            CommittedPurchaseCost = 38, ObservedCash = 962, ObservedBuyingPower = 950,
            BrokerObservedUtc = RecoveryAt.UtcDateTime }, result.Current.Account);
        Assert.Equal(before.Settings, result.Current.Settings);
        Assert.Equal(before.Restrictions.Single(), result.Current.Restrictions.Single());
        Assert.Equal(before.Symbols.Single() with { Version = result.Current.Symbols.Single().Version,
            LastExitUtc = RecoveryAt.UtcDateTime, LastExitReason = "FloorTriggered" }, result.Current.Symbols.Single());
        Assert.DoesNotContain(result.Notices, n => n.Action is "Holdings" or "Account observation");
        Assert.Equal(1, broker.Calls.Count(c => c == "account"));
        Assert.Equal("TEST3", Assert.Single(result.Current.Positions).Symbol);
        Assert.Equal(4, result.Current.Positions.Single().Quantity);
        Assert.Contains(result.Notices, n => n.OriginalId == InterruptedOrder.RequestId && n.Reason.Contains("retry"));
        Assert.Equal("Filled", (await db.Store.ReadOrderAsync(Binding, sale.RequestId, default))!.Status);
        int writes = db.CommittedWrites;
        var again = await broker.Restart().RunAsync(Binding);
        Assert.Single(broker.Sent);
        AssertAllowance(again.Current, 38, 100);
        Assert.Equal(writes, db.CommittedWrites);
    }

    [Fact]
    public async Task UnknownSellReplySurvivesRestartAndPartialFillIsAppliedOnce()
    {
        var db = new Database();
        await db.Store.SaveAsync(ActiveBook());
        var broker = new SimulatedAccountBroker(db) { AfterSend = () => throw new TimeoutException() };
        var first = await broker.Restart().RunAsync(Binding);
        var sale = Assert.Single(broker.Sent);
        Assert.Equal(10, first.Current.Positions.Single().ReservedSellQuantity);
        Assert.Contains(first.Notices, n => n.OriginalId == sale.RequestId);
        string status = "PARTIALLY_FILLED";
        broker.Status = id => id == $"order-{sale.RequestId:D}" ? SellFact(sale, status, 4) : null;
        var partial = await broker.Restart().RunAsync(Binding);
        var held = partial.Current.Positions.Single();
        Assert.Equal(6, held.Quantity);
        Assert.Equal(6, held.ReservedSellQuantity);
        Assert.Equal(60m, held.CostBasis);
        Assert.Equal(Triggered().ProtectionFloor, held.ProtectionFloor);
        Assert.Equal(Triggered().ExitDecisionId, held.ExitDecisionId);
        int writes = db.CommittedWrites;
        await broker.Restart().RunAsync(Binding);
        Assert.Equal(writes, db.CommittedWrites);
        status = "CANCELLED";
        var cancelled = await broker.Restart().RunAsync(Binding);
        Assert.Equal(0, cancelled.Current.Positions.Single().ReservedSellQuantity);
        Assert.Equal(6, cancelled.Current.Positions.Single().Quantity);
        Assert.Contains(cancelled.Notices, n => n.OriginalId == sale.RequestId && n.Reason.Contains("Cancelled"));
        var review = await broker.Restart().RunAsync(Binding);
        Assert.Contains(review.Notices, n => n.OriginalId == sale.RequestId && n.Reason.Contains("Cancelled"));
        Assert.Single(broker.Sent);
        AssertAllowance(review.Current, 0, 100);
    }

    [Fact]
    public async Task HoldingMismatchRestrictsOnlyItsSecurityAndNewAgreementClearsOnlyThatCause()
    {
        var db = new Database();
        var missing = Triggered() with { ExitDecisionId = null, ExitReason = null, ExitTriggeredUtc = null };
        await db.Store.SaveAsync(ActiveBook() with { Positions = [missing] });
        var broker = new SimulatedAccountBroker(db) { AccountBook = () => BrokerBook(RecoveryAt.AddSeconds(-1)) };
        var mismatch = await broker.Restart().RunAsync(Binding);
        var held = Assert.Single(mismatch.Current.Restrictions.Where(r => r.ReasonCode == "BrokerHoldingMismatch"));
        Assert.Equal("Security:" + missing.SecurityId, held.ScopeKey);
        Assert.Equal(10, mismatch.Current.Positions.Single().Quantity);
        Assert.Contains(mismatch.Notices, n => n.Action == "Holdings" && n.Symbol == missing.Symbol);

        await db.Store.SaveAsync(Change(mismatch.Current) with { Positions = [Triggered(), Triggered(4)] });
        broker.AccountBook = () => BrokerBook(RecoveryAt, (missing, 10));
        broker.Status = id => broker.Sent.SingleOrDefault(o => $"order-{o.RequestId:D}" == id) is { } sell
            ? SellFact(sell, "FILLED", 10) : null;
        var reconciled = await broker.Restart().RunAsync(Binding);
        Assert.Equal(Triggered(4).SecurityId, Assert.Single(broker.Sent).SecurityId);
        Assert.Contains(reconciled.Notices, n => n.Symbol == missing.Symbol && n.Reason.Contains("BrokerHoldingMismatch"));
        Assert.Equal("StopBuying", Assert.Single(reconciled.Current.Restrictions).ReasonCode);
        broker.AccountBook = () => BrokerBook(RecoveryAt);
        var next = await broker.Restart().RunAsync(Binding);
        Assert.Equal(missing.SecurityId, broker.Sent.Last().SecurityId);
        Assert.Equal(2, broker.Sent.Count);
        Assert.Empty(next.Current.Positions);
    }

    [Fact]
    public async Task BrokerRejectionLeavesLatchedExitVisibleWithoutAnotherSale()
    {
        var db = new Database();
        await db.Store.SaveAsync(ActiveBook());
        var broker = new SimulatedAccountBroker(db);
        broker.Status = id => id == $"order-{InterruptedOrder.RequestId:D}" ? null : SellFact(broker.Sent.Single(), "REJECTED", 0);
        var first = await broker.Restart().RunAsync(Binding);
        var next = await broker.Restart().RunAsync(Binding);
        var sale = Assert.Single(broker.Sent);
        foreach (var pass in new[] { first, next })
        {
            var p = pass.Current.Positions.Single();
            Assert.Equal(10, p.Quantity);
            Assert.Equal(0, p.ReservedSellQuantity);
            Assert.Equal(Triggered().ExitDecisionId, p.ExitDecisionId);
            Assert.Contains(pass.Notices, n => n.OriginalId == sale.RequestId && n.Reason.Contains("Rejected"));
        }
    }

    [Fact]
    public async Task LostReservationReplyRequiresOriginalLookupAndNeverCreatesReplacement()
    {
        var db = new Database();
        await db.Store.SaveAsync(ActiveBook());
        db.LoseCommitReply = true;
        var broker = new SimulatedAccountBroker(db);
        await broker.Restart().RunAsync(Binding);
        var again = await broker.Restart().RunAsync(Binding);
        Assert.Empty(broker.Sent);
        var sale = Assert.Single(again.Current.Orders.Where(o => o.Side == "SELL"));
        Assert.Equal(10, again.Current.Positions.Single().ReservedSellQuantity);
        Assert.Equal(2, broker.Calls.Count(c => c == $"order-{sale.RequestId:D}"));
    }

    [Fact]
    public async Task OverlappingRestartsCannotDispatchTheSameTriggerTwice()
    {
        var db = new Database();
        await db.Store.SaveAsync(ActiveBook());
        var broker = new SimulatedAccountBroker(db);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.AfterSend = async () => { started.SetResult(); await release.Task; };
        var first = broker.Restart().RunAsync(Binding);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await broker.Restart().RunAsync(Binding);
        }
        finally { release.SetResult(); }
        await first;
        Assert.Single(broker.Sent);
        Assert.Single(db.Rows.Values.OfType<TradingOrder>().Where(o => o.Side == "SELL"));
    }

    [Theory]
    [InlineData("older snapshot")]
    [InlineData("wrong account")]
    [InlineData("conflicting cash")]
    public async Task InvalidBrokerSnapshotPreservesSavedBalancesAndExistingRestrictions(string issue)
    {
        var db = new Database();
        var book = ActiveBook();
        await db.Store.SaveAsync(book with { Account = book.Account with { ExecutionEnabled = false, BrokerObservedUtc = Now,
            ObservedCash = 500, ObservedBuyingPower = 450 } });
        var broker = new SimulatedAccountBroker(db)
        {
            AccountBook = () => BrokerBook(issue == "older snapshot" ? Now.AddMinutes(-1) : Now, (Triggered(), 10)),
            SnapshotBinding = issue == "wrong account" ? Id(999) : Binding
        };
        var initial = (await db.Store.ReadAsync(Binding))!;
        var result = await broker.Restart().RunAsync(Binding);
        Assert.Equal(initial.Account, result.Current.Account);
        Assert.Equal(initial.Restrictions.Single(), result.Current.Restrictions.Single());
        Assert.Contains(result.Notices, n => n.Action == "Account observation");
        Assert.Empty(broker.Sent);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("disabled")]
    [InlineData("expired")]
    [InlineData("sell denied")]
    [InlineData("wrong owner")]
    [InlineData("sell restriction")]
    [InlineData("future trigger")]
    [InlineData("no trigger")]
    [InlineData("reserved")]
    [InlineData("overlapping sell")]
    [InlineData("overlapping positions")]
    public async Task NoNewSaleWithoutItsOwnAuthorityTriggerAndAvailableShares(string issue)
    {
        var db = new Database();
        var book = ActiveBook();
        var p = Triggered();
        var scope = new TradingRestrictionCurrent { BindingId = Binding, RestrictionId = Id(79),
            ScopeKey = $"Position:{p.PositionId:D}", ActionCode = "Sell", ReasonCode = "Review", Explanation = "Check this position" };
        book = issue switch
        {
            "paused" => book with { Account = book.Account with { Phase = "Paused" } },
            "disabled" => book with { Account = book.Account with { ExecutionEnabled = false } },
            "expired" => book with { Settings = [Settings with { ExpiresUtc = Now.AddMinutes(1) }] },
            "sell denied" => book with { Settings = [Settings with { SellAllowed = false }] },
            "wrong owner" => book with { Positions = [p with { ProtectionOwner = "other" }] },
            "sell restriction" => book with { Restrictions = [scope] },
            "future trigger" => book with { Positions = [p with { ExitTriggeredUtc = Now.AddDays(1) }] },
            "no trigger" => book with { Positions = [p with { ExitDecisionId = null, ExitReason = null, ExitTriggeredUtc = null }] },
            "reserved" => book with { Positions = [p with { ReservedSellQuantity = 1 }] },
            "overlapping sell" => book with { Orders = [InterruptedOrder, Order(2) with { Side = "SELL", Status = "Unknown" }] },
            "overlapping positions" => book with { Positions = [p, p with { PositionId = Id(88), ExitDecisionId = Id(89) }] },
            _ => throw new ArgumentException(issue)
        };
        await db.Store.SaveAsync(book);
        var broker = new SimulatedAccountBroker(db);
        var result = await broker.Restart().RunAsync(Binding);
        Assert.Empty(broker.Sent);
        Assert.All(result.Current.Positions, holding => Assert.Equal(10, holding.Quantity));
        if (issue != "no trigger") Assert.Contains(result.Notices, n => n.OriginalId != InterruptedOrder.RequestId);
    }
}
