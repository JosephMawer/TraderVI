#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class DelphiLiveSessionRepository
{
    private async Task RefreshOfficialDailySourceAsync(DateOnly date, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        var bounds = calendar.GetSessionBounds(date);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockSessionAsync(connection, transaction, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WITH(UPDLOCK,HOLDLOCK)
 WHERE LeaseId=@LeaseId AND OwnerId=@OwnerId AND FencingToken=@FencingToken AND IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 52310,'Daily source adoption requires the current host lease.',1;
""", lease, transaction, cancellationToken: cancellationToken));
        var frozen = await ReadFrozenAsync(connection, date, transaction, cancellationToken)
            ?? throw new InvalidOperationException("Daily source adoption requires the original session freeze.");
        DateTime now = Utc(await connection.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken)));
        if (now < bounds.OpenUtc || now >= bounds.CloseUtc) { await transaction.CommitAsync(cancellationToken); return; }
        var priorDate = calendar.GetImmediatelyPrecedingSession(date);
        var run = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(CalibrationEvidenceRepository.LatestOperationalRunSql,
            PaperSourceSqlParameters.Create(new { Date = Date(date), Prior = Date(priorDate) }, ("Now", now)), transaction, cancellationToken: cancellationToken));
        var current = await connection.QuerySingleOrDefaultAsync(new CommandDefinition("""
SELECT TOP (1) CalibrationRunId,PublishedUtc FROM dbo.DelphiLiveDailySourceRevision
 WHERE SessionId=@Session ORDER BY AdoptedUtc DESC,RevisionId;
""", new { Session = frozen.SessionId }, transaction, cancellationToken: cancellationToken));
        Guid? currentId = current is null ? frozen.CalibrationRunId : (Guid)current.CalibrationRunId;
        DateTime? currentPublished = current is null ? await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT CreatedUtc FROM dbo.CalibrationRun WHERE RunId=@Id;", new { Id = currentId }, transaction, cancellationToken: cancellationToken)) : (DateTime)current.PublishedUtc;
        if (run is null || !PaperDailySourcePolicy.ShouldAdopt(currentId, currentPublished, (Guid)run.RunId, Utc((DateTime)run.CreatedUtc), now))
        { await transaction.CommitAsync(cancellationToken); return; }
        Guid runId = (Guid)run.RunId, strategyId = (Guid)run.StrategyVersionId;
        await connection.ExecuteAsync(new CommandDefinition("""
IF EXISTS(SELECT l.Lens,l.Rank FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsEligible=1 AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25
 GROUP BY l.Lens,l.Rank HAVING COUNT(*)>1)
 THROW 52311,'Daily adoption source contains duplicate published ranks.',1;
IF EXISTS(SELECT 1 FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsEligible=1 AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25
 AND (c.ObservationDate<>@Prior OR c.ObservationClose<=0 OR c.Symbol=N'XIU'))
 THROW 52312,'Daily adoption candidate has an invalid observation identity.',1;
""", new { Run = runId, Prior = Date(priorDate) }, transaction, cancellationToken: cancellationToken));
        var rows = (await connection.QueryAsync(new CommandDefinition("""
SELECT c.CandidateId,c.Symbol,c.CompositeScore,c.SnapshotJson,l.LensEvaluationId,l.Lens,l.IsEligible,l.IsPublished,
 l.Rank,l.RankingKey,l.FirstFailedGate,l.GateTraceJson
FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
WHERE c.RunId=@Run AND l.IsEligible=1 AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25
 AND l.Lens IN(N'Continuation',N'Breakout') ORDER BY c.Symbol,l.Lens;
""", new { Run = runId }, transaction, cancellationToken: cancellationToken))).ToArray();
        var candidates = rows.GroupBy(r => (string)r.Symbol, StringComparer.Ordinal).Select(g =>
        {
            var c = g.First();
            return new DelphiLiveFrozenCandidate((Guid)c.CandidateId, g.Key, Convert.ToDecimal(c.CompositeScore), (string)c.SnapshotJson,
                g.Select(l => new DelphiLiveLensSource((Guid)l.LensEvaluationId, (Guid)l.CandidateId, (string)l.Lens,
                    (bool)l.IsEligible, (bool)l.IsPublished, (int)l.Rank, Convert.ToDecimal(l.RankingKey),
                    (string?)l.FirstFailedGate, (string)l.GateTraceJson)).ToArray());
        }).ToArray();
        var symbols = frozen.Symbols.Concat(candidates.Select(c => c.Symbol)).Distinct(StringComparer.Ordinal).ToArray();
        var baselines = new Dictionary<string, DelphiLiveFrozenBaseline>(StringComparer.Ordinal);
        await FreezeBaselinesAsync(connection, transaction, frozen.SessionId, date, priorDate, now, now, symbols, cancellationToken, baselines);
        now = Utc(await connection.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken)));
        if (now >= bounds.CloseUtc) { await transaction.RollbackAsync(cancellationToken); return; }
        Guid revisionId = Guid.NewGuid();
        DateTime effective = PaperDailySourcePolicy.EffectiveBoundary(now);
        var revision = new DelphiLiveDailySourceRevision(revisionId, runId, strategyId, Utc((DateTime)run.CreatedUtc), now, effective, candidates, baselines);
        string json = DelphiLiveLedgerJson.Serialize(revision);
        await connection.ExecuteAsync(new CommandDefinition("""
INSERT dbo.DelphiLiveDailySourceRevision(RevisionId,SessionId,CalibrationRunId,PreviousRunId,PublishedUtc,AdoptedUtc,EffectiveUtc,
 SnapshotJson,SnapshotSha256,CodeCommit,WorkingTreeState)
VALUES(@Revision,@Session,@Run,@Previous,@Published,@Now,@Effective,@Json,@Hash,@Code,@Tree);
INSERT dbo.DelphiLiveSessionSymbol(SessionSymbolId,SessionId,Symbol,IsFrozenDailyCandidate,IsAdoptedDailyCandidate,
 IsXiuBenchmark,IsTrackedHolding,IsDelphiLiveHolding,HasPendingProtectiveSell,IsSessionCarryCandidate,
 FrozenSourceLensCount,BestFrozenSourceLensRank,RequiredFromBarEndUtc,RequiredThroughBarEndUtc,SourceIdentityJson,AddedUtc)
SELECT NEWID(),@Session,s.Symbol,0,1,0,0,0,0,0,0,NULL,
 CASE WHEN DATEADD(MINUTE,5,@Effective)>@Close THEN @Close ELSE DATEADD(MINUTE,5,@Effective) END,@Close,
 (SELECT @Revision sourceRevisionId,@Run calibrationRunId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@Now
FROM OPENJSON(@Symbols) WITH(Symbol NVARCHAR(20) '$') s
WHERE NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveSessionSymbol t WHERE t.SessionId=@Session AND t.Symbol=s.Symbol);
""", PaperSourceSqlParameters.Create(new { Revision = revisionId, Session = frozen.SessionId, Run = runId, Previous = currentId, Published = revision.PublishedUtc,
            Now = now, Effective = effective, Json = json, Hash = SHA256.HashData(Encoding.UTF8.GetBytes(json)), Code = code.Commit,
            Tree = code.WorkingTreeState, Close = bounds.CloseUtc, Symbols = System.Text.Json.JsonSerializer.Serialize(candidates.Select(c => c.Symbol)) },
            ("Published", revision.PublishedUtc), ("Now", now), ("Effective", effective), ("Close", bounds.CloseUtc)),
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task<DelphiLiveDailySourceRevision?> ReadSourceRevisionAsync(SqlConnection connection, Guid sessionId,
        SqlTransaction? transaction, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition("""
SELECT TOP (1) RevisionId,CalibrationRunId,SnapshotJson,SnapshotSha256 FROM dbo.DelphiLiveDailySourceRevision
WHERE SessionId=@Session ORDER BY AdoptedUtc DESC,RevisionId;
""", new { Session = sessionId }, transaction, cancellationToken: ct));
        if (row is null) return null;
        string json = (string)row.SnapshotJson;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(json)), (byte[])row.SnapshotSha256))
            throw new InvalidOperationException("Daily source snapshot checksum mismatch.");
        var revision = DelphiLiveLedgerJson.Deserialize<DelphiLiveDailySourceRevision>(json);
        if (revision.RevisionId != (Guid)row.RevisionId || revision.RunId != (Guid)row.CalibrationRunId)
            throw new InvalidOperationException("Daily source snapshot identity mismatch.");
        return revision;
    }
}
