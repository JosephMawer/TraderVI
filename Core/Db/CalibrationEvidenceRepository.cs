using Core.Calibration;
using Core.Trader;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class CalibrationEvidenceRepository : SQLBase
{
    public async Task<IReadOnlyList<FreshDelphiBreakoutEvidenceSnapshot>>
        GetValidOfficialBreakoutTimelineAsync(
            string symbol,
            DateTime entryUtc,
            DateTime availableNoLaterThanUtc,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        if (entryUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Entry timestamp must be UTC.", nameof(entryUtc));
        if (availableNoLaterThanUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Availability timestamp must be UTC.", nameof(availableNoLaterThanUtc));
        if (availableNoLaterThanUtc < entryUtc)
            throw new ArgumentOutOfRangeException(
                nameof(availableNoLaterThanUtc),
                "Availability cannot precede entry.");

        const string sql = """
SELECT
 r.[RunId],r.[StartedUtc],r.[CreatedUtc],
 CAST(CASE WHEN r.[AuditState] = 'Valid' THEN 1 ELSE 0 END AS bit) AS [IsValid],
 CAST(COALESCE(l.[IsPublished], 0) AS bit) AS [IsBreakoutPublished],
 c.[BreakoutProbability],c.[DirectionEdge],c.[DownProbability]
FROM [dbo].[CalibrationRun] r
LEFT JOIN [dbo].[CalibrationCandidate] c
  ON c.[RunId] = r.[RunId]
 AND c.[Symbol] = @Symbol
LEFT JOIN [dbo].[CalibrationLensEvaluation] l
  ON l.[CandidateId] = c.[CandidateId]
 AND l.[Lens] = 'Breakout'
WHERE r.[RunPurpose] = 'OfficialPaper'
  AND r.[AuditState] = 'Valid'
  AND r.[StartedUtc] > @EntryUtc
  AND r.[StartedUtc] <= @AvailableNoLaterThanUtc
  AND r.[CreatedUtc] <= @AvailableNoLaterThanUtc
ORDER BY r.[CreatedUtc],r.[StartedUtc],r.[RunId];
""";

        var rows = new List<FreshDelphiBreakoutEvidenceSnapshot>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = symbol.Trim().ToUpperInvariant() },
            new SqlParameter("@EntryUtc", SqlDbType.DateTime2) { Value = entryUtc },
            new SqlParameter("@AvailableNoLaterThanUtc", SqlDbType.DateTime2) { Value = availableNoLaterThanUtc }
        ]);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FreshDelphiBreakoutEvidenceSnapshot(
                reader.GetGuid(0),
                DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7)));
        }

        return rows.AsReadOnly();
    }

    public async Task<CalibrationRunInfo?> GetLatestRunAsync(
        DateTime recommendationDate,
        CalibrationRunPurpose purpose = CalibrationRunPurpose.OfficialPaper,
        DateTime? createdNoLaterThanUtc = null)
    {
        const string sql = """
SELECT TOP 1
 [RunId],[RunPurpose],[RecommendationDate],[MarketDataAsOf],[StartedUtc],[CreatedUtc],
 [StrategyVersionId],[StrategyConfigJson],[ModelSnapshotJson],[RunContextJson],[CodeCommit],
 [AuditState],[AuditMessage],[SymbolsDiscovered],[SymbolsModelEvaluated],[SkippedHistory],
 [SkippedStaleHistory],[SkippedUnaffordable],[SkippedLowPrice],[SkippedLowVolume],[SkippedLeveragedEtp]
FROM [dbo].[CalibrationRun]
WHERE [RecommendationDate] = @RecommendationDate
  AND [RunPurpose] = @Purpose
  AND (@CreatedNoLaterThanUtc IS NULL OR [CreatedUtc] <= @CreatedNoLaterThanUtc)
ORDER BY [CreatedUtc] DESC;
""";
        List<CalibrationRunInfo> rows = await ExecuteReaderAsync(
            sql,
            [
                new SqlParameter("@RecommendationDate", SqlDbType.Date) { Value = recommendationDate.Date },
                new SqlParameter("@Purpose", SqlDbType.NVarChar, 32) { Value = purpose.ToString() },
                new SqlParameter("@CreatedNoLaterThanUtc", SqlDbType.DateTime2)
                {
                    Value = createdNoLaterThanUtc.HasValue ? createdNoLaterThanUtc.Value : DBNull.Value
                }
            ],
            reader => new CalibrationRunInfo(
                reader.GetGuid(0),
                Enum.Parse<CalibrationRunPurpose>(reader.GetString(1)),
                reader.GetDateTime(2),
                reader.GetDateTime(3),
                reader.GetDateTime(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                Enum.Parse<CalibrationAuditState>(reader.GetString(11)),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetInt32(13),
                reader.GetInt32(14),
                reader.GetInt32(15),
                reader.GetInt32(16),
                reader.GetInt32(17),
                reader.GetInt32(18),
                reader.GetInt32(19),
                reader.GetInt32(20)));
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<CalibrationCandidateRunInfo?> GetCandidateAsync(
        Guid runId,
        string symbol,
        string lens = "Continuation")
    {
        const string sql = """
SELECT c.[Symbol],c.[UpProbability],c.[DownProbability],c.[BreakoutProbability],
 c.[VolExpansionProbability],c.[DirectionEdge],c.[CompositeScore],c.[ObvState],
 c.[SnapshotJson],l.[GateTraceJson]
FROM [dbo].[CalibrationCandidate] c
INNER JOIN [dbo].[CalibrationLensEvaluation] l ON l.[CandidateId] = c.[CandidateId]
WHERE c.[RunId] = @RunId AND c.[Symbol] = @Symbol AND l.[Lens] = @Lens;
""";
        List<CalibrationCandidateRunInfo> rows = await ExecuteReaderAsync(
            sql,
            [
                new SqlParameter("@RunId", SqlDbType.UniqueIdentifier) { Value = runId },
                new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = symbol },
                new SqlParameter("@Lens", SqlDbType.NVarChar, 16) { Value = lens }
            ],
            reader => new CalibrationCandidateRunInfo(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9)));
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<IReadOnlyList<CalibrationObvStateCount>> GetObvStateCountsAsync(Guid runId)
    {
        const string sql = """
SELECT COALESCE([ObvState], 'Unavailable'), COUNT(*)
FROM [dbo].[CalibrationCandidate]
WHERE [RunId] = @RunId
GROUP BY [ObvState];
""";
        return await ExecuteReaderAsync(
            sql,
            [new SqlParameter("@RunId", SqlDbType.UniqueIdentifier) { Value = runId }],
            reader => new CalibrationObvStateCount(reader.GetString(0), reader.GetInt32(1)));
    }

    public async Task AppendAsync(CalibrationEvidenceBatch batch, CancellationToken cancellationToken = default)
    {
        Validate(batch);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (batch.Run.Purpose == CalibrationRunPurpose.OfficialPaper)
            {
                await EnsureActiveOfficialStrategyIdentityAsync(
                    connection,
                    transaction,
                    batch.Run.StrategyVersionId!.Value,
                    cancellationToken);
            }
            await InsertRunAsync(connection, transaction, batch.Run, cancellationToken);
            foreach (var candidate in batch.Candidates)
                await InsertCandidateAsync(connection, transaction, candidate, cancellationToken);
            foreach (var lens in batch.LensEvaluations)
                await InsertLensAsync(connection, transaction, lens, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task EnsureActiveOfficialStrategyIdentityAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid strategyVersionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT CASE
    WHEN COUNT(*) = 1
     AND MAX(CASE
            WHEN [VersionId] = @StrategyVersionId
             AND LEN(LTRIM(RTRIM([InitialCodeCommit]))) BETWEEN 7 AND 128
             AND LEN(LTRIM(RTRIM([DecisionRef]))) BETWEEN 1 AND 64
            THEN 1 ELSE 0
         END) = 1
    THEN 1 ELSE 0
END
FROM [dbo].[StrategyVersion]
WHERE [IsActive] = 1;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@StrategyVersionId", SqlDbType.UniqueIdentifier)
            { Value = strategyVersionId });
        int count = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (count != 1)
        {
            throw new InvalidOperationException(
                "Official evidence requires the one active strategy version to have an explicit code and decision identity.");
        }
    }

    public static void Validate(CalibrationEvidenceBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Run.RunId == Guid.Empty) throw new ArgumentException("Calibration run ID is required.");
        if (batch.Run.SymbolsModelEvaluated != batch.Candidates.Count)
            throw new ArgumentException("Model-evaluated count must equal the candidate snapshot count.");
        if (batch.Candidates.Any(x => x.RunId != batch.Run.RunId))
            throw new ArgumentException("Every candidate must belong to the batch run.");
        if (batch.Candidates.Select(x => x.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != batch.Candidates.Count)
            throw new ArgumentException("A run cannot contain duplicate candidate symbols.");
        var candidateIds = batch.Candidates.Select(x => x.CandidateId).ToHashSet();
        if (batch.LensEvaluations.Any(x => !candidateIds.Contains(x.CandidateId)))
            throw new ArgumentException("Every lens evaluation must reference a candidate in the batch.");
        if (batch.LensEvaluations.GroupBy(x => (x.CandidateId, x.Lens.ToUpperInvariant())).Any(g => g.Count() != 1))
            throw new ArgumentException("A candidate can have only one evaluation per lens.");
        if (batch.Run.Purpose == CalibrationRunPurpose.OfficialPaper)
        {
            if (batch.Run.StrategyVersionId is null) throw new ArgumentException("Official runs require a strategy version.");
            if (batch.Run.Code.Commit == "unavailable") throw new ArgumentException("Official runs require a code commit.");
            if (batch.Run.AuditState == CalibrationAuditState.Invalid) throw new ArgumentException("An invalid run cannot be official.");
        }
    }

    private static async Task InsertRunAsync(SqlConnection c, SqlTransaction t, CalibrationRunEvidence r, CancellationToken ct)
    {
        const string sql = """
INSERT INTO [dbo].[CalibrationRun]
([RunId],[RunPurpose],[RecommendationDate],[MarketDataAsOf],[StartedUtc],[StrategyVersionId],
 [StrategyConfigJson],[ModelSnapshotJson],[RunContextJson],[CodeCommit],[CodeVersionSource],[WorkingTreeState],
 [FeatureSchemaVersion],[CandidateSchemaVersion],[LensSchemaVersion],[AuditState],[AuditMessage],
 [SymbolsDiscovered],[SymbolsModelEvaluated],[SkippedHistory],[SkippedStaleHistory],
 [SkippedUnaffordable],[SkippedLowPrice],[SkippedLowVolume],[SkippedLeveragedEtp])
VALUES
(@RunId,@Purpose,@RecommendationDate,@MarketDataAsOf,@StartedUtc,@StrategyVersionId,
 @StrategyConfigJson,@ModelSnapshotJson,@RunContextJson,@CodeCommit,@CodeVersionSource,@WorkingTreeState,
 @FeatureSchemaVersion,@CandidateSchemaVersion,@LensSchemaVersion,@AuditState,@AuditMessage,
 @SymbolsDiscovered,@SymbolsModelEvaluated,@SkippedHistory,@SkippedStaleHistory,
 @SkippedUnaffordable,@SkippedLowPrice,@SkippedLowVolume,@SkippedLeveragedEtp);
""";
        await ExecuteAsync(c, t, sql, ct,
            P("@RunId", SqlDbType.UniqueIdentifier, r.RunId), P("@Purpose", SqlDbType.NVarChar, r.Purpose.ToString(), 32),
            P("@RecommendationDate", SqlDbType.Date, r.RecommendationDate.Date), P("@MarketDataAsOf", SqlDbType.Date, r.MarketDataAsOf.Date),
            P("@StartedUtc", SqlDbType.DateTime2, r.StartedUtc), P("@StrategyVersionId", SqlDbType.UniqueIdentifier, r.StrategyVersionId),
            P("@StrategyConfigJson", SqlDbType.NVarChar, r.StrategyConfigJson, -1), P("@ModelSnapshotJson", SqlDbType.NVarChar, r.ModelSnapshotJson, -1),
            P("@RunContextJson", SqlDbType.NVarChar, r.RunContextJson, -1),
            P("@CodeCommit", SqlDbType.NVarChar, r.Code.Commit, 128), P("@CodeVersionSource", SqlDbType.NVarChar, r.Code.Source, 32),
            P("@WorkingTreeState", SqlDbType.NVarChar, r.Code.WorkingTreeState, 16), P("@FeatureSchemaVersion", SqlDbType.Int, CalibrationSchemaVersions.Feature),
            P("@CandidateSchemaVersion", SqlDbType.Int, CalibrationSchemaVersions.CandidateSnapshot), P("@LensSchemaVersion", SqlDbType.Int, CalibrationSchemaVersions.LensTrace),
            P("@AuditState", SqlDbType.NVarChar, r.AuditState.ToString(), 16), P("@AuditMessage", SqlDbType.NVarChar, r.AuditMessage, 1024),
            P("@SymbolsDiscovered", SqlDbType.Int, r.SymbolsDiscovered), P("@SymbolsModelEvaluated", SqlDbType.Int, r.SymbolsModelEvaluated),
            P("@SkippedHistory", SqlDbType.Int, r.SkippedHistory), P("@SkippedStaleHistory", SqlDbType.Int, r.SkippedStaleHistory),
            P("@SkippedUnaffordable", SqlDbType.Int, r.SkippedUnaffordable), P("@SkippedLowPrice", SqlDbType.Int, r.SkippedLowPrice),
            P("@SkippedLowVolume", SqlDbType.Int, r.SkippedLowVolume), P("@SkippedLeveragedEtp", SqlDbType.Int, r.SkippedLeveragedEtp));
    }

    private static async Task InsertCandidateAsync(SqlConnection c, SqlTransaction t, CalibrationCandidateEvidence x, CancellationToken ct)
    {
        const string sql = """
INSERT INTO [dbo].[CalibrationCandidate]
([CandidateId],[RunId],[Symbol],[ObservationDate],[ObservationOpen],[ObservationHigh],[ObservationLow],[ObservationClose],[ObservationVolume],
 [UpProbability],[DownProbability],[BreakoutProbability],[VolExpansionProbability],[DirectionEdge],[CompositeScore],
 [RsCompositeScore],[RsCompositeScoreZ],[ObvState],[ObvTilt],[SnapshotSchemaVersion],[SnapshotJson])
VALUES
(@CandidateId,@RunId,@Symbol,@ObservationDate,@ObservationOpen,@ObservationHigh,@ObservationLow,@ObservationClose,@ObservationVolume,
 @UpProbability,@DownProbability,@BreakoutProbability,@VolExpansionProbability,@DirectionEdge,@CompositeScore,
 @RsCompositeScore,@RsCompositeScoreZ,@ObvState,@ObvTilt,@SnapshotSchemaVersion,@SnapshotJson);
""";
        await ExecuteAsync(c, t, sql, ct,
            P("@CandidateId", SqlDbType.UniqueIdentifier, x.CandidateId), P("@RunId", SqlDbType.UniqueIdentifier, x.RunId), P("@Symbol", SqlDbType.NVarChar, x.Symbol, 16),
            P("@ObservationDate", SqlDbType.Date, x.ObservationDate.Date), P("@ObservationOpen", SqlDbType.Real, x.ObservationOpen), P("@ObservationHigh", SqlDbType.Real, x.ObservationHigh),
            P("@ObservationLow", SqlDbType.Real, x.ObservationLow), P("@ObservationClose", SqlDbType.Real, x.ObservationClose), P("@ObservationVolume", SqlDbType.BigInt, x.ObservationVolume),
            P("@UpProbability", SqlDbType.Float, x.UpProbability), P("@DownProbability", SqlDbType.Float, x.DownProbability),
            P("@BreakoutProbability", SqlDbType.Float, x.BreakoutProbability), P("@VolExpansionProbability", SqlDbType.Float, x.VolExpansionProbability),
            P("@DirectionEdge", SqlDbType.Float, x.DirectionEdge), P("@CompositeScore", SqlDbType.Float, x.CompositeScore),
            P("@RsCompositeScore", SqlDbType.Float, x.RsCompositeScore), P("@RsCompositeScoreZ", SqlDbType.Float, x.RsCompositeScoreZ),
            P("@ObvState", SqlDbType.NVarChar, x.ObvState, 24), P("@ObvTilt", SqlDbType.Float, x.ObvTilt),
            P("@SnapshotSchemaVersion", SqlDbType.Int, CalibrationSchemaVersions.CandidateSnapshot), P("@SnapshotJson", SqlDbType.NVarChar, x.SnapshotJson, -1));
    }

    private static async Task InsertLensAsync(SqlConnection c, SqlTransaction t, CalibrationLensEvidence x, CancellationToken ct)
    {
        const string sql = """
INSERT INTO [dbo].[CalibrationLensEvaluation]
([LensEvaluationId],[CandidateId],[Lens],[Direction],[IsEligible],[Rank],[RankingKey],[IsPublished],[FirstFailedGate],[TraceSchemaVersion],[GateTraceJson])
VALUES
(@LensEvaluationId,@CandidateId,@Lens,@Direction,@IsEligible,@Rank,@RankingKey,@IsPublished,@FirstFailedGate,@TraceSchemaVersion,@GateTraceJson);
""";
        await ExecuteAsync(c, t, sql, ct,
            P("@LensEvaluationId", SqlDbType.UniqueIdentifier, x.LensEvaluationId), P("@CandidateId", SqlDbType.UniqueIdentifier, x.CandidateId),
            P("@Lens", SqlDbType.NVarChar, x.Lens, 16), P("@Direction", SqlDbType.NVarChar, x.Direction, 8), P("@IsEligible", SqlDbType.Bit, x.IsEligible),
            P("@Rank", SqlDbType.Int, x.Rank), P("@RankingKey", SqlDbType.Float, x.RankingKey), P("@IsPublished", SqlDbType.Bit, x.IsPublished),
            P("@FirstFailedGate", SqlDbType.NVarChar, x.FirstFailedGate, 64), P("@TraceSchemaVersion", SqlDbType.Int, CalibrationSchemaVersions.LensTrace),
            P("@GateTraceJson", SqlDbType.NVarChar, x.GateTraceJson, -1));
    }

    private static async Task ExecuteAsync(SqlConnection c, SqlTransaction t, string sql, CancellationToken ct, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, c, t);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter P(string name, SqlDbType type, object? value, int? size = null)
    {
        var parameter = size.HasValue ? new SqlParameter(name, type, size.Value) : new SqlParameter(name, type);
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }
}
