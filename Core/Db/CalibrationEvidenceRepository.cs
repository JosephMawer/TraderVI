using Core.Calibration;
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
    public async Task AppendAsync(CalibrationEvidenceBatch batch, CancellationToken cancellationToken = default)
    {
        Validate(batch);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
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
