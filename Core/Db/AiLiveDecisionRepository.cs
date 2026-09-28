#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Calibration;
using Core.Oracle;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Core.Db;

/// <summary>One SELECT-only transaction. Does not collect, evaluate, advance confirmation or change any account.</summary>
public sealed class AiLiveDecisionRepository : SQLBase, IAiLiveDecisionSource
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly int historyDays;
    public AiLiveDecisionRepository(int historyDays)
    {
        if (historyDays < 1) throw new ArgumentOutOfRangeException(nameof(historyDays));
        this.historyDays = historyDays;
    }
    public async Task<AiLiveSavedContext?> ReadAsync(Guid portfolioId, DateOnly date, CancellationToken ct = default)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A saved paper account is required.");
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        CommandDefinition Query(string sql, object? args = null) => new(sql, args, t, cancellationToken: ct);
        var row = await c.QuerySingleOrDefaultAsync(Query("""
SELECT l.*,g.EffectiveSessionOpenUtc,g.EndExclusiveTradingDate,a.CancelledUtc
FROM dbo.DelphiLivePortfolioLedger l JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
JOIN dbo.DelphiLivePolicyAssignment a ON a.AssignmentId=g.AssignmentId WHERE l.PortfolioId=@Id;
""", new { Id = portfolioId }));
        if (row is null || row.CancelledUtc is not null || row.EndExclusiveTradingDate is not null &&
            DateOnly.FromDateTime((DateTime)row.EndExclusiveTradingDate) <= date) return null;
        var snapshot = await DelphiLiveSnapshotStorage.ReadAsync(c, t, (string)row.SnapshotJson, ct);
        if (snapshot.PortfolioId != portfolioId || snapshot.GenerationId != (Guid)row.GenerationId ||
            snapshot.PolicyVersionId != (Guid)row.DelphiLivePolicyVersionId || snapshot.Revision != (long)row.Revision ||
            snapshot.UpdatedUtc != Utc((DateTime)row.UpdatedUtc)) throw new InvalidDataException("Live ledger identity mismatch.");
        if (snapshot.CurrentSession != date) return null;
        var comparison = (await PaperComparisonRepository.ReadAccountsAsync(c, t, ct)).SingleOrDefault(a => a.AccountId == portfolioId);
        if (snapshot.ComparisonAccount != comparison) throw new InvalidDataException("Live comparison identity mismatch.");
        var policy = await DelphiLiveSessionRepository.ReadPolicyAsync(c, snapshot.PolicyVersionId, t, ct);
        var session = await c.QuerySingleOrDefaultAsync(Query("""
SELECT SessionId,CalibrationRunId,DailyStrategyVersionId,SessionOpenUtc FROM dbo.DelphiLiveSession WHERE TradingDate=@Date;
""", new { Date = date.ToDateTime(TimeOnly.MinValue) }));
        if (session is null) return null;
        Guid sessionId = session.SessionId;
        Guid? sourceRevision = null, runId = session.CalibrationRunId;
        DateTime effective = Utc((DateTime)session.SessionOpenUtc);
        if (comparison is null)
        {
            var revision = await DelphiLiveSessionRepository.ReadSourceRevisionAsync(c, sessionId, t, ct);
            if (revision is not null) { sourceRevision = revision.RevisionId; runId = revision.RunId; effective = revision.EffectiveUtc; }
        }
        else
        {
            var source = await PaperComparisonRepository.ReadSourceAsync(c, t, comparison.BatchId, date.ToDateTime(TimeOnly.MinValue), ct);
            if (source is null || source.SessionId != sessionId || source.DailyStrategyVersionId != comparison.DailyStrategyVersionId) return null;
            sourceRevision = source.RevisionId; runId = source.RunId; effective = source.EffectiveUtc > comparison.EffectiveUtc ? source.EffectiveUtc : comparison.EffectiveUtc;
        }
        if (runId is null) return null;
        effective = new[] { effective, Utc((DateTime)row.EffectiveSessionOpenUtc) }.Max();
        var run = await c.QuerySingleAsync(Query("""
SELECT StrategyVersionId,RecommendationDate,MarketDataAsOf,CreatedUtc,RunPurpose,AuditState,StrategyConfigJson,ModelSnapshotJson
FROM dbo.CalibrationRun WHERE RunId=@Run;
""", new { Run = runId }));
        if ((string)run.RunPurpose != "OfficialPaper" || (string)run.AuditState != "Valid" || run.StrategyVersionId is null ||
            DateOnly.FromDateTime((DateTime)run.RecommendationDate) != date) throw new InvalidDataException("Invalid Live daily publication.");
        var config = JsonSerializer.Deserialize<StrategyConfig>((string)run.StrategyConfigJson, Json) ?? throw new InvalidDataException("Missing daily configuration.");
        var models = JsonSerializer.Deserialize<ImmutableArray<ModelArtifactProvenance>>((string)run.ModelSnapshotJson, Json);
        if (models.IsDefaultOrEmpty) throw new InvalidDataException("Missing daily model identities.");
        var epoch = await c.QuerySingleOrDefaultAsync(Query("""
SELECT TOP(1) EpochNumber,OperationalBuffersResetUtc,EndedUtc FROM dbo.DelphiLiveContinuityEpoch
WHERE SessionId=@Session ORDER BY EpochNumber DESC;
""", new { Session = sessionId }));
        if (epoch is null) return null;
        var daily = (await c.QueryAsync(Query("""
SELECT DISTINCT c.Symbol,c.CandidateId FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
WHERE c.RunId=@Run AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25 AND (@Ungated=1 OR l.IsEligible=1);
""", new { Run = runId, Ungated = comparison?.Profile == PaperGateProfile.PortfolioRulesOnlyV1 })))
            .ToDictionary(r => (string)r.Symbol, r => (Guid)r.CandidateId, StringComparer.Ordinal);
        string[] symbols = RequiredSymbols(snapshot, daily.Keys, date);
        var signals = ImmutableArray.CreateBuilder<AiLiveSignalHistory>();
        // Retain invalid/conflicted/latest rows: never fall back to an older usable checkpoint.
        var evaluations = symbols.Length == 0 ? [] : (await c.QueryAsync(Query("""
WITH ranked AS(SELECT e.*,ROW_NUMBER() OVER(PARTITION BY e.Symbol ORDER BY e.BarEndUtc DESC) n
 FROM dbo.DelphiLiveEvaluation e WHERE e.SessionId=@Session AND e.PolicyVersionId=@Policy AND e.Symbol IN @Symbols)
SELECT r.*,CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict f
 JOIN dbo.IntradayCollectionSlot s ON s.CollectionSlotId=f.CollectionSlotId
 WHERE s.SessionId=r.SessionId AND s.ExpectedBarEndUtc=r.BarEndUtc AND s.Symbol IN(r.Symbol,N'XIU')) THEN 1 ELSE 0 END AS bit) HasConflict
FROM ranked r WHERE n<=@Count ORDER BY Symbol,BarEndUtc;
""", new { Session = sessionId, Policy = policy.PolicyVersionId, Symbols = symbols,
            Count = System.Math.Max(policy.EntryConfirmationCount, policy.WeakeningConfirmationCount) + 1 }))).ToArray();
        foreach (string symbol in symbols)
        {
            var rows = evaluations.Where(e => (string)e.Symbol == symbol).ToArray();
            var checkpoints = rows.Select(e => ReadCheckpoint((Guid)e.EvaluationId, (Guid)e.SessionId, (Guid)e.PolicyVersionId,
                (string)e.Symbol, Utc((DateTime)e.BarEndUtc), Utc((DateTime)e.RecordedUtc), (int)e.ContinuityEpoch,
                (string)e.InputJson, (string)e.ResultJson)).Cast<DelphiLiveStoredEvaluation>().ToImmutableArray();
            signals.Add(new(symbol, daily.TryGetValue(symbol, out var id) ? id : null,
                snapshot.CandidateStates.GetValueOrDefault(symbol), checkpoints, rows.Any(e => (bool)e.HasConflict)));
        }
        var result = new AiLiveSavedContext(portfolioId, snapshot.GenerationId, snapshot.Revision, date, snapshot.UpdatedUtc,
            sessionId, policy, comparison, sourceRevision, snapshot.DailySourceRevisionId, runId.Value, (Guid)run.StrategyVersionId,
            config, models.Select(m => new TradingModelIdentity(m.ModelId, m.TaskType, m.ArtifactSha256)).ToImmutableArray(),
            DateOnly.FromDateTime((DateTime)run.MarketDataAsOf), Utc((DateTime)run.CreatedUtc), effective,
            (int)epoch.EpochNumber, Utc((DateTime)epoch.OperationalBuffersResetUtc), epoch.EndedUtc is null, signals.ToImmutable(),
            PortfolioDailyPerformance.FromLiveMarks(snapshot.Marks.Where(m => m.TradingDate >= date.AddDays(1 - historyDays))).ToImmutableArray());
        await t.CommitAsync(ct);
        return result;
    }
    internal static DelphiLiveStoredEvaluation ReadCheckpoint(Guid id, Guid session, Guid policy, string symbol,
        DateTime end, DateTime recorded, int epoch, string inputJson, string resultJson)
    {
        var input = DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationInput>(inputJson);
        var result = DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationResult>(resultJson);
        if (input.EvaluationId != id || result.EvaluationId != id || input.SessionId != session ||
            input.Policy.PolicyVersionId != policy || input.Stock.Symbol != symbol || input.BarEndUtc != end || input.EvaluatedUtc > recorded)
            throw new InvalidDataException("Stored Live checkpoint columns and content differ.");
        return new(input, result, epoch);
    }
    internal static string[] RequiredSymbols(DelphiLivePortfolioSnapshot snapshot, IEnumerable<string> selected, DateOnly date)
    {
        var daily = selected.ToHashSet(StringComparer.Ordinal);
        return daily.Concat(snapshot.Positions.Select(p => p.Symbol)).Distinct(StringComparer.Ordinal)
            .Where(symbol => DelphiLiveMonitorWorkflow.IsWithinOwnEntryScope(snapshot, symbol, date, daily.Contains(symbol)))
            .Order(StringComparer.Ordinal).ToArray();
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
