#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Orders;
using Dapper;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData("approved")]
    [InlineData("different-build")]
    [InlineData("different-database")]
    [InlineData("disabled")]
    [InlineData("old-terms")]
    [InlineData("weekend")]
    [InlineData("unexpected-risk")]
    public Task InitialCompactTransferRequiresTheExactAssignmentAndCannotReplaceAnExistingBook(string scenario) =>
        VerifyTransferAsync(scenario);

    private sealed class CompactTransferFactAttribute : FactAttribute
    {
        public CompactTransferFactAttribute()
        {
            if (!Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_COMPACT_TRANSFER_TEST"), "N", out _))
                Skip = "Requires a new marked disposable LocalDB.";
        }
    }

    [CompactTransferFact]
    public Task NativeCompactAndStrategyChangesCommitTogether() => VerifyTransferAsync("approved",
        new RestartRehearsalDatabase(Guid.ParseExact(Environment.GetEnvironmentVariable("TRADERVI_COMPACT_TRANSFER_TEST")!, "N")));

    private async Task VerifyTransferAsync(string scenario, RestartRehearsalDatabase? rehearsal = null)
    {
        var fixture = await OngoingFixture();
        var saved = await fixture.Workflow().ApproveAsync(fixture.Review);
        if (scenario is "weekend" or "unexpected-risk")
            fixture.F.Store.State = saved = saved with { Account = saved.Account with { NewRiskBlocks =
                [scenario == "weekend" ? BrokerAccountProjector.RiskSessionContinuationRequired : "Unverified cash change"] } };
        if (scenario != "old-terms")
        {
            var reviewed = new BrokerImplementationReview(Guid.NewGuid(), saved.RevisionId, "fixture-hash", "prior-build",
                fixture.F.ImplementationIdentity, fixture.F.Now, fixture.F.Now, "Fixture")
                { PermissionRequestId = saved.EffectiveReview.RequestId, RestartPolicy = BrokerImplementationReview.ScopedRestart };
            fixture.F.Store.State = saved = saved with { ImplementationReview = reviewed };
        }
        ITradingStorageDatabase sql = rehearsal == null ? new TradingCompactStorageTests.Database() : new SqlTradingStorageDatabase(rehearsal.OpenAsync);
        var store = new TradingCurrentStore(sql);
        var run = new EngineContinuityTests.Store();
        if (rehearsal != null)
        {
            await using var admin = await rehearsal.OpenAsync();
            await admin.ExecuteAsync("""
                INSERT dbo.BrokerAccountImportBinding VALUES (@BindingId,N'TFSA',REPLICATE('A',64),REPLICATE('B',64),SYSUTCDATETIME(),N'Synthetic');
                CREATE TABLE dbo.CompactTransferSource(RevisionId uniqueidentifier NOT NULL);
                INSERT dbo.CompactTransferSource VALUES (@RevisionId);
                """, new { saved.BindingId, saved.RevisionId });
            run.Identity = await EngineContinuityRepository.IdentityAsync(admin, null!, default);
        }
        run.Value = new(1, run.Identity, Guid.NewGuid(), 17, false);
        var witness = new EngineContinuityTests.MemoryFile { Value = scenario == "different-database" ? null : run.Value };
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var protector = new HostProtector();
            var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(dir, "grant"), protector);
            var grant = new BrokerExecutionHostGrant(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint,
                fixture.F.ImplementationIdentity, DateTimeOffset.UtcNow, "Fixture", scenario != "disabled")
                { RestoreAfterRestart = true, ContinuationId = saved.OngoingManagement!.RequestId };
            grants.Write(null, grant);
            using var journal = new ManualBuyFileJournal(dir, protector);
            var prepare = new CompactAccountPreparation(sql, run, witness, fixture.F.Store, grants, journal, protector, dir);
            var build = scenario == "different-build" ? "unapproved" : grant.ImplementationIdentity;
            if (scenario is not ("approved" or "weekend"))
            {
                await Assert.ThrowsAnyAsync<Exception>(() => prepare.PrepareAsync(build, default));
                Assert.Null(await store.ReadAsync(saved.BindingId));
                return;
            }
            await prepare.PrepareAsync(build, default);
            var current = (await store.ReadAsync(saved.BindingId))!;
            Assert.Equal(saved.NewBuysStopped, current.Account.NewBuysStopped);
            Assert.Equal(saved.Account.Risk.TradingDate.ToDateTime(TimeOnly.MinValue), current.Account.TradingDate);
            Assert.Equal(saved.Account.Risk.SessionReference.Value, current.Account.SessionReference);
            Assert.Equal(saved.Account.NewRiskBlocks, fixture.F.Store.State!.Account.NewRiskBlocks);
            Assert.Equal(saved.CommittedPurchaseCost, current.Account.CommittedPurchaseCost);
            var sealedBook = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir, 0), protector);
            Assert.True(sealedBook.Matches(saved.BindingId, run.Identity, current));
            Assert.True(current.Settings.BuyAllowed && current.Settings.SellAllowed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => prepare.PrepareAsync(build, default));
            Assert.Equal(0, (await store.ReadAsync(saved.BindingId))!.Account.Version);
            var buy = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "fixture-security", "FIX", "BUY", 10, 10, "Initial", null, saved.SavedAt);
            var next = saved with { Revision = saved.Revision + 1, RevisionId = Guid.NewGuid(),
                PreviousRevisionId = saved.RevisionId, NewBuysStopped = !saved.NewBuysStopped, Orders = saved.Orders.Add(buy) };
            if (rehearsal == null) await CompactManagedWriter.PublishAsync(store, saved, next, run.Identity, dir, protector, default);
            else
            {
                foreach (bool commit in new[] { false, true })
                {
                    await using var c = await rehearsal.OpenAsync();
                    using var t = c.BeginTransaction();
                    await c.ExecuteAsync("UPDATE dbo.CompactTransferSource SET RevisionId=@RevisionId;", new { next.RevisionId }, t);
                    await CompactManagedWriter.PublishAsync(SqlTradingStorageDatabase.InTransaction(c,t), saved, next,
                        run.Identity, dir, protector, default);
                    if (commit) t.Commit(); else t.Rollback();
                    Assert.Equal(commit ? next.RevisionId : saved.RevisionId,
                        await c.QuerySingleAsync<Guid>("SELECT RevisionId FROM dbo.CompactTransferSource;"));
                    var reread = (await store.ReadAsync(saved.BindingId))!;
                    Assert.Equal(commit ? 100m : 0m, reread.Account.ReservedPurchaseCost);
                    var receipt = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir, reread.Account.Version), protector);
                    Assert.True(receipt.Matches(saved.BindingId, run.Identity, reread));
                }
            }
            var changed = (await store.ReadAsync(saved.BindingId))!;
            Assert.Equal(100m, changed.Account.ReservedPurchaseCost);
            Assert.Equal(buy.RequestId, Assert.Single(changed.Orders).RequestId);
            Assert.Equal(next.NewBuysStopped, changed.Account.NewBuysStopped);
            Assert.Equal(1, changed.Account.Version);
            var second = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir, 1), protector);
            Assert.True(second.Matches(saved.BindingId, run.Identity, changed));
            Assert.Equal(next.RevisionId, second.ManagedRevision);
            Assert.True(sealedBook.Matches(saved.BindingId, run.Identity, current));
        }
        finally { Directory.Delete(dir, true); }
    }
}
