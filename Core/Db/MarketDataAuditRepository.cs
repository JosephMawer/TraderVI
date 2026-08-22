#nullable enable

using Core.DataQuality;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Loads the local market-data audit snapshot using SELECT statements only.
/// It does not call external services or mutate TraderDB.
/// </summary>
public sealed class MarketDataAuditRepository
{
    private readonly string _connectionString;

    public MarketDataAuditRepository(string? connectionString = null)
    {
        var builder = new SqlConnectionStringBuilder(connectionString ?? SQLBase.Database)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task<MarketDataAuditSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT [Date]
            FROM dbo.DailyBars
            WHERE Symbol = 'XIU'
            ORDER BY [Date];

            WITH BarStats AS
            (
                SELECT
                    Symbol,
                    COUNT_BIG(*) AS BarCount,
                    MIN([Date]) AS FirstBarDate,
                    MAX([Date]) AS LatestBarDate,
                    SUM(CONVERT(bigint, CASE
                        WHEN [Open] <= 0 OR High <= 0 OR Low <= 0 OR [Close] <= 0
                          OR High < Low OR High < [Open] OR Low > [Open]
                        THEN 1 ELSE 0 END)) AS InvalidOhlcBars,
                    SUM(CONVERT(bigint, CASE WHEN Volume < 0 THEN 1 ELSE 0 END)) AS NegativeVolumeBars
                FROM dbo.DailyBars
                GROUP BY Symbol
            )
            SELECT
                s.Symbol,
                s.LongName,
                s.ShortName,
                s.SecurityType,
                s.IsActive,
                s.IsLeveragedOrInverseEtp,
                COALESCE(b.BarCount, 0) AS BarCount,
                b.FirstBarDate,
                b.LatestBarDate,
                COALESCE(b.InvalidOhlcBars, 0) AS InvalidOhlcBars,
                COALESCE(b.NegativeVolumeBars, 0) AS NegativeVolumeBars
            FROM dbo.Symbols AS s
            LEFT JOIN BarStats AS b ON b.Symbol = s.Symbol
            ORDER BY s.Symbol;

            SELECT Symbol, Sector, Industry, SectorIndexSymbol, LastUpdated
            FROM dbo.StockSectorMap
            ORDER BY Symbol;

            WITH DuplicateDates AS
            (
                SELECT Symbol, [Date], COUNT_BIG(*) AS DuplicateRowCount
                FROM dbo.DailyBars
                GROUP BY Symbol, [Date]
                HAVING COUNT_BIG(*) > 1
            )
            SELECT
                Symbol,
                COUNT(*) AS DuplicateDates,
                SUM(DuplicateRowCount - 1) AS ExtraRows
            FROM DuplicateDates
            GROUP BY Symbol
            ORDER BY Symbol;

            SELECT
                b.Symbol,
                COUNT_BIG(*) AS BarCount,
                MAX(b.[Date]) AS LatestBarDate
            FROM dbo.DailyBars AS b
            LEFT JOIN dbo.Symbols AS s ON s.Symbol = b.Symbol
            WHERE s.Symbol IS NULL
            GROUP BY b.Symbol
            ORDER BY b.Symbol;

            SELECT
                Symbol,
                COUNT_BIG(*) AS BarCount,
                MAX([Date]) AS LatestDate,
                SUM(CONVERT(bigint, CASE WHEN Price <= 0 THEN 1 ELSE 0 END)) AS InvalidPriceRows
            FROM dbo.SectorIndices
            GROUP BY Symbol
            ORDER BY Symbol;
            """;

        var benchmarkSessions = new List<DateTime>();
        var symbols = new List<AuditedSymbol>();
        var mappings = new List<AuditedSectorMapping>();
        var duplicates = new List<DuplicateDailyBarSummary>();
        var orphans = new List<OrphanDailyBarSummary>();
        var sectorIndices = new List<SectorIndexAuditSummary>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            benchmarkSessions.Add(reader.GetDateTime(0).Date);

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            symbols.Add(new AuditedSymbol(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7).Date,
                reader.IsDBNull(8) ? null : reader.GetDateTime(8).Date,
                reader.GetInt64(9),
                reader.GetInt64(10)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            mappings.Add(new AuditedSectorMapping(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDateTime(4)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            duplicates.Add(new DuplicateDailyBarSummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt64(2)));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            orphans.Add(new OrphanDailyBarSummary(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetDateTime(2).Date));
        }

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sectorIndices.Add(new SectorIndexAuditSummary(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetDateTime(2).Date,
                reader.GetInt64(3)));
        }

        return new MarketDataAuditSnapshot(
            benchmarkSessions,
            symbols,
            mappings,
            duplicates,
            orphans,
            sectorIndices);
    }
}
