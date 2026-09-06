#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class DelphiLiveEvaluationRepository : SQLBase, IDelphiLiveEvaluationStore
{
    public async Task<DelphiLiveStoredEvaluation?> GetLatestAsync(Guid sessionId, Guid policyId, string symbol, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition("""
SELECT TOP(1) InputJson,ResultJson,ContinuityEpoch FROM dbo.DelphiLiveEvaluation
WHERE SessionId=@Session AND PolicyVersionId=@Policy AND Symbol=@Symbol ORDER BY BarEndUtc DESC;
""", new { Session = sessionId, Policy = policyId, Symbol = symbol }, cancellationToken: cancellationToken));
        return row is null ? null : Read(row);
    }

    public async Task<IReadOnlyList<DelphiLiveStoredEvaluation>> GetLatestSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync(new CommandDefinition("""
WITH ranked AS (SELECT InputJson,ResultJson,ContinuityEpoch,
 ROW_NUMBER() OVER(PARTITION BY PolicyVersionId,Symbol ORDER BY BarEndUtc DESC) AS Sequence
 FROM dbo.DelphiLiveEvaluation WHERE SessionId=@Session)
SELECT InputJson,ResultJson,ContinuityEpoch FROM ranked WHERE Sequence=1;
""", new { Session = sessionId }, cancellationToken: cancellationToken));
        return rows.Select(row => Read(row)).Cast<DelphiLiveStoredEvaluation>().ToArray();
    }

    public async Task PersistAsync(DelphiLiveEvaluationInput input, DelphiLiveEvaluationResult result,
        int continuityEpoch, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        ValidateEnvelope(input, result, continuityEpoch, lease);
        await using var connection = new SqlConnection(ConnectionString);
        var storedPolicy = await DelphiLiveSessionRepository.ReadPolicyAsync(connection,
            input.Policy.PolicyVersionId, null, cancellationToken);
        using var actualPolicy = JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(input.Policy));
        using var expectedPolicy = JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(storedPolicy));
        if (!JsonElement.DeepEquals(actualPolicy.RootElement, expectedPolicy.RootElement))
            throw new InvalidOperationException("Evaluation settings differ from the immutable stored policy identity.");
        await connection.ExecuteAsync(new CommandDefinition(PersistSql, new
        {
            Id = input.EvaluationId, Session = input.SessionId, Policy = input.Policy.PolicyVersionId,
            Symbol = input.Stock.Symbol, End = input.BarEndUtc, Epoch = continuityEpoch,
            Lease = lease.LeaseId, Token = lease.FencingToken, Owner = lease.OwnerId,
            Valid = result.ObservationIsValid, Confirmed = result.ConfirmedLiveEligible,
            Evidence = result.CurrentStockObservationId, Evaluated = input.EvaluatedUtc,
            Input = DelphiLiveLedgerJson.Serialize(input), Result = DelphiLiveLedgerJson.Serialize(result)
        }, cancellationToken: cancellationToken));
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

    internal const string PersistSql = """
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @LockResult INT;
EXEC @LockResult=sys.sp_getapplock @Resource=N'DelphiLiveCollectionV1',@LockMode=N'Exclusive',
 @LockOwner=N'Transaction',@LockTimeout=10000;
IF @LockResult<0 THROW 51241,'Delphi Live evaluation store lock unavailable.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WITH(UPDLOCK,HOLDLOCK)
 WHERE LeaseId=@Lease AND OwnerId=@Owner AND FencingToken=@Token AND IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 51242,'Delphi Live evaluation writer lost its host lease.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveSessionPolicy WHERE SessionId=@Session AND DelphiLivePolicyVersionId=@Policy)
 THROW 51243,'Evaluation policy is not frozen for this session.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveContinuityEpoch WHERE SessionId=@Session AND EpochNumber=@Epoch
 AND LeaseId=@Lease AND LeaseFencingToken=@Token AND EndedUtc IS NULL)
 THROW 51245,'Evaluation does not belong to this active continuity epoch.',1;
IF @Evaluated>SYSUTCDATETIME() OR @Evaluated<=@End
 THROW 51246,'Evaluation time must be causal and already observed.',1;
IF @Valid=1 AND NOT EXISTS
 (
  SELECT 1 FROM dbo.IntradayCollectionSlot s JOIN dbo.IntradayCollectionSlot x ON x.CycleId=s.CycleId AND x.Symbol=N'XIU'
  WHERE s.SessionId=@Session AND s.Symbol=@Symbol AND s.ExpectedBarEndUtc=@End AND s.EvidenceBarId=@Evidence
   AND s.OperationallyUsable=1 AND x.OperationallyUsable=1 AND s.SettledUtc<=@Evaluated AND x.SettledUtc<=@Evaluated
   AND NOT EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict c WHERE c.CollectionSlotId IN(s.CollectionSlotId,x.CollectionSlotId))
 ) THROW 51247,'Valid evaluation requires its exact durable on-time symbol and XIU observations.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveEvaluation WITH(UPDLOCK,HOLDLOCK)
 WHERE SessionId=@Session AND PolicyVersionId=@Policy AND Symbol=@Symbol AND BarEndUtc=@End)
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveEvaluation WHERE EvaluationId=@Id AND InputJson=@Input AND ResultJson=@Result)
  THROW 51244,'An immutable checkpoint evaluation already exists.',1;
END
ELSE
 INSERT dbo.DelphiLiveEvaluation
 (EvaluationId,SessionId,PolicyVersionId,Symbol,BarEndUtc,ContinuityEpoch,LeaseFencingToken,
 ObservedOnTime,ConfirmedLiveEligible,InputJson,ResultJson)
 VALUES(@Id,@Session,@Policy,@Symbol,@End,@Epoch,@Token,@Valid,@Confirmed,@Input,@Result);
COMMIT TRANSACTION;
""";
}
