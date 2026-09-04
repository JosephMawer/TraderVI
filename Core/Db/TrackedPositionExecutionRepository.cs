#nullable enable

using Core.Trader;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record PositionModeChangeResult(
    Guid PositionId,
    string Symbol,
    TrackedExecutionMode FromMode,
    TrackedExecutionMode ToMode,
    string AccountLabel,
    bool Changed);

public sealed record TrackedRealExitResult(
    Guid TradeId,
    Guid PositionId,
    string Symbol,
    string AccountLabel,
    int Shares,
    decimal Price,
    decimal Amount,
    decimal RealizedPnL,
    double RealizedPnLPct,
    int HoldingDays,
    bool WasAlreadyRecorded);

/// <summary>
/// Persists operator-reported Real reconciliation only. It has no broker client
/// and cannot submit an order.
/// </summary>
public sealed class TrackedPositionExecutionRepository : SQLBase
{
    public async Task<PositionModeChangeResult> MarkActiveGhostAsRealAsync(
        Guid positionId,
        string accountLabel,
        string reason,
        CancellationToken cancellationToken = default)
    {
        string normalizedAccount =
            TrackedExecutionModeContract.NormalizeAccountLabel(
                TrackedExecutionMode.Real,
                accountLabel)!;
        if (positionId == Guid.Empty)
            throw new ArgumentException("Position ID is required.", nameof(positionId));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 256)
            throw new ArgumentException("A reconciliation reason of 1 to 256 characters is required.", nameof(reason));
        if (!await TrackedExecutionSchema.IsInstalledAsync(ConnectionString, cancellationToken))
            throw TrackedExecutionSchema.MigrationRequired();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            const string selectSql = """
SELECT [Symbol],[ExecutionMode],[AccountLabel]
FROM [dbo].[ActivePosition] WITH (UPDLOCK, HOLDLOCK)
WHERE [PositionId] = @PositionId
  AND [IsActive] = 1;
""";
            string symbol;
            TrackedExecutionMode fromMode;
            string? existingAccount;
            await using (var select = new SqlCommand(selectSql, connection, transaction))
            {
                select.Parameters.Add(P("@PositionId", SqlDbType.UniqueIdentifier, positionId));
                await using SqlDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("The selected active position no longer exists.");
                symbol = reader.GetString(0);
                fromMode = TrackedExecutionModeContract.Parse(reader.GetString(1));
                existingAccount = reader.IsDBNull(2) ? null : reader.GetString(2);
            }

