using Core.Indicators.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Data access for the <c>[dbo].[SymbolObv]</c> table.
/// Stores and retrieves Granville's per-symbol cumulative On-Balance Volume (OBV).
/// Mirrors <see cref="AdvanceDeclineRepository"/> (the market-wide cumulative A/D Line).
/// </summary>
public class SymbolObvRepository : SQLBase
{
    public SymbolObvRepository()
        : base("[dbo].[SymbolObv]", "[Symbol],[Date],[Obv]")
    { }

    /// <summary>
    /// Retrieves the full stored OBV series for a symbol, sorted ascending by date.
    /// </summary>
    public async Task<List<OBV>> GetSeriesAsync(string symbol)
    {
        string query = $"SELECT [Date],[Obv] FROM {DbName} WHERE [Symbol] = @Symbol ORDER BY [Date] ASC";

        var rows = await ExecuteReaderAsync(query,
            [new SqlParameter("@Symbol", SqlDbType.VarChar, 10) { Value = symbol }],
            ReadRow);

        return WithDeltas(rows);
    }

    /// <summary>
    /// Retrieves a symbol's OBV series from a given start date onwards, sorted ascending.
    /// Useful for Delphi to read only the window needed for field-trend computation.
    /// </summary>
    public async Task<List<OBV>> GetSeriesFromDateAsync(string symbol, DateTime startDate)
    {
        string query = $@"
SELECT [Date],[Obv]
FROM {DbName}
WHERE [Symbol] = @Symbol AND [Date] >= @StartDate
ORDER BY [Date] ASC";

        var rows = await ExecuteReaderAsync(query,
            [
                new SqlParameter("@Symbol", SqlDbType.VarChar, 10) { Value = symbol },
                new SqlParameter("@StartDate", SqlDbType.Date) { Value = startDate.Date }
            ],
            ReadRow);

        return WithDeltas(rows);
    }

    /// <summary>
    /// Gets the last stored OBV point for a symbol, so Hermes can continue the
    /// running cumulative without recomputing history. Returns (null, 0) if none exist.
    /// </summary>
    public async Task<(DateTime? LastDate, long Obv)> GetLatestAsync(string symbol)
    {
        const string sql = @"
SELECT TOP 1 [Date], [Obv]
FROM [dbo].[SymbolObv]
WHERE [Symbol] = @Symbol
ORDER BY [Date] DESC";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add(new SqlParameter("@Symbol", SqlDbType.VarChar, 10) { Value = symbol });
        using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
            return (reader.GetDateTime(0), reader.GetInt64(1));

        return (null, 0);
    }

    /// <summary>
    /// Upserts OBV points for a symbol using MERGE (idempotent — safe to re-run).
    /// </summary>
    public async Task UpsertAsync(string symbol, IReadOnlyList<OBV> points)
    {
        if (points.Count == 0) return;

        const string mergeSql = @"
MERGE [dbo].[SymbolObv] AS target
USING (SELECT @Symbol AS [Symbol], @Date AS [Date], @Obv AS [Obv]) AS source
ON (target.[Symbol] = source.[Symbol] AND target.[Date] = source.[Date])
WHEN MATCHED THEN
    UPDATE SET [Obv] = source.[Obv]
WHEN NOT MATCHED THEN
    INSERT ([Symbol], [Date], [Obv], [CreatedAt])
    VALUES (source.[Symbol], source.[Date], source.[Obv], SYSUTCDATETIME());";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();

        using var cmd = new SqlCommand(mergeSql, con);
        cmd.Parameters.Add("@Symbol", SqlDbType.VarChar, 10).Value = symbol;
        cmd.Parameters.Add("@Date", SqlDbType.Date);
        cmd.Parameters.Add("@Obv", SqlDbType.BigInt);

        foreach (var point in points)
        {
            cmd.Parameters["@Date"].Value = point.Date.Date;
            cmd.Parameters["@Obv"].Value = point.Value;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Deletes OBV rows older than <paramref name="cutoffDate"/>, enforcing the
    /// rolling retention window (see ObvRetentionMonths). Safe because the running
    /// cumulative is already baked into the retained rows — pruning the tail never
    /// alters the head. Pass a single symbol to prune one, or null to prune all.
    /// </summary>
    public async Task<int> PruneOlderThanAsync(DateTime cutoffDate, string symbol = null)
    {
        string sql = symbol is null
            ? "DELETE FROM [dbo].[SymbolObv] WHERE [Date] < @Cutoff"
            : "DELETE FROM [dbo].[SymbolObv] WHERE [Date] < @Cutoff AND [Symbol] = @Symbol";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add(new SqlParameter("@Cutoff", SqlDbType.Date) { Value = cutoffDate.Date });
        if (symbol is not null)
            cmd.Parameters.Add(new SqlParameter("@Symbol", SqlDbType.VarChar, 10) { Value = symbol });

        return await cmd.ExecuteNonQueryAsync();
    }

    private static (DateTime Date, long Value) ReadRow(SqlDataReader reader) =>
        (reader.GetDateTime(0), reader.GetInt64(1));

    /// <summary>
    /// Attaches the per-session <see cref="OBV.Delta"/> to a raw (Date, cumulative) series.
    /// The first retained row has no in-window predecessor, so its delta is reported as 0.
    /// </summary>
    private static List<OBV> WithDeltas(List<(DateTime Date, long Value)> rows)
    {
        var result = new List<OBV>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            long delta = i == 0 ? 0 : rows[i].Value - rows[i - 1].Value;
            result.Add(new OBV(rows[i].Date, rows[i].Value, delta));
        }
        return result;
    }
}
