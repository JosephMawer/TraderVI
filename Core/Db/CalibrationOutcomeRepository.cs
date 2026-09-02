using Core.Calibration;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record PendingCalibrationCandidate(Guid CandidateId, string Symbol, DateTime ObservationDate);
public sealed record PendingTradeableCalibrationCandidate(
    Guid CandidateId,
    string Symbol,
    DateTime ObservationDate,
    DateTime RunStartedUtc);

public sealed class CalibrationOutcomeRepository : SQLBase
{
    public static readonly Guid PredictionLabel10DefinitionId =
        OfficialPredictionScorecardCalculator.PredictionLabels10DefinitionId;
    public static readonly Guid PredictionPath20DefinitionId = new("FA0C8F51-0C48-4E0C-BB26-DFBD82C0D640");
    public static readonly Guid SwingMarkToMarket3DefinitionId = new("491D7C6C-EBBB-4B5E-8259-3E3169D732B6");
    public static readonly Guid SwingExcursion3DefinitionId = new("BBB218C1-616E-46F5-A70B-826E547A7DE3");
    public static readonly Guid DelayedIntradaySwingDefinitionId = new("77134C9C-595A-4BF4-9DB7-2AE67FA48C92");

    public async Task EnsureOutcomeDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
IF NOT EXISTS (SELECT 1 FROM [dbo].[CalibrationOutcomeDefinition] WHERE [OutcomeDefinitionId] = @LabelId)
    INSERT INTO [dbo].[CalibrationOutcomeDefinition]
        ([OutcomeDefinitionId],[DefinitionName],[DefinitionVersion],[DefinitionKind],[DefinitionJson],[IsActive])
    VALUES (@LabelId,N'PredictionLabels10',1,N'Prediction',@LabelJson,1);
IF NOT EXISTS (SELECT 1 FROM [dbo].[CalibrationOutcomeDefinition] WHERE [OutcomeDefinitionId] = @PathId)
    INSERT INTO [dbo].[CalibrationOutcomeDefinition]
        ([OutcomeDefinitionId],[DefinitionName],[DefinitionVersion],[DefinitionKind],[DefinitionJson],[IsActive])
    VALUES (@PathId,N'PredictionPath20',1,N'Prediction',@PathJson,1);
IF NOT EXISTS (SELECT 1 FROM [dbo].[CalibrationOutcomeDefinition] WHERE [OutcomeDefinitionId] = @SwingId)
    INSERT INTO [dbo].[CalibrationOutcomeDefinition]
        ([OutcomeDefinitionId],[DefinitionName],[DefinitionVersion],[DefinitionKind],[DefinitionJson],[IsActive])
    VALUES (@SwingId,N'SwingMarkToMarket3',1,N'Tradeable',@SwingJson,1);
IF NOT EXISTS (SELECT 1 FROM [dbo].[CalibrationOutcomeDefinition] WHERE [OutcomeDefinitionId] = @ExcursionId)
    INSERT INTO [dbo].[CalibrationOutcomeDefinition]
        ([OutcomeDefinitionId],[DefinitionName],[DefinitionVersion],[DefinitionKind],[DefinitionJson],[IsActive])
    VALUES (@ExcursionId,N'SwingExcursion3',1,N'Tradeable',@ExcursionJson,1);
IF NOT EXISTS (SELECT 1 FROM [dbo].[CalibrationOutcomeDefinition] WHERE [OutcomeDefinitionId] = @DelayedId)
    INSERT INTO [dbo].[CalibrationOutcomeDefinition]
        ([OutcomeDefinitionId],[DefinitionName],[DefinitionVersion],[DefinitionKind],[DefinitionJson],[IsActive])
    VALUES (@DelayedId,N'DelayedIntradaySwing',1,N'Tradeable',@DelayedJson,1);
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@LabelId", SqlDbType.UniqueIdentifier) { Value = PredictionLabel10DefinitionId });
        command.Parameters.Add(new SqlParameter("@PathId", SqlDbType.UniqueIdentifier) { Value = PredictionPath20DefinitionId });
        command.Parameters.Add(new SqlParameter("@SwingId", SqlDbType.UniqueIdentifier) { Value = SwingMarkToMarket3DefinitionId });
        command.Parameters.Add(new SqlParameter("@ExcursionId", SqlDbType.UniqueIdentifier) { Value = SwingExcursion3DefinitionId });
        command.Parameters.Add(new SqlParameter("@DelayedId", SqlDbType.UniqueIdentifier) { Value = DelayedIntradaySwingDefinitionId });
        command.Parameters.Add(new SqlParameter("@LabelJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"horizonSessions\":10,\"labelSource\":\"ProfitModelRegistry.ILabeler\",\"benchmark\":\"XIU\"}" });
        command.Parameters.Add(new SqlParameter("@PathJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"horizons\":[1,5,10,20],\"start\":\"observationClose\",\"benchmark\":\"XIU\"}" });
        command.Parameters.Add(new SqlParameter("@SwingJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"measure\":\"markToMarket\",\"horizons\":[1,2,3],\"population\":\"publishedLensCandidates\",\"entry\":\"firstEligibleOpen\",\"entryTimeZone\":\"America/Toronto\",\"marketOpenLocal\":\"09:30:00\",\"entrySessionAllowance\":3,\"slippageRatePerSide\":0.001,\"halfSpreadRatePerSide\":0.0015,\"benchmark\":\"XIU\",\"benchmarkCosts\":false}" });
        command.Parameters.Add(new SqlParameter("@ExcursionJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"measure\":\"excursion\",\"horizons\":[1,2,3],\"population\":\"publishedLensCandidates\",\"entry\":\"firstEligibleOpen\",\"entryTimeZone\":\"America/Toronto\",\"marketOpenLocal\":\"09:30:00\",\"entrySessionAllowance\":3,\"mfe\":\"maxHigh/rawEntry-1\",\"mae\":\"minLow/rawEntry-1\",\"maeSign\":\"nonPositive\",\"timeUnit\":\"sessionOrdinal\",\"ties\":\"earliestSession\",\"sameSessionOrder\":\"unknown\",\"costAdjusted\":false}" });
        command.Parameters.Add(new SqlParameter("@DelayedJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"measure\":\"policyExit\",\"population\":\"publishedLensCandidates\",\"entry\":\"firstEligibleOpen\",\"entrySessionAllowance\":3,\"policyBarMinutes\":15,\"fill\":\"firstFiveMinuteBarOpenAtOrAfterDetection\",\"grossCommissionRate\":0.0,\"executionFrictionRatePerSide\":0.0025,\"benchmark\":\"XIU\",\"benchmarkAlignment\":\"sameFiveMinuteBarStart\"}" });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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

