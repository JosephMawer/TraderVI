#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class DelphiLiveEvaluationRepository : SQLBase, IDelphiLiveEvaluationStore
{
    private readonly bool independentInputs;
    public DelphiLiveEvaluationRepository(bool independentInputs = false) => this.independentInputs = independentInputs;
    private string StorageSql(string sql) => independentInputs
        ? sql.Replace("dbo.DelphiLiveEvaluation", "dbo.EngineInputPublication", StringComparison.Ordinal) : sql;
    public async Task<DelphiLiveStoredEvaluation?> GetLatestAsync(Guid sessionId, Guid policyId, string symbol, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(StorageSql("""
SELECT TOP(1) InputJson,ResultJson,ContinuityEpoch FROM dbo.DelphiLiveEvaluation
WHERE SessionId=@Session AND PolicyVersionId=@Policy AND Symbol=@Symbol ORDER BY BarEndUtc DESC;
"""), new { Session = sessionId, Policy = policyId, Symbol = symbol }, cancellationToken: cancellationToken));
        if (row is null) return null;
        if (!independentInputs) return Read(row);
        var contents = await EngineEvidenceStorage.ReadAsync(connection, null, new[] { (string)row.InputJson }, cancellationToken);
        return Read(row, contents);
    }

    public async Task<IReadOnlyList<DelphiLiveStoredEvaluation>> GetLatestSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = (await connection.QueryAsync(new CommandDefinition(StorageSql("""
WITH ranked AS (SELECT InputJson,ResultJson,ContinuityEpoch,
 ROW_NUMBER() OVER(PARTITION BY PolicyVersionId,Symbol ORDER BY BarEndUtc DESC) AS Sequence
 FROM dbo.DelphiLiveEvaluation WHERE SessionId=@Session)
SELECT InputJson,ResultJson,ContinuityEpoch FROM ranked WHERE Sequence=1;
"""), new { Session = sessionId }, cancellationToken: cancellationToken))).ToArray();
        if (!independentInputs) return rows.Select(row => Read(row)).Cast<DelphiLiveStoredEvaluation>().ToArray();
        var contents = await EngineEvidenceStorage.ReadAsync(connection, null, rows.Select(row => (string)row.InputJson), cancellationToken);
        return rows.Select(row => Read(row, contents)).Cast<DelphiLiveStoredEvaluation>().ToArray();
    }

    public async Task PersistAsync(DelphiLiveEvaluationInput input, DelphiLiveEvaluationResult result,
        int continuityEpoch, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        ValidateEnvelope(input, result, continuityEpoch, lease);
        // A timeout may occur after commit. Replay these exact bytes/IDs/times, never a new evaluation.
        var package = independentInputs ? DelphiLiveInputCodec.Pack(input) : null;
        string inputJson = package?.InputJson ?? DelphiLiveLedgerJson.Serialize(input), resultJson = DelphiLiveLedgerJson.Serialize(result);
        string sql = StorageSql(PersistSql).Replace("-- SharedEvidence", independentInputs ? EngineEvidenceStorage.InsertSql : "", StringComparison.Ordinal);
        var stage = Core.Runtime.DelphiLiveEvaluationSaveStage.Opening;
        long started = Stopwatch.GetTimestamp(), stageStarted = started;
        void Stage(Core.Runtime.DelphiLiveEvaluationSaveStage value)
        { stage = value; stageStarted = Stopwatch.GetTimestamp(); }
        await DelphiLiveEvaluationWriteRecovery.RunAsync(async (_, token) =>
        {
            started = Stopwatch.GetTimestamp(); Stage(Core.Runtime.DelphiLiveEvaluationSaveStage.Opening);
            // Disposing this failed connection completes before RunAsync can obtain a new one.
            await using var connection = new SqlConnection(ConnectionString);
            connection.InfoMessage += (_, message) =>
            {
                foreach (SqlError info in message.Errors)
                    if (Core.Runtime.DelphiLiveFailureDiagnostics.ReadEvaluationStage(info.Message) is { } value) Stage(value);
            };
            await connection.OpenAsync(token);
            Stage(Core.Runtime.DelphiLiveEvaluationSaveStage.PolicyRead);
            var storedPolicy = await DelphiLiveSessionRepository.ReadPolicyAsync(connection, input.Policy.PolicyVersionId, null, token);
            using var actualPolicy = JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(input.Policy));
            using var expectedPolicy = JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(storedPolicy));
            if (!JsonElement.DeepEquals(actualPolicy.RootElement, expectedPolicy.RootElement))
                throw new InvalidOperationException("Evaluation settings differ from the immutable stored policy identity.");
            Stage(Core.Runtime.DelphiLiveEvaluationSaveStage.ObservedClock);
            await SqlObservedTimeBarrier.WaitAsync(connection, input.EvaluatedUtc, token);
            Stage(Core.Runtime.DelphiLiveEvaluationSaveStage.PolicyAssignment);
            bool assigned = await EngineSettingsRepository.IsLivePolicyAssignedAsync(connection, input.SessionId, input.Policy.PolicyVersionId);
            token.ThrowIfCancellationRequested();
            Stage(Core.Runtime.DelphiLiveEvaluationSaveStage.BatchStart);
            await connection.ExecuteAsync(new CommandDefinition(sql, PaperSourceSqlParameters.Create(new
            {
                Id = input.EvaluationId, Session = input.SessionId, Policy = input.Policy.PolicyVersionId,
                Symbol = input.Stock.Symbol, End = input.BarEndUtc, Epoch = continuityEpoch,
                Lease = lease.LeaseId, Token = lease.FencingToken, Owner = lease.OwnerId,
                Valid = result.ObservationIsValid, Confirmed = result.ConfirmedLiveEligible, SettingsAuthorized = assigned,
                Evidence = result.CurrentStockObservationId, Evaluated = input.EvaluatedUtc, Input = inputJson, Result = resultJson,
                Contents = package is null ? "[]" : EngineEvidenceStorage.Serialize(package.Contents)
            }, ("Evaluated", input.EvaluatedUtc), ("End", input.BarEndUtc)), cancellationToken: token));
        }, (attempt, error, retry) => Core.Runtime.DelphiLiveFailureDiagnostics.RecordEvaluationSaveAsync(
            attempt, stage, Stopwatch.GetElapsedTime(stageStarted), Stopwatch.GetElapsedTime(started), error, retry), cancellationToken);
    }

    internal static void ValidateEnvelope(DelphiLiveEvaluationInput input, DelphiLiveEvaluationResult result,
        int continuityEpoch, DelphiLiveLease lease)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(lease);
        input.Policy.Validate();
        if (input.EvaluationId == Guid.Empty || input.SessionId == Guid.Empty || input.EvaluationId != result.EvaluationId ||
            continuityEpoch < 1 || lease.LeaseId == Guid.Empty || lease.FencingToken < 1 ||
            input.BarEndUtc.Kind != DateTimeKind.Utc || input.EvaluatedUtc.Kind != DateTimeKind.Utc ||
            input.EvaluatedUtc <= input.BarEndUtc || input.Xiu.Symbol != "XIU" ||
            (result.ObservationIsValid && (!input.ExactPairPersistedOnTime || result.CurrentStockObservationId is null)) ||
            (result.ConfirmedLiveEligible && !result.ObservationIsValid))
            throw new ArgumentException("Evaluation identities, evidence, timestamps, and lease must agree.");
    }

    private static DelphiLiveStoredEvaluation Read(dynamic row) => new(
        DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationInput>((string)row.InputJson),
        DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationResult>((string)row.ResultJson), (int)row.ContinuityEpoch);

    private static DelphiLiveStoredEvaluation Read(dynamic row, IReadOnlyDictionary<string, string> contents) => new(
        DelphiLiveInputCodec.Read((string)row.InputJson, contents),
        DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationResult>((string)row.ResultJson), (int)row.ContinuityEpoch);

    internal const string PersistSql = """
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @LockResult INT;
RAISERROR(N'EvaluationSave:ApplicationLock',10,1) WITH NOWAIT;
EXEC @LockResult=sys.sp_getapplock @Resource=N'DelphiLiveCollectionV1',@LockMode=N'Exclusive',
 @LockOwner=N'Transaction',@LockTimeout=10000;
IF @LockResult<0 THROW 51241,'Delphi Live evaluation store lock unavailable.',1;
RAISERROR(N'EvaluationSave:Lease',10,1) WITH NOWAIT;
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WITH(UPDLOCK,HOLDLOCK)
 WHERE LeaseId=@Lease AND OwnerId=@Owner AND FencingToken=@Token AND IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 51242,'Delphi Live evaluation writer lost its host lease.',1;
RAISERROR(N'EvaluationSave:Policy',10,1) WITH NOWAIT;
IF @SettingsAuthorized=0 AND NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveSessionPolicy WHERE SessionId=@Session AND DelphiLivePolicyVersionId=@Policy)
 THROW 51243,'Evaluation policy is not frozen for this session.',1;
RAISERROR(N'EvaluationSave:Epoch',10,1) WITH NOWAIT;
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveContinuityEpoch WHERE SessionId=@Session AND EpochNumber=@Epoch
 AND LeaseId=@Lease AND LeaseFencingToken=@Token AND EndedUtc IS NULL)
 THROW 51245,'Evaluation does not belong to this active continuity epoch.',1;
IF @Evaluated>SYSUTCDATETIME() OR @Evaluated<=@End
 THROW 51246,'Evaluation time must be causal and already observed.',1;
RAISERROR(N'EvaluationSave:Evidence',10,1) WITH NOWAIT;
IF @Valid=1 AND NOT EXISTS
 (
  SELECT 1 FROM dbo.IntradayCollectionSlot s JOIN dbo.IntradayCollectionSlot x ON x.CycleId=s.CycleId AND x.Symbol=N'XIU'
  WHERE s.SessionId=@Session AND s.Symbol=@Symbol AND s.ExpectedBarEndUtc=@End AND s.EvidenceBarId=@Evidence
   AND s.OperationallyUsable=1 AND x.OperationallyUsable=1 AND s.SettledUtc<=@Evaluated AND x.SettledUtc<=@Evaluated
   AND NOT EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict c WHERE c.CollectionSlotId IN(s.CollectionSlotId,x.CollectionSlotId))
 ) THROW 51247,'Valid evaluation requires its exact durable on-time symbol and XIU observations.',1;
RAISERROR(N'EvaluationSave:Checkpoint',10,1) WITH NOWAIT;
-- SharedEvidence
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveEvaluation WITH(UPDLOCK,HOLDLOCK)
 WHERE SessionId=@Session AND PolicyVersionId=@Policy AND Symbol=@Symbol AND BarEndUtc=@End)
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveEvaluation WHERE EvaluationId=@Id AND InputJson=@Input AND ResultJson=@Result)
  THROW 51244,'An immutable checkpoint evaluation already exists.',1;
END
ELSE
BEGIN
 RAISERROR(N'EvaluationSave:Insert',10,1) WITH NOWAIT;
 INSERT dbo.DelphiLiveEvaluation
 (EvaluationId,SessionId,PolicyVersionId,Symbol,BarEndUtc,ContinuityEpoch,LeaseFencingToken,
 ObservedOnTime,ConfirmedLiveEligible,InputJson,ResultJson)
 VALUES(@Id,@Session,@Policy,@Symbol,@End,@Epoch,@Token,@Valid,@Confirmed,@Input,@Result);
END
RAISERROR(N'EvaluationSave:Commit',10,1) WITH NOWAIT;
COMMIT TRANSACTION;
""";
}
