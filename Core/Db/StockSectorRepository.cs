using Core.TMX.Models.Domain;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Stores and retrieves the stock → sector → sector-index mapping table.
///
/// Table schema:
/// <code>
/// CREATE TABLE [dbo].[StockSectorMap] (
///     [Symbol]            NVARCHAR(10)    NOT NULL PRIMARY KEY,
///     [Sector]            NVARCHAR(50)    NOT NULL,
///     [Industry]          NVARCHAR(100)   NULL,
///     [SectorIndexSymbol] NVARCHAR(10)    NULL,
///     [LastUpdated]       DATETIME2       NOT NULL
/// );
/// </code>
/// </summary>
public sealed class StockSectorRepository : SQLBase
{
    /// <summary>
    /// Upserts a batch of stock-sector mappings.
    /// </summary>
    public async Task UpsertAsync(IReadOnlyList<StockSectorMapping> mappings)
    {
        if (mappings.Count == 0) return;

        const string sql = """
            MERGE [dbo].[StockSectorMap] AS target
            USING (VALUES (@Symbol, @Sector, @Industry, @SectorIndexSymbol, @LastUpdated))
                AS source ([Symbol], [Sector], [Industry], [SectorIndexSymbol], [LastUpdated])
            ON target.[Symbol] = source.[Symbol]
            WHEN MATCHED THEN
                UPDATE SET
                    [Sector]            = source.[Sector],
                    [Industry]          = source.[Industry],
                    [SectorIndexSymbol] = source.[SectorIndexSymbol],
                    [LastUpdated]       = source.[LastUpdated]
            WHEN NOT MATCHED THEN
                INSERT ([Symbol], [Sector], [Industry], [SectorIndexSymbol], [LastUpdated])
                VALUES (source.[Symbol], source.[Sector], source.[Industry], source.[SectorIndexSymbol], source.[LastUpdated]);
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        foreach (var m in mappings)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Symbol", SqlDbType.NVarChar, 10) { Value = m.Symbol });
            cmd.Parameters.Add(new SqlParameter("@Sector", SqlDbType.NVarChar, 50) { Value = m.Sector });
            cmd.Parameters.Add(new SqlParameter("@Industry", SqlDbType.NVarChar, 100) { Value = (object?)m.Industry ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@SectorIndexSymbol", SqlDbType.NVarChar, 10) { Value = (object?)m.SectorIndexSymbol ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@LastUpdated", SqlDbType.DateTime2) { Value = m.LastUpdated });
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Gets all stored mappings.
    /// </summary>
    public async Task<IReadOnlyList<StockSectorMapping>> GetAllAsync()
    {
        const string sql = """
            SELECT [Symbol], [Sector], [Industry], [SectorIndexSymbol], [LastUpdated]
            FROM [dbo].[StockSectorMap]
            ORDER BY [Symbol]
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var results = new List<StockSectorMapping>();
        while (await reader.ReadAsync())
        {
            results.Add(new StockSectorMapping(
                Symbol: reader.GetString(0),
                Sector: reader.GetString(1),
                Industry: reader.IsDBNull(2) ? null : reader.GetString(2),
                SectorIndexSymbol: reader.IsDBNull(3) ? null : reader.GetString(3),
                LastUpdated: reader.GetDateTime(4)));
        }
        return results;
    }

    /// <summary>
    /// Gets the mapping for a single symbol, or null if not found.
    /// </summary>
    public async Task<StockSectorMapping?> GetBySymbolAsync(string symbol)
    {
        const string sql = """
            SELECT [Symbol], [Sector], [Industry], [SectorIndexSymbol], [LastUpdated]
            FROM [dbo].[StockSectorMap]
            WHERE [Symbol] = @Symbol
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@Symbol", SqlDbType.NVarChar, 10) { Value = symbol });
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new StockSectorMapping(
                Symbol: reader.GetString(0),
                Sector: reader.GetString(1),
                Industry: reader.IsDBNull(2) ? null : reader.GetString(2),
                SectorIndexSymbol: reader.IsDBNull(3) ? null : reader.GetString(3),
                LastUpdated: reader.GetDateTime(4));
        }
        return null;
    }

    /// <summary>
    /// Gets all stocks belonging to the specified sector index symbol (e.g., "^TTFS").
    /// </summary>
    public async Task<IReadOnlyList<StockSectorMapping>> GetBySectorIndexAsync(string sectorIndexSymbol)
    {
        const string sql = """
            SELECT [Symbol], [Sector], [Industry], [SectorIndexSymbol], [LastUpdated]
            FROM [dbo].[StockSectorMap]
            WHERE [SectorIndexSymbol] = @SectorIndexSymbol
            ORDER BY [Symbol]
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@SectorIndexSymbol", SqlDbType.NVarChar, 10) { Value = sectorIndexSymbol });
        await using var reader = await cmd.ExecuteReaderAsync();

        var results = new List<StockSectorMapping>();
        while (await reader.ReadAsync())
        {
            results.Add(new StockSectorMapping(
                Symbol: reader.GetString(0),
                Sector: reader.GetString(1),
                Industry: reader.IsDBNull(2) ? null : reader.GetString(2),
                SectorIndexSymbol: reader.IsDBNull(3) ? null : reader.GetString(3),
                LastUpdated: reader.GetDateTime(4)));
        }
        return results;
    }

    /// <summary>
    /// Gets the most recent stock-sector refresh timestamp, or null if no rows exist.
    /// </summary>
    public async Task<DateTime?> GetLatestRefreshDateAsync()
    {
        const string sql = """
            SELECT MAX([LastUpdated])
            FROM [dbo].[StockSectorMap]
            """;

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);

        var result = await cmd.ExecuteScalarAsync();
        return result is DateTime dt ? dt : null;
    }
}