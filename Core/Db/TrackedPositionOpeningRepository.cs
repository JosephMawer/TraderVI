#nullable enable

using Core.Trader;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed record TrackedPositionOpenRequest(
    string Symbol,
    DateTime TradeDate,
    int Shares,
    decimal Price,
    string Reason,
    string? Notes,
    Guid? OriginalPickId,
    double? EntryComposite,
    decimal StopLossPrice,
    decimal WarningPrice,
    TrackedExecutionMode ExecutionMode,
    string? AccountLabel);

public sealed record TrackedPositionOpenResult(
    Guid TradeId,
    Guid PositionId,
    string Symbol,
    int Shares,
    decimal Price,
    decimal Amount,
    TrackedExecutionMode ExecutionMode,
    string? AccountLabel);

/// <summary>
/// Inserts a tracked BUY and its active position in one guarded transaction.
/// The BUY is linked to the position at insert time, so a crash cannot leave an
/// unattached trade or a position without its entry trade.
/// </summary>
public sealed class TrackedPositionOpeningRepository : SQLBase
{
    public async Task<TrackedPositionOpenResult?> TryOpenAsync(
        TrackedPositionOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        string symbol = request.Symbol.Trim().ToUpperInvariant();
        string? normalizedAccount = TrackedExecutionModeContract.NormalizeAccountLabel(
            request.ExecutionMode,
            request.AccountLabel);
        bool trackedSchema = await TrackedExecutionSchema.IsInstalledAsync(
            ConnectionString,
            cancellationToken);
        if (!trackedSchema && request.ExecutionMode == TrackedExecutionMode.Real)
            throw TrackedExecutionSchema.MigrationRequired();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            const string existingSql = """
SELECT TOP (1) [PositionId]
FROM [dbo].[ActivePosition] WITH (UPDLOCK, HOLDLOCK)
WHERE [Symbol] = @Symbol
  AND [IsActive] = 1;
""";
            await using (var existing = new SqlCommand(existingSql, connection, transaction))
            {
                existing.Parameters.Add(P("@Symbol", SqlDbType.NVarChar, symbol, 16));
                if (await existing.ExecuteScalarAsync(cancellationToken) is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }
            }

            Guid tradeId = Guid.NewGuid();
            Guid positionId = Guid.NewGuid();
            decimal amount = decimal.Round(request.Price * request.Shares, 2);

            string tradeSql = trackedSchema ? """
INSERT INTO [dbo].[TradeLog]
([TradeId],[Symbol],[TradeType],[TradeDate],[Shares],[Price],[Amount],[Commission],
 [NetAmount],[PositionId],[Reason],[EntryComposite],[CreatedUtc],[Notes],
 [ExecutionMode],[AccountLabel])
VALUES
(@TradeId,@Symbol,N'BUY',@TradeDate,@Shares,@Price,@Amount,0,
 @Amount,@PositionId,@Reason,@EntryComposite,SYSUTCDATETIME(),@Notes,
 @ExecutionMode,@AccountLabel);
""" : """
INSERT INTO [dbo].[TradeLog]
([TradeId],[Symbol],[TradeType],[TradeDate],[Shares],[Price],[Amount],[Commission],
 [NetAmount],[PositionId],[Reason],[EntryComposite],[CreatedUtc],[Notes])
VALUES
(@TradeId,@Symbol,N'BUY',@TradeDate,@Shares,@Price,@Amount,0,
 @Amount,@PositionId,@Reason,@EntryComposite,SYSUTCDATETIME(),@Notes);
""";
            await using (var insertTrade = new SqlCommand(tradeSql, connection, transaction))
            {
                insertTrade.Parameters.AddRange(
                [
                    P("@TradeId", SqlDbType.UniqueIdentifier, tradeId),
                    P("@Symbol", SqlDbType.NVarChar, symbol, 16),
                    P("@TradeDate", SqlDbType.DateTime2, request.TradeDate),
                    P("@Shares", SqlDbType.Int, request.Shares),
                    DecimalParameter("@Price", request.Price, 18, 4),
                    DecimalParameter("@Amount", amount, 18, 2),
                    P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                    P("@Reason", SqlDbType.NVarChar, request.Reason.Trim(), 64),
                    P("@EntryComposite", SqlDbType.Float, request.EntryComposite),
                    P("@Notes", SqlDbType.NVarChar, request.Notes, 512),
                    P("@ExecutionMode", SqlDbType.NVarChar, request.ExecutionMode.ToStorageValue(), 8),
                    P("@AccountLabel", SqlDbType.NVarChar, normalizedAccount, 64)
                ]);
                await insertTrade.ExecuteNonQueryAsync(cancellationToken);
            }

            string positionSql = trackedSchema ? """
INSERT INTO [dbo].[ActivePosition]
([PositionId],[Symbol],[EntryDate],[EntryPrice],[Shares],[CostBasis],[OriginalPickId],
 [StopLossPrice],[WarningPrice],[HighWaterMark],[IsActive],[LastUpdatedUtc],[Notes],
 [ExecutionMode],[AccountLabel])
VALUES
(@PositionId,@Symbol,@EntryDate,@Price,@Shares,@Amount,@OriginalPickId,
 @StopLossPrice,@WarningPrice,@Price,1,SYSUTCDATETIME(),@Notes,
 @ExecutionMode,@AccountLabel);
""" : """
INSERT INTO [dbo].[ActivePosition]
([PositionId],[Symbol],[EntryDate],[EntryPrice],[Shares],[CostBasis],[OriginalPickId],
 [StopLossPrice],[WarningPrice],[HighWaterMark],[IsActive],[LastUpdatedUtc],[Notes])
VALUES
(@PositionId,@Symbol,@EntryDate,@Price,@Shares,@Amount,@OriginalPickId,
 @StopLossPrice,@WarningPrice,@Price,1,SYSUTCDATETIME(),@Notes);
""";
            await using (var insertPosition = new SqlCommand(positionSql, connection, transaction))
            {
                insertPosition.Parameters.AddRange(
                [
                    P("@PositionId", SqlDbType.UniqueIdentifier, positionId),
                    P("@Symbol", SqlDbType.NVarChar, symbol, 16),
                    P("@EntryDate", SqlDbType.Date, request.TradeDate.Date),
                    P("@Shares", SqlDbType.Int, request.Shares),
                    DecimalParameter("@Price", request.Price, 18, 4),
                    DecimalParameter("@Amount", amount, 18, 2),
                    P("@OriginalPickId", SqlDbType.UniqueIdentifier, request.OriginalPickId),
                    DecimalParameter("@StopLossPrice", request.StopLossPrice, 18, 4),
                    DecimalParameter("@WarningPrice", request.WarningPrice, 18, 4),
                    P("@Notes", SqlDbType.NVarChar, request.Notes, 512),
                    P("@ExecutionMode", SqlDbType.NVarChar, request.ExecutionMode.ToStorageValue(), 8),
                    P("@AccountLabel", SqlDbType.NVarChar, normalizedAccount, 64)
                ]);
                await insertPosition.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new TrackedPositionOpenResult(
                tradeId,
                positionId,
                symbol,
                request.Shares,
                request.Price,
                amount,
                request.ExecutionMode,
                normalizedAccount);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static void Validate(TrackedPositionOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Symbol.Trim().Length > 16)
            throw new ArgumentException("Symbol is required and cannot exceed 16 characters.", nameof(request));
        if (request.TradeDate == default)
            throw new ArgumentException("Trade date is required.", nameof(request));
        if (request.Shares <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Shares must be positive.");
        if (request.Price <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Price must be positive.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 64)
            throw new ArgumentException("Reason is required and cannot exceed 64 characters.", nameof(request));
        if (request.Notes?.Length > 512)
            throw new ArgumentException("Notes cannot exceed 512 characters.", nameof(request));
        if (request.StopLossPrice <= 0m || request.WarningPrice <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Risk prices must be positive.");
    }

    private static SqlParameter P(string name, SqlDbType type, object? value, int? size = null)
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
