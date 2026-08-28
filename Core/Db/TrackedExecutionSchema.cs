#nullable enable

using Microsoft.Data.SqlClient;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public static class TrackedExecutionSchema
{
    public const string MigrationFileName = "20260827_013_AddTrackedExecutionMode.sql";

    internal static async Task<bool> IsInstalledAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT
    (CASE WHEN COL_LENGTH(N'dbo.ActivePosition', N'ExecutionMode') IS NULL THEN 0 ELSE 1 END) +
    (CASE WHEN COL_LENGTH(N'dbo.ActivePosition', N'AccountLabel') IS NULL THEN 0 ELSE 1 END) +
    (CASE WHEN COL_LENGTH(N'dbo.TradeLog', N'ExecutionMode') IS NULL THEN 0 ELSE 1 END) +
    (CASE WHEN COL_LENGTH(N'dbo.TradeLog', N'AccountLabel') IS NULL THEN 0 ELSE 1 END) +
    (CASE WHEN OBJECT_ID(N'dbo.PositionExecutionAudit', N'U') IS NULL THEN 0 ELSE 1 END);
""";
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        int presentObjects = Convert.ToInt32(value);
        return presentObjects switch
        {
            0 => false,
            5 => true,
            _ => throw new InvalidOperationException(
                $"The tracked-execution database schema is partial ({presentObjects}/5 objects present). " +
                $"TraderVI refused to treat it as legacy Ghost-only state. Review {MigrationFileName} and the database before monitoring.")
        };
    }

    public static async Task<bool> IsInstalledAsync(
        CancellationToken cancellationToken = default) =>
        await IsInstalledAsync(SQLBase.Database, cancellationToken);

    internal static InvalidOperationException MigrationRequired() => new(
        $"Real-position tracking requires the reviewed {MigrationFileName} migration. " +
        "The existing database remains usable in legacy Ghost mode until that migration is manually applied.");
}
