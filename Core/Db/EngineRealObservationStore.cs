#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Real subscriptions are bound to broker revision heads, with no paper ledger or order-history expansion.</summary>
public sealed class EngineRealObservationStore : SQLBase
{
    internal static async Task SaveIfInstalledAsync(SqlConnection c, SqlTransaction t, BrokerManagedSession session,
        string sourceHash, CancellationToken ct)
    {
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.EngineCurrentState',N'U') IS NULL THEN 0 ELSE 1 END AS bit);",
            transaction: t, cancellationToken: ct))) return;
        string json = DelphiLiveLedgerJson.Serialize(EngineRealObservation.From(session));
        await c.ExecuteAsync(new CommandDefinition("""
IF EXISTS(SELECT 1 FROM dbo.EngineCurrentState WITH(UPDLOCK,HOLDLOCK) WHERE Kind=N'RealObservation' AND OwnerId=@Owner)
BEGIN
 IF EXISTS(SELECT 1 FROM dbo.EngineCurrentState WHERE Kind=N'RealObservation' AND OwnerId=@Owner
  AND (Revision>@Revision OR Revision=@Revision AND (SourceHash<>@Source OR StateHash<>@Hash)))
  THROW 54470,'Real collection subscription conflicts with its broker revision.',1;
 UPDATE dbo.EngineCurrentState SET Revision=@Revision,SourceHash=@Source,StateHash=@Hash,StateJson=@Json,UpdatedUtc=SYSUTCDATETIME()
 WHERE Kind=N'RealObservation' AND OwnerId=@Owner;
END
ELSE INSERT dbo.EngineCurrentState(Kind,OwnerId,Revision,SourceHash,StateHash,StateJson)
 VALUES(N'RealObservation',@Owner,@Revision,@Source,@Hash,@Json);
""", new { Owner = session.BindingId, session.Revision, Source = sourceHash, Hash = DelphiLiveSnapshotCodec.Hash(json), Json = json }, t, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<EngineRealObservation>> ReadAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var rows = await c.QueryAsync<Row>(new CommandDefinition("""
SELECT b.BindingId,b.SessionId,b.Revision,b.ContentHash,s.Revision StateRevision,s.SourceHash,s.StateHash,s.StateJson
FROM dbo.BrokerAccountImportBinding owner CROSS APPLY
 (SELECT TOP(1) BindingId,SessionId,Revision,ContentHash FROM dbo.BrokerManagedSessionRevision
  WHERE BindingId=owner.BindingId ORDER BY Revision DESC) b
LEFT JOIN dbo.EngineCurrentState s ON s.Kind=N'RealObservation' AND s.OwnerId=b.BindingId;
""", transaction: t, cancellationToken: ct));
        var result = rows.Select(row =>
        {
            if (row.StateJson is null || row.StateRevision != row.Revision || row.SourceHash != row.ContentHash ||
                DelphiLiveSnapshotCodec.Hash(row.StateJson) != row.StateHash)
                throw new InvalidDataException("Real collection subscription is missing or differs from its broker revision. Qualify it before starting collection.");
            var state = DelphiLiveLedgerJson.Deserialize<EngineRealObservation>(row.StateJson);
            if (state.BindingId != row.BindingId || state.SessionId != row.SessionId || state.Revision != row.Revision)
                throw new InvalidDataException("Real collection subscription identity differs from its broker revision.");
            state.Strategy.Validate();
            _ = DelphiLiveSessionRepository.NormalizeObservedHoldings(state.Holdings);
            return state;
        }).ToArray();
        await t.CommitAsync(ct); return result;
    }

    public async Task<int> QualifyExistingAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        var ids = (await c.QueryAsync<Guid>(new CommandDefinition(
            "SELECT DISTINCT BindingId FROM dbo.BrokerManagedSessionRevision;", cancellationToken: ct))).ToArray();
        var store = new BrokerManagedSessionRepository();
        foreach (var id in ids)
        {
            await using var lease = await store.AcquireAsync(id, ct);
            var state = lease.Current ?? throw new InvalidDataException("A broker assignment disappeared during qualification.");
            using var t = c.BeginTransaction(IsolationLevel.Serializable);
            string hash = await c.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT ContentHash FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@Id AND Revision=@Revision;",
                new { Id = id, state.Revision }, t, cancellationToken: ct));
            await SaveIfInstalledAsync(c, t, state, hash, ct); t.Commit();
        }
        _ = await ReadAsync(ct); return ids.Length;
    }

    public async Task RequireCurrentAsync(BrokerManagedSession session, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await RequireCurrentAsync(c, t, session, ct);
        await t.CommitAsync(ct);
    }
    internal static async Task RequireCurrentAsync(SqlConnection c, SqlTransaction t, BrokerManagedSession session, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<ExecutionRow>(new CommandDefinition("""
SELECT b.Revision,b.RevisionId,b.ContentHash,s.Revision StateRevision,s.SourceHash,s.StateHash,s.StateJson
FROM (SELECT TOP(1) Revision,RevisionId,ContentHash FROM dbo.BrokerManagedSessionRevision
 WHERE BindingId=@Binding ORDER BY Revision DESC) b
LEFT JOIN dbo.EngineCurrentState s ON s.Kind=N'RealObservation' AND s.OwnerId=@Binding;
""", new { Binding = session.BindingId }, t, cancellationToken: ct));
        // Another legitimate operator revision invalidates this cycle without inventing an integrity incident.
        if (row is null || row.Revision != session.Revision || row.RevisionId != session.RevisionId)
            throw new InvalidOperationException("The current account revision changed. Read it again before execution.");
        if (row.StateJson is null || row.StateRevision != row.Revision || row.SourceHash != row.ContentHash ||
            DelphiLiveSnapshotCodec.Hash(row.StateJson) != row.StateHash)
            throw new InvalidDataException("The real collection subscription is missing or has changed. Verify recovery before execution.");
        DelphiLiveLedgerJson.Deserialize<EngineRealObservation>(row.StateJson).RequireMatches(session);
    }
    private sealed record ExecutionRow(long Revision, Guid RevisionId, string ContentHash,
        long? StateRevision, string? SourceHash, string? StateHash, string? StateJson);
    private sealed record Row(Guid BindingId, Guid SessionId, long Revision, string ContentHash,
        long? StateRevision, string? SourceHash, string? StateHash, string? StateJson);
}
