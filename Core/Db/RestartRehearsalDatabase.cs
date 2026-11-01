#nullable enable
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>A rehearsal can only open its own token-marked LocalDB. No configured trading connection is consulted.</summary>
public sealed class RestartRehearsalDatabase(Guid token)
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        if (token == Guid.Empty) throw new ArgumentException("Missing rehearsal identity.");
        var settings = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\TVIRestart_" + token.ToString("N"),
            InitialCatalog = "TraderDB", IntegratedSecurity = true,
            TrustServerCertificate = true, ConnectTimeout = 10
        };
        var connection = new SqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            var marker = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties
                WHERE class=0 AND name=N'CompactRehearsal' AND SERVERPROPERTY('IsLocalDB')=1;
                """, cancellationToken: ct));
            if (marker != token.ToString("N")) throw new InvalidOperationException("Wrong rehearsal instance.");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
}
