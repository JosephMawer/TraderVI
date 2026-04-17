using Core.Indicators.Granville;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Persists Granville indicator evaluations to [dbo].[GranvilleIndicatorLog].
/// Each row = one indicator result for one evaluation date.
/// Links to DailyPick via [EvalDate] for historical review.
/// </summary>
public class GranvilleIndicatorLogRepository : SQLBase
{
    public GranvilleIndicatorLogRepository()
        : base("[dbo].[GranvilleIndicatorLog]",
               "[LogId],[EvalDate],[IndicatorNumber],[Category],[Name],[Signal],[GranvillePoints],[Description],[NetPoints],[CompositeAdjustment],[CreatedUtc]")
    { }

    /// <summary>
    /// Logs all indicator results from a daily forecast evaluation.
    /// </summary>
    public async Task LogForecastAsync(DateTime evalDate, GranvilleDailyForecast forecast)
    {
        if (forecast.Results.Count == 0) return;

        const string sql = @"
INSERT INTO [dbo].[GranvilleIndicatorLog]
([LogId],[EvalDate],[IndicatorNumber],[Category],[Name],[Signal],[GranvillePoints],[Description],[NetPoints],[CompositeAdjustment])
VALUES
(@LogId, @EvalDate, @IndicatorNumber, @Category, @Name, @Signal, @GranvillePoints, @Description, @NetPoints, @CompositeAdjustment);";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();

        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@LogId", SqlDbType.UniqueIdentifier);
        cmd.Parameters.Add("@EvalDate", SqlDbType.Date);
        cmd.Parameters.Add("@IndicatorNumber", SqlDbType.Int);
        cmd.Parameters.Add("@Category", SqlDbType.NVarChar, 50);
        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 128);
        cmd.Parameters.Add("@Signal", SqlDbType.NVarChar, 20);
        cmd.Parameters.Add("@GranvillePoints", SqlDbType.Int);
        cmd.Parameters.Add("@Description", SqlDbType.NVarChar, 512);
        cmd.Parameters.Add("@NetPoints", SqlDbType.Int);
        cmd.Parameters.Add("@CompositeAdjustment", SqlDbType.Float);

        foreach (var result in forecast.Results)
        {
            cmd.Parameters["@LogId"].Value = Guid.NewGuid();
            cmd.Parameters["@EvalDate"].Value = evalDate.Date;
            cmd.Parameters["@IndicatorNumber"].Value = result.IndicatorNumber;
            cmd.Parameters["@Category"].Value = result.Category.ToString();
            cmd.Parameters["@Name"].Value = result.Name;
            cmd.Parameters["@Signal"].Value = result.Signal.ToString();
            cmd.Parameters["@GranvillePoints"].Value = result.GranvillePoints;
            cmd.Parameters["@Description"].Value = result.Description;
            cmd.Parameters["@NetPoints"].Value = forecast.NetPoints;
            cmd.Parameters["@CompositeAdjustment"].Value = forecast.CompositeAdjustment;

            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Deletes all log entries for a given date (for idempotent re-runs).
    /// </summary>
    public async Task DeleteByDateAsync(DateTime evalDate)
    {
        const string sql = "DELETE FROM [dbo].[GranvilleIndicatorLog] WHERE [EvalDate] = @EvalDate";
        await Delete(sql, [new SqlParameter("@EvalDate", SqlDbType.Date) { Value = evalDate.Date }]);
    }

    /// <summary>
    /// Retrieves all indicator log entries for a given date.
    /// </summary>
    public async Task<List<GranvilleIndicatorLogEntry>> GetByDateAsync(DateTime evalDate)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [EvalDate] = @EvalDate ORDER BY [IndicatorNumber]";
        return await ExecuteReaderAsync(query,
            [new SqlParameter("@EvalDate", SqlDbType.Date) { Value = evalDate.Date }],
            MapEntry);
    }

    private static GranvilleIndicatorLogEntry MapEntry(SqlDataReader reader) => new()
    {
        LogId = reader.GetGuid(0),
        EvalDate = reader.GetDateTime(1),
        IndicatorNumber = reader.GetInt32(2),
        Category = reader.GetString(3),
        Name = reader.GetString(4),
        Signal = reader.GetString(5),
        GranvillePoints = reader.GetInt32(6),
        Description = reader.GetString(7),
        NetPoints = reader.GetInt32(8),
        CompositeAdjustment = reader.GetDouble(9),
        CreatedUtc = reader.GetDateTime(10)
    };
}

/// <summary>
/// Represents a persisted Granville indicator log row.
/// </summary>
public sealed record GranvilleIndicatorLogEntry
{
    public Guid LogId { get; init; }
    public DateTime EvalDate { get; init; }
    public int IndicatorNumber { get; init; }
    public required string Category { get; init; }
    public required string Name { get; init; }
    public required string Signal { get; init; }
    public int GranvillePoints { get; init; }
    public required string Description { get; init; }
    public int NetPoints { get; init; }
    public double CompositeAdjustment { get; init; }
    public DateTime CreatedUtc { get; init; }
}