    public async Task<List<PendingCalibrationCandidate>> GetPendingOfficialCandidatesAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT c.[CandidateId], c.[Symbol], c.[ObservationDate]
FROM [dbo].[CalibrationCandidate] c
JOIN [dbo].[CalibrationRun] r ON r.[RunId] = c.[RunId]
LEFT JOIN [dbo].[CalibrationCandidateOutcome] o
  ON o.[CandidateId] = c.[CandidateId] AND o.[OutcomeDefinitionId] = @DefinitionId
WHERE r.[RunPurpose] = N'OfficialPaper'
  AND r.[AuditState] <> N'Invalid'
  AND o.[CandidateOutcomeId] IS NULL
ORDER BY c.[ObservationDate], c.[Symbol];
""";
        var result = new List<PendingCalibrationCandidate>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@DefinitionId", SqlDbType.UniqueIdentifier) { Value = definitionId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new PendingCalibrationCandidate(reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2)));
        return result;
    }

    public async Task<List<PendingTradeableCalibrationCandidate>> GetPendingPublishedCandidatesAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT c.[CandidateId], c.[Symbol], c.[ObservationDate], r.[StartedUtc]
FROM [dbo].[CalibrationCandidate] c
JOIN [dbo].[CalibrationRun] r ON r.[RunId] = c.[RunId]
LEFT JOIN [dbo].[CalibrationCandidateOutcome] o
  ON o.[CandidateId] = c.[CandidateId] AND o.[OutcomeDefinitionId] = @DefinitionId
WHERE r.[RunPurpose] = N'OfficialPaper'
  AND r.[AuditState] <> N'Invalid'
  AND o.[CandidateOutcomeId] IS NULL
  AND EXISTS
  (
      SELECT 1
      FROM [dbo].[CalibrationLensEvaluation] l
      WHERE l.[CandidateId] = c.[CandidateId] AND l.[IsPublished] = 1
  )
ORDER BY c.[ObservationDate], c.[Symbol];
""";
        var result = new List<PendingTradeableCalibrationCandidate>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@DefinitionId", SqlDbType.UniqueIdentifier) { Value = definitionId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new PendingTradeableCalibrationCandidate(
                reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2), reader.GetDateTime(3)));
        return result;
    }

    public async Task<List<CalibrationCoverageCounts>> GetOutcomeCoverageAsync(
        OfficialEvidenceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
WITH [Definitions] AS
(
    SELECT [OutcomeDefinitionId], [DefinitionName], [DefinitionVersion], [DefinitionKind]
    FROM [dbo].[CalibrationOutcomeDefinition]
    WHERE [DefinitionKind] IN (N'Prediction', N'Tradeable') AND [IsActive] = 1
),
[OfficialRuns] AS
(
    SELECT r.[RunId], r.[MarketDataAsOf]
    FROM [dbo].[CalibrationRun] r
    WHERE r.[RunPurpose] = N'OfficialPaper'
      AND r.[AuditState] <> N'Invalid'
      AND r.[StrategyVersionId] = @StrategyVersionId
),
[DefinitionRunCandidates] AS
(
    SELECT d.[OutcomeDefinitionId], d.[DefinitionName], d.[DefinitionVersion], d.[DefinitionKind],
           r.[RunId], r.[MarketDataAsOf], c.[CandidateId],
           o.[CandidateOutcomeId], o.[MaturityState], o.[AuditState]
    FROM [Definitions] d
    CROSS JOIN [OfficialRuns] r
    LEFT JOIN [dbo].[CalibrationCandidate] c
      ON c.[RunId] = r.[RunId]
     AND
     (
         d.[DefinitionKind] = N'Prediction'
         OR
         (
             d.[DefinitionKind] = N'Tradeable'
             AND EXISTS
             (
                 SELECT 1
                 FROM [dbo].[CalibrationLensEvaluation] l
                 WHERE l.[CandidateId] = c.[CandidateId] AND l.[IsPublished] = 1
             )
         )
     )
    LEFT JOIN [dbo].[CalibrationCandidateOutcome] o
      ON o.[CandidateId] = c.[CandidateId]
     AND o.[OutcomeDefinitionId] = d.[OutcomeDefinitionId]
),
[CandidateSummary] AS
(
    SELECT [OutcomeDefinitionId],
           COUNT(DISTINCT [RunId]) AS [OfficialRuns],
           COUNT(DISTINCT [MarketDataAsOf]) AS [TotalCohorts],
           COUNT([CandidateId]) AS [ExpectedCandidates],
           SUM(CASE WHEN [MaturityState] <> N'Pending' AND [AuditState] = N'Valid' THEN 1 ELSE 0 END) AS [ValidOutcomes],
           SUM(CASE WHEN [MaturityState] <> N'Pending' AND [AuditState] = N'Degraded' THEN 1 ELSE 0 END) AS [DegradedOutcomes],
           SUM(CASE WHEN [MaturityState] <> N'Pending' AND [AuditState] = N'Invalid' THEN 1 ELSE 0 END) AS [InvalidOutcomes],
           SUM(CASE WHEN [CandidateId] IS NOT NULL AND ([CandidateOutcomeId] IS NULL OR [MaturityState] = N'Pending') THEN 1 ELSE 0 END) AS [PendingOutcomes]
    FROM [DefinitionRunCandidates]
    GROUP BY [OutcomeDefinitionId]
),
[CohortSummary] AS
(
    SELECT [OutcomeDefinitionId], [MarketDataAsOf],
           COUNT([CandidateId]) AS [ExpectedCandidates],
           SUM(CASE WHEN [CandidateOutcomeId] IS NOT NULL AND [MaturityState] <> N'Pending' THEN 1 ELSE 0 END) AS [CompletedCandidates]
    FROM [DefinitionRunCandidates]
    GROUP BY [OutcomeDefinitionId], [MarketDataAsOf]
),
[MaturitySummary] AS
(
    SELECT [OutcomeDefinitionId],
           SUM(CASE WHEN [ExpectedCandidates] = [CompletedCandidates] THEN 1 ELSE 0 END) AS [MaturedCohorts]
    FROM [CohortSummary]
    GROUP BY [OutcomeDefinitionId]
)
SELECT d.[OutcomeDefinitionId], d.[DefinitionName], d.[DefinitionVersion], d.[DefinitionKind],
       COALESCE(c.[OfficialRuns], 0) AS [OfficialRuns],
       COALESCE(c.[TotalCohorts], 0) AS [TotalCohorts],
       COALESCE(m.[MaturedCohorts], 0) AS [MaturedCohorts],
       COALESCE(c.[ExpectedCandidates], 0) AS [ExpectedCandidates],
       COALESCE(c.[ValidOutcomes], 0) AS [ValidOutcomes],
       COALESCE(c.[DegradedOutcomes], 0) AS [DegradedOutcomes],
       COALESCE(c.[InvalidOutcomes], 0) AS [InvalidOutcomes],
       COALESCE(c.[PendingOutcomes], 0) AS [PendingOutcomes]
FROM [Definitions] d
LEFT JOIN [CandidateSummary] c ON c.[OutcomeDefinitionId] = d.[OutcomeDefinitionId]
LEFT JOIN [MaturitySummary] m ON m.[OutcomeDefinitionId] = d.[OutcomeDefinitionId]
ORDER BY d.[DefinitionName], d.[DefinitionVersion];
""";
        var result = new List<CalibrationCoverageCounts>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@StrategyVersionId", SqlDbType.UniqueIdentifier)
            { Value = identity.StrategyVersionId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CalibrationCoverageCounts(
                reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9),
                reader.GetInt32(10), reader.GetInt32(11)));
        }

        return result;
    }

    public async Task<LensTradeabilityEvidenceSet> GetLensTradeabilityEvidenceAsync(
        OfficialEvidenceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT r.[RunId], r.[MarketDataAsOf]
FROM [dbo].[CalibrationRun] r
WHERE r.[RunPurpose] = N'OfficialPaper'
  AND r.[AuditState] <> N'Invalid'
  AND r.[StrategyVersionId] = @StrategyVersionId
ORDER BY r.[MarketDataAsOf], r.[StartedUtc], r.[RunId];

SELECT r.[RunId], r.[MarketDataAsOf], l.[Lens], l.[Rank], c.[CandidateId], c.[Symbol],
       m.[MaturityState], m.[AuditState], m.[OutcomeJson],
       e.[MaturityState], e.[AuditState], e.[OutcomeJson]
FROM [dbo].[CalibrationRun] r
JOIN [dbo].[CalibrationCandidate] c ON c.[RunId] = r.[RunId]
JOIN [dbo].[CalibrationLensEvaluation] l
  ON l.[CandidateId] = c.[CandidateId] AND l.[IsPublished] = 1
LEFT JOIN [dbo].[CalibrationCandidateOutcome] m
  ON m.[CandidateId] = c.[CandidateId] AND m.[OutcomeDefinitionId] = @MarkDefinitionId
LEFT JOIN [dbo].[CalibrationCandidateOutcome] e
  ON e.[CandidateId] = c.[CandidateId] AND e.[OutcomeDefinitionId] = @ExcursionDefinitionId
WHERE r.[RunPurpose] = N'OfficialPaper'
  AND r.[AuditState] <> N'Invalid'
  AND r.[StrategyVersionId] = @StrategyVersionId
ORDER BY r.[MarketDataAsOf], r.[StartedUtc], l.[Lens], l.[Rank], c.[Symbol];
""";
        var runs = new List<LensTradeabilityRunEvidence>();
        var recommendations = new List<LensTradeabilityEvidenceRow>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@MarkDefinitionId", SqlDbType.UniqueIdentifier)
            { Value = SwingMarkToMarket3DefinitionId });
        command.Parameters.Add(new SqlParameter("@ExcursionDefinitionId", SqlDbType.UniqueIdentifier)
            { Value = SwingExcursion3DefinitionId });
        command.Parameters.Add(new SqlParameter("@StrategyVersionId", SqlDbType.UniqueIdentifier)
            { Value = identity.StrategyVersionId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            runs.Add(new LensTradeabilityRunEvidence(reader.GetGuid(0), reader.GetDateTime(1)));

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            recommendations.Add(new LensTradeabilityEvidenceRow(
                reader.GetGuid(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return new LensTradeabilityEvidenceSet(runs, recommendations);
    }

    public async Task<IReadOnlyList<DelayedIntradayLensEvidenceRow>> GetDelayedIntradayLensEvidenceAsync(
        OfficialEvidenceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT r.[RunId],r.[MarketDataAsOf],l.[Lens],c.[CandidateId],
       o.[MaturityState],o.[AuditState],o.[OutcomeJson]
FROM [dbo].[CalibrationRun] r
JOIN [dbo].[CalibrationCandidate] c ON c.[RunId] = r.[RunId]
JOIN [dbo].[CalibrationLensEvaluation] l
  ON l.[CandidateId] = c.[CandidateId] AND l.[IsPublished] = 1
LEFT JOIN [dbo].[CalibrationCandidateOutcome] o
  ON o.[CandidateId] = c.[CandidateId] AND o.[OutcomeDefinitionId] = @DefinitionId
WHERE r.[RunPurpose] = N'OfficialPaper'
  AND r.[AuditState] <> N'Invalid'
  AND r.[StrategyVersionId] = @StrategyVersionId
ORDER BY r.[MarketDataAsOf],r.[StartedUtc],l.[Lens],l.[Rank],c.[Symbol];
""";
        var result = new List<DelayedIntradayLensEvidenceRow>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@DefinitionId", SqlDbType.UniqueIdentifier)
            { Value = DelayedIntradaySwingDefinitionId });
        command.Parameters.Add(new SqlParameter("@StrategyVersionId", SqlDbType.UniqueIdentifier)
            { Value = identity.StrategyVersionId });
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DelayedIntradayLensEvidenceRow(
                reader.GetGuid(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return result.AsReadOnly();
    }

    public async Task<OfficialPredictionEvidenceSet> GetOfficialPredictionScorecardEvidenceAsync(
        OfficialEvidenceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT [OutcomeDefinitionId],[DefinitionName],[DefinitionVersion]
FROM [dbo].[CalibrationOutcomeDefinition]
WHERE [OutcomeDefinitionId] = @DefinitionId;

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
            { Value = PredictionLabel10DefinitionId });
        command.Parameters.Add(new SqlParameter("@StrategyVersionId", SqlDbType.UniqueIdentifier)
            { Value = identity.StrategyVersionId });
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("PredictionLabels10 outcome definition is missing.");
        var definition = new OfficialPredictionScorecardDefinition(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2));

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

        return new OfficialPredictionEvidenceSet(identity, definition, runs, candidates, lenses);
    }

    public async Task<bool> InsertOutcomeAsync(
        Guid candidateId,
        Guid definitionId,
        CalibrationOutcomeMaturityState maturityState,
        string outcomeJson,
        CalibrationAuditState auditState,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
IF NOT EXISTS
(
    SELECT 1 FROM [dbo].[CalibrationCandidateOutcome]
    WHERE [CandidateId] = @CandidateId AND [OutcomeDefinitionId] = @DefinitionId
)
INSERT INTO [dbo].[CalibrationCandidateOutcome]
    ([CandidateOutcomeId],[CandidateId],[OutcomeDefinitionId],[MaturityState],[AuditState],[OutcomeJson])
VALUES
    (@OutcomeId,@CandidateId,@DefinitionId,@MaturityState,@AuditState,@OutcomeJson);
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@OutcomeId", SqlDbType.UniqueIdentifier) { Value = Guid.NewGuid() });
        command.Parameters.Add(new SqlParameter("@CandidateId", SqlDbType.UniqueIdentifier) { Value = candidateId });
        command.Parameters.Add(new SqlParameter("@DefinitionId", SqlDbType.UniqueIdentifier) { Value = definitionId });
        command.Parameters.Add(new SqlParameter("@MaturityState", SqlDbType.NVarChar, 16) { Value = maturityState.ToString() });
        command.Parameters.Add(new SqlParameter("@AuditState", SqlDbType.NVarChar, 16) { Value = auditState.ToString() });
        command.Parameters.Add(new SqlParameter("@OutcomeJson", SqlDbType.NVarChar, -1) { Value = outcomeJson });
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}
