#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Small current membership checked against the exact current portfolio revision, never an archive scan.</summary>
public sealed class EnginePaperObservationStore : SQLBase
{
    public async Task<IReadOnlyList<DelphiLiveObservationPortfolio>> ReadForCollectionAsync(DateOnly date, CancellationToken ct = default)
    {
        try { return await ReadAsync(date, ct); }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
        {
            // This reader's validation covers only paper membership, never the shared or real input store.
            await new EngineIntegrityRepository().RaiseAsync(Core.Runtime.EngineIntegrityScope.Paper, null,
                "PaperObservation", "PaperObservationInvalid", true, ct);
            return Array.Empty<DelphiLiveObservationPortfolio>();
        }
    }
    internal static async Task SaveIfInstalledAsync(SqlConnection c, SqlTransaction t,
        DelphiLiveSnapshotPackage package, CancellationToken ct, DelphiLiveObservationPortfolio? prepared = null)
    {
        // Compatibility only: the old host continues to work before the reviewed migration.
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.EngineCurrentState',N'U') IS NULL THEN 0 ELSE 1 END AS bit);", transaction: t, cancellationToken: ct))) return;
        await SaveAsync(c, t, prepared ?? DelphiLiveObservationPortfolio.FromPackage(package), package.SnapshotJson, ct);
    }

    private static async Task SaveAsync(SqlConnection c, SqlTransaction t,
        DelphiLiveObservationPortfolio state, string sourceJson, CancellationToken ct)
    {
        string json = DelphiLiveLedgerJson.Serialize(state);
        await c.ExecuteAsync(new CommandDefinition("""
IF EXISTS(SELECT 1 FROM dbo.EngineCurrentState WITH(UPDLOCK,HOLDLOCK) WHERE Kind=N'PaperObservation' AND OwnerId=@Owner)
BEGIN
 IF EXISTS(SELECT 1 FROM dbo.EngineCurrentState WHERE Kind=N'PaperObservation' AND OwnerId=@Owner
  AND Revision=@Revision AND StateHash<>@Hash)
  THROW 54428,'Current observation content conflicts at the same portfolio revision.',1;
 UPDATE dbo.EngineCurrentState SET Revision=@Revision,SourceHash=@Source,StateHash=@Hash,StateJson=@Json,UpdatedUtc=SYSUTCDATETIME()
 WHERE Kind=N'PaperObservation' AND OwnerId=@Owner AND Revision<=@Revision;
END
ELSE INSERT dbo.EngineCurrentState(Kind,OwnerId,Revision,SourceHash,StateHash,StateJson)
 VALUES(N'PaperObservation',@Owner,@Revision,@Source,@Hash,@Json);
""", new { Owner = state.PortfolioId, state.Revision, Source = DelphiLiveSnapshotCodec.Hash(sourceJson),
            Hash = DelphiLiveSnapshotCodec.Hash(json), Json = json }, t, cancellationToken: ct));
    }

    /// <summary>Explicit offline cutover operation. Fully verifies each existing snapshot once; never runs on an engine tick.</summary>
    public async Task<int> QualifyExistingAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        try
        {
            await c.ExecuteAsync(new CommandDefinition("""
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'DelphiLiveCollectionV1',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
IF @lock<0 THROW 54425,'Stop collection before qualifying current observation state.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54426,'An active Live host prevents observation qualification.',1;
EXEC @lock=sys.sp_getapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
IF @lock<0 THROW 54427,'Another snapshot writer prevents observation qualification.',1;
""", cancellationToken: ct));
            var roots = await c.QueryAsync<string>(new CommandDefinition("""
SELECT l.SnapshotJson FROM dbo.DelphiLivePortfolioLedger l
JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
WHERE g.EndExclusiveTradingDate IS NULL;
""", cancellationToken: ct));
            int count = 0;
            foreach (var root in roots)
            {
                if (!DelphiLiveSnapshotCodec.IsCompact(root)) throw new InvalidDataException("Complete reviewed compact-history conversion before qualification.");
                var full = await DelphiLiveSnapshotStorage.ReadAsync(c, null, root, ct);
                using var t = c.BeginTransaction(IsolationLevel.Serializable);
                string actual = await c.QuerySingleAsync<string>(new CommandDefinition(
                    "SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger WITH(UPDLOCK,HOLDLOCK) WHERE PortfolioId=@Id;",
                    new { Id = full.PortfolioId }, t, cancellationToken: ct));
                if (actual != root) throw new InvalidDataException("Portfolio advanced during qualification; preserve its new state and retry offline.");
                await SaveAsync(c, t, DelphiLiveObservationPortfolio.From(full), root, ct);
                t.Commit(); count++;
            }
            return count;
        }
        finally
        {
            try
            {
                await c.ExecuteAsync("""
EXEC sys.sp_releaseapplock @Resource=N'TraderVI.LiveSnapshotStorage',@LockOwner=N'Session';
EXEC sys.sp_releaseapplock @Resource=N'DelphiLiveCollectionV1',@LockOwner=N'Session';
""");
            }
            catch { SqlConnection.ClearPool(c); }
        }
    }

    public async Task<IReadOnlyList<DelphiLiveObservationPortfolio>> ReadAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString);
        var rows = await c.QueryAsync<Row>(new CommandDefinition("""
SELECT l.PortfolioId,l.Revision,l.SnapshotJson,s.Revision StateRevision,s.SourceHash,s.StateHash,s.StateJson
FROM dbo.DelphiLivePortfolioLedger l JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
LEFT JOIN dbo.EngineCurrentState s ON s.Kind=N'PaperObservation' AND s.OwnerId=l.PortfolioId
WHERE g.EffectiveTradingDate<=@Date AND (g.EndExclusiveTradingDate IS NULL OR g.EndExclusiveTradingDate>@Date);
""", new { Date = date.ToDateTime(TimeOnly.MinValue) }, cancellationToken: ct));
        var result = new List<DelphiLiveObservationPortfolio>();
        foreach (var row in rows)
        {
            if (row.StateJson is null || row.StateRevision != row.Revision || row.SourceHash != DelphiLiveSnapshotCodec.Hash(row.SnapshotJson) ||
                row.StateHash != DelphiLiveSnapshotCodec.Hash(row.StateJson))
                throw new InvalidDataException("Current paper observation state is missing, stale or damaged; qualify its checkpoint before collection.");
            var state = DelphiLiveLedgerJson.Deserialize<DelphiLiveObservationPortfolio>(row.StateJson);
            var root = System.Text.Json.Nodes.JsonNode.Parse(row.SnapshotJson)!;
            if (state.PortfolioId != row.PortfolioId || state.Revision != row.Revision || state.AsOfDate > date ||
                state.PolicyVersionId != root["policyVersionId"]!.GetValue<Guid>() || state.Role != root["role"]!.GetValue<string>())
                throw new InvalidDataException("Current observation identity differs from its source revision.");
            result.Add(state);
        }
        return result;
    }
    private sealed record Row(Guid PortfolioId, long Revision, string SnapshotJson, long? StateRevision,
        string? SourceHash, string? StateHash, string? StateJson);
}
