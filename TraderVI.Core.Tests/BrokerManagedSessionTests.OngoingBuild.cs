#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Storage;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using Dapper;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData("valid")][InlineData("scoped")][InlineData("stale")][InlineData("cash")][InlineData("pending")]
    [InlineData("protection")][InlineData("risk")][InlineData("source")]
    public async Task OngoingInvestedBuildReviewPreservesAuthorityAndRequiresUnchangedCheckedBook(string scenario)
    {
        var x = await OngoingFixture(); var prior = await x.Workflow().ApproveAsync(x.Review);
        x.F.Now = new(2026,10,23,14,0,0,TimeSpan.Zero);
        var bought = x.F.Now.AddMinutes(-1);
        var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "owned-security", "FIX", "BUY", 2, 10, "Initial", null,
            bought, WealthsimpleSubmissionStatus.Acknowledged);
        var fact = new BrokerOrderFact(order.ExternalId, order.SecurityId, "FIX.TO", "BUY", "POSTED", "CAD", "EQUITY", 2, 2, 20, 20, 0, 0, bought, bought.AddSeconds(1));
        order = order with { Latest = fact, Reconciled = true };
        var position = new BrokerPositionCycle(Guid.NewGuid(), order.SecurityId, fact.Symbol, 2, 20, bought.AddSeconds(1), order.ExternalId, null);
        var observed = prior.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now,
            Cash = prior.OngoingManagement!.CashAnchor.Cash - 20,
            Orders = prior.OngoingManagement.CashAnchor.Orders.Append(fact).ToArray(),
            Holdings = [new(order.SecurityId, fact.Symbol, 2, 2, 20, 20, bought.AddSeconds(1), 0)],
            Instruments = [new(order.SecurityId, fact.Symbol, "XTSE", "CAD", "EQUITY")] };
        var account = prior.Account with { Positions = [position],
            Import = prior.Account.Import with { Plan = prior.Account.Import.Plan with { Observation = observed } },
            Account = prior.Account.Account with { ReportedUtc = x.F.Now.UtcDateTime,
                Holdings = [new(position.PositionId, position.Symbol, 2, 20, new(2026,10,23))] } };
        prior = prior with { Account = account, Orders = [order], NewBuysStopped = true,
            Protection = [BrokerLiveProtection.Transfer(prior.Strategy, position, null, bought)] };
        if (scenario == "pending") prior = prior with { Orders = [order with { Reconciled = false }] };
        if (scenario == "protection") prior = prior with { Protection = [] };
        if (scenario == "risk") prior = prior with { Memory = prior.Memory with { DailyLossHold = true } };
        var fresh = account;
        if (scenario == "scoped")
        {
            order = order with { PositionTarget = 25m };
            var fill = new TradingActualFill(BrokerManagedStrategy.StableId($"{prior.BindingId:D}:{order.ExternalId}"),
                position.PositionId, fact.Symbol, "BUY", bought.AddSeconds(1).UtcDateTime, new(2026, 10, 23),
                2, 10m, null, order.Kind, order.PositionTarget);
            prior = prior with { Orders = [order], Account = account with { Fills = [fill] },
                ImplementationReview = new(Guid.NewGuid(), prior.RevisionId, BrokerAccountImport.Hash(prior),
                    "earlier-build", prior.Strategy.Strategy.ImplementationIdentity, bought, bought, "fixture")
                { RestartPolicy = BrokerImplementationReview.ScopedRestart, PermissionRequestId = prior.EffectiveReview.RequestId } };
            // Fresh broker history contains fill facts. Only the original strategy order has the target.
            fresh = account with { Fills = [fill with { RecommendationKind = null, PositionTarget = null }] };
            Assert.True(prior.HasReviewedCompactRestart);
        }
        x.F.Store.State = prior;
        if (scenario == "stale") fresh = account with { Account = account.Account with { ReportedUtc = x.F.Now.AddMinutes(-1).UtcDateTime } };
        if (scenario == "cash") fresh = account with { Import = account.Import with { Plan = account.Import.Plan with { Observation = observed with { Cash = observed.Cash + 1 } } } };
        x.F.TransformAccount = _ => fresh;
        x.F.RunningImplementation = "repaired-build";
        var current = x.Existing.Current with { PreparedUtc = x.F.Now.UtcDateTime,
            Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = x.F.ImplementationIdentity } } };
        BrokerImplementationReviewProposal Prepare() => BrokerImplementationReviewWorkflow.Prepare(prior, current,
            scenario == "source" ? x.F.Now.AddMinutes(-3) : x.F.Now, fresh, true, x.F.ImplementationIdentity, "fixture", SelectedSessionCalendar(), x.F.Now,
            reviewScopedRestart: scenario == "scoped");
        if (scenario is not ("valid" or "scoped")) { Assert.Throws<InvalidOperationException>(() => Prepare()); Assert.Equal(0, x.F.Transport.Sent); return; }
        var proposal = Prepare();
        var workflow = new BrokerImplementationReviewWorkflow(x.F.Store, x.F, x.Existing.Grants, SelectedSessionCalendar(), new HostClock(x.F));
        var saved = await workflow.ApproveAsync(proposal);
        if (scenario == "scoped")
        {
            Assert.Equal(prior.Account.Fills.Single(), saved.Account.Fills.Single());
            await CheckImmutableBuildSuccessorAsync(prior, saved, x.F.Now);
        }
        Assert.Equal(scenario == "scoped", saved.HasReviewedCompactRestart);
        var changed = saved with { ImplementationReview = saved.ImplementationReview! with
            { RestartPolicy = scenario == "scoped" ? null : BrokerImplementationReview.ScopedRestart } };
        Assert.Throws<InvalidOperationException>(() => BrokerImplementationReviewWorkflow.ValidateAppend(prior, changed, SelectedSessionCalendar()));
        Assert.Equal(x.F.ImplementationIdentity, saved.Strategy.Strategy.ImplementationIdentity);
        Assert.Equal(prior.OngoingManagement, saved.OngoingManagement);
        Assert.Equal(prior.Orders, saved.Orders); Assert.Equal(prior.Protection, saved.Protection);
        Assert.Equal(prior.Memory, saved.Memory); Assert.True(saved.NewBuysStopped);
        Assert.Equal(prior.CommittedPurchaseCost, saved.CommittedPurchaseCost);
        Assert.Equal(prior.Account.Risk, saved.Account.Risk);
        var diagnostic = EngineBrokerDiagnostics.From(saved, true).Protection!;
        Assert.True(diagnostic.NewBuysStopped);
        Assert.Equal(1, diagnostic.OwnedOrders); Assert.Equal(1, diagnostic.ReconciledOrders);
        Assert.True(Assert.Single(diagnostic.Holdings).SavedProtectionMatches);
        Assert.Null(Assert.Single(diagnostic.Holdings).LastUsableBidUtc);
        Assert.Null(x.Existing.Grants.Value); Assert.Equal(0, x.F.Transport.Sent);
        BrokerManagedSessionRepository.ValidateNext(prior, saved, SelectedSessionCalendar());
        x.F.Store.Restart(); Assert.Equal(saved.Strategy.Fingerprint, x.F.Store.State!.Strategy.Fingerprint);
        Assert.Equal(saved.RevisionId, (await workflow.ApproveAsync(proposal)).RevisionId);
        var altered = saved with { ImplementationReview = saved.ImplementationReview! with { PermissionRequestId = Guid.NewGuid() } };
        Assert.Equal(prior.Strategy.Strategy.ImplementationIdentity, altered.Strategy.Strategy.ImplementationIdentity);
        Assert.ThrowsAny<Exception>(() => BrokerManagedSessionRepository.ValidateNext(prior, altered, SelectedSessionCalendar()));
    }

    private static async Task CheckImmutableBuildSuccessorAsync(BrokerManagedSession prior, BrokerManagedSession next, DateTimeOffset now)
    {
        var grant = new BrokerExecutionHostGrant(prior.BindingId, prior.SessionId, prior.Strategy.Fingerprint,
            prior.Strategy.Strategy.ImplementationIdentity, now, "fixture", true)
            { RestoreAfterRestart = true, ContinuationId = prior.OngoingManagement!.RequestId };
        var original = RestartAccountRehearsal.ProjectSettled(prior, grant, grant.ImplementationIdentity, Fixture.Hash, now, true);
        original = original with { Settings = original.Settings with { BuyAllowed = true, SellAllowed = true } };
        var memory = new TradingCompactStorageTests.Database();
        RestartRehearsalDatabase? native = Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_COMPACT_SUCCESSOR_TEST"),
            "N", out var token) ? new(token) : null;
        ITradingStorageDatabase db = native == null ? memory : new SqlTradingStorageDatabase(native.OpenAsync);
        var store = new TradingCurrentStore(db);
        if (native != null)
        {
            await using var c = await native.OpenAsync();
            await c.ExecuteAsync("INSERT dbo.BrokerAccountImportBinding VALUES (@BindingId,N'TFSA',REPLICATE('A',64),REPLICATE('B',64),SYSUTCDATETIME(),N'Synthetic');",
                new { prior.BindingId });
        }
        await using (var tx = await db.BeginAsync(prior.BindingId, true, default))
        {
            await RestartAccountRehearsal.InsertInitialAsync(tx, original, default);
            await tx.CommitAsync(default);
        }
        using var dir = new ManualMarketSellWorkflowTests.TempDirectory();
        System.IO.Directory.CreateDirectory(dir.Path);
        var protector = new HostProtector();
        var id = new EngineDatabaseIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        original = (await store.ReadAsync(prior.BindingId))!;
        var assignment = CompactDelphiAssignment.Capture(prior, grant, original, grant.ImplementationIdentity, now);
        var initial = CompactDelphiCheckpoint.From(prior.BindingId, id, assignment, original) with { ManagedRevision = prior.RevisionId };
        initial.Write(CompactAccountPreparation.Slot(dir.Path, 0), protector);
        async Task Publish(bool commit)
        {
            if (native == null)
            {
                if (!commit) memory.BeforeNextWriteCommit = () => throw new System.IO.IOException("Interrupt after sealing.");
                await CompactManagedWriter.PublishAsync(store, prior, next, id, dir.Path, protector, default);
                return;
            }
            await using var c = await native.OpenAsync();
            using var tx = c.BeginTransaction();
            await CompactManagedWriter.PublishAsync(SqlTradingStorageDatabase.InTransaction(c, tx), prior, next, id, dir.Path, protector, default);
            if (commit) tx.Commit();
            else { tx.Rollback(); throw new System.IO.IOException("Rollback after sealing."); }
        }
        await Assert.ThrowsAsync<System.IO.IOException>(() => Publish(false));
        var unchanged = (await store.ReadAsync(prior.BindingId))!;
        Assert.Equal(original.Settings, unchanged.Settings);
        Assert.True(initial.Matches(prior.BindingId, id, unchanged));
        await Publish(true);
        var current = (await store.ReadAsync(prior.BindingId))!;
        var seal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir.Path, current.Account.Version), protector);
        Assert.Equal(next.RevisionId, seal.ManagedRevision);
        Assert.True(seal.Matches(prior.BindingId, id, current));
        await using (var tx = await db.BeginAsync(prior.BindingId, false, default))
        {
            Assert.Equal(original.Settings, await tx.FindAsync<TradingApprovedSettings>(original.Settings.PermissionId, default));
            await tx.CommitAsync(default);
        }
        Assert.Equal(next.RevisionId, current.Settings.PermissionId);
        Assert.Equal(original.Settings.PermissionId, current.Settings.FinancialPermissionId);
        Assert.Equal(original.Account with { Version = 1, PermissionId = next.RevisionId, StrategyId = next.Strategy.VersionId }, current.Account);
        Assert.Equal(original.Positions, current.Positions.ToArray());
        Assert.All(current.Positions, p => Assert.Equal(original.Settings.OwnsProtection(p), current.Settings.OwnsProtection(p)));
        Assert.False(seal.Assignment.Grant.Enabled);
        var build = next.Strategy.Strategy.ImplementationIdentity;
        Assert.Throws<InvalidOperationException>(() => seal.Assignment.RequireAssignment(current, grant, build, now));
        Assert.Throws<InvalidOperationException>(() => seal.Assignment.RequireAssignment(current, seal.Assignment.Grant, build, now));
        var enabled = grant with { StrategyFingerprint = next.Strategy.Fingerprint, ImplementationIdentity = build };
        seal.Assignment.RequireAssignment(current, enabled, build, now);
        var later = next with { PreviousRevisionId = next.RevisionId, RevisionId = Guid.NewGuid(), Revision = next.Revision + 1 };
        later = later with { Event = "AccountSynchronized" };
        await CompactManagedWriter.PublishAsync(store, next, later, id, dir.Path, protector, default);
        Assert.Equal(current.Settings.PermissionId, (await store.ReadAsync(prior.BindingId))!.Account.PermissionId);
    }

    [Theory]
    [InlineData("original", true)]
    [InlineData("different decision", false)]
    [InlineData("different owner", false)]
    [InlineData("different session", false)]
    [InlineData("missing receipt", false)]
    public async Task MissingCompactOwnerRequiresTheOpeningIntentAndItsReconciledFill(string evidence, bool recognized)
    {
        var fixture = await OngoingFixture();
        var source = await fixture.Workflow().ApproveAsync(fixture.Review);
        var at = source.SavedAt.AddMinutes(1);
        var intentOrder = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security", "FIX",
            "BUY", 3, 10, "Initial", 30, at);
        var filled = at.AddSeconds(2);
        var fact = new BrokerOrderFact(intentOrder.ExternalId, "security", "FIX.TO", "BUY", "POSTED", "CAD",
            "EQUITY", 3, 3, 30, 30, 0, 0, at, filled);
        var order = intentOrder with { Latest = fact, Reconciled = true, Submission = WealthsimpleSubmissionStatus.Acknowledged };
        var id = BrokerManagedStrategy.StableId($"{source.BindingId:D}:security:{order.ExternalId}");
        var held = new BrokerPositionCycle(id, "security", "FIX.TO", 3, 30, filled, order.ExternalId, null);
        var fill = new TradingActualFill(BrokerManagedStrategy.StableId($"{source.BindingId:D}:{order.ExternalId}"),
            id, held.Symbol, "BUY", filled.UtcDateTime, DateOnly.FromDateTime(filled.Date), 3, 10, null, "Initial", 30);
        var protection = BrokerLiveProtection.Transfer(source.Strategy, held, null, filled);
        if (evidence == "different owner") protection = protection with { Owner = Guid.NewGuid().ToString("D") };
        var intent = source with { Event = "OrderIntentReserved", SavedAt = at, Orders = [intentOrder] };
        if (evidence == "different decision") intent = intent with { Orders = [intentOrder with { DecisionId = Guid.NewGuid() }] };
        if (evidence == "different session") intent = intent with { SessionId = Guid.NewGuid() };
        var saved = source with { Revision = source.Revision + 2, Account = source.Account with { Positions = [held], Fills = [fill] },
            Orders = [order], Protection = [protection] };
        var positions = RestartAccountRehearsal.Positions(saved);
        var settings = new TradingApprovedSettings { StrategyId = Guid.NewGuid(), PermissionId = source.EffectiveReview.RequestId,
            BaselineId = source.EffectiveReview.BaselineId };
        var book = new TradingCurrentState(RestartAccountRehearsal.Account(saved, true), settings, positions, [], [], [], []);
        var repaired = await CompactOpeningProtection.RestoreAsync(saved, book,
            (_, _, _, _) => Task.FromResult(evidence == "missing receipt" ? null : intent), default);
        Assert.False(settings.OwnsProtection(positions.Single()));
        Assert.Equal(recognized, repaired.OwnsProtection(positions.Single()));
        Assert.Equal(repaired, await CompactOpeningProtection.RestoreAsync(saved, book with { Settings = repaired },
            (_, _, _, _) => Task.FromResult<BrokerManagedSession?>(null), default));
    }
}
