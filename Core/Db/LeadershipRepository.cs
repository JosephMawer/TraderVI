using Core.Indicators.Granville;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Data access for the <c>[dbo].[LeadershipData]</c> table.
/// Stores daily leadership snapshots used by Granville Leadership indicators (#7–#10).
/// </summary>
public class LeadershipRepository : SQLBase
{
    public LeadershipRepository()
        : base("[dbo].[LeadershipData]",
               "[Date],[NewHighs],[NewLows],[IssuesTraded]," +
               "[ActiveAdvancers],[ActiveDecliners],[ActiveN]," +
               "[Tsx60Close],[EqualWeightClose]")
    { }

    /// <summary>
    /// Retrieves the most recent <paramref name="count"/> snapshots (ascending by date).
    /// </summary>
    public async Task<List<LeadershipSnapshot>> GetRecentAsync(int count = 50)
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
    /// Upserts leadership snapshots using MERGE (idempotent).
    /// </summary>
    public async Task UpsertAsync(IReadOnlyList<LeadershipSnapshot> entries)
    {
        if (entries.Count == 0) return;

        const string mergeSql = @"
MERGE [dbo].[LeadershipData] AS target
USING (SELECT @Date AS [Date], @NewHighs AS NewHighs, @NewLows AS NewLows,
              @IssuesTraded AS IssuesTraded,
              @ActiveAdvancers AS ActiveAdvancers, @ActiveDecliners AS ActiveDecliners,
              @ActiveN AS ActiveN,
              @Tsx60Close AS Tsx60Close, @EqualWeightClose AS EqualWeightClose) AS source
ON (target.[Date] = source.[Date])
WHEN MATCHED THEN
    UPDATE SET NewHighs = source.NewHighs, NewLows = source.NewLows,
               IssuesTraded = source.IssuesTraded,
               ActiveAdvancers = source.ActiveAdvancers, ActiveDecliners = source.ActiveDecliners,
               ActiveN = source.ActiveN,
               Tsx60Close = source.Tsx60Close, EqualWeightClose = source.EqualWeightClose
WHEN NOT MATCHED THEN
    INSERT ([Date], NewHighs, NewLows, IssuesTraded, ActiveAdvancers, ActiveDecliners, ActiveN, Tsx60Close, EqualWeightClose)
    VALUES (source.[Date], source.NewHighs, source.NewLows, source.IssuesTraded,
            source.ActiveAdvancers, source.ActiveDecliners, source.ActiveN,
            source.Tsx60Close, source.EqualWeightClose);";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();

        using var cmd = new SqlCommand(mergeSql, con);
        cmd.Parameters.Add("@Date", SqlDbType.Date);
        cmd.Parameters.Add("@NewHighs", SqlDbType.Int);
        cmd.Parameters.Add("@NewLows", SqlDbType.Int);
        cmd.Parameters.Add("@IssuesTraded", SqlDbType.Int);
        cmd.Parameters.Add("@ActiveAdvancers", SqlDbType.Int);
        cmd.Parameters.Add("@ActiveDecliners", SqlDbType.Int);
        cmd.Parameters.Add("@ActiveN", SqlDbType.Int);
        cmd.Parameters.Add("@Tsx60Close", SqlDbType.Decimal);
        cmd.Parameters.Add("@EqualWeightClose", SqlDbType.Decimal);

        foreach (var entry in entries)
        {
            cmd.Parameters["@Date"].Value = entry.Date.Date;
            cmd.Parameters["@NewHighs"].Value = entry.NewHighs;
            cmd.Parameters["@NewLows"].Value = entry.NewLows;
            cmd.Parameters["@IssuesTraded"].Value = entry.IssuesTraded;
            cmd.Parameters["@ActiveAdvancers"].Value = entry.ActiveAdvancers;
            cmd.Parameters["@ActiveDecliners"].Value = entry.ActiveDecliners;
            cmd.Parameters["@ActiveN"].Value = entry.ActiveN;
            cmd.Parameters["@Tsx60Close"].Value = (object?)entry.Tsx60Close ?? DBNull.Value;
            cmd.Parameters["@EqualWeightClose"].Value = (object?)entry.EqualWeightClose ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static LeadershipSnapshot MapEntry(SqlDataReader reader) => new()
    {
        Date = reader.GetDateTime(0),
        NewHighs = reader.GetInt32(1),
        NewLows = reader.GetInt32(2),
        IssuesTraded = reader.GetInt32(3),
        ActiveAdvancers = reader.GetInt32(4),
        ActiveDecliners = reader.GetInt32(5),
        ActiveN = reader.GetInt32(6),
        Tsx60Close = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
        EqualWeightClose = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
    };
}