using Core.Indicators;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Data access for the <c>[dbo].[MarketClimax]</c> table.
/// Stores and retrieves Granville's market-wide Climax (CLX) — the net count of OBV
/// breakouts across the S&amp;P/TSX 60 leaders. Mirrors <see cref="AdvanceDeclineRepository"/>
/// (the market-wide cumulative A/D Line), its sibling market-breadth signal.
/// </summary>
public class MarketClimaxRepository : SQLBase
{
    public MarketClimaxRepository()
        : base("[dbo].[MarketClimax]",
               "[Date],[UpBreakouts],[DownBreakouts],[Clx],[FreshUp],[FreshDown],[Covered],[BasketSize],[XiuClose]")
    { }

    /// <summary>
    /// Retrieves the most recent <paramref name="count"/> entries, sorted ascending by date.
    /// </summary>
    public async Task<List<MarketClimaxEntry>> GetRecentAsync(int count = 200)
    {
        string query = $@"
SELECT {Fields}
FROM (
    SELECT TOP (@Count) {Fields}
    FROM {DbName}
    ORDER BY [Date] DESC
) AS recent
ORDER BY [Date] ASC";

        return await ExecuteReaderAsync(query,
            [new SqlParameter("@Count", SqlDbType.Int) { Value = count }],
            MapEntry);
    }

    /// <summary>
    /// Retrieves entries from a given start date onwards, sorted ascending.
    /// </summary>
    public async Task<List<MarketClimaxEntry>> GetFromDateAsync(DateTime startDate)
    {
        string query = $"SELECT {Fields} FROM {DbName} WHERE [Date] >= @StartDate ORDER BY [Date] ASC";

        return await ExecuteReaderAsync(query,
            [new SqlParameter("@StartDate", SqlDbType.Date) { Value = startDate.Date }],
            MapEntry);
    }

    /// <summary>
    /// Gets the last stored CLX date so producers can avoid recomputing history.
    /// Returns null if no rows exist yet.
    /// </summary>
    public async Task<DateTime?> GetLastDateAsync()
    {
        const string sql = @"
SELECT TOP 1 [Date]
FROM [dbo].[MarketClimax]
ORDER BY [Date] DESC";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
            return reader.GetDateTime(0);

        return null;
    }

    /// <summary>
    /// Upserts CLX entries using MERGE on <c>[Date]</c> (idempotent — safe to re-run).
    /// </summary>
    public async Task UpsertAsync(IReadOnlyList<MarketClimaxEntry> entries)
    {
        if (entries.Count == 0) return;

        const string mergeSql = @"
MERGE [dbo].[MarketClimax] AS target
USING (SELECT @Date AS [Date], @UpBreakouts AS UpBreakouts, @DownBreakouts AS DownBreakouts,
              @Clx AS Clx, @FreshUp AS FreshUp, @FreshDown AS FreshDown,
              @Covered AS Covered, @BasketSize AS BasketSize, @XiuClose AS XiuClose) AS source
ON (target.[Date] = source.[Date])
WHEN MATCHED THEN
    UPDATE SET UpBreakouts = source.UpBreakouts, DownBreakouts = source.DownBreakouts,
               Clx = source.Clx, FreshUp = source.FreshUp, FreshDown = source.FreshDown,
               Covered = source.Covered, BasketSize = source.BasketSize, XiuClose = source.XiuClose
WHEN NOT MATCHED THEN
    INSERT ([Date], UpBreakouts, DownBreakouts, Clx, FreshUp, FreshDown, Covered, BasketSize, XiuClose)
    VALUES (source.[Date], source.UpBreakouts, source.DownBreakouts, source.Clx,
            source.FreshUp, source.FreshDown, source.Covered, source.BasketSize, source.XiuClose);";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();

        using var cmd = new SqlCommand(mergeSql, con);
        cmd.Parameters.Add("@Date", SqlDbType.Date);
        cmd.Parameters.Add("@UpBreakouts", SqlDbType.Int);
        cmd.Parameters.Add("@DownBreakouts", SqlDbType.Int);
        cmd.Parameters.Add("@Clx", SqlDbType.Int);
        cmd.Parameters.Add("@FreshUp", SqlDbType.Int);
        cmd.Parameters.Add("@FreshDown", SqlDbType.Int);
        cmd.Parameters.Add("@Covered", SqlDbType.Int);
        cmd.Parameters.Add("@BasketSize", SqlDbType.Int);
        cmd.Parameters.Add("@XiuClose", SqlDbType.Real);

        foreach (var entry in entries)
        {
            cmd.Parameters["@Date"].Value = entry.Date.Date;
            cmd.Parameters["@UpBreakouts"].Value = entry.UpBreakouts;
            cmd.Parameters["@DownBreakouts"].Value = entry.DownBreakouts;
            cmd.Parameters["@Clx"].Value = entry.Clx;
            cmd.Parameters["@FreshUp"].Value = entry.FreshUp;
            cmd.Parameters["@FreshDown"].Value = entry.FreshDown;
            cmd.Parameters["@Covered"].Value = entry.Covered;
            cmd.Parameters["@BasketSize"].Value = entry.BasketSize;
            cmd.Parameters["@XiuClose"].Value = entry.XiuClose.HasValue ? entry.XiuClose.Value : DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static MarketClimaxEntry MapEntry(SqlDataReader reader) => new()
    {
        Date = reader.GetDateTime(0),
        UpBreakouts = reader.GetInt32(1),
        DownBreakouts = reader.GetInt32(2),
        Clx = reader.GetInt32(3),
        FreshUp = reader.GetInt32(4),
        FreshDown = reader.GetInt32(5),
        Covered = reader.GetInt32(6),
        BasketSize = reader.GetInt32(7),
        XiuClose = reader.IsDBNull(8) ? null : reader.GetFloat(8)
    };
}
