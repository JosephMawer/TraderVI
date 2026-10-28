#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record MarketCollectorLease(string SourceContract, Guid OwnerId, long FencingToken, DateTime ExpiresUtc);

/// <summary>
/// ADR-0111 additive collector storage. Constructing this repository does not connect or migrate.
/// Each append fences the owner and atomically saves raw receipt, first observations and conflicts.
/// Native SQL qualification is separate from offline reducer tests; no installed caller yet.
/// </summary>
public sealed partial class MarketCollectorRepository : SQLBase
{
    public MarketCollectorRepository(string? connectionString = null)
    {
        if (connectionString is not null)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Empty connection configuration.");
            ConnectionString = connectionString;
        }
    }

    public async Task<MarketCollectorLease?> TryAcquireAsync(string sourceContract, Guid ownerId,
        int leaseSeconds = 120, CancellationToken ct = default)
    {
        Source(sourceContract); Owner(ownerId); Duration(leaseSeconds);
        await using var c = new SqlConnection(ConnectionString);
        var row = await c.QuerySingleOrDefaultAsync<MarketCollectorLease>(new CommandDefinition(AcquireSql,
            new { Source = sourceContract, Owner = ownerId, Seconds = leaseSeconds }, cancellationToken: ct));
        return row is null ? null : row with { ExpiresUtc = Utc(row.ExpiresUtc) };
    }

    public async Task<bool> RenewAsync(MarketCollectorLease lease, int leaseSeconds = 120, CancellationToken ct = default)
    {
        Lease(lease); Duration(leaseSeconds);
        await using var c = new SqlConnection(ConnectionString);
        return await c.QuerySingleAsync<bool>(new CommandDefinition("""
UPDATE dbo.MarketCollectorLease SET ExpiresUtc=DATEADD(SECOND,@Seconds,SYSUTCDATETIME())
WHERE SourceContract=@SourceContract AND OwnerId=@OwnerId AND FencingToken=@FencingToken AND ExpiresUtc>SYSUTCDATETIME();
SELECT CAST(CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END AS bit);
""", new { lease.SourceContract, lease.OwnerId, lease.FencingToken, Seconds = leaseSeconds }, cancellationToken: ct));
    }

    public async Task ReleaseAsync(MarketCollectorLease lease, CancellationToken ct = default)
    {
        Lease(lease);
        await using var c = new SqlConnection(ConnectionString);
        await c.ExecuteAsync(new CommandDefinition("""
UPDATE dbo.MarketCollectorLease SET ExpiresUtc=SYSUTCDATETIME()
WHERE SourceContract=@SourceContract AND OwnerId=@OwnerId AND FencingToken=@FencingToken;
""", lease, cancellationToken: ct));
    }

    public async Task<DelphiLiveMinuteEvidenceBook> AppendAsync(MarketCollectorLease lease,
        DelphiLiveCollectorBatch batch, CancellationToken ct = default)
    {
        Lease(lease); DelphiLiveCollectorBatches.Validate(batch);
        if (lease.SourceContract != batch.SourceContract) throw new ArgumentException("Receipt does not belong to the fenced source.");
        string json = JsonSerializer.Serialize(batch), hash = DelphiLiveCollectorBatches.Hash(json);
        if (System.Text.Encoding.Unicode.GetByteCount(json) > 8388608) throw new ArgumentException("Collector receipt exceeds storage limit.");
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await FenceAsync(c, tx, lease, ct);
        var scope = new { batch.SourceContract, batch.Symbol, batch.SessionOpenUtc, batch.SessionCloseUtc };
        var existing = await c.QuerySingleOrDefaultAsync<RequestRow>(new CommandDefinition(
            "SELECT ContentHash,ContentJson FROM dbo.MarketCollectorRequest WITH(UPDLOCK,HOLDLOCK) WHERE RequestId=@RequestId;", batch, tx, cancellationToken: ct));
        if (existing is not null && (existing.ContentHash != hash || DelphiLiveCollectorBatches.Hash(existing.ContentJson) != existing.ContentHash))
            throw new InvalidDataException("Collector request identity contains changed or damaged evidence.");
        await c.ExecuteAsync(new CommandDefinition("""
IF EXISTS(SELECT 1 FROM dbo.MarketCollectorBook WITH(UPDLOCK,HOLDLOCK)
 WHERE SourceContract=@SourceContract AND Symbol=@Symbol AND SessionOpenUtc=@SessionOpenUtc AND SessionCloseUtc<>@SessionCloseUtc)
 THROW 54560,'Collector session bounds changed.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.MarketCollectorBook WITH(UPDLOCK,HOLDLOCK)
 WHERE SourceContract=@SourceContract AND Symbol=@Symbol AND SessionOpenUtc=@SessionOpenUtc)
 INSERT dbo.MarketCollectorBook(SourceContract,Symbol,SessionOpenUtc,SessionCloseUtc)
 VALUES(@SourceContract,@Symbol,@SessionOpenUtc,@SessionCloseUtc);
""", scope, tx, cancellationToken: ct));
        var saved = await ReadAsync(c, tx, batch.SourceContract, batch.Symbol, batch.SessionOpenUtc, ct)
            ?? throw new InvalidDataException("Collector book disappeared inside its transaction.");
        if (existing is not null)
        {
            // An uncertain retry returns current evidence, never a stale snapshot or renewed authority.
            await FenceAsync(c, tx, lease, ct);
            await tx.CommitAsync(ct); return saved;
        }
        var next = DelphiLiveCollectorBatches.Apply(saved, batch);
        await c.ExecuteAsync(new CommandDefinition("""
IF @ValidatedUtc>SYSUTCDATETIME() THROW 54561,'Collector validation timestamp is in the future.',1;
INSERT dbo.MarketCollectorRequest(RequestId,SourceContract,Symbol,SessionOpenUtc,OwnerId,FencingToken,ContentHash,ContentJson)
VALUES(@RequestId,@SourceContract,@Symbol,@SessionOpenUtc,@OwnerId,@FencingToken,@Hash,@Json);
""", new { batch.RequestId, batch.SourceContract, batch.Symbol, batch.SessionOpenUtc, batch.ValidatedUtc,
            lease.OwnerId, lease.FencingToken, Hash = hash, Json = json }, tx, cancellationToken: ct));
        var savedIds = saved.Observations.Select(o => o.ObservationId).ToHashSet();
        foreach (var o in next.Observations.Where(o => !savedIds.Contains(o.ObservationId)))
        {
            string content = JsonSerializer.Serialize(o);
            await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.MarketCollectorObservation(ObservationId,SourceContract,Symbol,SessionOpenUtc,BarStartUtc,RequestId,ContentHash,ContentJson)
VALUES(@ObservationId,@SourceContract,@Symbol,@SessionOpenUtc,@Start,@RequestId,@Hash,@Json);
""", new { o.ObservationId, batch.SourceContract, batch.Symbol, batch.SessionOpenUtc, Start = o.Bar.TimestampUtc,
                batch.RequestId, Hash = DelphiLiveCollectorBatches.Hash(content), Json = content }, tx, cancellationToken: ct));
        }
        foreach (var start in next.ConflictingStartsUtc.Except(saved.ConflictingStartsUtc))
            await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.MarketCollectorConflict(SourceContract,Symbol,SessionOpenUtc,BarStartUtc,RequestId)
VALUES(@SourceContract,@Symbol,@SessionOpenUtc,@Start,@RequestId);
""", new { batch.SourceContract, batch.Symbol, batch.SessionOpenUtc, Start = start, batch.RequestId }, tx, cancellationToken: ct));
        await FenceAsync(c, tx, lease, ct);
        await tx.CommitAsync(ct);
        return next;
    }

    public async Task<DelphiLiveMinuteEvidenceBook?> ReadAsync(string sourceContract, string symbol,
        DateTime sessionOpenUtc, CancellationToken ct = default)
    {
        Source(sourceContract); DelphiLiveFiveMinuteBar.RequireCanonicalSymbol(symbol);
        DelphiLiveFiveMinuteBar.RequireUtc(sessionOpenUtc, nameof(sessionOpenUtc));
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        // A short serializable read gives one committed observation/conflict boundary without requiring
        // database-wide snapshot settings or letting a revision race between separate reads.
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var book = await ReadAsync(c, tx, sourceContract, symbol, sessionOpenUtc, ct);
        await tx.CommitAsync(ct); return book;
    }

    private static async Task<DelphiLiveMinuteEvidenceBook?> ReadAsync(SqlConnection c, SqlTransaction tx,
        string sourceContract, string symbol, DateTime sessionOpenUtc, CancellationToken ct)
    {
        var scope = new { SourceContract = sourceContract, Symbol = symbol, SessionOpenUtc = sessionOpenUtc };
        var close = await c.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition("""
SELECT SessionCloseUtc FROM dbo.MarketCollectorBook
WHERE SourceContract=@SourceContract AND Symbol=@Symbol AND SessionOpenUtc=@SessionOpenUtc;
""", scope, tx, cancellationToken: ct));
        if (close is null) return null;
        var rows = await c.QueryAsync<ObservationRow>(new CommandDefinition("""
SELECT ObservationId,BarStartUtc,ContentHash,ContentJson FROM dbo.MarketCollectorObservation
WHERE SourceContract=@SourceContract AND Symbol=@Symbol AND SessionOpenUtc=@SessionOpenUtc ORDER BY BarStartUtc;
""", scope, tx, cancellationToken: ct));
        var observations = rows.Select(row =>
        {
            if (DelphiLiveCollectorBatches.Hash(row.ContentJson) != row.ContentHash) throw new InvalidDataException("Collector observation checksum mismatch.");
            var o = JsonSerializer.Deserialize<DelphiLiveMinuteObservation>(row.ContentJson) ?? throw new InvalidDataException("Missing observation content.");
            if (o.ObservationId != row.ObservationId || o.Bar.TimestampUtc != Utc(row.BarStartUtc)) throw new InvalidDataException("Collector observation identity mismatch.");
            return o;
        }).ToImmutableArray();
        var conflicts = (await c.QueryAsync<DateTime>(new CommandDefinition("""
SELECT BarStartUtc FROM dbo.MarketCollectorConflict
WHERE SourceContract=@SourceContract AND Symbol=@Symbol AND SessionOpenUtc=@SessionOpenUtc ORDER BY BarStartUtc;
""", scope, tx, cancellationToken: ct))).Select(Utc).ToImmutableArray();
        var book = new DelphiLiveMinuteEvidenceBook(symbol, sourceContract, sessionOpenUtc, Utc(close.Value), observations, conflicts);
        DelphiLiveMinuteEvidence.Validate(book); return book;
    }

    private static Task FenceAsync(SqlConnection c, SqlTransaction tx, MarketCollectorLease lease, CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition("""
IF NOT EXISTS(SELECT 1 FROM dbo.MarketCollectorLease WITH(UPDLOCK,HOLDLOCK)
 WHERE SourceContract=@SourceContract AND OwnerId=@OwnerId AND FencingToken=@FencingToken AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54562,'Collector writer lease is absent, expired or superseded.',1;
""", lease, tx, cancellationToken: ct));

    private static void Source(string source) { if (string.IsNullOrWhiteSpace(source) || source.Length > 128 || source != source.Trim()) throw new ArgumentException("Pinned collector source is required."); }
    private static void Owner(Guid owner) { if (owner == Guid.Empty) throw new ArgumentException("Collector owner identity is required."); }
    private static void Duration(int seconds) { if (seconds is < 30 or > 300) throw new ArgumentOutOfRangeException(nameof(seconds)); }
    private static void Lease(MarketCollectorLease lease) { ArgumentNullException.ThrowIfNull(lease); Source(lease.SourceContract); Owner(lease.OwnerId); if (lease.FencingToken <= 0) throw new ArgumentException("Positive fencing generation required."); }
    private static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);
    private sealed record RequestRow(string ContentHash, string ContentJson);
    private sealed record ObservationRow(Guid ObservationId, DateTime BarStartUtc, string ContentHash, string ContentJson);
}
