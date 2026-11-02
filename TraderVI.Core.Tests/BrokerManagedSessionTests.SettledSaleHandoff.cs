#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private sealed record SettledSale(Fixture Fixture, BrokerManagedSession Managed, BrokerStrategyOrder Sale,
        BrokerOrderFact Fill, TradingCompactStorageTests.Database Database, AccountRestartResult Recovery);

    private sealed class JournalProtector : IManualBuyProtector
    {
        private readonly HostProtector protector = new();
        internal bool FailNextWrite;
        internal int Writes;
        public byte[] Protect(byte[] data)
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new IOException("Interrupted fixture journal write.");
            }
            Writes++;
            return protector.Protect(data);
        }
        public byte[] Unprotect(byte[] data) => protector.Unprotect(data);
    }

    private static async Task<SettledSale> RecoverFixtureSaleAsync()
    {
        var history = await OngoingFixture();
        var approved = await history.Workflow().ApproveAsync(history.Review);
        var f = history.F;
        f.TransformAccount = null;
        f.Risk = approved.Account.Risk;
        f.Facts.Add(f.Fact("current-cycle-buy", "AAA", "BUY", 10, 100, f.Now.AddMinutes(-15)));
        f.Facts.Add(f.Fact("earlier-buy", "BBB", "BUY", 2, 20, f.Now.AddDays(-1)));
        approved = approved with { Account = f.Project(), ImplementationReview = new BrokerImplementationReview(
            Guid.NewGuid(), approved.RevisionId, Fixture.Hash, "previous", f.ImplementationIdentity, f.Now, f.Now, "Fixture")
            { PermissionRequestId = approved.EffectiveReview.RequestId, RestartPolicy = BrokerImplementationReview.ScopedRestart } };
        f.Strategy = approved.Strategy;
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
        var database = new TradingCompactStorageTests.Database();
        var store = database.Store;
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
        return new(f, managed, sale, fill, database, result);
    }

    [Fact]
    public async Task ClosedSaleRejectsStaleProtectionPublicationAndAcceptsOriginalReconciledBook()
    {
        var (f, managed, sale, fill, database, result) = await RecoverFixtureSaleAsync();
        var store = database.Store;
        var account = result.Current.Account;
        var protection = managed.Protection.Single(p => p.PositionId == sale.PositionId);
        var carried = managed.Protection.Single(p => p.PositionId != sale.PositionId);
        var current = result.Current;
        var terminal = await store.ReadOrderAsync(account.BindingId, sale.RequestId, default);
        Assert.Equal(sale.Quantity, terminal!.AppliedFilledQuantity);
        Assert.Empty(current.Orders);
        var stillProtected = Assert.Single(current.Positions);
        Assert.Equal(carried.PositionId, stillProtected.PositionId);

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

    [Theory]
    [InlineData("uninterrupted")]
    [InlineData("before journal save")]
    [InlineData("after fill before inventory")]
    [InlineData("after journal before managed commit")]
    [InlineData("after sealing before database commit")]
    [InlineData("lost managed commit reply")]
    [InlineData("conflicting net proceeds")]
    [InlineData("wrong original request")]
    [InlineData("corrected broker fee")]
    [InlineData("broker unavailable")]
    [InlineData("same-security pending")]
    [InlineData("unknown pending scope")]
    [InlineData("assignment revoked")]
    [InlineData("different managed decision")]
    [InlineData("nonzero remaining inventory")]
    public async Task OriginalSaleHandoffRecoversAcrossInterruptedAndRepeatedPolls(string scenario)
    {
        var (f, managed, sale, fill, database, recovery) = await RecoverFixtureSaleAsync();
        var binding = managed.BindingId;
        var store = database.Store;
        var permission = managed.EffectiveReview.RequestId;
        var buy = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-CCC", "CCC", "BUY",
            3, 10, "Initial", null, f.Now);
        managed = managed with { Orders = managed.Orders.Add(buy) };
        f.Store.State = managed;
        var account = recovery.Current.Account;
        await store.SaveAsync(new(Guid.NewGuid(), "ReserveOtherBuy", f.Now.UtcDateTime, account.Version, account)
        {
            Orders = [new TradingOrder { BindingId = binding, PermissionId = permission, RequestId = buy.RequestId,
                DecisionId = buy.DecisionId, Side = "BUY", SecurityId = buy.SecurityId, Symbol = buy.Symbol,
                Quantity = buy.Quantity, ReferencePrice = buy.ReferencePrice, CreatedUtc = buy.CreatedAt.UtcDateTime, Status = "Unknown" }]
        });
        recovery = recovery with { Current = (await store.ReadAsync(binding))! };
        var terminal = await store.ReadOrderAsync(binding, sale.RequestId, default);
        var prior = recovery.Current;
        Assert.Equal(30, prior.Account.ReservedPurchaseCost);

        using var dir = new ManualMarketSellWorkflowTests.TempDirectory();
        Directory.CreateDirectory(dir.Path);
        var protector = new HostProtector();
        var dbIdentity = new EngineDatabaseIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var grant = new BrokerExecutionHostGrant(binding, managed.SessionId, managed.Strategy.Fingerprint,
            f.ImplementationIdentity, f.Now, "Fixture", true);
        var observation = managed.Account.Import.Plan.Observation;
        var assignment = new CompactDelphiAssignment(grant, permission, managed.Strategy.VersionId,
            managed.Strategy.PaperAccountId, managed.Account.Risk.BaselineId, observation.IdentityId, observation.AccountId, []);
        (CompactDelphiCheckpoint.From(binding, dbIdentity, assignment, prior) with { ManagedRevision = managed.RevisionId })
            .Write(CompactAccountPreparation.Slot(dir.Path, prior.Account.Version), protector);
        f.Store.Publish = (previous, next, ct) => CompactManagedWriter.PublishAsync(store, previous, next, dbIdentity, dir.Path, protector, ct);

        var native = new ManualMarketSellWorkflowTests.Fixture();
        var intent = new ManualSellIntent(sale.RequestId, observation.IdentityId, observation.AccountId, sale.SecurityId,
            "Fixture", sale.Symbol, "XTSE", sale.Quantity, sale.CreatedAt, native.Broker.Context.Quote, sale.CreatedAt);
        var journalProtector = new JournalProtector();
        string journalDirectory = Path.Combine(dir.Path, "orders");
        using (var journal = new ManualBuyFileJournal(journalDirectory, journalProtector))
            journal.SaveSell(new(intent, WealthsimpleSubmissionStatus.Acknowledged, []));
        ManualSellRecord ReadJournal()
        {
            using var journal = new ManualBuyFileJournal(journalDirectory, journalProtector);
            return Assert.Single(journal.ReadSells());
        }
        var full = new WealthsimpleMarketOrderState("FILLED", sale.Quantity, sale.Quantity,
            new(11, "CAD"), new(fill.GrossValue!.Value, "CAD"), new(fill.NetValue!.Value, "CAD"), new(0, "CAD"), new(0, "CAD"),
            fill.CreatedAt, fill.LastFilledAt, null, "REGULAR", f.Now);
        native.Broker.State = full;
        var flat = new ManualSellInventory(0, true, f.Now) { HasPendingOnSecurity = false };
        native.Broker.Inventory = flat;
        BrokerOrderFact? originalFact = fill;
        var clock = new HostClock(f);
        async Task Poll(bool revoke = false)
        {
            await using var lease = await f.Store.AcquireAsync(binding, default);
            using var journal = new ManualBuyFileJournal(journalDirectory, journalProtector);
            var checks = new ManualMarketSellWorkflow(native.Broker, journal, clock);
            int authorityChecks = 0;
            await CompactManagedSaleHandoff.ReconcileAsync(lease, recovery, store,
                journal.FindSell, (original, _) =>
                {
                    Assert.Equal(sale.RequestId, original.RequestId);
                    return Task.FromResult(originalFact);
                }, checks,
                observation.IdentityId, observation.AccountId, () =>
                {
                    if (++authorityChecks == 2 && revoke) throw new InvalidOperationException("Fixture revocation.");
                }, clock, default);
        }
        if (scenario != "uninterrupted")
        {
            if (scenario == "before journal save") journalProtector.FailNextWrite = true;
            if (scenario == "after fill before inventory") native.Broker.ThrowInventory = true;
            if (scenario == "after journal before managed commit") database.FailAfterWrites = 1;
            if (scenario == "after sealing before database commit")
                database.BeforeNextWriteCommit = () => throw new IOException("Interrupted after sealing the alternate checkpoint.");
            if (scenario == "lost managed commit reply") f.Store.LoseResponseAtEvent = CompactManagedSaleHandoff.Event;
            if (scenario == "conflicting net proceeds") native.Broker.State = full with { FilledNetValue = new(1, "CAD") };
            if (scenario == "wrong original request") originalFact = fill with { ExternalId = "another-original" };
            if (scenario == "corrected broker fee") originalFact = fill with { Fees = 1, GrossValue = fill.GrossValue + 1 };
            if (scenario == "broker unavailable") originalFact = null;
            if (scenario == "same-security pending") native.Broker.Inventory = flat with { HasPendingOnSecurity = true };
            if (scenario == "unknown pending scope") native.Broker.Inventory = flat with { HasPendingOnSecurity = null };
            if (scenario == "nonzero remaining inventory") native.Broker.Inventory = flat with { Quantity = 1 };
            if (scenario == "different managed decision") f.Store.State = managed with
                { Orders = managed.Orders.Replace(sale, sale with { DecisionId = Guid.NewGuid() }) };
            await Assert.ThrowsAnyAsync<Exception>(() => Poll(scenario == "assignment revoked"));
            bool committed = scenario == "lost managed commit reply";
            Assert.Equal(committed, f.Store.State!.Orders.Single(o => o.RequestId == sale.RequestId).Reconciled);
            Assert.Equal(prior.Account.Version + (committed ? 1 : 0), (await store.ReadAsync(binding))!.Account.Version);
            var local = ReadJournal();
            Assert.Equal(scenario is "after journal before managed commit" or "after sealing before database commit"
                or "lost managed commit reply" or "assignment revoked",
                local.IsResolved);
            if (scenario == "after sealing before database commit")
            {
                var selected = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir.Path, prior.Account.Version), protector);
                Assert.True(selected.Matches(binding, dbIdentity, (await store.ReadAsync(binding))!));
                Assert.Equal(managed.RevisionId, selected.ManagedRevision);
                var unused = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir.Path, prior.Account.Version + 1), protector);
                Assert.NotEqual(selected.ManagedRevision, unused.ManagedRevision);
            }
            Assert.Equal(scenario is "before journal save" or "conflicting net proceeds" or "wrong original request"
                or "corrected broker fee" or "broker unavailable" or "different managed decision" ? 0 : 1, local.Observations.Count);
            if (scenario == "different managed decision") f.Store.State = managed;
            originalFact = fill;
            native.Broker.ThrowInventory = false;
            native.Broker.Inventory = flat;
            native.Broker.State = full;
            database.FailAfterWrites = 0;
        }

        f.Store.Restart();
        await Poll();
        var synced = f.Store.State!;
        var aligned = (await store.ReadAsync(binding))!;
        Assert.Equal(managed.Revision + 1, synced.Revision);
        Assert.Equal(prior.Account with { Version = prior.Account.Version + 1 }, aligned.Account);
        Assert.Equal(prior.Settings, aligned.Settings);
        Assert.Equal(prior.Positions.Single(), Assert.Single(aligned.Positions));
        Assert.Equal(prior.Symbols.ToArray(), aligned.Symbols.ToArray());
        Assert.Equal(buy.RequestId, Assert.Single(aligned.Orders).RequestId);
        Assert.Equal(terminal, await store.ReadOrderAsync(binding, sale.RequestId, default));
        Assert.Equal(sale with { Latest = fill, Reconciled = true }, synced.Orders.Single(o => o.Side == "SELL"));
        Assert.Equal(buy, synced.Orders.Single(o => o.Side == "BUY"));
        Assert.DoesNotContain(synced.Account.Account.Holdings, h => h.PositionId == sale.PositionId);
        Assert.Equal(prior.Positions.Single().PositionId, Assert.Single(synced.Account.Positions).PositionId);
        Assert.Equal(fill.FilledQuantity, Assert.Single(synced.Account.Fills.Where(t => t.Side == "SELL" && t.PositionId == sale.PositionId)).Quantity);
        Assert.Equal(BrokerAccountImport.Hash(managed.Protection), BrokerAccountImport.Hash(synced.Protection));
        Assert.Equal(BrokerAccountImport.Hash(managed.Account.Risk), BrokerAccountImport.Hash(synced.Account.Risk));
        Assert.Equal(managed.EffectiveReview.PurchaseCeiling - managed.CommittedPurchaseCost,
            synced.EffectiveReview.PurchaseCeiling - synced.CommittedPurchaseCost);
        var seal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir.Path, aligned.Account.Version), protector);
        Assert.True(seal.Matches(binding, dbIdentity, aligned));
        Assert.Equal(synced.RevisionId, seal.ManagedRevision);

        var journalWrites = journalProtector.Writes;
        for (int retry = 0; retry < 2; retry++)
        {
            f.Store.Restart();
            await Poll();
            Assert.Equal(synced.RevisionId, f.Store.State!.RevisionId);
            Assert.Equal(aligned.Account.Version, (await store.ReadAsync(binding))!.Account.Version);
        }
        Assert.Equal(journalWrites, journalProtector.Writes);
        var settled = ReadJournal();
        Assert.True(settled.IsResolved);
        Assert.Single(settled.Observations);
        Assert.Equal(sale.RequestId, native.Broker.LastReadId);
        Assert.Equal(0, native.Broker.Submissions);
        Assert.Equal(0, f.Transport.Sent);

        // Exercise the managed protection branch with the unrelated buy still outstanding.
        bool evaluatedRemainingHolding = false;
        f.TransformInput = input =>
        {
            evaluatedRemainingHolding = input.CurrentHoldings.Length == 1 &&
                input.CurrentHoldings[0].PositionId == aligned.Positions.Single().PositionId &&
                input.ActualFills.Any(o => o.Side == "SELL" && o.PositionId == sale.PositionId);
            return input;
        };
        await using (var protectionLease = await f.Store.AcquireAsync(binding, default))
        {
            var protectedState = await f.Workflow().CheckProtectionDuringRestartAsync(protectionLease, default);
            Assert.Equal("RestartProtectionChecked", protectedState.Event);
        }
        Assert.True(evaluatedRemainingHolding);
        Assert.Equal(terminal, await store.ReadOrderAsync(binding, sale.RequestId, default));
        Assert.Equal(0, f.Transport.Sent);

        if (scenario == "uninterrupted")
        {
            // Once the independent buy has ended, ordinary import can supersede the scoped report.
            // The paused fixture exercises the usual Tick synchronization without enabling decisions.
            await f.Workflow().PauseAsync(binding);
            var posted = fill with { Status = "POSTED", Symbol = sale.Symbol + ".TO" };
            f.Facts.Add(posted);
            f.Facts.Add(f.Fact(buy.ExternalId, buy.Symbol, "BUY", 0, 0, f.Now) with
                { Status = "CANCELLED", SubmittedQuantity = buy.Quantity, LastFilledAt = null });
            f.TransformAccount = a => a with { Import = a.Import with { Plan = a.Import.Plan with {
                Observation = a.Import.Plan.Observation with { Instruments = [new(sale.SecurityId, posted.Symbol, "XTSE", "CAD", "EQUITY")] } } } };
            var listing = f.TransformAccount(f.Project()).Import.Plan.Observation;
            var reconciledSale = synced.Orders.Single(o => o.RequestId == sale.RequestId);
            var reviewable = synced with { Orders = [reconciledSale] };
            var preSale = synced.Account.Import.Plan.Observation;
            var complete = listing with { Orders = preSale.Orders.Append(posted).ToArray(), Cash = preSale.Cash + posted.NetValue };
            Assert.True(CompactManagedSaleHandoff.MatchesReviewedAccount(reviewable, complete));
            Assert.False(CompactManagedSaleHandoff.MatchesReviewedAccount(reviewable, preSale));
            Assert.False(CompactManagedSaleHandoff.MatchesReviewedAccount(reviewable, complete with { Cash = complete.Cash + 1 }));
            Assert.False(CompactManagedSaleHandoff.MatchesReviewedAccount(reviewable, complete with { Orders = complete.Orders.Append(
                posted with { ExternalId = "outside-order" }).ToArray() }));
            Assert.False(CompactManagedSaleHandoff.MatchesFirstImport(synced, reconciledSale, posted, listing with { Instruments = null }));
            Assert.False(CompactManagedSaleHandoff.MatchesFirstImport(synced, reconciledSale, posted with { Fees = 1 }, listing));
            var imported = await f.Workflow().TickAsync(binding);
            Assert.Equal("AccountSynchronized", imported.Event);
            Assert.Equal("Verified broker projection", imported.Account.Account.ReportedBy);
            Assert.All(imported.Orders, o => Assert.True(o.Reconciled));
            Assert.Single(imported.Account.Fills.Where(t => t.Side == "SELL" && t.PositionId == sale.PositionId));
            Assert.Equal(terminal, await store.ReadOrderAsync(binding, sale.RequestId, default));
            Assert.Equal(0, f.Transport.Sent);
        }
    }
}
