using Core.Indicators.Granville;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Persists daily OHLC bars for US-listed indices (e.g., S&amp;P 500, NYSE Composite)
/// in <c>[dbo].[UsIndexBars]</c>. Consumed by Granville's Genuity indicators (#17–#20).
/// </summary>
public sealed class UsIndexBarsRepository : SQLBase
{
    /// <summary>Latest stored bar date for <paramref name="symbol"/>, or null if no rows exist.</summary>
    public async Task<DateTime?> GetLatestBarDateAsync(string symbol)
    {
        const string sql = @"
SELECT MAX([Date])
FROM [dbo].[UsIndexBars]
WHERE [Symbol] = @Symbol;";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add(new SqlParameter("@Symbol", SqlDbType.VarChar, 10) { Value = symbol });

        var value = await cmd.ExecuteScalarAsync();
        if (value == null || value == DBNull.Value) return null;
        return (DateTime)value;
    }

    /// <summary>
    /// Bulk-upserts a batch of bars. Uses a staging-table + MERGE pattern so re-running
    /// Hermes doesn't produce PK violations on already-stored sessions.
    /// </summary>
    public async Task UpsertBarsAsync(IReadOnlyList<UsIndexBar> bars)
    {
        if (bars == null || bars.Count == 0) return;

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();

        // Create a session-scoped temp table for the staging payload.
        using (var create = new SqlCommand(@"
CREATE TABLE #UsIndexBarsStage (
    [Symbol] VARCHAR(10) NOT NULL,
    [Date]   DATE        NOT NULL,
    [Open]   REAL        NOT NULL,
    [High]   REAL        NOT NULL,
    [Low]    REAL        NOT NULL,
    [Close]  REAL        NOT NULL,
    [Volume] BIGINT      NOT NULL
);", con))
        {
            await create.ExecuteNonQueryAsync();
        }

        var dt = new DataTable();
        dt.Columns.Add("Symbol", typeof(string));
        dt.Columns.Add("Date",   typeof(DateTime));
        dt.Columns.Add("Open",   typeof(float));
        dt.Columns.Add("High",   typeof(float));
        dt.Columns.Add("Low",    typeof(float));
        dt.Columns.Add("Close",  typeof(float));
        dt.Columns.Add("Volume", typeof(long));

        foreach (var b in bars)
        {
            dt.Rows.Add(b.Symbol, b.Date.Date,
                (float)b.Open, (float)b.High, (float)b.Low, (float)b.Close, b.Volume);
        }

        using (var bulk = new SqlBulkCopy(con)
        {
            DestinationTableName = "#UsIndexBarsStage",
            BatchSize = 5000,
            BulkCopyTimeout = 120
        })
        {
            bulk.ColumnMappings.Add("Symbol", "Symbol");
            bulk.ColumnMappings.Add("Date",   "Date");
            bulk.ColumnMappings.Add("Open",   "Open");
            bulk.ColumnMappings.Add("High",   "High");
            bulk.ColumnMappings.Add("Low",    "Low");
            bulk.ColumnMappings.Add("Close",  "Close");
            bulk.ColumnMappings.Add("Volume", "Volume");

            await bulk.WriteToServerAsync(dt);
        }

        using (var merge = new SqlCommand(@"
MERGE [dbo].[UsIndexBars] AS T
USING #UsIndexBarsStage AS S
   ON T.[Symbol] = S.[Symbol] AND T.[Date] = S.[Date]
WHEN MATCHED THEN UPDATE SET
    T.[Open]   = S.[Open],
    T.[High]   = S.[High],
    T.[Low]    = S.[Low],
    T.[Close]  = S.[Close],
    T.[Volume] = S.[Volume]
WHEN NOT MATCHED THEN
    INSERT ([Symbol],[Date],[Open],[High],[Low],[Close],[Volume],[CreatedAt])
    VALUES (S.[Symbol],S.[Date],S.[Open],S.[High],S.[Low],S.[Close],S.[Volume], SYSUTCDATETIME());

DROP TABLE #UsIndexBarsStage;", con))
        {
            await merge.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Returns bars for <paramref name="symbol"/> ordered ascending by date,
    /// optionally bounded by <paramref name="fromDate"/> (inclusive).
    /// </summary>
    public async Task<IReadOnlyList<UsIndexBar>> GetBarsAsync(string symbol, DateTime? fromDate = null)
    {
        var sql = @"
SELECT [Symbol],[Date],[Open],[High],[Low],[Close],[Volume]
FROM [dbo].[UsIndexBars]
WHERE [Symbol] = @Symbol";
        if (fromDate.HasValue) sql += " AND [Date] >= @From";
        sql += " ORDER BY [Date] ASC;";

        var bars = new List<UsIndexBar>();
        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add(new SqlParameter("@Symbol", SqlDbType.VarChar, 10) { Value = symbol });
        if (fromDate.HasValue)
            cmd.Parameters.Add(new SqlParameter("@From", SqlDbType.Date) { Value = fromDate.Value.Date });

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            bars.Add(new UsIndexBar(
                Symbol: reader.GetString(0),
                Date:   reader.GetDateTime(1),
                Open:   reader.GetFloat(2),
                High:   reader.GetFloat(3),
                Low:    reader.GetFloat(4),
                Close:  reader.GetFloat(5),
                Volume: reader.GetInt64(6)));
        }
        return bars;
    }
}
