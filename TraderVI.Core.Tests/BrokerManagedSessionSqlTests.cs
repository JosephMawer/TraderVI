#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

/// <summary>
/// Opt-in native SQL rehearsal. Creates and retains a uniquely named synthetic database on localhost.
/// Never connects to TraderDB, loads a login or constructs a broker client. Parent tables contain IDs
/// only: these checks qualify the session repository, not baseline/import repositories or migrations.
/// </summary>
public sealed class BrokerManagedSessionSqlTests(ITestOutputHelper output)
{
    [NativeBrokerSqlFact]
    [Trait("Category", "NativeSql")]
    public async Task NativeJournalRecoversPreciseSnapshotsExclusiveLeasesInterruptedAndUncertainSaves()
    {
        string database = "TraderVI_BrokerRecovery_" + Guid.NewGuid().ToString("N");
        Assert.Matches("^TraderVI_BrokerRecovery_[a-f0-9]{32}$", database);
        string application = database;
        string connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "localhost", InitialCatalog = database, IntegratedSecurity = true,
            TrustServerCertificate = true, ApplicationName = application, ConnectTimeout = 10
        }.ConnectionString;
        string master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using (var administration = new SqlConnection(master))
        {
            await administration.OpenAsync();
            await administration.ExecuteAsync($"CREATE DATABASE [{database}];");
        }
        output.WriteLine($"Retained synthetic database: {database}. No production records or broker calls.");