            if (fromMode == TrackedExecutionMode.Real)
            {
                if (!string.Equals(existingAccount, normalizedAccount, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"{symbol} is already Real in account '{existingAccount}'. Account changes require a separately audited correction.");
                await transaction.CommitAsync(cancellationToken);
                return new PositionModeChangeResult(
                    positionId,
                    symbol,
                    fromMode,
                    TrackedExecutionMode.Real,
                    normalizedAccount,
                    false);
            }

            const string updatePositionSql = """
UPDATE [dbo].[ActivePosition]
SET [ExecutionMode] = N'Real',
    [AccountLabel] = @AccountLabel,
    [LastUpdatedUtc] = SYSUTCDATETIME()
WHERE [PositionId] = @PositionId
  AND [IsActive] = 1
  AND [ExecutionMode] = N'Ghost';
""";
            await using (var update = new SqlCommand(updatePositionSql, connection, transaction))
            {
                update.Parameters.AddRange(
                [
                    P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                    P("@AccountLabel", SqlDbType.NVarChar, normalizedAccount, 64)
                ]);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("The position changed during Real reconciliation.");
            }

            const string updateTradesSql = """
UPDATE [dbo].[TradeLog]
SET [ExecutionMode] = N'Real',
    [AccountLabel] = @AccountLabel
WHERE [PositionId] = @PositionId
  AND [ExecutionMode] = N'Ghost';
""";
            await using (var update = new SqlCommand(updateTradesSql, connection, transaction))
            {
                update.Parameters.AddRange(
                [
                    P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                    P("@AccountLabel", SqlDbType.NVarChar, normalizedAccount, 64)
                ]);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            const string auditSql = """
INSERT INTO [dbo].[PositionExecutionAudit]
([AuditId],[PositionId],[FromMode],[ToMode],[AccountLabel],[Reason])
VALUES
(@AuditId,@PositionId,N'Ghost',N'Real',@AccountLabel,@Reason);
""";
            await using (var insert = new SqlCommand(auditSql, connection, transaction))
            {
                insert.Parameters.AddRange(
                [
                    P("@AuditId", SqlDbType.UniqueIdentifier, Guid.NewGuid()),
                    P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                    P("@AccountLabel", SqlDbType.NVarChar, normalizedAccount, 64),
                    P("@Reason", SqlDbType.NVarChar, reason.Trim(), 256)
                ]);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new PositionModeChangeResult(
                positionId,
                symbol,
                fromMode,
                TrackedExecutionMode.Real,
                normalizedAccount,
                true);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TrackedRealExitResult?> TryRecordManualRealExitAsync(
        Guid positionId,
        decimal price,
        DateTime tradeDate,
        string reason,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (positionId == Guid.Empty)
            throw new ArgumentException("Position ID is required.", nameof(positionId));
        if (price <= 0m)
            throw new ArgumentOutOfRangeException(nameof(price), "Exit fill must be positive.");
        if (tradeDate == default)
            throw new ArgumentException("The reported fill time is required.", nameof(tradeDate));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 64)
            throw new ArgumentException("Exit reason is required and cannot exceed 64 characters.", nameof(reason));
        if (notes?.Length > 512)
            throw new ArgumentException("Exit notes cannot exceed 512 characters.", nameof(notes));
        if (!await TrackedExecutionSchema.IsInstalledAsync(ConnectionString, cancellationToken))
            throw TrackedExecutionSchema.MigrationRequired();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            const string selectSql = """
SELECT [Symbol],[Shares],[CostBasis],[EntryDate],[AccountLabel]
FROM [dbo].[ActivePosition] WITH (UPDLOCK, HOLDLOCK)
WHERE [PositionId] = @PositionId
  AND [IsActive] = 1
  AND [ExecutionMode] = N'Real';
""";
            string symbol;
            int shares;
            decimal costBasis;
            DateTime entryDate;
            string accountLabel;
            bool activeRealPositionFound;
            await using (var select = new SqlCommand(selectSql, connection, transaction))
            {
                select.Parameters.Add(P("@PositionId", SqlDbType.UniqueIdentifier, positionId));
                await using SqlDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
                activeRealPositionFound = await reader.ReadAsync(cancellationToken);
                if (activeRealPositionFound)
                {
                    symbol = reader.GetString(0);
                    shares = reader.GetInt32(1);
                    costBasis = reader.GetDecimal(2);
                    entryDate = reader.GetDateTime(3);
                    accountLabel = reader.GetString(4);
                }
                else
                {
                    symbol = string.Empty;
                    shares = 0;
                    costBasis = 0m;
                    entryDate = default;
                    accountLabel = string.Empty;
                }
            }

            if (!activeRealPositionFound)
            {
                TrackedRealExitResult? existing = await ReadExistingRealExitAsync(
                    connection,
                    transaction,
                    positionId,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            if (tradeDate.Date < entryDate.Date)
                throw new InvalidOperationException(
                    $"The reported exit date {tradeDate:yyyy-MM-dd} is before the {symbol} entry date {entryDate:yyyy-MM-dd}.");

            decimal amount = decimal.Round(price * shares, 2);
            decimal realizedPnL = decimal.Round(amount - costBasis, 2);
            double realizedPnLPct = costBasis == 0m ? 0d : (double)(realizedPnL / costBasis);
            int holdingDays = System.Math.Max(0, (tradeDate.Date - entryDate.Date).Days);
            Guid tradeId = Guid.NewGuid();

            const string insertSql = """
INSERT INTO [dbo].[TradeLog]
([TradeId],[Symbol],[TradeType],[TradeDate],[Shares],[Price],[Amount],[Commission],
 [NetAmount],[PositionId],[ExecutionMode],[AccountLabel],[Reason],[RealizedPnL],
 [RealizedPnLPct],[HoldingDays],[CreatedUtc],[Notes])
VALUES
(@TradeId,@Symbol,N'SELL',@TradeDate,@Shares,@Price,@Amount,0,
 @Amount,@PositionId,N'Real',@AccountLabel,@Reason,@RealizedPnL,
 @RealizedPnLPct,@HoldingDays,SYSUTCDATETIME(),@Notes);
""";
            await using (var insert = new SqlCommand(insertSql, connection, transaction))
            {
                insert.Parameters.AddRange(
                [
                    P("@TradeId", SqlDbType.UniqueIdentifier, tradeId),
                    P("@Symbol", SqlDbType.NVarChar, symbol, 16),
                    P("@TradeDate", SqlDbType.DateTime2, tradeDate),
                    P("@Shares", SqlDbType.Int, shares),
                    DecimalParameter("@Price", price, 18, 4),
                    DecimalParameter("@Amount", amount, 18, 2),
                    P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                    P("@AccountLabel", SqlDbType.NVarChar, accountLabel, 64),
                    P("@Reason", SqlDbType.NVarChar, reason, 64),
                    DecimalParameter("@RealizedPnL", realizedPnL, 18, 2),
                    P("@RealizedPnLPct", SqlDbType.Float, realizedPnLPct),
                    P("@HoldingDays", SqlDbType.Int, holdingDays),
                    P("@Notes", SqlDbType.NVarChar, notes, 512)
                ]);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            const string closeSql = """
UPDATE [dbo].[ActivePosition]
SET [CurrentPrice] = @Price,
    [CurrentValue] = @Amount,
    [UnrealizedPnL] = @RealizedPnL,
    [UnrealizedPnLPct] = @RealizedPnLPct,
    [IsActive] = 0,
    [LastUpdatedUtc] = SYSUTCDATETIME()
WHERE [PositionId] = @PositionId
  AND [IsActive] = 1
  AND [ExecutionMode] = N'Real';
""";
            await using (var close = new SqlCommand(closeSql, connection, transaction))
            {
                close.Parameters.AddRange(
                [
                    P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                    DecimalParameter("@Price", price, 18, 4),
                    DecimalParameter("@Amount", amount, 18, 2),
                    DecimalParameter("@RealizedPnL", realizedPnL, 18, 2),
                    P("@RealizedPnLPct", SqlDbType.Float, realizedPnLPct)
                ]);
                if (await close.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("The active Real position changed during exit recording.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new TrackedRealExitResult(
                tradeId,
                positionId,
                symbol,
                accountLabel,
                shares,
                price,
                amount,
                realizedPnL,
                realizedPnLPct,
                holdingDays,
                WasAlreadyRecorded: false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<TrackedRealExitResult?> ReadExistingRealExitAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid positionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT TOP (2)
       [TradeId],[Symbol],[AccountLabel],[Shares],[Price],[Amount],
       [RealizedPnL],[RealizedPnLPct],[HoldingDays]
FROM [dbo].[TradeLog] WITH (UPDLOCK, HOLDLOCK)
WHERE [PositionId] = @PositionId
  AND [TradeType] = N'SELL'
  AND [ExecutionMode] = N'Real'
ORDER BY [CreatedUtc] DESC, [TradeId] DESC;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(P("@PositionId", SqlDbType.UniqueIdentifier, positionId));
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var result = new TrackedRealExitResult(
            reader.GetGuid(0),
            positionId,
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
            reader.IsDBNull(7) ? 0d : reader.GetDouble(7),
            reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
            WasAlreadyRecorded: true);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Multiple Real SELL records exist for this full-exit position; manual reconciliation is required.");
        }

        return result;
    }

    private static SqlParameter P(
        string name,
        SqlDbType type,
        object? value,
        int? size = null)
    {
        var parameter = size.HasValue
            ? new SqlParameter(name, type, size.Value)
            : new SqlParameter(name, type);
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    private static SqlParameter DecimalParameter(
        string name,
        decimal value,
        byte precision,
        byte scale) =>
        new(name, SqlDbType.Decimal)
        {
            Precision = precision,
            Scale = scale,
            Value = value
        };
}
