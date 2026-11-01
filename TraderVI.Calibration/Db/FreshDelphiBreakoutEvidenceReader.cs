#nullable enable
using Core.Trader;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class FreshDelphiBreakoutEvidenceReader : SQLBase
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
}
