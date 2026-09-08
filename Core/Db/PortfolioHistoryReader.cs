#nullable enable
using Core.Trader;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>SELECT-only saved daily Shadow closes. Does not run a monitor or reconstruct prices.</summary>
public sealed class PortfolioHistoryReader : SQLBase
{
    public async Task<IReadOnlyDictionary<Guid, List<PortfolioClosingObservation>>> ReadShadowAsync(
        Guid generationId, CancellationToken token = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT s.PortfolioId,s.TradingDate,s.ClosingValue
            FROM dbo.ShadowPortfolioSession s
            JOIN dbo.ShadowPortfolio p ON p.PortfolioId=s.PortfolioId
            WHERE p.GenerationId=@GenerationId AND s.Status=N'Completed'
            ORDER BY s.TradingDate,s.PortfolioId;
            """, connection);
        command.Parameters.Add("@GenerationId", SqlDbType.UniqueIdentifier).Value = generationId;
        var result = new Dictionary<Guid, List<PortfolioClosingObservation>>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            Guid id = reader.GetGuid(0);
            if (!result.TryGetValue(id, out var points)) result[id] = points = [];
            points.Add(new(DateOnly.FromDateTime(reader.GetDateTime(1)), reader.IsDBNull(2) ? null : reader.GetDecimal(2)));
        }
        return result;
    }
}