        async Task<SqlConnection> Open(CancellationToken ct)
        {
            var c = new SqlConnection(connectionString);
            try
            {
                await c.OpenAsync(ct);
                Assert.Equal(database, c.Database);
                return c;
            }
            catch { await c.DisposeAsync(); throw; }
        }
        await using var control = await Open(default);
        await control.ExecuteAsync("""
CREATE TABLE dbo.BrokerAccountImportBinding (BindingId uniqueidentifier PRIMARY KEY);
CREATE TABLE dbo.BrokerAccountRiskBaseline (BaselineId uniqueidentifier PRIMARY KEY);
""");
        foreach (var file in new[] { "BrokerManagedSessionRevision.sql", "BrokerManagedSessionRevision_Immutable.sql" })
            foreach (string batch in Regex.Split(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqlRehearsal", file)),
                @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await control.ExecuteAsync(batch);

        async Task<BrokerManagedSessionTests.Fixture> Fixture(bool normalLive = false)
        {
            var f = new BrokerManagedSessionTests.Fixture();
            if (normalLive) BrokerManagedSessionTests.UseNormalLive(f);
            f.Now = f.Now.AddTicks(1234567);
            await control.ExecuteAsync("""
INSERT dbo.BrokerAccountImportBinding VALUES (@Binding);
INSERT dbo.BrokerAccountRiskBaseline VALUES (@Baseline);
""", new { Binding = f.Binding.BindingId, Baseline = f.Risk.BaselineId });
            return f;
        }
        var repository = new BrokerManagedSessionRepository(Open);
        var fixture = await Fixture(normalLive: true);
        var workflow = Workflow(repository, fixture);
        var approved = await workflow.ApproveAsync(fixture.Approval(retain: true, ongoing: true));
        Assert.Equal(1234567, approved.SavedAt.Ticks % TimeSpan.TicksPerSecond);
        Assert.Equal(0, fixture.Transport.Sent);
        await using (var first = await repository.AcquireAsync(fixture.Binding.BindingId, default))
        {
            Assert.Equal(JsonSerializer.Serialize(approved), JsonSerializer.Serialize(first.Current));
            var competing = new BrokerManagedSessionRepository(Open);
            var busy = await Assert.ThrowsAsync<SqlException>(() => competing.AcquireAsync(fixture.Binding.BindingId, default));
            Assert.Equal(54001, busy.Number);
        }
        await workflow.StopNewBuysAsync(fixture.Binding.BindingId);
        await using (var recovered = await new BrokerManagedSessionRepository(Open).AcquireAsync(fixture.Binding.BindingId, default))
        {
            Assert.True(recovered.Current!.NewBuysStopped);
            Assert.Equal(approved.Approval.Fingerprint, recovered.Current.Approval.Fingerprint);
            Assert.Equal(approved.Account.Risk, recovered.Current.Account.Risk);
        }
        output.WriteLine("PASS: exact timestamp/JSON readback, competing lease rejection, lease release, saved buying mode and authority.");

        var liveProtected = await workflow.TickAsync(fixture.Binding.BindingId);
        Assert.NotNull(Assert.Single(liveProtected.Protection).State.LiveProtection);
        Assert.True(liveProtected.NewBuysStopped); Assert.Equal(0, fixture.Transport.Sent);
        await using (var readLive = await repository.AcquireAsync(fixture.Binding.BindingId, default))
            Assert.Equal(JsonSerializer.Serialize(liveProtected), JsonSerializer.Serialize(readLive.Current));
        output.WriteLine("PASS: Normal Live real-cost protection and stopped mode round-trip through the native session repository, with no broker client or transmission.");

        // Interrupt the actual repository command after SQL has inserted but before it can commit.
        await control.ExecuteAsync("""
CREATE TRIGGER dbo.SqlRehearsalInterrupt ON dbo.BrokerManagedSessionRevision AFTER INSERT AS
BEGIN
 IF EXISTS(SELECT 1 FROM inserted WHERE JSON_VALUE(SnapshotJson,'$.Event')='SqlRehearsalInterrupted')
  WAITFOR DELAY '00:00:30';
END;
""");
        int ownedSpid = 0;
        var interruptible = new BrokerManagedSessionRepository(async ct =>
        {
            var c = await Open(ct);
            ownedSpid = await c.ExecuteScalarAsync<int>("SELECT @@SPID;");
            return c;
        });
        long previousRevision;
        await using (var lease = await interruptible.AcquireAsync(fixture.Binding.BindingId, default))
        {
            var previous = lease.Current!;
            previousRevision = previous.Revision;
            var next = previous with { Revision = previous.Revision + 1, RevisionId = Guid.NewGuid(),
                PreviousRevisionId = previous.RevisionId, Event = "SqlRehearsalInterrupted" };
            var append = lease.AppendAsync(next, default);
            var deadline = Stopwatch.StartNew();
            bool waiting = false;
            while (deadline.Elapsed < TimeSpan.FromSeconds(10) && !append.IsCompleted)
            {
                waiting = await control.ExecuteScalarAsync<bool>("""
SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE session_id=@Spid AND wait_type=N'WAITFOR') THEN 1 ELSE 0 END AS bit);
""", new { Spid = ownedSpid });
                if (waiting) break;
                await Task.Delay(50);
            }
            Assert.True(waiting, "The owned append must be waiting inside its transaction before interruption.");
            int owned = await control.ExecuteScalarAsync<int>("""
SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE session_id=@Spid AND database_id=DB_ID() AND program_name=@Application;
""", new { Spid = ownedSpid, Application = application });
            Assert.Equal(1, owned);
            // Only the positively identified connection in this newly created database can be killed.
            await control.ExecuteAsync($"KILL {ownedSpid};");
            await Assert.ThrowsAnyAsync<Exception>(() => append);
        }
        await using (var recovered = await repository.AcquireAsync(fixture.Binding.BindingId, default))
            Assert.Equal(previousRevision, recovered.Current!.Revision);
        Assert.Equal(previousRevision + 1, await control.ExecuteScalarAsync<long>(
            "SELECT COUNT_BIG(*) FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@Id", new { Id = fixture.Binding.BindingId }));
        output.WriteLine("PASS: SQL connection loss during an uncommitted append rolls back and releases ownership; restart returns the last committed revision.");

        // A real committed intent followed by a lost caller acknowledgement must never be resent.
        var lost = await Fixture();
        var lossy = new LostAcknowledgementStore(repository) { FailEvent = "OrderIntentReserved" };
        var lostWorkflow = Workflow(lossy, lost);
        await lostWorkflow.ApproveAsync(lost.Approval());
        await Assert.ThrowsAsync<IOException>(() => lostWorkflow.TickAsync(lost.Binding.BindingId));
        Assert.Equal(0, lost.Transport.Sent);
        Guid intentId;
        await using (var read = await repository.AcquireAsync(lost.Binding.BindingId, default))
            intentId = Assert.Single(read.Current!.Orders).RequestId;
        var uncertain = await Workflow(new BrokerManagedSessionRepository(Open), lost).TickAsync(lost.Binding.BindingId);
        Assert.Equal(intentId, Assert.Single(uncertain.Orders).RequestId);
        Assert.Equal(0, lost.Transport.Sent);
        Assert.Null(uncertain.ActivatedAt);
        output.WriteLine("PASS: committed intent with lost save acknowledgement survives restart without any transmission.");

        var timeout = await Fixture();
        timeout.Transport.BeforeSend = null;
        timeout.Transport.ThrowOnSubmit = true;
        var timeoutWorkflow = Workflow(repository, timeout);
        await timeoutWorkflow.ApproveAsync(timeout.Approval());
        var sent = await timeoutWorkflow.TickAsync(timeout.Binding.BindingId);
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, Assert.Single(sent.Orders).Submission);
        var restart = await Workflow(new BrokerManagedSessionRepository(Open), timeout).TickAsync(timeout.Binding.BindingId);
        Assert.Equal(sent.Orders.Single().RequestId, Assert.Single(restart.Orders).RequestId);
        Assert.Equal(1, timeout.Transport.Sent);
        Assert.Null(restart.ActivatedAt);
        Assert.Equal(BrokerManagedPhase.NeedsAttention, restart.Phase);
        output.WriteLine("PASS: uncertain synthetic submission recovers the original ID; restart transmits no duplicate or buy.");

        var partial = await Fixture();
        partial.Transport.BeforeSend = null;
        var partialWorkflow = Workflow(repository, partial);
        await partialWorkflow.ApproveAsync(partial.Approval());
        var firstExit = Assert.Single((await partialWorkflow.TickAsync(partial.Binding.BindingId)).Orders);
        partial.Pending = true;
        var pending = await Workflow(new BrokerManagedSessionRepository(Open), partial).TickAsync(partial.Binding.BindingId);
        Assert.Null(pending.ActivatedAt);
        Assert.Equal(1, partial.Transport.Sent);
        partial.Pending = false;
        partial.Facts.Add(partial.Fact(firstExit.ExternalId, "AAA", "SELL", 4, 44, partial.Now.AddSeconds(-1)) with
            { SubmittedQuantity = 10, Status = "CANCELLED" });
        var residual = await Workflow(new BrokerManagedSessionRepository(Open), partial).TickAsync(partial.Binding.BindingId);
        Assert.Equal(2, partial.Transport.Sent);
        Assert.Equal(6, residual.Orders.Last().Quantity);
        Assert.Null(residual.ActivatedAt);
        Assert.All(residual.Orders, o => Assert.Equal("SELL", o.Side));
        partial.Now = partial.Now.AddMinutes(1);
        partial.Facts.Add(partial.Fact(residual.Orders.Last().ExternalId, "AAA", "SELL", 6, 66, partial.Now.AddSeconds(-1)));
        partial.Cash = 1110;
        var active = await Workflow(new BrokerManagedSessionRepository(Open), partial).TickAsync(partial.Binding.BindingId);
        Assert.NotNull(active.ActivatedAt);
        Assert.Empty(active.Account.Positions);
        Assert.All(active.Orders, o => Assert.True(o.Reconciled));
        partial.Now = partial.Now.AddMinutes(6);
        partial.OfferBuy = true;
        var buying = await Workflow(new BrokerManagedSessionRepository(Open), partial).TickAsync(partial.Binding.BindingId);
        Assert.Equal("BUY", buying.Orders.Last().Side);
        Assert.Equal(3, partial.Transport.Sent);
        await Workflow(new BrokerManagedSessionRepository(Open), partial).TickAsync(partial.Binding.BindingId);
        Assert.Equal(3, partial.Transport.Sent);
        output.WriteLine("PASS: partial exits survive restart, residual quantity follows terminal reconciliation, and buying waits for every required exit; pending buys are not duplicated.");

        var immutable = await Assert.ThrowsAsync<SqlException>(() => control.ExecuteAsync(
            "UPDATE dbo.BrokerManagedSessionRevision SET ContentHash=ContentHash WHERE 1=0;"));
        Assert.True(immutable.Number >= 50000);
        output.WriteLine("PASS: canonical immutable trigger rejects updates. Baseline/import integration and actual broker lifecycle remain unqualified.");
    }

