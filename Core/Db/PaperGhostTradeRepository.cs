#nullable enable

using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record PaperGhostExitResult(
    Guid TradeId,
    Guid PositionId,
    string Symbol,
    int Shares,
    decimal Price,
    decimal Amount,
    decimal RealizedPnL,
    double RealizedPnLPct,
    int HoldingDays);

/// <summary>
/// Records a ghost exit and closes its active position in one guarded SQL
/// transaction. This is the exactly-once persistence boundary for ADR-0031.
/// </summary>
public sealed class PaperGhostTradeRepository : SQLBase
{
    public async Task<PaperGhostExitResult?> TryRecordExitAsync(
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
            throw new ArgumentOutOfRangeException(nameof(price), "Exit price must be positive.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 64)
            throw new ArgumentException("Exit reason is required and cannot exceed 64 characters.", nameof(reason));
        if (notes?.Length > 512)
            throw new ArgumentException("Exit notes cannot exceed 512 characters.", nameof(notes));

        bool trackedSchema = await TrackedExecutionSchema.IsInstalledAsync(
            ConnectionString,
            cancellationToken);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            string selectSql = trackedSchema ? """
SELECT [Symbol],[Shares],[CostBasis],[EntryDate]
FROM [dbo].[ActivePosition] WITH (UPDLOCK, HOLDLOCK)
WHERE [PositionId] = @PositionId
  AND [IsActive] = 1
  AND [ExecutionMode] = N'Ghost';
""" : """
SELECT [Symbol],[Shares],[CostBasis],[EntryDate]
FROM [dbo].[ActivePosition] WITH (UPDLOCK, HOLDLOCK)
WHERE [PositionId] = @PositionId
  AND [IsActive] = 1;
""";
            string symbol;
            int shares;
            decimal costBasis;
            DateTime entryDate;
            await using (var select = new SqlCommand(selectSql, connection, transaction))
            {
                select.Parameters.Add(new SqlParameter("@PositionId", SqlDbType.UniqueIdentifier)
                {
                    Value = positionId
                });
                await using SqlDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }

                symbol = reader.GetString(0);
                shares = reader.GetInt32(1);
                costBasis = reader.GetDecimal(2);
                entryDate = reader.GetDateTime(3);
            }

            decimal amount = decimal.Round(price * shares, 2);
            decimal realizedPnL = decimal.Round(amount - costBasis, 2);
            double realizedPnLPct = costBasis == 0m
                ? 0d
                : (double)(realizedPnL / costBasis);
            int holdingDays = System.Math.Max(0, (tradeDate.Date - entryDate.Date).Days);
            Guid tradeId = Guid.NewGuid();

            string insertSql = trackedSchema ? """
INSERT INTO [dbo].[TradeLog]
([TradeId],[Symbol],[TradeType],[TradeDate],[Shares],[Price],[Amount],[Commission],
 [NetAmount],[PositionId],[Reason],[RealizedPnL],[RealizedPnLPct],[HoldingDays],
 [CreatedUtc],[Notes],[ExecutionMode],[AccountLabel])
VALUES
(@TradeId,@Symbol,'SELL',@TradeDate,@Shares,@Price,@Amount,0,
 @Amount,@PositionId,@Reason,@RealizedPnL,@RealizedPnLPct,@HoldingDays,
 SYSUTCDATETIME(),@Notes,N'Ghost',NULL);
""" : """
INSERT INTO [dbo].[TradeLog]
([TradeId],[Symbol],[TradeType],[TradeDate],[Shares],[Price],[Amount],[Commission],
 [NetAmount],[PositionId],[Reason],[RealizedPnL],[RealizedPnLPct],[HoldingDays],
 [CreatedUtc],[Notes])
VALUES
(@TradeId,@Symbol,'SELL',@TradeDate,@Shares,@Price,@Amount,0,
 @Amount,@PositionId,@Reason,@RealizedPnL,@RealizedPnLPct,@HoldingDays,
 SYSUTCDATETIME(),@Notes);
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
                    P("@Reason", SqlDbType.NVarChar, reason, 64),
                    DecimalParameter("@RealizedPnL", realizedPnL, 18, 2),
                    P("@RealizedPnLPct", SqlDbType.Float, realizedPnLPct),
                    P("@HoldingDays", SqlDbType.Int, holdingDays),
                    P("@Notes", SqlDbType.NVarChar, notes, 512)
                ]);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            string closeSql = trackedSchema ? """
UPDATE [dbo].[ActivePosition]
SET [CurrentPrice] = @Price,
    [CurrentValue] = @Amount,
    [UnrealizedPnL] = @RealizedPnL,
    [UnrealizedPnLPct] = @RealizedPnLPct,
    [IsActive] = 0,
    [LastUpdatedUtc] = SYSUTCDATETIME()
WHERE [PositionId] = @PositionId
  AND [IsActive] = 1
  AND [ExecutionMode] = N'Ghost';
""" : """
UPDATE [dbo].[ActivePosition]
SET [CurrentPrice] = @Price,
    [CurrentValue] = @Amount,
    [UnrealizedPnL] = @RealizedPnL,
    [UnrealizedPnLPct] = @RealizedPnLPct,
    [IsActive] = 0,
    [LastUpdatedUtc] = SYSUTCDATETIME()
WHERE [PositionId] = @PositionId
  AND [IsActive] = 1;
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
                    throw new InvalidOperationException("The active ghost position changed during exit recording.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new PaperGhostExitResult(
                tradeId,
                positionId,
                symbol,
                shares,
                price,
                amount,
                realizedPnL,
                realizedPnLPct,
                holdingDays);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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
