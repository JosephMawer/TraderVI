#nullable enable
using Core.Db;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Services;

/// <summary>Local display metadata only; no provider calls or reference-data writes.</summary>
public static class HoldingSectorReader
{
    public static async Task<IReadOnlyDictionary<string, HoldingSectorInfo>> ReadAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        ct = timeout.Token;
        const string sql = """
            SELECT m.Symbol AS BrokerSymbol, m.Symbol, m.Sector, m.Industry, m.SectorIndexSymbol, m.LastUpdated
            FROM dbo.StockSectorMap m
            UNION ALL
            SELECT a.Alias, a.Symbol, COALESCE(m.Sector, 'Unknown'), m.Industry,
                m.SectorIndexSymbol, COALESCE(m.LastUpdated, a.VerifiedUtc)
            FROM dbo.SymbolAliases a LEFT JOIN dbo.StockSectorMap m ON m.Symbol = a.Symbol
            WHERE a.Source = 'Broker';
            """;
        await using var connection = new SqlConnection(SQLBase.Database);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<HoldingSectorReference>();
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDateTime(5)));
        return HoldingSectorInfo.From(rows);
    }
}
