#nullable enable
using Core.Trader.Storage;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Explicit connection factory only: not registered with the engine and no default/live database discovery.
/// Schema installation is a separate reviewed migration, never a side effect of opening this store.
/// </summary>
public sealed class SqlTradingStorageDatabase : ITradingStorageDatabase
{
    private readonly Func<CancellationToken, Task<SqlConnection>> open;
    public SqlTradingStorageDatabase(Func<CancellationToken, Task<SqlConnection>> open) =>
        this.open = open ?? throw new ArgumentNullException(nameof(open));
    public TradingCurrentStore CreateStore() => new(this);

    async Task<ITradingStorageTransaction> ITradingStorageDatabase.BeginAsync(Guid bindingId, bool write, CancellationToken ct)
    {
        var connection = await open(ct);
        SqlTransaction? transaction = null;
        try
        {
            if (connection.State != ConnectionState.Open) throw new InvalidOperationException("Connection factory must return an open owned connection.");
            transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            await connection.ExecuteAsync(new CommandDefinition("""
                SET ANSI_NULLS ON;
                SET ANSI_PADDING ON;
                SET ANSI_WARNINGS ON;
                SET ARITHABORT ON;
                SET CONCAT_NULL_YIELDS_NULL ON;
                SET QUOTED_IDENTIFIER ON;
                SET NUMERIC_ROUNDABORT OFF;
                SET XACT_ABORT ON;
                SET NOCOUNT OFF;
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource=@Resource, @LockMode=@Mode,
                    @LockOwner='Transaction', @LockTimeout=5000;
                IF @result<0 THROW 54600,'Compact current-state account is busy.',1;
                """, new { Resource = "TradingCurrent:" + bindingId.ToString("D"), Mode = write ? "Exclusive" : "Shared" },
                transaction, commandTimeout: 10, cancellationToken: ct));
            return new Session(connection, transaction, bindingId, write);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    internal static string CurrentReadSql => string.Join("\n", TradingTable.All.OrderBy(t =>
        t.RowType == typeof(TradingAccountCurrent) ? 0 : t.RowType == typeof(TradingApprovedSettings) ? 1 : 2)
        .Select(t => t.RowType == typeof(TradingApprovedSettings)
            ? $"SELECT {string.Join(",", t.Columns.Select(c => "s.[" + c.Name + "]"))} FROM dbo.[{t.Name}] s JOIN dbo.TradingAccountCurrent a ON a.BindingId=s.BindingId AND a.PermissionId=s.PermissionId WHERE a.BindingId=@BindingId;"
            : $"SELECT {t.ColumnSql} FROM dbo.[{t.Name}] WHERE [BindingId]=@BindingId{(t.ActiveColumn is null ? "" : " AND [" + t.ActiveColumn + "]=1")};"));

    private sealed class Session(SqlConnection connection, SqlTransaction transaction, Guid bindingId, bool write)
        : ITradingStorageTransaction
    {
        public async Task<T?> FindAsync<T>(object? key, CancellationToken ct) where T : TradingStorageRow
        {
            var table = TradingTable.For<T>();
            return Utc(await connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(
                $"SELECT {table.ColumnSql} FROM dbo.[{table.Name}] WHERE {table.Predicate};",
                new { BindingId = bindingId, Key = key }, transaction, commandTimeout: 10, cancellationToken: ct)));
        }

        public async Task<TradingCurrentState?> ReadCurrentAsync(CancellationToken ct)
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(CurrentReadSql,
                new { BindingId = bindingId }, transaction, commandTimeout: 10, cancellationToken: ct));
            var account = Utc(await grid.ReadSingleOrDefaultAsync<TradingAccountCurrent>());
            var settings = Utc(await grid.ReadSingleOrDefaultAsync<TradingApprovedSettings>());
            var positions = (await grid.ReadAsync<TradingPositionCurrent>()).Select(r => Utc(r)!).ToImmutableArray();
            var symbols = (await grid.ReadAsync<TradingSymbolCurrent>()).Select(r => Utc(r)!).ToImmutableArray();
            var orders = (await grid.ReadAsync<TradingOrder>()).Select(r => Utc(r)!).ToImmutableArray();
            var restrictions = (await grid.ReadAsync<TradingRestrictionCurrent>()).Select(r => Utc(r)!).ToImmutableArray();
            var checkpoints = (await grid.ReadAsync<TradingCheckpoint>()).Select(r => Utc(r)!).ToImmutableArray();
            if (account is null)
            {
                if (settings is not null || positions.Length + symbols.Length + orders.Length + restrictions.Length + checkpoints.Length != 0)
                    throw new InvalidOperationException("Orphaned compact current state.");
                return null;
            }
            if (settings is null) throw new InvalidOperationException("Current approval is missing.");
            return new(account, settings, positions, symbols, orders, restrictions, checkpoints);
        }

        public async Task PutAsync(TradingStorageRow row, bool insertOnly, CancellationToken ct)
        {
            if (!write || row.BindingId != bindingId) throw new InvalidOperationException("Wrong write transaction.");
            var table = TradingTable.For(row.GetType());
            var parameters = new DynamicParameters();
            foreach (var column in table.Columns)
            {
                var value = row.GetType().GetProperty(column.Name)!.GetValue(row);
                DbType? type = column.SqlType.StartsWith("datetime2", StringComparison.Ordinal) ? DbType.DateTime2 :
                    column.SqlType == "date" ? DbType.Date : column.SqlType.StartsWith("nvarchar", StringComparison.Ordinal) ? DbType.String :
                    column.SqlType.StartsWith("varchar", StringComparison.Ordinal) ? DbType.AnsiString : null;
                parameters.Add(column.Name, value, type);
            }
            parameters.Add("Key", table.Key(row));
            string sql = insertOnly
                ? $"INSERT dbo.[{table.Name}] ({table.ColumnSql}) VALUES ({string.Join(",", table.Columns.Select(c => "@" + c.Name))});"
                : $"UPDATE dbo.[{table.Name}] SET {string.Join(",", table.Columns.Where(c => c.Name != "BindingId" && c.Name != table.KeyColumn).Select(c => "[" + c.Name + "]=@" + c.Name))} WHERE {table.Predicate};";
            if (await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, commandTimeout: 10, cancellationToken: ct)) != 1)
                throw new InvalidOperationException("Compact state write did not affect exactly one row.");
        }

        public async Task CommitAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await transaction.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await connection.DisposeAsync(); }
        }
    }

    // SQL datetime2 has no time zone. All non-date fields in this isolated contract store UTC.
    private static T? Utc<T>(T? row) where T : TradingStorageRow
    {
        if (row is null) return null;
        var copy = row with { };
        foreach (var column in TradingTable.For<T>().Columns.Where(c => c.SqlType.StartsWith("datetime2", StringComparison.Ordinal)))
        {
            var property = typeof(T).GetProperty(column.Name)!;
            if (property.GetValue(copy) is DateTime value) property.SetValue(copy, DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }
        return (T)copy;
    }
}
