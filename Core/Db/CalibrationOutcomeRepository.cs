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
    public static readonly Guid PredictionLabel10DefinitionId = new("A72C01CB-9C83-45A6-9A72-CC49E67B9F5A");
    public static readonly Guid PredictionPath20DefinitionId = new("FA0C8F51-0C48-4E0C-BB26-DFBD82C0D640");
    public static readonly Guid SwingMarkToMarket3DefinitionId = new("491D7C6C-EBBB-4B5E-8259-3E3169D732B6");
    public static readonly Guid SwingExcursion3DefinitionId = new("BBB218C1-616E-46F5-A70B-826E547A7DE3");

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
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@LabelId", SqlDbType.UniqueIdentifier) { Value = PredictionLabel10DefinitionId });
        command.Parameters.Add(new SqlParameter("@PathId", SqlDbType.UniqueIdentifier) { Value = PredictionPath20DefinitionId });
        command.Parameters.Add(new SqlParameter("@SwingId", SqlDbType.UniqueIdentifier) { Value = SwingMarkToMarket3DefinitionId });
        command.Parameters.Add(new SqlParameter("@ExcursionId", SqlDbType.UniqueIdentifier) { Value = SwingExcursion3DefinitionId });
        command.Parameters.Add(new SqlParameter("@LabelJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"horizonSessions\":10,\"labelSource\":\"ProfitModelRegistry.ILabeler\",\"benchmark\":\"XIU\"}" });
        command.Parameters.Add(new SqlParameter("@PathJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"horizons\":[1,5,10,20],\"start\":\"observationClose\",\"benchmark\":\"XIU\"}" });
        command.Parameters.Add(new SqlParameter("@SwingJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"measure\":\"markToMarket\",\"horizons\":[1,2,3],\"population\":\"publishedLensCandidates\",\"entry\":\"firstEligibleOpen\",\"entryTimeZone\":\"America/Toronto\",\"marketOpenLocal\":\"09:30:00\",\"entrySessionAllowance\":3,\"slippageRatePerSide\":0.001,\"halfSpreadRatePerSide\":0.0015,\"benchmark\":\"XIU\",\"benchmarkCosts\":false}" });
        command.Parameters.Add(new SqlParameter("@ExcursionJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"measure\":\"excursion\",\"horizons\":[1,2,3],\"population\":\"publishedLensCandidates\",\"entry\":\"firstEligibleOpen\",\"entryTimeZone\":\"America/Toronto\",\"marketOpenLocal\":\"09:30:00\",\"entrySessionAllowance\":3,\"mfe\":\"maxHigh/rawEntry-1\",\"mae\":\"minLow/rawEntry-1\",\"maeSign\":\"nonPositive\",\"timeUnit\":\"sessionOrdinal\",\"ties\":\"earliestSession\",\"sameSessionOrder\":\"unknown\",\"costAdjusted\":false}" });
        await command.ExecuteNonQueryAsync(cancellationToken);
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
    WHERE r.[RunPurpose] = N'OfficialPaper' AND r.[AuditState] <> N'Invalid'
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
