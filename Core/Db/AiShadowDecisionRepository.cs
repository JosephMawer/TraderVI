#nullable enable
using System;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Calibration;
using Core.Oracle;
using Core.Runtime;
using Core.Trader;
using Core.Trader.Gates;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Core.Db;

/// <summary>SELECT-only coherent read of the adopted source, saved decisions and bounded daily history.</summary>
public sealed class AiShadowDecisionRepository : SQLBase, IAiShadowDecisionSource
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly int historyDays;
    public AiShadowDecisionRepository(int historyDays)
    {
        if (historyDays < 1) throw new ArgumentOutOfRangeException(nameof(historyDays));
        this.historyDays = historyDays;
    }

    public async Task<AiShadowSavedContext?> ReadAsync(Guid portfolioId, DateOnly date, CancellationToken ct = default)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A saved paper account is required.");
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        CommandDefinition Query(string sql, object? args = null) => new(sql, args, t, cancellationToken: ct);
        var header = await c.QuerySingleOrDefaultAsync(Query("""
SELECT p.Lens,p.MaximumPositions,p.Status PortfolioStatus,g.Status GenerationStatus,g.IsGateComparison,
 s.SessionId,s.Status SessionStatus,s.TradingDate,
 COALESCE(r.CalibrationRunId,s.CalibrationRunId) RunId,r.RevisionId,
 COALESCE(r.EffectiveUtc,s.ActivationBaselineUtc,s.StartedUtc) EffectiveUtc,
 a.VersionId,a.AssignedUtc,v.Family,v.SettingsJson,v.SettingsHash
FROM dbo.ShadowPortfolio p JOIN dbo.ShadowPortfolioGeneration g ON g.GenerationId=p.GenerationId
JOIN dbo.ShadowPortfolioSession s ON s.PortfolioId=p.PortfolioId AND s.TradingDate=@Date
OUTER APPLY(SELECT TOP(1) * FROM dbo.ShadowDailySourceRevision WHERE SessionId=s.SessionId ORDER BY AdoptedUtc DESC,RevisionId) r
OUTER APPLY(SELECT TOP(1) * FROM dbo.EngineStrategyAssignment WHERE TargetId=p.PortfolioId ORDER BY Sequence DESC) a
LEFT JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId WHERE p.PortfolioId=@Id;
""", new { Id = portfolioId, Date = date.ToDateTime(TimeOnly.MinValue) }));
        if (header is null || header.RunId is null || (string)header.SessionStatus != "Active" ||
            (string)header.PortfolioStatus is "Draft" or "Stopped" || (string)header.GenerationStatus is "Draft" or "Stopped") return null;
        var comparison = (await PaperComparisonRepository.ReadAccountsAsync(c, t, ct)).SingleOrDefault(a => a.AccountId == portfolioId);
        if ((bool)header.IsGateComparison != (comparison is not null)) throw new InvalidDataException("Comparison account identity is missing.");
        string policyJson = comparison?.ShadowSettingsJson ?? (string?)header.SettingsJson ?? throw new InvalidDataException("Shadow policy is missing.");
        if (comparison is null && ((string?)header.Family != EngineStrategySettings.Shadow ||
            (string?)header.SettingsHash != EngineStrategySettings.Hash(policyJson))) throw new InvalidDataException("Shadow policy checksum differs.");
        Guid policyId = (comparison?.ShadowStrategyVersionId ?? (Guid?)header.VersionId) ?? throw new InvalidDataException("Shadow assignment is missing.");
        var policy = (SystemShadowPolicyConfig)EngineStrategySettings.Read(EngineStrategySettings.Shadow, policyJson);
        Guid runId = header.RunId;
        var run = await c.QuerySingleAsync(Query("""
SELECT RunId,StrategyVersionId,RecommendationDate,MarketDataAsOf,CreatedUtc,RunPurpose,AuditState,
 StrategyConfigJson,ModelSnapshotJson FROM dbo.CalibrationRun WHERE RunId=@Run;
""", new { Run = runId }));
        if (DateOnly.FromDateTime((DateTime)run.RecommendationDate) != date || run.StrategyVersionId is null)
            throw new InvalidDataException("Adopted publication has a different recommendation date or no strategy.");
        var config = JsonSerializer.Deserialize<StrategyConfig>((string)run.StrategyConfigJson, Json) ?? throw new InvalidDataException("Daily configuration is missing.");
        var models = JsonSerializer.Deserialize<ImmutableArray<ModelArtifactProvenance>>((string)run.ModelSnapshotJson, Json);
        if (models.IsDefaultOrEmpty) throw new InvalidDataException("Daily model identities are missing.");
        // Public identity projection deliberately drops local model paths and the full training provenance.
        var identities = models.Select(m => new TradingModelIdentity(m.ModelId, m.TaskType, m.ArtifactSha256)).ToImmutableArray();
        var rows = await c.QueryAsync(Query("""
SELECT c.CandidateId,c.Symbol,l.Rank,l.Direction,l.IsEligible,l.RankingKey,l.FirstFailedGate,l.TraceSchemaVersion,l.GateTraceJson,
 x.CandidateTrackingId,x.SessionId,x.State,x.ReasonCode,x.LastEvaluatedUtc,CAST(c.ObservationClose AS decimal(19,6)) PreviousClose
FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
LEFT JOIN dbo.ShadowPortfolioCandidate x ON x.CalibrationCandidateId=c.CandidateId AND x.SessionId=@Session
 AND (x.SourceRevisionId=@Revision OR (x.SourceRevisionId IS NULL AND @Revision IS NULL))
WHERE c.RunId=@Run AND l.Lens=@Lens AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND @Maximum ORDER BY l.Rank;
""", new { Run = runId, Session = (Guid)header.SessionId, Revision = (Guid?)header.RevisionId,
            Lens = (string)header.Lens, Maximum = (byte)header.MaximumPositions }));
        var candidates = rows.Select(row => (AiShadowDailyCandidate)MapCandidate(row)).ToImmutableArray();
        var history = await c.QueryAsync(Query("""
SELECT TradingDate,OpeningValue,ClosingValue,CompletedUtc FROM dbo.ShadowPortfolioSession
WHERE PortfolioId=@Id AND TradingDate BETWEEN @From AND @Through ORDER BY TradingDate;
""", new { Id = portfolioId, From = date.AddDays(1 - historyDays).ToDateTime(TimeOnly.MinValue), Through = date.ToDateTime(TimeOnly.MinValue) }));
        var days = history.Select(h => new PortfolioDailyObservation(DateOnly.FromDateTime((DateTime)h.TradingDate),
            (decimal)h.OpeningValue, (decimal?)h.ClosingValue, h.CompletedUtc is null ? null : Utc((DateTime)h.CompletedUtc),
            h.CompletedUtc is not null, h.CompletedUtc is null ? "No saved closing value" : "Saved paper session close")).ToImmutableArray();
        var result = new AiShadowSavedContext(portfolioId, (Guid)header.SessionId, date, policyId, policy,
            (string)header.Lens, (byte)header.MaximumPositions, comparison, runId, (Guid)run.StrategyVersionId,
            config, identities, DateOnly.FromDateTime((DateTime)run.MarketDataAsOf), Utc((DateTime)run.CreatedUtc),
            Utc((DateTime)header.EffectiveUtc), comparison?.EffectiveUtc ?? Utc((DateTime)header.AssignedUtc),
            (string)run.RunPurpose, (string)run.AuditState, candidates, days);
        await t.CommitAsync(ct);
        return result;
    }

    private static AiShadowDailyCandidate MapCandidate(dynamic row)
    {
        var gates = ReadGateTrace((int)row.TraceSchemaVersion, (string)row.GateTraceJson);
        SystemShadowRuntimeCandidate? decision = row.CandidateTrackingId is null ? null : new((Guid)row.CandidateTrackingId,
            (Guid)row.SessionId, (Guid)row.CandidateId, (string)row.Symbol, (int)row.Rank, (decimal)row.PreviousClose,
            (string)row.State, (string?)row.ReasonCode, row.LastEvaluatedUtc is null ? null : Utc((DateTime)row.LastEvaluatedUtc));
        return new((Guid)row.CandidateId, (string)row.Symbol, (int)row.Rank, (string)row.Direction, (bool)row.IsEligible,
            (double)row.RankingKey, (string?)row.FirstFailedGate, gates, decision);
    }
    internal static ImmutableArray<GateTraceEntry> ReadGateTrace(int version, string json)
    {
        var trace = JsonSerializer.Deserialize<LensTracePayload>(json, Json);
        if (version != CalibrationSchemaVersions.LensTrace || trace?.SchemaVersion != CalibrationSchemaVersions.LensTrace || trace.Gates is null)
            throw new InvalidDataException("Unknown or missing gate trace contract.");
        return trace.Gates.ToImmutableArray();
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
