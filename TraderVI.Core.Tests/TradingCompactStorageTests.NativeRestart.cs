#nullable enable
using Core.Db;
using Core.Trader.Storage;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    [RestartLocalDbFact]
    public async Task NativeCompactRestartStorage()
    {
        var rehearsal = new RestartRehearsalDatabase(Guid.ParseExact(Environment.GetEnvironmentVariable("TRADERVI_RESTART_REHEARSAL")!, "N"));
        await using var admin = await rehearsal.OpenAsync();
        await admin.ExecuteAsync("""
            INSERT dbo.BrokerAccountImportBinding VALUES (@Binding,N'TFSA',REPLICATE('A',64),REPLICATE('B',64),SYSUTCDATETIME(),N'Synthetic rehearsal');
            """, new { Binding });
        var store = new SqlTradingStorageDatabase(rehearsal.OpenAsync).CreateStore();
        await store.SaveAsync(BenchmarkBook());
        var state = (await store.ReadAsync(Binding))!;
        var change = Change(state) with { Account = state.Account with { ObservedCash = 1 } };
        await Task.WhenAll(store.SaveAsync(change), store.SaveAsync(change));
        Assert.True((await store.SaveAsync(change)).AlreadyCommitted);
        state = (await store.ReadAsync(Binding))!;
        Assert.Equal(1, state.Account.Version);
        var before = state.Account;
        await admin.ExecuteAsync("CREATE TRIGGER dbo.RehearsalFault ON dbo.TradingCheckpoint AFTER UPDATE AS THROW 54710,'Injected failure after account update',1;");
        try
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => store.SaveAsync(Change(state) with
                { Account = state.Account with { ObservedCash = 2 } }));
            Assert.Equal(54710, error.Number);
        }
        finally { await admin.ExecuteAsync("DROP TRIGGER dbo.RehearsalFault;"); }
        Assert.Equal(before, (await store.ReadAsync(Binding))!.Account);
        // Interrupt only this test connection after its account update and before its marker commits.
        var spid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var interrupted = new SqlTradingStorageDatabase(async ct =>
        {
            var connection = await rehearsal.OpenAsync(ct);
            spid.SetResult(connection.ServerProcessId);
            return connection;
        }).CreateStore();
        await admin.ExecuteAsync("CREATE TRIGGER dbo.RehearsalPause ON dbo.TradingCheckpoint AFTER UPDATE AS WAITFOR DELAY '00:00:09';");
        try
        {
            var pending = interrupted.SaveAsync(Change(state) with { Account = state.Account with { ObservedCash = 3 } });
            int ownedSession = await spid.Task;
            var deadline = Stopwatch.StartNew();
            while (await admin.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id=@ownedSession AND database_id=DB_ID() AND wait_type='WAITFOR';",
                new { ownedSession }) == 0)
            {
                if (deadline.Elapsed.TotalSeconds >= 6) throw new TimeoutException("Writer did not reach the interruption point.");
                await Task.Delay(25);
            }
            await admin.ExecuteAsync($"KILL {ownedSession};");
            await Assert.ThrowsAsync<SqlException>(() => pending);
        }
        finally { await admin.ExecuteAsync("DROP TRIGGER dbo.RehearsalPause;"); }
        Assert.Equal(before, (await store.ReadAsync(Binding))!.Account);
        const string footprint = """
            SELECT SUM(used_page_count)*8 FROM sys.dm_db_partition_stats
            WHERE OBJECT_NAME(object_id) LIKE 'Trading%';
            """;
        long initialKiB = await admin.ExecuteScalarAsync<long>(footprint);
        var elapsed = new List<double>();
        var position = state.Positions[0];
        var account = state.Account;
        for (int i = 1; i <= 10000; i++)
        {
            var write = new TradingStateWrite(Guid.NewGuid(), "Protection", Now.AddSeconds(i), account.Version, account)
                { Positions = [position with { ProtectionFloor = 9m + i / 100000m }] };
            var clock = Stopwatch.StartNew();
            var result = await store.SaveAsync(write);
            elapsed.Add(clock.Elapsed.TotalMilliseconds);
            Assert.Equal(3, result.WrittenRows);
            account = account with { Version = result.Version };
        }
        state = (await store.ReadAsync(Binding))!;
        Assert.Equal(2, state.Checkpoints.Length);
        Assert.Equal(154L, await admin.ExecuteScalarAsync<long>("SELECT SUM(row_count) FROM sys.dm_db_partition_stats WHERE index_id IN (0,1) AND OBJECT_NAME(object_id) LIKE 'Trading%';"));
        double writeP95 = Percentile(elapsed, .95);
        output.WriteLine($"10000 writes, 154 rows, two markers, storage used {initialKiB} -> {await admin.ExecuteScalarAsync<long>(footprint)} KiB, p95 {writeP95:F2} ms");
        Assert.True(writeP95 <= 100);
        int originalBytes = TradingStorageValidation.PayloadBytes(state);
        Assert.InRange(originalBytes, 1, TradingStorageValidation.BenchmarkPayloadBudget);
        int archived = 0;
        foreach (int size in new[] { 1000, 100000, 1000000 })
        {
            while (archived < size)
            {
                int batch = Math.Min(size - archived, 10000);
                await admin.ExecuteAsync("""
                    SET ARITHABORT ON; SET QUOTED_IDENTIFIER ON;
                    WITH digits(n) AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n))
                    INSERT dbo.TradingOrder (BindingId,Version,RequestId,DecisionId,PermissionId,SecurityId,Symbol,Side,
                       Quantity,ReferencePrice,CreatedUtc,Status,LatestFilledQuantity,AppliedFilledQuantity,LatestNetValue,
                       AppliedNetValue,ReservedQuantity,ReservedCash,NeedsWork)
                    SELECT TOP (@batch) @Binding,0,NEWID(),NEWID(),@Permission,'TEST','TEST','BUY',1,1,
                        SYSUTCDATETIME(),'Expired',0,0,0,0,0,0,0 FROM digits a CROSS JOIN digits b CROSS JOIN digits c CROSS JOIN digits d;
                    """, new { batch, Binding, Permission }, commandTimeout: 120);
                archived += batch;
            }
            elapsed.Clear();
            for (int i = 0; i < 100; i++)
            {
                var clock = Stopwatch.StartNew();
                var read = (await store.ReadAsync(Binding))!;
                elapsed.Add(clock.Elapsed.TotalMilliseconds);
                Assert.Equal(originalBytes, TradingStorageValidation.PayloadBytes(read));
                Assert.Equal(10, read.Orders.Length);
                Assert.All(read.Orders, o => Assert.True(o.NeedsWork));
            }
            double p95 = Percentile(elapsed, .95), p99 = Percentile(elapsed, .99);
            output.WriteLine($"{size:N0} completed: {originalBytes:N0} current bytes, p95 {p95:F2} ms, p99 {p99:F2} ms, total used {await admin.ExecuteScalarAsync<long>(footprint)} KiB");
            Assert.True(p95 <= 250 && p99 <= 1000);
        }
    }

    private static double Percentile(List<double> values, double fraction) =>
        values.Order().ElementAt((int)Math.Ceiling(values.Count * fraction) - 1);

    private sealed class RestartLocalDbFactAttribute : FactAttribute
    {
        public RestartLocalDbFactAttribute()
        {
            if (!Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_RESTART_REHEARSAL"), "N", out _))
                Skip = "Requires explicit opt-in with a disposable marked rehearsal LocalDB.";
        }
    }
}