    private static BrokerManagedSessionWorkflow Workflow(IBrokerManagedSessionStore store, BrokerManagedSessionTests.Fixture f) =>
        new(store, f, f.Transport, new FixedClock(f));

    private sealed class FixedClock(BrokerManagedSessionTests.Fixture f) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => f.Now; }

    private sealed class LostAcknowledgementStore(IBrokerManagedSessionStore inner) : IBrokerManagedSessionStore
    {
        public string? FailEvent;
        public async Task<IBrokerManagedSessionLease> AcquireAsync(Guid id, CancellationToken ct) =>
            new Lease(this, await inner.AcquireAsync(id, ct));
        private sealed class Lease(LostAcknowledgementStore owner, IBrokerManagedSessionLease inner) : IBrokerManagedSessionLease
        {
            public BrokerManagedSession? Current => inner.Current;
            public async Task AppendAsync(BrokerManagedSession next, CancellationToken ct)
            {
                await inner.AppendAsync(next, ct);
                if (owner.FailEvent == next.Event)
                { owner.FailEvent = null; throw new IOException("Synthetic lost acknowledgement after SQL commit."); }
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}

public sealed class NativeBrokerSqlFactAttribute : FactAttribute
{
    public NativeBrokerSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRADERVI_RUN_BROKER_SQL_REHEARSAL") != "1")
            Skip = "Explicit opt-in required: creates a retained synthetic SQL database and interrupts its own test connection.";
    }
}
