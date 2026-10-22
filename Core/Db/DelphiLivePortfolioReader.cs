#nullable enable
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record DelphiLiveAccountsRead(bool SchemaInstalled, IReadOnlyList<DelphiLivePortfolioAccount> Accounts);

/// <summary>SELECT-only portfolio overview. Does not compose or start an operational workflow.</summary>
public sealed class DelphiLivePortfolioReader : SQLBase
{
    public async Task<DelphiLiveAccountsRead> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var schema = new SqlCommand("""
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.DelphiLivePortfolioLedger',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLivePortfolioGeneration',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLivePolicyAssignment',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit);
""", connection);
        if (!(bool)(await schema.ExecuteScalarAsync(cancellationToken))!) return new(false, []);
        await using var command = new SqlCommand("""
SELECT l.SnapshotJson,g.EffectiveSessionOpenUtc,g.EndExclusiveTradingDate,a.CancelledUtc
FROM dbo.DelphiLivePortfolioLedger l
JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
JOIN dbo.DelphiLivePolicyAssignment a ON a.AssignmentId=g.AssignmentId
ORDER BY CASE WHEN g.EndExclusiveTradingDate IS NULL AND a.CancelledUtc IS NULL THEN 0 ELSE 1 END,
 g.AuthorizedUtc DESC,l.PortfolioId;
""", connection);
        var saved = new List<(string Json, DateTime Open, DateOnly? End, DateTime? Cancelled)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            saved.Add((reader.GetString(0), DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? null : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)));
        await reader.DisposeAsync();
        bool working = await EnginePaperWorkingStore.InstalledAsync(connection, null, cancellationToken);
        var accounts = new List<DelphiLivePortfolioAccount>();
        foreach (var row in saved)
        {
            var snapshot = working
                ? await DelphiLiveLedgerRepository.ReadWorkingAsync(connection,
                    System.Text.Json.Nodes.JsonNode.Parse(row.Json)!["portfolioId"]!.GetValue<Guid>(), cancellationToken)
                    ?? throw new InvalidOperationException("A paper account disappeared during refresh.")
                : await DelphiLiveSnapshotStorage.ReadAsync(connection, null, row.Json, cancellationToken);
            accounts.Add(new(snapshot, row.Open, row.End, row.Cancelled));
        }
        return new(true, accounts);
    }
}
