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
    /// <summary>Saved session baselines/closes, plus available same-day marks for the latest unfinished session.</summary>
    public async Task<IReadOnlyDictionary<Guid, List<PortfolioDailyObservation>>> ReadShadowDaysAsync(
        Guid generationId, CancellationToken token = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT s.PortfolioId,s.TradingDate,s.OpeningValue,s.ClosingValue,s.CompletedUtc,s.Status,
                   CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.ShadowPortfolioSession later
                                         WHERE later.PortfolioId=p.PortfolioId AND later.TradingDate>s.TradingDate)
                         AND CAST(p.UpdatedUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS date)<=s.TradingDate
                         AND COALESCE(h.MissingMarks,0)=0
                         AND (h.OpenPositions=0 OR (h.OldestMarkDate=s.TradingDate AND h.NewestMarkDate=s.TradingDate))
                        THEN p.CashBalance+COALESCE(h.MarketValue,0) END AS CurrentValue,
                   COALESCE(h.OldestMarkUtc,CASE WHEN p.UpdatedUtc>s.UpdatedUtc THEN p.UpdatedUtc ELSE s.UpdatedUtc END) AS MarkUtc
            FROM dbo.ShadowPortfolioSession s
            JOIN dbo.ShadowPortfolio p ON p.PortfolioId=s.PortfolioId
            OUTER APPLY (
                SELECT SUM(x.Shares*x.LastPrice) AS MarketValue,MIN(x.LastPriceEventUtc) AS OldestMarkUtc,COUNT(*) AS OpenPositions,
                       MIN(CAST(x.LastPriceEventUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS date)) AS OldestMarkDate,
                       MAX(CAST(x.LastPriceEventUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS date)) AS NewestMarkDate,
                       SUM(CASE WHEN x.LastPrice IS NULL OR x.LastPriceEventUtc IS NULL
                           THEN 1 ELSE 0 END) AS MissingMarks
                FROM dbo.ShadowPosition x WHERE x.PortfolioId=p.PortfolioId AND x.Status=N'Open'
            ) h
            WHERE p.GenerationId=@GenerationId
            ORDER BY s.TradingDate,s.PortfolioId;
            """, connection);
        command.Parameters.Add("@GenerationId", SqlDbType.UniqueIdentifier).Value = generationId;
        var result = new Dictionary<Guid, List<PortfolioDailyObservation>>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            Guid id = reader.GetGuid(0);
            if (!result.TryGetValue(id, out var days)) result[id] = days = [];
            bool closing = reader.GetString(5) == "Completed";
            int valueColumn = closing ? 3 : 6, timeColumn = closing ? 4 : 7;
            decimal? value = reader.IsDBNull(valueColumn) ? null : reader.GetDecimal(valueColumn);
            days.Add(new(DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetDecimal(2), value,
                reader.IsDBNull(timeColumn) ? null : DateTime.SpecifyKind(reader.GetDateTime(timeColumn), DateTimeKind.Utc),
                closing, closing ? "Saved close" : value is null ? "Same-day holding valuations unavailable"
                    : "Partial day · saved holdings/cash; holding prices may have different times"));
        }
        return result;
    }

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
