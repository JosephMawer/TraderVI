using Core.Indicators.Granville;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Data access for the <c>[dbo].[LeadershipData]</c> table.
/// Stores daily leadership snapshots used by Granville Leadership indicators (#7–#10).
/// </summary>
public class LeadershipRepository : SQLBase
{
    private const string MissingnessMigration = "migration 017";

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
        Validate(entries);
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
               ActiveAdvancers = CASE WHEN source.ActiveN IS NULL THEN target.ActiveAdvancers ELSE source.ActiveAdvancers END,
               ActiveDecliners = CASE WHEN source.ActiveN IS NULL THEN target.ActiveDecliners ELSE source.ActiveDecliners END,
               ActiveN = CASE WHEN source.ActiveN IS NULL THEN target.ActiveN ELSE source.ActiveN END,
               Tsx60Close = source.Tsx60Close, EqualWeightClose = source.EqualWeightClose
WHEN NOT MATCHED THEN
    INSERT ([Date], NewHighs, NewLows, IssuesTraded, ActiveAdvancers, ActiveDecliners, ActiveN, Tsx60Close, EqualWeightClose)
    VALUES (source.[Date], source.NewHighs, source.NewLows, source.IssuesTraded,
            source.ActiveAdvancers, source.ActiveDecliners, source.ActiveN,
            source.Tsx60Close, source.EqualWeightClose);";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        await EnsureMissingnessSchemaAsync(con);

        using var cmd = new SqlCommand(mergeSql, con);
        cmd.Parameters.Add("@Date", SqlDbType.Date);
        cmd.Parameters.Add("@NewHighs", SqlDbType.Int);
        cmd.Parameters.Add("@NewLows", SqlDbType.Int);
        cmd.Parameters.Add("@IssuesTraded", SqlDbType.Int);
        cmd.Parameters.Add("@ActiveAdvancers", SqlDbType.Int);
        cmd.Parameters.Add("@ActiveDecliners", SqlDbType.Int);
        cmd.Parameters.Add("@ActiveN", SqlDbType.Int);
        cmd.Parameters.Add("@Tsx60Close", SqlDbType.Decimal).Precision = 10;
        cmd.Parameters["@Tsx60Close"].Scale = 2;
        cmd.Parameters.Add("@EqualWeightClose", SqlDbType.Decimal).Precision = 10;
        cmd.Parameters["@EqualWeightClose"].Scale = 2;

