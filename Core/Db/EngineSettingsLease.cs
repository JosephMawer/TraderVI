#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Session lock spans evaluation, persistence and fills. Assignment's exclusive lock fences all older workers.</summary>
public sealed class EngineSettingsLease : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private EngineSettingsLease(SqlConnection connection) => this.connection = connection;
    public static async Task<EngineSettingsLease> AcquireAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(new SQLBase().ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new SqlCommand("""
                DECLARE @r int;
                EXEC @r=sys.sp_getapplock @Resource=N'TraderVI.EngineSettings',@LockMode=N'Shared',@LockOwner=N'Session',@LockTimeout=30000;
                IF @r<0 THROW 51310,'A settings assignment is in progress. Retry the evaluation.',1;
                """, connection);
            command.CommandTimeout = 40;
            await command.ExecuteNonQueryAsync(ct);
            return new(connection);
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var command = new SqlCommand("EXEC sys.sp_releaseapplock @Resource=N'TraderVI.EngineSettings',@LockOwner=N'Session';", connection);
            await command.ExecuteNonQueryAsync();
        }
        finally { await connection.DisposeAsync(); }
    }
}
