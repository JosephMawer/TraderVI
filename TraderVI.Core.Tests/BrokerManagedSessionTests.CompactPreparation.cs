#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Orders;
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
    public async Task InitialCompactTransferRequiresTheExactAssignmentAndCannotReplaceAnExistingBook(string scenario)
    {
        var fixture = await OngoingFixture();
        var saved = await fixture.Workflow().ApproveAsync(fixture.Review);
        var sql = new TradingCompactStorageTests.Database();
        var run = new EngineContinuityTests.Store();
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
            if (scenario != "approved")
            {
                await Assert.ThrowsAnyAsync<Exception>(() => prepare.PrepareAsync(build, default));
                Assert.Null(await sql.Store.ReadAsync(saved.BindingId));
                return;
            }
            await prepare.PrepareAsync(build, default);
            var current = (await sql.Store.ReadAsync(saved.BindingId))!;
            Assert.Equal(saved.NewBuysStopped, current.Account.NewBuysStopped);
            Assert.Equal(saved.CommittedPurchaseCost, current.Account.CommittedPurchaseCost);
            var sealedBook = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(dir, 0), protector);
            Assert.True(sealedBook.Matches(saved.BindingId, run.Identity, current));
            Assert.True(current.Settings.BuyAllowed && current.Settings.SellAllowed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => prepare.PrepareAsync(build, default));
            Assert.Equal(0, (await sql.Store.ReadAsync(saved.BindingId))!.Account.Version);
        }
        finally { Directory.Delete(dir, true); }
    }
}