        foreach (var entry in entries)
        {
            cmd.Parameters["@Date"].Value = entry.Date.Date;
            cmd.Parameters["@NewHighs"].Value = entry.NewHighs;
            cmd.Parameters["@NewLows"].Value = entry.NewLows;
            cmd.Parameters["@IssuesTraded"].Value = entry.IssuesTraded;
            cmd.Parameters["@ActiveAdvancers"].Value = entry.ActiveAdvancers.HasValue
                ? entry.ActiveAdvancers.Value
                : DBNull.Value;
            cmd.Parameters["@ActiveDecliners"].Value = entry.ActiveDecliners.HasValue
                ? entry.ActiveDecliners.Value
                : DBNull.Value;
            cmd.Parameters["@ActiveN"].Value = entry.ActiveN.HasValue
                ? entry.ActiveN.Value
                : DBNull.Value;
            cmd.Parameters["@Tsx60Close"].Value = (object?)entry.Tsx60Close ?? DBNull.Value;
            cmd.Parameters["@EqualWeightClose"].Value = (object?)entry.EqualWeightClose ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureMissingnessSchemaAsync(SqlConnection connection)
    {
        const string sql = """
SELECT CASE
    WHEN (
        SELECT COUNT(*)
        FROM sys.columns
        WHERE [object_id] = OBJECT_ID(N'dbo.LeadershipData', N'U')
          AND [name] IN (N'ActiveAdvancers', N'ActiveDecliners', N'ActiveN')
          AND [is_nullable] = 1
    ) = 3
     AND EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'dbo.LeadershipData', N'U')
          AND [name] = N'CK_LeadershipData_ActiveBreadthObservation'
          AND [is_disabled] = 0
          AND [is_not_trusted] = 0
    )
    THEN 1 ELSE 0
END;
""";
        using var command = new SqlCommand(sql, connection);
        int isReady = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (isReady != 1)
        {
            throw new InvalidOperationException(
                $"Leadership data writes require {MissingnessMigration} to be manually applied: " +
                "ActiveAdvancers, ActiveDecliners, and ActiveN must be nullable, and " +
                "CK_LeadershipData_ActiveBreadthObservation must be present, enabled, and trusted.");
        }
    }

    /// <summary>
    /// Gets the date of the most recent stored leadership snapshot.
    /// </summary>
    public async Task<DateTime?> GetLatestDateAsync()
    {
        const string sql = "SELECT MAX([Date]) FROM [dbo].[LeadershipData]";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var cmd = new SqlCommand(sql, con);
        var result = await cmd.ExecuteScalarAsync();

        return result is DateTime dt ? dt : null;
    }

    /// <summary>
    /// Validates the persistence boundary. The active-breadth observation is atomic:
    /// either every mover field is null, or all fields describe a valid reported basket.
    /// </summary>
    public static void Validate(IReadOnlyList<LeadershipSnapshot> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Any(entry => entry is null))
            throw new ArgumentException("Leadership entries cannot contain null snapshots.", nameof(entries));
        if (entries.GroupBy(entry => entry.Date.Date).Any(group => group.Count() > 1))
            throw new ArgumentException("Leadership entries cannot contain duplicate dates.", nameof(entries));

        foreach (var entry in entries)
        {
            if (entry.Date == default)
                throw new ArgumentException("Every leadership entry requires a date.", nameof(entries));
            if (entry.NewHighs < 0 || entry.NewLows < 0 || entry.IssuesTraded <= 0)
                throw new ArgumentException(
                    "New highs and lows must be nonnegative, and issues traded must be positive.",
                    nameof(entries));
            if (entry.NewHighs > entry.IssuesTraded || entry.NewLows > entry.IssuesTraded)
                throw new ArgumentException(
                    "New-high and new-low counts cannot exceed issues traded.",
                    nameof(entries));
            if (entry.Tsx60Close is <= 0m || entry.EqualWeightClose is <= 0m)
                throw new ArgumentException(
                    "Stored leadership benchmark prices must be positive when present.",
                    nameof(entries));

            bool hasAnyActiveValue = entry.ActiveAdvancers.HasValue
                                     || entry.ActiveDecliners.HasValue
                                     || entry.ActiveN.HasValue;
            bool hasAllActiveValues = entry.ActiveAdvancers.HasValue
                                      && entry.ActiveDecliners.HasValue
                                      && entry.ActiveN.HasValue;

            if (hasAnyActiveValue && !hasAllActiveValues)
                throw new ArgumentException(
                    "Active breadth must be entirely unavailable or contain advancers, decliners, and basket size.",
                    nameof(entries));

            if (!hasAllActiveValues)
                continue;

            int advancers = entry.ActiveAdvancers!.Value;
            int decliners = entry.ActiveDecliners!.Value;
            int basketSize = entry.ActiveN!.Value;
            if (basketSize <= 0 || advancers < 0 || decliners < 0
                || (long)advancers + decliners > basketSize)
            {
                throw new ArgumentException(
                    "Observed active breadth requires a positive basket with nonnegative counts whose sum does not exceed N.",
                    nameof(entries));
            }
        }
    }

    private static LeadershipSnapshot MapEntry(SqlDataReader reader) => new()
    {
        Date = reader.GetDateTime(0),
        NewHighs = reader.GetInt32(1),
        NewLows = reader.GetInt32(2),
        IssuesTraded = reader.GetInt32(3),
        ActiveAdvancers = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        ActiveDecliners = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        ActiveN = reader.IsDBNull(6) ? null : reader.GetInt32(6),
        Tsx60Close = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
        EqualWeightClose = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
    };
}
