#nullable enable

using Core.TMX.Models.Domain;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record StoredIntradayOutcomeBar(
    DateTime EventUtc,
    DateTime FirstReceivedUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    string AuditState,
    int EvidenceSchemaVersion,
    string SourceContractVersion);

public sealed class IntradayOutcomeReader : SQLBase
{
    public async Task<IReadOnlyList<StoredIntradayOutcomeBar>> GetOutcomeBarsAsync(
        string symbol,
        int intervalMinutes,
        DateTime fromUtc,
        DateTime throughUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        if (intervalMinutes is not (5 or 15))
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes));
        if (fromUtc.Kind != DateTimeKind.Utc || throughUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Outcome evidence bounds must be UTC.");
        if (throughUtc < fromUtc)
            throw new ArgumentOutOfRangeException(nameof(throughUtc));

        const string sql = """
SELECT b.[EventUtc],o.[ReceivedUtc],b.[Open],b.[High],b.[Low],b.[Close],b.[Volume],
       o.[AuditState],o.[EvidenceSchemaVersion],o.[SourceContractVersion]
FROM [dbo].[IntradayEvidenceBar] b
JOIN [dbo].[IntradayPollObservation] o
  ON o.[ObservationId] = b.[FirstObservationId]
WHERE b.[Symbol] = @Symbol
  AND b.[IntervalMinutes] = @IntervalMinutes
  AND b.[EventUtc] >= @FromUtc
  AND b.[EventUtc] <= @ThroughUtc
  AND o.[ReceivedUtc] IS NOT NULL
  AND o.[AuditState] <> N'Invalid'
  AND o.[EvidenceSchemaVersion] = @EvidenceSchemaVersion
  AND o.[SourceContractVersion] = @SourceContractVersion
  AND
  (
      o.[PolicyVersion] = @PolicyVersion
      OR
      (
          o.[PolicyVersion] IS NULL
          AND o.[CollectorVersion] = N'IntradayEvidenceCollectorV3'
          AND b.[IntervalMinutes] = 5
          AND o.[Purpose] = N'PaperMonitor'
          AND o.[AuditState] IN (N'Valid', N'Degraded')
      )
  )
ORDER BY b.[EventUtc];
""";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(P("@Symbol", SqlDbType.NVarChar, symbol.Trim().ToUpperInvariant(), 20));
        command.Parameters.Add(P("@IntervalMinutes", SqlDbType.SmallInt, intervalMinutes));
        command.Parameters.Add(P("@FromUtc", SqlDbType.DateTime2, fromUtc));
        command.Parameters.Add(P("@ThroughUtc", SqlDbType.DateTime2, throughUtc));
        command.Parameters.Add(P("@EvidenceSchemaVersion", SqlDbType.Int, IntradayEvidenceVersions.Schema));
        command.Parameters.Add(P("@SourceContractVersion", SqlDbType.NVarChar, IntradayEvidenceVersions.SourceContract, 64));
        command.Parameters.Add(P("@PolicyVersion", SqlDbType.NVarChar, IntradayEvidenceVersions.Policy, 64));
        var result = new List<StoredIntradayOutcomeBar>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StoredIntradayOutcomeBar(
                DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetInt64(6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetString(9)));
        }

        return result.AsReadOnly();
    }

    private static SqlParameter P(
        string name,
        SqlDbType type,
        object? value,
        int? size = null)
    {
        var parameter = size.HasValue
            ? new SqlParameter(name, type, size.Value)
            : new SqlParameter(name, type);
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }
}
