#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class EngineContinuityRepository : IEngineContinuityStore
{
    private static readonly Guid Owner = new("EEC83433-0955-4502-A26E-2BFD1BEDF4F3");
    internal static Task<EngineDatabaseIdentity> IdentityAsync(SqlConnection c, SqlTransaction t, CancellationToken ct) =>
        c.QuerySingleAsync<EngineDatabaseIdentity>(new CommandDefinition("""
SELECT database_guid [Database],family_guid Family,recovery_fork_guid RecoveryFork
FROM sys.database_recovery_status WHERE database_id=DB_ID();
""", transaction: t, cancellationToken: ct));
    public async Task<EngineContinuityObservation> ReadAsync(CancellationToken ct)
    {
        await using var c = await EngineIntegrityRepository.OpenAsync(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        if (!await EngineIntegrityRepository.RequiredGuardsAsync(c, t, ct))
            throw new InvalidDataException("Immutable guards are unavailable; engine continuity cannot be verified.");
        var identity = await IdentityAsync(c, t, ct); identity.Validate();
        var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
SELECT Revision,SourceHash,StateHash,StateJson FROM dbo.EngineCurrentState WHERE Kind=N'EngineContinuity' AND OwnerId=@Owner;
""", new { Owner }, t, cancellationToken: ct));
        EngineContinuityStamp? stamp = null;
        if (row is not null)
        {
            stamp = JsonSerializer.Deserialize<EngineContinuityStamp>(row.StateJson) ?? throw new InvalidDataException("Missing database continuity witness.");
            stamp.Validate();
            if (row.Revision != stamp.Sequence || row.StateHash != DelphiLiveSnapshotCodec.Hash(row.StateJson) || row.SourceHash != row.StateHash)
                throw new InvalidDataException("Database continuity witness is damaged.");
        }
        t.Commit(); return new(identity, stamp);
    }
    public async Task WriteAsync(EngineContinuityStamp? expected, EngineContinuityStamp next, CancellationToken ct)
    {
        next.Validate();
        await using var c = await EngineIntegrityRepository.OpenAsync(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        if (await IdentityAsync(c, t, ct) != next.Database) throw new InvalidDataException("Database identity changed during engine checkpoint.");
        await c.ExecuteAsync(new CommandDefinition("""
IF @Previous IS NULL
BEGIN
 IF EXISTS(SELECT 1 FROM dbo.EngineCurrentState WITH(UPDLOCK,HOLDLOCK) WHERE Kind=N'EngineContinuity' AND OwnerId=@Owner)
  THROW 54471,'Engine continuity changed during startup.',1;
 INSERT dbo.EngineCurrentState(Kind,OwnerId,Revision,SourceHash,StateHash,StateJson)
 VALUES(N'EngineContinuity',@Owner,@Revision,@Hash,@Hash,@Json);
END
ELSE
BEGIN
 UPDATE dbo.EngineCurrentState SET Revision=@Revision,SourceHash=@Hash,StateHash=@Hash,StateJson=@Json,UpdatedUtc=SYSUTCDATETIME()
 WHERE Kind=N'EngineContinuity' AND OwnerId=@Owner AND StateHash=@Previous AND SourceHash=@Previous;
 IF @@ROWCOUNT<>1 THROW 54472,'Engine continuity checkpoint changed.',1;
END
""", new { Owner, Revision = next.Sequence, Previous = expected?.Hash, Hash = next.Hash, Json = next.Json }, t, cancellationToken: ct));
        t.Commit();
    }
    public Task HoldAsync(string key, string code, CancellationToken ct) => new EngineIntegrityRepository()
        .RaiseAsync(EngineIntegrityScope.Global, null, key, code, true, ct);
    private sealed record Row(long Revision, string SourceHash, string StateHash, string StateJson);
}
