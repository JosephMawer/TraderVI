using Core.Indicators.Granville;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Retrieves the top-N most active stocks by volume for a given date.
/// Used to populate <see cref="GranvilleMarketContext.MostActiveStocks"/> for
/// Granville's Most Active indicators (#11–#14).
/// </summary>
public sealed class MostActiveStocksRepository : SQLBase
{
    public MostActiveStocksRepository()
        : base("[dbo].[DailyStock]", "[Date],[Ticker],[Open],[Close],[Volume]")
    { }

    /// <summary>
    /// Returns the top <paramref name="count"/> stocks by volume on <paramref name="date"/>, 
    /// descending by volume. Defaults to 15 — Granville's "most active" list size.
    /// </summary>
    public async Task<List<MostActiveSnapshot>> GetTopByVolumeAsync(DateTime date, int count = 15)
    {
        const string sql = @"
SELECT TOP (@Count) [Ticker], [Open], [Close], [Volume]
FROM   [dbo].[DailyStock]
WHERE  CAST([Date] AS DATE) = CAST(@Date AS DATE)
  AND  [Open]  > 0
  AND  [Close] > 0
ORDER  BY [Volume] DESC";

        // fix: SQLBase.ExecuteReaderAsync expects List<SqlParameter>, not SqlParameter[]
        return await ExecuteReaderAsync(
            sql,
            new List<SqlParameter>
            {
                new SqlParameter("@Count", SqlDbType.Int)  { Value = count },
                new SqlParameter("@Date",  SqlDbType.Date) { Value = date.Date }
            },
            static r => new MostActiveSnapshot(
                Ticker: r.GetString(0),
                Open:   r.GetDecimal(1),
                Close:  r.GetDecimal(2),
                Volume: r.GetInt64(3)));
    }
}