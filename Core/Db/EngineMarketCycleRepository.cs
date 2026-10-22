#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Durable handoff of exact shared inputs; consumer progress cannot hold up collection.</summary>
public sealed class EngineMarketCycleRepository : SQLBase
{
    public async Task PublishAsync(DelphiLiveSharedCheckpoint frame, DelphiLiveLease lease, CancellationToken ct)
    {
        frame.Validate();
        string context = DelphiLiveLedgerJson.Serialize(frame.Context), hash = DelphiLiveSnapshotCodec.Hash(context);
        string ids = DelphiLiveLedgerJson.Serialize(frame.Evaluations.Select(e => e.Input.EvaluationId).Order().ToArray());
        await using var c = new SqlConnection(ConnectionString);
        await c.ExecuteAsync(new CommandDefinition("""
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WITH(UPDLOCK,HOLDLOCK)
 WHERE LeaseId=@Lease AND OwnerId=@Owner AND FencingToken=@Fence AND IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 54430,'Shared cycle publisher lost its lease.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.IntradayCollectionCycle c JOIN dbo.DelphiLiveContinuityEpoch e ON e.ContinuityEpochId=c.ContinuityEpochId
 WHERE c.CycleId=@Cycle AND c.SessionId=@Session AND c.BarEndUtc=@End AND e.EpochNumber=@Epoch
 AND c.LeaseId=@Lease AND c.LeaseFencingToken=@Fence AND c.CompletedUtc IS NOT NULL)
 THROW 54431,'Shared checkpoint has no matching collection cycle.',1;
IF EXISTS(SELECT 1 FROM OPENJSON(@Ids) i LEFT JOIN dbo.EngineInputPublication e ON e.EvaluationId=TRY_CONVERT(uniqueidentifier,i.value)
 WHERE e.EvaluationId IS NULL OR e.SessionId<>@Session OR e.BarEndUtc<>@End OR e.ContinuityEpoch<>@Epoch)
 THROW 54432,'Shared checkpoint references incomplete or unrelated inputs.',1;
IF EXISTS(SELECT 1 FROM dbo.EngineEvidenceContent WITH(UPDLOCK,HOLDLOCK) WHERE ContentHash=@Hash
 AND CONVERT(varbinary(max),ContentJson)<>CONVERT(varbinary(max),@Context))
 THROW 54433,'Shared context fingerprint conflicts with existing content.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.EngineEvidenceContent WITH(UPDLOCK,HOLDLOCK) WHERE ContentHash=@Hash)
 INSERT dbo.EngineEvidenceContent(ContentHash,ContentJson) VALUES(@Hash,@Context);
IF EXISTS(SELECT 1 FROM dbo.EngineMarketCycle WITH(UPDLOCK,HOLDLOCK) WHERE CycleId=@Cycle)
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.EngineMarketCycle WHERE CycleId=@Cycle AND SessionId=@Session AND BarEndUtc=@End
  AND ContinuityEpoch=@Epoch AND ContextHash=@Hash AND InputIdsJson=@Ids)
  THROW 54434,'An immutable shared cycle already exists with different inputs.',1;
END
ELSE INSERT dbo.EngineMarketCycle(CycleId,SessionId,BarEndUtc,ContinuityEpoch,ContextHash,InputIdsJson)
 VALUES(@Cycle,@Session,@End,@Epoch,@Hash,@Ids);
COMMIT TRANSACTION;
""", new { Cycle = frame.CycleId, Session = frame.Context.Session.SessionId, End = frame.BarEndUtc,
            Epoch = frame.ContinuityEpoch, Hash = hash, Context = context, Ids = ids,
            Lease = lease.LeaseId, Owner = lease.OwnerId, Fence = lease.FencingToken }, cancellationToken: ct));
    }

