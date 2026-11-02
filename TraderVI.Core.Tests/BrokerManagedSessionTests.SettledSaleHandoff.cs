#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Fact]
    public async Task ClosedSaleRejectsStaleProtectionPublicationAndAcceptsOriginalReconciledBook()
    {
        var f = new Fixture();
        UseNormalLive(f);
        f.Facts.Add(f.Fact("earlier-buy", "BBB", "BUY", 2, 20, f.Now.AddDays(-1)));
        var approved = await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        var holding = approved.Account.Positions.Single(p => p.Symbol == "AAA");
        var retained = approved.Account.Positions.Single(p => p.Symbol == "BBB");
        var permission = approved.EffectiveReview.RequestId;
        var decision = Guid.NewGuid();
        var exit = new BrokerProtectiveExitDecision(decision, permission, "RiskExit", f.Now.UtcDateTime);
        var protection = BrokerLiveProtection.Transfer(f.Strategy, holding, null, f.Now) with { FirstExit = exit };
        var carried = BrokerLiveProtection.Transfer(f.Strategy, retained, null, f.Now) with
        {
            Owner = "previous-owner", CarriedFloor = 9, FirstExit = exit with { DecisionId = Guid.NewGuid() }
        };
        var sale = new BrokerStrategyOrder(BrokerManagedStrategy.StableId(decision.ToString("D")), decision,
            holding.PositionId, holding.SecurityId, holding.Symbol, "SELL", holding.Quantity, 11, exit.Reason,
            null, f.Now, WealthsimpleSubmissionStatus.Acknowledged);
        var managed = approved with { Phase = BrokerManagedPhase.Active, ActivatedAt = f.Now,
            Event = "OrderTransmissionObserved", Protection = [protection, carried], Orders = [sale] };
        var account = RestartAccountRehearsal.Account(managed, true);
        var settings = new TradingApprovedSettings { BindingId = account.BindingId, AssignmentId = account.AssignmentId,
            StrategyId = account.StrategyId, PermissionId = permission, BaselineId = account.BaselineId,
            SourceId = f.Strategy.PaperAccountId, ImplementationIdentity = f.ImplementationIdentity, ScopeCode = "Fixture",
            ApprovedUtc = f.Now.UtcDateTime, PurchaseCeiling = managed.EffectiveReview.PurchaseCeiling, SellAllowed = true };
        var positions = RestartAccountRehearsal.Positions(managed);
        var position = positions.Single(p => p.PositionId == holding.PositionId);
        var retainedPosition = positions.Single(p => p.PositionId == retained.PositionId);
        var receipt = new TradingOrder { BindingId = account.BindingId, PermissionId = permission, DecisionId = decision,
            RequestId = sale.RequestId, PositionId = position.PositionId, SecurityId = holding.SecurityId,
            Symbol = holding.Symbol, Side = "SELL", Quantity = sale.Quantity, ReservedQuantity = sale.Quantity,
            ReferencePrice = sale.ReferencePrice, CreatedUtc = sale.CreatedAt.UtcDateTime, Status = "Submitted" };
        var store = new TradingCompactStorageTests.Database().Store;
        await store.SaveAsync(new(Guid.NewGuid(), "ReserveSale", f.Now.UtcDateTime, -1, account)
        {
            Settings = [settings], Positions = [position with { ReservedSellQuantity = sale.Quantity }, retainedPosition],
            Orders = [receipt], Symbols = CompactDelphiAssignment.CurrentSymbols(managed)
        });

        f.Now = f.Now.AddSeconds(15);
        var fill = f.Fact(sale.ExternalId, sale.Symbol, "SELL", sale.Quantity, sale.Quantity * 11m, f.Now);
        var projected = managed.Account.Import.Plan.Observation;
        var snapshot = new WealthsimpleTfsaSnapshot(new(projected.AccountId, "Fixture"), f.Now, null,
            new(1000, "CAD"), new(900, "CAD"), null, null, null, null, null,
            [new WealthsimpleHolding(retained.Symbol, "Fixture", "EQUITY", retained.Quantity, null, null, null)
                { SecurityId = retained.SecurityId }]);
        var recovery = new OfflineAccountRestart(store, (binding, original, _) =>
        {
            Assert.Equal(account.BindingId, binding);
            Assert.Equal(sale.ExternalId, original);
            return Task.FromResult<BrokerOrderFact?>(fill);
        }, (binding, _) => Task.FromResult((binding, snapshot)),
            (_, _) => throw new InvalidOperationException("A repeated sell is forbidden."),
            _ => throw new InvalidOperationException("An existing order needs no dispatch lease."),
            (_, _) => throw new InvalidOperationException("Unexpected purchase."), new HostClock(f));
        var result = await recovery.RunAsync(account.BindingId);
        var notice = Assert.Single(result.Notices);
        Assert.Equal(retained.Symbol, notice.Symbol);
        Assert.Contains("protection owner", notice.Reason);
        var current = result.Current;
        var terminal = await store.ReadOrderAsync(account.BindingId, sale.RequestId, default);
        Assert.Equal(sale.Quantity, terminal!.AppliedFilledQuantity);
        Assert.Empty(current.Orders);
        var stillProtected = Assert.Single(current.Positions);
        Assert.Equal(retained.PositionId, stillProtected.PositionId);

        // Managed protection advances against a position the compact book has already closed.
        var stale = managed with { PreviousRevisionId = managed.RevisionId, RevisionId = Guid.NewGuid(),
            Revision = managed.Revision + 1, SavedAt = f.Now, Event = "LiveProtectionPrepared",
            Protection = [protection with { ThroughUtc = f.Now.UtcDateTime }, carried] };
        var rejected = CompactManagedPublication.Prepare(managed, stale, current);
        Assert.True(Assert.Single(rejected.Positions).IsOpen);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(rejected));
        Assert.Equal("A closed position cycle cannot be reopened by a repeated fill.", error.Message);
        Assert.Equal(current.Account, (await store.ReadAsync(account.BindingId))!.Account);

        // Once the existing account and original receipt agree, publication needs no second position change.
        f.Facts.Add(fill);
        var synced = stale with { Event = "AccountSynchronized", Account = f.Project(), Protection = managed.Protection,
            Orders = [sale with { Latest = fill, Reconciled = true }] };
        var aligned = CompactManagedPublication.Prepare(managed, synced, current);
        Assert.Empty(aligned.Positions);
        await store.SaveAsync(aligned);
        Assert.True((await store.SaveAsync(aligned)).AlreadyCommitted);
        var after = (await store.ReadAsync(account.BindingId))!;
        Assert.Equal(stillProtected, Assert.Single(after.Positions));
        Assert.Equal(terminal, await store.ReadOrderAsync(account.BindingId, sale.RequestId, default));
        Assert.Equal(current.Settings, after.Settings);
        Assert.Equal(current.Account.RiskEvidenceId, after.Account.RiskEvidenceId);
        Assert.Equal(current.Account.SessionReference, after.Account.SessionReference);
        Assert.Equal(current.Account.DrawdownReference, after.Account.DrawdownReference);
        Assert.Equal(current.Account.CommittedPurchaseCost, after.Account.CommittedPurchaseCost);
        Assert.Equal(current.Account.ReservedPurchaseCost, after.Account.ReservedPurchaseCost);
        Assert.Equal(0, f.Transport.Sent);
    }
}
