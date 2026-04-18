using Core.TMX.Models.Domain;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Stores and retrieves daily TSX sector index snapshots.
/// 
/// Table schema (create if not exists):
/// <code>
/// CREATE TABLE [dbo].[SectorIndices] (
///     [Date]          DATE            NOT NULL,
///     [Symbol]        NVARCHAR(10)    NOT NULL,
///     [SectorName]    NVARCHAR(50)    NOT NULL,
///     [Price]         DECIMAL(18,4)   NOT NULL,
///     [PriceChange]   DECIMAL(18,4)   NOT NULL,
///     [PercentChange] DECIMAL(18,4)   NOT NULL,
///     CONSTRAINT PK_SectorIndices PRIMARY KEY ([Date], [Symbol])
/// );
/// </code>
/// </summary>
public sealed class SectorIndexRepository : SQLBase
{
    /// <summary>
    /// Upserts a batch of sector index snapshots for a single day.
    /// </summary>
    public async Task UpsertAsync(IReadOnlyList<SectorIndexSnapshot> snapshots)
    {
        if (snapshots.Count == 0) return;

        const string sql = """
            MERGE [dbo].[SectorIndices] AS target
            USING (VALUES (@Date, @Symbol, @SectorName, @Price, @PriceChange, @PercentChange))
                AS source ([Date], [Symbol], [SectorName], [Price], [PriceChange], [PercentChange])
            ON target.[Date] = source.[Date] AND target.[Symbol] = source.[Symbol]
            WHEN MATCHED THEN
                UPDATE SET
                    SectorName    = source.SectorName,
                    Price         = source.Price,
                    PriceChange   = source.PriceChange,
                    PercentChange = source.PercentChange
            WHEN NOT MATCHED THEN
                INSERT ([Date], [Symbol], [SectorName], [Price], [PriceChange], [PercentChange])
                VALUES (source.[Date], source.[Symbol], source.[SectorName], source.[Price], source.[PriceChange], source.[PercentChange]);
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        foreach (var s in snapshots)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Date", SqlDbType.Date) { Value = s.Date });
            cmd.Parameters.Add(new SqlParameter("@Symbol", SqlDbType.NVarChar, 10) { Value = s.Symbol });
            cmd.Parameters.Add(new SqlParameter("@SectorName", SqlDbType.NVarChar, 50) { Value = s.SectorName });
            cmd.Parameters.Add(new SqlParameter("@Price", SqlDbType.Decimal) { Value = s.Price });
            cmd.Parameters.Add(new SqlParameter("@PriceChange", SqlDbType.Decimal) { Value = s.PriceChange });
            cmd.Parameters.Add(new SqlParameter("@PercentChange", SqlDbType.Decimal) { Value = s.PercentChange });
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Gets the most recent N trading days of sector index data for the specified symbols.
    /// Results are ascending by date.
    /// </summary>
    public async Task<IReadOnlyList<SectorIndexSnapshot>> GetRecentAsync(
        IReadOnlyList<string> symbols,
        int days = 10)
    {
        if (symbols.Count == 0) return [];

        // Build parameterized IN clause
        var paramNames = symbols.Select((_, i) => $"@s{i}").ToList();
        var inClause = string.Join(", ", paramNames);

        var sql = $"""
            SELECT [Date], [Symbol], [SectorName], [Price], [PriceChange], [PercentChange]
            FROM [dbo].[SectorIndices]
            WHERE [Symbol] IN ({inClause})
              AND [Date] >= (
                  SELECT DATEADD(DAY, -@LookbackDays, MAX([Date]))
                  FROM [dbo].[SectorIndices]
              )
            ORDER BY [Date] ASC, [Symbol] ASC
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        // Extra calendar days to cover weekends/holidays for N trading days
        cmd.Parameters.Add(new SqlParameter("@LookbackDays", SqlDbType.Int) { Value = days * 2 });
        for (int i = 0; i < symbols.Count; i++)
            cmd.Parameters.Add(new SqlParameter(paramNames[i], SqlDbType.NVarChar, 10) { Value = symbols[i] });

        var results = new List<SectorIndexSnapshot>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new SectorIndexSnapshot(
                Symbol: reader.GetString(1),
                SectorName: reader.GetString(2),
                Price: reader.GetDecimal(3),
                PriceChange: reader.GetDecimal(4),
                PercentChange: reader.GetDecimal(5),
                Date: reader.GetDateTime(0)));
        }

        return results;
    }

    /// <summary>
    /// Gets the latest date stored in the sector indices table, or null if empty.
    /// </summary>
    public async Task<DateTime?> GetLatestDateAsync()
    {
        const string sql = "SELECT MAX([Date]) FROM [dbo].[SectorIndices]";

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result is DateTime dt ? dt : null;
    }
}