    public async Task<DelphiLiveSharedCheckpoint?> ReadNextAsync(string consumer, bool newest, CancellationToken ct = default)
    {
        RequireConsumer(consumer);
        await using var c = new SqlConnection(ConnectionString);
        var row = await c.QuerySingleOrDefaultAsync<CycleRow>(new CommandDefinition("""
SELECT TOP(1) m.CycleId,m.SessionId,m.BarEndUtc,m.ContinuityEpoch,m.ContextHash,m.InputIdsJson,c.ContentJson
FROM dbo.EngineMarketCycle m LEFT JOIN dbo.EngineEvidenceContent c ON c.ContentHash=m.ContextHash
WHERE NOT EXISTS(SELECT 1 FROM dbo.EngineConsumerReceipt r WHERE r.Consumer=@Consumer AND r.CycleId=m.CycleId)
ORDER BY
""" + (newest ? "m.Sequence DESC;" : "m.Sequence;"), new { Consumer = consumer }, cancellationToken: ct));
        if (row is null) return null;
        if (row.ContentJson is null || DelphiLiveSnapshotCodec.Hash(row.ContentJson) != row.ContextHash) throw new InvalidDataException("Shared market context is missing or its checksum failed.");
        var context = DelphiLiveLedgerJson.Deserialize<DelphiLiveSessionContext>(row.ContentJson);
        var ids = DelphiLiveLedgerJson.Deserialize<ImmutableArray<Guid>>(row.InputIdsJson);
        var values = (await c.QueryAsync<InputRow>(new CommandDefinition("""
SELECT e.EvaluationId,e.InputJson,e.ResultJson,e.ContinuityEpoch
FROM dbo.EngineInputPublication e JOIN OPENJSON(@Ids) i ON e.EvaluationId=TRY_CONVERT(uniqueidentifier,i.value);
""", new { Ids = row.InputIdsJson }, cancellationToken: ct))).ToArray();
        if (ids.Distinct().Count() != ids.Length || values.Length != ids.Length || !ids.ToHashSet().SetEquals(values.Select(v => v.EvaluationId)))
            throw new InvalidDataException("Shared market checkpoint has missing or duplicate inputs.");
        var contents = await EngineEvidenceStorage.ReadAsync(c, null, values.Select(v => v.InputJson), ct);
        var restored = values.Select(v => new DelphiLiveStoredEvaluation(DelphiLiveInputCodec.Read(v.InputJson, contents),
            DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationResult>(v.ResultJson), v.ContinuityEpoch)).ToImmutableArray();
        if (values.Where((v, i) => v.EvaluationId != restored[i].Input.EvaluationId).Any())
            throw new InvalidDataException("Shared market input identity changed.");
        var frame = new DelphiLiveSharedCheckpoint(row.CycleId, context, DateTime.SpecifyKind(row.BarEndUtc, DateTimeKind.Utc), row.ContinuityEpoch, restored);
        if (frame.Context.Session.SessionId != row.SessionId) throw new InvalidDataException("Shared market context identity changed.");
        frame.Validate(); return frame;
    }

    public async Task<bool> HasConflictAsync(DelphiLiveSharedCheckpoint frame, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString);
        return await c.QuerySingleAsync<bool>(new CommandDefinition("""
SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict f
 JOIN dbo.IntradayCollectionSlot s ON s.CollectionSlotId=f.CollectionSlotId
 WHERE s.SessionId=@Session AND s.ExpectedBarEndUtc=@End) THEN 1 ELSE 0 END AS bit);
""", new { Session = frame.Context.Session.SessionId, End = frame.BarEndUtc }, cancellationToken: ct));
    }

    public async Task CompleteAsync(string consumer, Guid cycle, string outcome, CancellationToken ct = default)
    {
        RequireConsumer(consumer);
        if (outcome is not ("Applied" or "NotActionableLate" or "DefinitionChanged" or "EvidenceConflict" or "ResearchRecorded" or "ResearchNotApplicable"))
            throw new ArgumentException("An explicit consumer outcome is required.");
        await using var c = new SqlConnection(ConnectionString);
        await c.ExecuteAsync(new CommandDefinition("""
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.EngineConsumerReceipt WITH(UPDLOCK,HOLDLOCK) WHERE Consumer=@Consumer AND CycleId=@Cycle)
 INSERT dbo.EngineConsumerReceipt(Consumer,CycleId,Outcome) VALUES(@Consumer,@Cycle,@Outcome);
COMMIT TRANSACTION;
""", new { Consumer = consumer, Cycle = cycle, Outcome = outcome }, cancellationToken: ct));
    }
    private static void RequireConsumer(string consumer)
    { if (consumer is not ("Paper" or "Research")) throw new ArgumentException("Unknown shared-input consumer."); }
    private sealed record CycleRow(Guid CycleId, Guid SessionId, DateTime BarEndUtc, int ContinuityEpoch,
        string ContextHash, string InputIdsJson, string? ContentJson);
    private sealed record InputRow(Guid EvaluationId, string InputJson, string ResultJson, int ContinuityEpoch);
}
