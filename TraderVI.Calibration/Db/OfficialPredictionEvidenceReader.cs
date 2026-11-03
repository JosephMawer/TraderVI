using Core.Calibration;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Shared read-only evidence queries for Athena and desktop scorecards.</summary>
public class OfficialPredictionEvidenceReader : SQLBase
{
    public async Task<OfficialEvidenceIdentity> GetActiveOfficialEvidenceIdentityAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT v.[VersionId],v.[VersionName],v.[InitialCodeCommit],v.[DecisionRef],
       COUNT(CASE WHEN r.[StrategyVersionId] = v.[VersionId] THEN 1 END) AS [IncludedOfficialRuns],
       COUNT(CASE WHEN r.[RunId] IS NOT NULL AND
                           (r.[StrategyVersionId] IS NULL OR r.[StrategyVersionId] <> v.[VersionId])
                  THEN 1 END) AS [ExcludedOfficialRuns]
FROM [dbo].[StrategyVersion] v
LEFT JOIN [dbo].[CalibrationRun] r
  ON r.[RunPurpose] = N'OfficialPaper' AND r.[AuditState] <> N'Invalid'
WHERE v.[IsActive] = 1
GROUP BY v.[VersionId],v.[VersionName],v.[InitialCodeCommit],v.[DecisionRef];
""";

        var result = new List<OfficialEvidenceIdentity>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OfficialEvidenceIdentity(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        if (result.Count != 1)
            throw new InvalidOperationException(
                "Official reports require exactly one active strategy version.");

        OfficialEvidenceIdentity identity = result[0];
        if (string.IsNullOrWhiteSpace(identity.InitialCodeCommit) ||
            string.IsNullOrWhiteSpace(identity.DecisionRef))
        {
            throw new InvalidOperationException(
                "The active strategy lacks InitialCodeCommit or DecisionRef and cannot scope official reports.");
        }

        return identity;
    }

    public async Task<OfficialPredictionEvidenceSet> GetOfficialPredictionScorecardEvidenceAsync(
        OfficialEvidenceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT [OutcomeDefinitionId],[DefinitionName],[DefinitionVersion]
FROM [dbo].[CalibrationOutcomeDefinition]
WHERE [OutcomeDefinitionId] = @DefinitionId;

SELECT TOP (@LabelHorizon) [Date]
FROM [dbo].[DailyBars]
WHERE [Symbol] = 'XIU'
ORDER BY [Date] DESC;

SELECT r.[RunId],r.[StrategyVersionId],r.[MarketDataAsOf],r.[RunPurpose],r.[AuditState],r.[RunContextJson]
FROM [dbo].[CalibrationRun] r
WHERE r.[RunPurpose] = N'OfficialPaper'
  AND r.[AuditState] <> N'Invalid'
  AND r.[StrategyVersionId] = @StrategyVersionId
ORDER BY r.[MarketDataAsOf],r.[StartedUtc],r.[RunId];

SELECT r.[RunId],r.[MarketDataAsOf],c.[CandidateId],c.[Symbol],c.[ObservationDate],
       c.[ObservationOpen],c.[ObservationHigh],c.[ObservationLow],c.[ObservationClose],c.[ObservationVolume],
       c.[UpProbability],c.[DownProbability],c.[BreakoutProbability],c.[VolExpansionProbability],
       c.[RsCompositeScore],c.[RsCompositeScoreZ],c.[ObvState],c.[SnapshotJson],
       o.[MaturityState],o.[AuditState],o.[OutcomeJson]
FROM [dbo].[CalibrationRun] r
JOIN [dbo].[CalibrationCandidate] c ON c.[RunId] = r.[RunId]
LEFT JOIN [dbo].[CalibrationCandidateOutcome] o
  ON o.[CandidateId] = c.[CandidateId]
 AND o.[OutcomeDefinitionId] = @DefinitionId
WHERE r.[RunPurpose] = N'OfficialPaper'
  AND r.[AuditState] <> N'Invalid'
  AND r.[StrategyVersionId] = @StrategyVersionId
ORDER BY r.[MarketDataAsOf],r.[StartedUtc],c.[Symbol];

SELECT l.[CandidateId],l.[Lens],l.[IsEligible],l.[IsPublished],l.[Rank],l.[FirstFailedGate]
FROM [dbo].[CalibrationRun] r
JOIN [dbo].[CalibrationCandidate] c ON c.[RunId] = r.[RunId]
JOIN [dbo].[CalibrationLensEvaluation] l ON l.[CandidateId] = c.[CandidateId]
WHERE r.[RunPurpose] = N'OfficialPaper'
  AND r.[AuditState] <> N'Invalid'
  AND r.[StrategyVersionId] = @StrategyVersionId
ORDER BY r.[MarketDataAsOf],r.[StartedUtc],l.[Lens],l.[Rank],c.[Symbol];
""";

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@DefinitionId", SqlDbType.UniqueIdentifier)
            { Value = OfficialPredictionScorecardCalculator.PredictionLabels10DefinitionId });
        command.Parameters.Add(new SqlParameter("@LabelHorizon", SqlDbType.Int)
            { Value = PredictionOutcomeContract.LabelHorizon });
        command.Parameters.Add(new SqlParameter("@StrategyVersionId", SqlDbType.UniqueIdentifier)
            { Value = identity.StrategyVersionId });
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("PredictionLabels10 outcome definition is missing.");
        var definition = new OfficialPredictionScorecardDefinition(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2));

        var benchmarkSessions = new List<DateTime>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            benchmarkSessions.Add(reader.GetDateTime(0));

        var runs = new List<OfficialPredictionRunEvidence>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new OfficialPredictionRunEvidence(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetDateTime(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        var candidates = new List<OfficialPredictionCandidateEvidence>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new OfficialPredictionCandidateEvidence(
                reader.GetGuid(0),
                reader.GetDateTime(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetDateTime(4),
                reader.GetFloat(5),
                reader.GetFloat(6),
                reader.GetFloat(7),
                reader.GetFloat(8),
                reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.IsDBNull(12) ? null : reader.GetDouble(12),
                reader.IsDBNull(13) ? null : reader.GetDouble(13),
                reader.IsDBNull(14) ? null : reader.GetDouble(14),
                reader.IsDBNull(15) ? null : reader.GetDouble(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20)));
        }

        var lenses = new List<OfficialPredictionLensEvidence>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lenses.Add(new OfficialPredictionLensEvidence(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return new OfficialPredictionEvidenceSet(identity, definition, runs, candidates, lenses, benchmarkSessions);
    }
}
