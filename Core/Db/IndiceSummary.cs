using Core.TMX.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Represents information about a single index for a given day.
/// </summary>
public struct IndiceValue
{
    public DateTime Date { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public decimal Change { get; set; }
    public decimal PercentChange { get; set; }
}

public enum Indices
{
    TSX,
    TSX_Venture,
    VIXC,
    Energy,
    Financials,
    Health_Care,
    Industrials,
    Info_Tech,
    Metals_Mining,
    Telecom,
    Utilities,
    Materials,      // Added for sector index parity with TMX ^TTMT
    Technology,     // Added for sector index parity with TMX ^TTTK
}

/// <summary>
/// Data access for the [dbo].[IndiceSummary] table.
/// Modernized: parameterized queries, proper async, no string interpolation in SQL.
/// </summary>
public class IndiceSummary : SQLBase
{
    #region Inserts

    /// <summary>
    /// Upserts a single index summary entry.
    /// </summary>
    public async Task InsertIndiceSummaryAsync(MarketIndices index)
    {
        const string sql = """
            MERGE [dbo].[IndiceSummary] AS target
            USING (VALUES (@Date, @Name, @Last, @Change, @PercentChange))
                AS source ([Date], [Name], [Last], [Change], [PercentChange])
            ON target.[Date] = source.[Date] AND target.[Name] = source.[Name]
            WHEN MATCHED THEN
                UPDATE SET [Last] = source.[Last], [Change] = source.[Change], [PercentChange] = source.[PercentChange]
            WHEN NOT MATCHED THEN
                INSERT ([Date], [Name], [Last], [Change], [PercentChange])
                VALUES (source.[Date], source.[Name], source.[Last], source.[Change], source.[PercentChange]);
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@Date", SqlDbType.DateTime2) { Value = index.Date });
        cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 25) { Value = index.Name });
        cmd.Parameters.Add(new SqlParameter("@Last", SqlDbType.Real) { Value = index.Last });
        cmd.Parameters.Add(new SqlParameter("@Change", SqlDbType.Real) { Value = index.Change });
        cmd.Parameters.Add(new SqlParameter("@PercentChange", SqlDbType.Real) { Value = index.PercentChange });
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Upserts a batch of index summary entries.
    /// </summary>
    public async Task InsertIndiceSummaryAsync(IList<MarketIndices> indexList)
    {
        foreach (var index in indexList)
            await InsertIndiceSummaryAsync(index);
    }

    // Keep legacy overloads for backward compatibility until callers are migrated.
    [Obsolete("Use InsertIndiceSummaryAsync instead.")]
    public Task InsertIndiceSummary(MarketIndices index) => InsertIndiceSummaryAsync(index);

    [Obsolete("Use InsertIndiceSummaryAsync instead.")]
    public Task InsertIndiceSummary(IList<MarketIndices> indexList) => InsertIndiceSummaryAsync(indexList);

    #endregion

    /// <summary>
    /// Gets the closing price for the specified index on the given date.
    /// Returns 0 if no data found.
    /// </summary>
    public async Task<decimal> GetDailyAverage(Indices indice, DateTime date)
    {
        const string sql = """
            SELECT [Last]
            FROM [dbo].[IndiceSummary]
            WHERE [Name] = @Name
              AND [Date] >= @DateStart AND [Date] < @DateEnd
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 25) { Value = EnumToString(indice) });
        cmd.Parameters.Add(new SqlParameter("@DateStart", SqlDbType.DateTime2) { Value = date.Date });
        cmd.Parameters.Add(new SqlParameter("@DateEnd", SqlDbType.DateTime2) { Value = date.Date.AddDays(1) });

        var result = await cmd.ExecuteScalarAsync();
        return result is decimal d ? d : (result != null ? Convert.ToDecimal(result) : 0m);
    }

    /// <summary>
    /// Gets the most recent index value for the specified index.
    /// </summary>
    public async Task<IndiceValue> GetDailyMarketAverage(Indices indice)
    {
        const string sql = """
            SELECT TOP(1) [Date], [Name], [Last], [Change], [PercentChange]
            FROM [dbo].[IndiceSummary]
            WHERE [Name] = @Name
            ORDER BY [Date] DESC
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 25) { Value = EnumToString(indice) });

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new IndiceValue
            {
                Date = reader.GetDateTime(0),
                Name = reader.GetString(1),
                Price = Convert.ToDecimal(reader.GetValue(2)),
                Change = Convert.ToDecimal(reader.GetValue(3)),
                PercentChange = Convert.ToDecimal(reader.GetValue(4)),
            };
        }

        throw new InvalidOperationException($"No data found for index '{EnumToString(indice)}'.");
    }

    private static string EnumToString(Indices indice) => indice switch
    {
        Indices.Metals_Mining => "Metals & Mining",
        Indices.Health_Care => "Health Care",
        Indices.Info_Tech => "Info Tech",
        _ => indice.ToString().Replace("_", " "),
    };
}