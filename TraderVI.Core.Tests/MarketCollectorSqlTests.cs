#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.TMX.Models.Domain;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

/// <summary>Explicit opt-in only. Creates and retains synthetic collector tables in a unique local
/// database. No production connection, financial tables, broker or market transport is constructed.</summary>
public sealed class MarketCollectorSqlTests(ITestOutputHelper output)
{
    [NativeCollectorSqlFact]
    [Trait("Category", "NativeSql")]
    public async Task NativeOwnershipIntentAtomicRecoveryConflictsAndImmutableEvidence()
    {
        string database = "TraderVI_CollectorQualification_" + Guid.NewGuid().ToString("N");
        Assert.Matches("^TraderVI_CollectorQualification_[a-f0-9]{32}$", database);
        var configuration = new SqlConnectionStringBuilder
        { DataSource = "localhost", InitialCatalog = database, IntegratedSecurity = true,
          TrustServerCertificate = true, ApplicationName = database, ConnectTimeout = 10 };
        string connection = configuration.ConnectionString;
        configuration.InitialCatalog = "master";
        await using (var administration = new SqlConnection(configuration.ConnectionString))
            await administration.ExecuteAsync($"CREATE DATABASE [{database}];");
        output.WriteLine("Retained synthetic database: " + database);
        await using var control = new SqlConnection(connection);
        await control.OpenAsync(); Assert.Equal(database, control.Database);
        var names = new[] { "Lease", "Attempt", "Book", "Request", "Observation", "Conflict" };
        foreach (string name in names)
            await Apply("MarketCollector" + name + ".sql");
        foreach (string name in names.Where(n => n != "Lease"))
            await Apply("MarketCollector" + name + "_Immutable.sql");
        async Task Apply(string file)
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "CollectorSqlRehearsal", file));
            foreach (string batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await control.ExecuteAsync(batch);
        }

        var repository = new MarketCollectorRepository(connection);
        const string source = "SyntheticCollectorQualificationV1";
        var owners = await Task.WhenAll(repository.TryAcquireAsync(source, Guid.NewGuid()), repository.TryAcquireAsync(source, Guid.NewGuid()));
        var lease = Assert.Single(owners.Where(x => x is not null))!;
        Assert.Null(await repository.TryAcquireAsync(source, Guid.NewGuid()));
        Assert.True(await repository.RenewAsync(lease));
        DateTime now = Utc(await control.QuerySingleAsync<DateTime>("SELECT SYSUTCDATETIME();"));
        var end = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        var subscriptions = new CollectorSubscriptionSnapshot(end.AddHours(-1), end.AddHours(1), ["ABC", "DEF"], [], []);
        var reservations = await Task.WhenAll(repository.ReserveAsync(lease, subscriptions, "XIU", end, 3, default),
            repository.ReserveAsync(lease, subscriptions, "XIU", end, 3, default));
        var attempt = Assert.Single(reservations.Where(x => x is not null))!;
        var first = Batch(attempt, 10m);
        var original = await repository.AppendAsync(lease, first);
        Assert.Single(original.Observations);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(await repository.AppendAsync(lease, first)));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.AppendAsync(lease, first with { RawResponseJson = "{\"changed\":true}" }));
        Assert.NotNull(await repository.ReserveAsync(lease, subscriptions, "ABC", end, 3, default));
        Assert.NotNull(await repository.ReserveAsync(lease, subscriptions, "DEF", end, 3, default));
        Assert.Null(await repository.ReserveAsync(lease, subscriptions with { Candidates = ["NEW"] }, "NEW", end, 3, default));
        // A persisted intent with no receipt represents a crashed request; it cannot be retried.
        Assert.Null(await new MarketCollectorRepository(connection).ReserveAsync(lease, subscriptions, "ABC", end, 3, default));
        output.WriteLine("PASS: competing owners, duplicate reservations, exact receipt retry, changed receipt rejection and crash/budget persistence.");

        await repository.ReleaseAsync(lease);
        var replacement = (await repository.TryAcquireAsync(source, Guid.NewGuid()))!;
        Assert.True(replacement.FencingToken > lease.FencingToken);
        Assert.False(await repository.RenewAsync(lease));
        await Assert.ThrowsAsync<SqlException>(() => repository.AppendAsync(lease, first));
        var recovered = await new MarketCollectorRepository(connection).ReadAsync(source, "XIU", subscriptions.SessionOpenUtc);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(recovered));

        // Synthetic delayed request from an earlier endpoint; the fixture creates only its request intent.
        // It tests a late conflicting response without waiting for another wall-clock market minute.
        now = Utc(await control.QuerySingleAsync<DateTime>("SELECT SYSUTCDATETIME();"));
        var late = attempt with { RequestId = Guid.NewGuid(), ExpectedEndUtc = end.AddMinutes(-1), ReservedUtc = now };
        await control.ExecuteAsync("""
INSERT dbo.MarketCollectorAttempt(RequestId,SourceContract,Symbol,SessionOpenUtc,SessionCloseUtc,ExpectedEndUtc,ReservedUtc,OwnerId,FencingToken)
VALUES(@RequestId,@SourceContract,@Symbol,@SessionOpenUtc,@SessionCloseUtc,@ExpectedEndUtc,@ReservedUtc,@OwnerId,@FencingToken);
""", new { late.RequestId, late.SourceContract, late.Symbol, late.SessionOpenUtc, late.SessionCloseUtc,
            late.ExpectedEndUtc, late.ReservedUtc, replacement.OwnerId, replacement.FencingToken });
        var changed = Batch(late, 11m) with { Bars = [first.Bars[0] with { Close = 11m, High = 11m }] };
        var conflicted = await repository.AppendAsync(replacement, changed);
        Assert.Single(conflicted.ConflictingStartsUtc);
        Assert.Equal(original.Observations[0], conflicted.Observations[0]);
        Assert.Single((await repository.ReadAsync(source, "XIU", subscriptions.SessionOpenUtc))!.ConflictingStartsUtc);
        foreach (string name in names.Where(n => n != "Lease"))
            await Assert.ThrowsAsync<SqlException>(() => control.ExecuteAsync($"DELETE FROM dbo.MarketCollector{name};"));

        await using (var tx = (SqlTransaction)await control.BeginTransactionAsync())
        {
            await control.ExecuteAsync("""
INSERT dbo.MarketCollectorBook(SourceContract,Symbol,SessionOpenUtc,SessionCloseUtc)
VALUES(@source,N'ROLLBACK',@SessionOpenUtc,@SessionCloseUtc);
""", new { source, subscriptions.SessionOpenUtc, subscriptions.SessionCloseUtc }, tx);
            var reader = repository.ReadAsync(source, "ROLLBACK", subscriptions.SessionOpenUtc);
            await Task.Delay(100); Assert.False(reader.IsCompleted);
            await tx.RollbackAsync(); Assert.Null(await reader);
        }
        output.WriteLine("PASS: takeover fencing, restart readback, first-observation preservation, latched conflict, immutable guards and rollback/read isolation.");

        DelphiLiveCollectorBatch Batch(CollectorAttempt a, decimal price)
        {
            var time = DateTime.UtcNow;
            return new(a.RequestId, a.SourceContract, a.Symbol, a.SessionOpenUtc, a.SessionCloseUtc, a.ExpectedEndUtc,
                TimeSpan.FromSeconds(45), time, time, time,
                [new OhlcvBar(a.ExpectedEndUtc.AddMinutes(-1), price, price, price, price, 100)], "{}", null);
        }
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

public sealed class NativeCollectorSqlFactAttribute : FactAttribute
{
    public NativeCollectorSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRADERVI_COLLECTOR_SQL_QUALIFICATION") != "1")
            Skip = "Explicit authorization required: creates and retains an isolated synthetic local database.";
    }
}
