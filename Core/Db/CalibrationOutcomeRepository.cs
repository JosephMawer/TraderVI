using Core.Calibration;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record PendingCalibrationCandidate(Guid CandidateId, string Symbol, DateTime ObservationDate);

public sealed class CalibrationOutcomeRepository : SQLBase
{
    public static readonly Guid PredictionLabel10DefinitionId = new("A72C01CB-9C83-45A6-9A72-CC49E67B9F5A");
    public static readonly Guid PredictionPath20DefinitionId = new("FA0C8F51-0C48-4E0C-BB26-DFBD82C0D640");

    public async Task EnsurePredictionDefinitionsAsync(CancellationToken cancellationToken = default)
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
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@LabelId", SqlDbType.UniqueIdentifier) { Value = PredictionLabel10DefinitionId });
        command.Parameters.Add(new SqlParameter("@PathId", SqlDbType.UniqueIdentifier) { Value = PredictionPath20DefinitionId });
        command.Parameters.Add(new SqlParameter("@LabelJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"horizonSessions\":10,\"labelSource\":\"ProfitModelRegistry.ILabeler\",\"benchmark\":\"XIU\"}" });
        command.Parameters.Add(new SqlParameter("@PathJson", SqlDbType.NVarChar, -1) { Value = "{\"schemaVersion\":1,\"horizons\":[1,5,10,20],\"start\":\"observationClose\",\"benchmark\":\"XIU\"}" });
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

    public async Task InsertMaturedOutcomeAsync(
        Guid candidateId,
        Guid definitionId,
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
    (@OutcomeId,@CandidateId,@DefinitionId,N'Matured',@AuditState,@OutcomeJson);
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@OutcomeId", SqlDbType.UniqueIdentifier) { Value = Guid.NewGuid() });
        command.Parameters.Add(new SqlParameter("@CandidateId", SqlDbType.UniqueIdentifier) { Value = candidateId });
        command.Parameters.Add(new SqlParameter("@DefinitionId", SqlDbType.UniqueIdentifier) { Value = definitionId });
        command.Parameters.Add(new SqlParameter("@AuditState", SqlDbType.NVarChar, 16) { Value = auditState.ToString() });
        command.Parameters.Add(new SqlParameter("@OutcomeJson", SqlDbType.NVarChar, -1) { Value = outcomeJson });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
