#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>All daily hosts hold a shared session lock; reviewed selection needs the exclusive lock.</summary>
internal sealed class DelphiStrategySelectionLease : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private DelphiStrategySelectionLease(SqlConnection connection) => this.connection = connection;
    internal const string Resource = "TraderVI.StrategyModelSelection";

    internal static async Task<DelphiStrategySelectionLease> AcquireForRunAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(SQLBase.Database);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource=N'TraderVI.StrategyModelSelection',
                    @LockMode=N'Shared', @LockOwner=N'Session', @LockTimeout=0;
                IF @result<0 THROW 51300, 'Strategy selection is in progress. Retry Delphi after it completes.', 1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new(connection);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'TraderVI.StrategyModelSelection', @LockOwner=N'Session';";
                await command.ExecuteNonQueryAsync();
            }
        }
        finally { await connection.DisposeAsync(); }
    }
}
