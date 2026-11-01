using Core.Indicators;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Data access for the <c>[dbo].[AdvanceDeclineLine]</c> table.
/// Stores and retrieves Granville's cumulative Advance-Decline Line.
/// </summary>
public class AdvanceDeclineRepository : SQLBase
{
    public AdvanceDeclineRepository()
        : base("[dbo].[AdvanceDeclineLine]",
               "[Date],[Advancers],[Decliners],[Unchanged],[DailyPlurality],[CumulativeDifferential],[XiuClose]")
    { }

    /// <summary>
    /// Retrieves the full A/D Line series, sorted ascending by date.
    /// </summary>
    public async Task<List<ADLineEntry>> GetAllAsync()
    {
        string query = $"SELECT {Fields} FROM {DbName} ORDER BY [Date] ASC";

        return await ExecuteReaderAsync(query, MapEntry);
    }

    /// <summary>
    /// Retrieves the most recent <paramref name="count"/> entries (for derived calculations).
    /// </summary>
    public async Task<List<ADLineEntry>> GetRecentAsync(int count = 200)
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
    /// Retrieves entries from a given start date onwards.
    /// </summary>
    public async Task<List<ADLineEntry>> GetFromDateAsync(DateTime startDate)
    {
        string query = $"SELECT {Fields} FROM {DbName} WHERE [Date] >= @StartDate ORDER BY [Date] ASC";

        return await ExecuteReaderAsync(query,
            [new SqlParameter("@StartDate", SqlDbType.Date) { Value = startDate.Date }],
            MapEntry);
    }

    /// <summary>
    /// Gets the last stored CumulativeDifferential, so we can continue the running total.
    /// Returns 0 if no rows exist yet.
    /// </summary>
    public async Task<(DateTime? LastDate, int CumulativeDifferential)> GetLastCumulativeAsync()
    {
        const string sql = @"
SELECT TOP 1 [Date], [CumulativeDifferential]
FROM [dbo].[AdvanceDeclineLine]
ORDER BY [Date] DESC";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return (reader.GetDateTime(0), reader.GetInt32(1));
        }

        return (null, 0);
    }

    /// <summary>
    /// Upserts A/D Line entries using MERGE (idempotent — safe to re-run).
    /// </summary>
    public async Task UpsertAsync(IReadOnlyList<ADLineEntry> entries)
    {
        if (entries.Count == 0) return;

        const string mergeSql = @"
MERGE [dbo].[AdvanceDeclineLine] AS target
USING (SELECT @Date AS [Date], @Advancers AS Advancers, @Decliners AS Decliners,
              @Unchanged AS Unchanged, @DailyPlurality AS DailyPlurality,
              @CumulativeDifferential AS CumulativeDifferential, @XiuClose AS XiuClose) AS source
ON (target.[Date] = source.[Date])
WHEN MATCHED THEN
    UPDATE SET Advancers = source.Advancers, Decliners = source.Decliners,
               Unchanged = source.Unchanged, DailyPlurality = source.DailyPlurality,
               CumulativeDifferential = source.CumulativeDifferential, XiuClose = source.XiuClose
WHEN NOT MATCHED THEN
    INSERT ([Date], Advancers, Decliners, Unchanged, DailyPlurality, CumulativeDifferential, XiuClose)
    VALUES (source.[Date], source.Advancers, source.Decliners, source.Unchanged,
            source.DailyPlurality, source.CumulativeDifferential, source.XiuClose);";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();

        using var cmd = new SqlCommand(mergeSql, con);
        cmd.Parameters.Add("@Date", SqlDbType.Date);
        cmd.Parameters.Add("@Advancers", SqlDbType.Int);
        cmd.Parameters.Add("@Decliners", SqlDbType.Int);
        cmd.Parameters.Add("@Unchanged", SqlDbType.Int);
        cmd.Parameters.Add("@DailyPlurality", SqlDbType.Int);
        cmd.Parameters.Add("@CumulativeDifferential", SqlDbType.Int);
        cmd.Parameters.Add("@XiuClose", SqlDbType.Real);

        foreach (var entry in entries)
        {
            cmd.Parameters["@Date"].Value = entry.Date.Date;
            cmd.Parameters["@Advancers"].Value = entry.Advancers;
            cmd.Parameters["@Decliners"].Value = entry.Decliners;
            cmd.Parameters["@Unchanged"].Value = entry.Unchanged;
            cmd.Parameters["@DailyPlurality"].Value = entry.DailyPlurality;
            cmd.Parameters["@CumulativeDifferential"].Value = entry.CumulativeDifferential;
            cmd.Parameters["@XiuClose"].Value = entry.XiuClose.HasValue ? entry.XiuClose.Value : DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static ADLineEntry MapEntry(SqlDataReader reader) => new()
    {
        Date = reader.GetDateTime(0),
        Advancers = reader.GetInt32(1),
        Decliners = reader.GetInt32(2),
        Unchanged = reader.GetInt32(3),
        DailyPlurality = reader.GetInt32(4),
        CumulativeDifferential = reader.GetInt32(5),
        XiuClose = reader.IsDBNull(6) ? null : reader.GetFloat(6)
    };
}