using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Core.Db;

/// <summary>
/// Persists and retrieves relative strength features.
/// Used by Hermes for backfill and by Hercules for training data retrieval.
/// Delphi computes live and does not read from this table.
/// </summary>
public sealed class RelativeStrengthRepository : SQLBase
{
    public RelativeStrengthRepository(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public async Task UpsertAsync(RelativeStrength.RelativeStrengthRow row)
    {
        const string sql = """
            MERGE [dbo].[RelativeStrengthFeatures] AS target
            USING (SELECT @Symbol AS Symbol, @Date AS Date) AS source
            ON target.Symbol = source.Symbol AND target.Date = source.Date
            WHEN MATCHED THEN UPDATE SET
                SectorIndexSymbol = @SectorIndexSymbol,
                RS_StockVsSector_5d = @SvS5, RS_StockVsSector_10d = @SvS10,
                RS_StockVsSector_20d = @SvS20, RS_StockVsSector_60d = @SvS60,
                RS_StockVsMarket_5d = @SvM5, RS_StockVsMarket_10d = @SvM10,
                RS_StockVsMarket_20d = @SvM20, RS_StockVsMarket_60d = @SvM60,
                RS_SectorVsMarket_5d = @SecM5, RS_SectorVsMarket_10d = @SecM10,
                RS_SectorVsMarket_20d = @SecM20, RS_SectorVsMarket_60d = @SecM60,
                RS_Z_StockVsSector = @ZSvS, RS_Z_StockVsMarket = @ZSvM,
                RS_Z_SectorVsMarket = @ZSecM, CompositeScore = @Composite
            WHEN NOT MATCHED THEN INSERT (
                Symbol, Date, SectorIndexSymbol,
                RS_StockVsSector_5d, RS_StockVsSector_10d, RS_StockVsSector_20d, RS_StockVsSector_60d,
                RS_StockVsMarket_5d, RS_StockVsMarket_10d, RS_StockVsMarket_20d, RS_StockVsMarket_60d,
                RS_SectorVsMarket_5d, RS_SectorVsMarket_10d, RS_SectorVsMarket_20d, RS_SectorVsMarket_60d,
                RS_Z_StockVsSector, RS_Z_StockVsMarket, RS_Z_SectorVsMarket, CompositeScore
            ) VALUES (
                @Symbol, @Date, @SectorIndexSymbol,
                @SvS5, @SvS10, @SvS20, @SvS60,
                @SvM5, @SvM10, @SvM20, @SvM60,
                @SecM5, @SecM10, @SecM20, @SecM60,
                @ZSvS, @ZSvM, @ZSecM, @Composite
            );
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Symbol", row.Symbol);
        cmd.Parameters.AddWithValue("@Date", row.Date.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@SectorIndexSymbol", row.SectorIndexSymbol);
        cmd.Parameters.AddWithValue("@SvS5", (object?)row.RS_StockVsSector_5d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SvS10", (object?)row.RS_StockVsSector_10d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SvS20", (object?)row.RS_StockVsSector_20d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SvS60", (object?)row.RS_StockVsSector_60d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SvM5", (object?)row.RS_StockVsMarket_5d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SvM10", (object?)row.RS_StockVsMarket_10d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SvM20", (object?)row.RS_StockVsMarket_20d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SvM60", (object?)row.RS_StockVsMarket_60d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SecM5", (object?)row.RS_SectorVsMarket_5d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SecM10", (object?)row.RS_SectorVsMarket_10d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SecM20", (object?)row.RS_SectorVsMarket_20d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SecM60", (object?)row.RS_SectorVsMarket_60d ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ZSvS", (object?)row.RS_Z_StockVsSector ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ZSvM", (object?)row.RS_Z_StockVsMarket ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ZSecM", (object?)row.RS_Z_SectorVsMarket ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Composite", (object?)row.CompositeScore ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task BulkUpsertAsync(IEnumerable<RelativeStrength.RelativeStrengthRow> rows)
    {
        foreach (var row in rows)
            await UpsertAsync(row);
    }

    /// <summary>
    /// Gets all RS features for a date range (for Hercules training).
    /// </summary>
    public async Task<List<RelativeStrength.RelativeStrengthRow>> GetByDateRangeAsync(
        DateOnly from, DateOnly to)
    {
        const string sql = """
            SELECT * FROM [dbo].[RelativeStrengthFeatures]
            WHERE Date >= @From AND Date <= @To
            ORDER BY Date, Symbol
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));

        var results = new List<RelativeStrength.RelativeStrengthRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapRow(reader));

        return results;
    }

    /// <summary>Latest date with RS data (for incremental Hermes backfill).</summary>
    public async Task<DateOnly?> GetLatestDateAsync()
    {
        const string sql = "SELECT MAX(Date) FROM [dbo].[RelativeStrengthFeatures]";
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result is DateTime dt ? DateOnly.FromDateTime(dt) : null;
    }

    private static RelativeStrength.RelativeStrengthRow MapRow(SqlDataReader r)
    {
        double? Get(string col) => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetDouble(r.GetOrdinal(col));

        return new RelativeStrength.RelativeStrengthRow
        {
            Symbol = r.GetString(r.GetOrdinal("Symbol")),
            Date = DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("Date"))),
            SectorIndexSymbol = r.GetString(r.GetOrdinal("SectorIndexSymbol")),
            RS_StockVsSector_5d = Get("RS_StockVsSector_5d"),
            RS_StockVsSector_10d = Get("RS_StockVsSector_10d"),
            RS_StockVsSector_20d = Get("RS_StockVsSector_20d"),
            RS_StockVsSector_60d = Get("RS_StockVsSector_60d"),
            RS_StockVsMarket_5d = Get("RS_StockVsMarket_5d"),
            RS_StockVsMarket_10d = Get("RS_StockVsMarket_10d"),
            RS_StockVsMarket_20d = Get("RS_StockVsMarket_20d"),
            RS_StockVsMarket_60d = Get("RS_StockVsMarket_60d"),
            RS_SectorVsMarket_5d = Get("RS_SectorVsMarket_5d"),
            RS_SectorVsMarket_10d = Get("RS_SectorVsMarket_10d"),
            RS_SectorVsMarket_20d = Get("RS_SectorVsMarket_20d"),
            RS_SectorVsMarket_60d = Get("RS_SectorVsMarket_60d"),
            RS_Z_StockVsSector = Get("RS_Z_StockVsSector"),
            RS_Z_StockVsMarket = Get("RS_Z_StockVsMarket"),
            RS_Z_SectorVsMarket = Get("RS_Z_SectorVsMarket"),
            CompositeScore = Get("CompositeScore"),
        };
    }
}