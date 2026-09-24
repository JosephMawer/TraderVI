#nullable enable
using Core.WSTrade.Accounts;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

internal static class BrokerTradeSyncStorage
{
    internal static async Task<bool> Installed(SqlConnection c, SqlTransaction t) =>
        await c.ExecuteScalarAsync<int>("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.BrokerTradeSync',N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.BrokerTradeSync_Immutable',N'TR') IS NOT NULL THEN 1 ELSE 0 END
            """, transaction: t) == 1;

    private sealed record Row(Guid ImportId, Guid BindingId, Guid? PreviousImportId, string ContentHash, string ReceiptJson);
    private static async Task<BrokerTradeSyncReceipt?> Latest(SqlConnection c, SqlTransaction t, Guid binding)
    {
        if (!await Installed(c, t)) return null;
        var row = await c.QuerySingleOrDefaultAsync<Row>("""
            SELECT TOP(1) ImportId,BindingId,PreviousImportId,ContentHash,ReceiptJson
            FROM dbo.BrokerTradeSync WHERE BindingId=@binding ORDER BY Sequence DESC
            """, new { binding }, t);
        if (row is null) return null;
        var receipt = Read<BrokerTradeSyncReceipt>(row.ReceiptJson);
        if (BrokerAccountImport.Hash(receipt) != row.ContentHash || receipt.ImportId != row.ImportId ||
            receipt.BindingId != row.BindingId || receipt.PreviousImportId != row.PreviousImportId)
            throw new InvalidOperationException("Saved broker trade synchronization is inconsistent. No records were changed.");
        return receipt;
    }
    internal static async Task<BrokerImportLegacyLedger> ReviewedBaseline(SqlConnection c, SqlTransaction t,
        BrokerImportBinding binding, BrokerImportLegacyLedger current)
    {
        var prior = await Latest(c, t, binding.BindingId);
        if (prior is null) return current;
        return ValidateBaseline(binding, current, prior);
    }
    internal static BrokerImportLegacyLedger ValidateBaseline(BrokerImportBinding binding, BrokerImportLegacyLedger current,
        BrokerTradeSyncReceipt prior)
    {
        if (prior.BindingId != binding.BindingId || prior.Baseline.Fingerprint != binding.LegacyFingerprint ||
            current.Fingerprint != prior.LedgerFingerprint || prior.Baseline.AccountLabel != binding.AccountLabel)
            throw new InvalidOperationException("Recorded trades changed outside broker synchronization. Review the discrepancy before continuing.");
        // Preserve the immutable first-link comparison, while checking the complete current ledger against
        // the last committed synchronization. This is not a bypass for subsequent manual ledger edits.
        return prior.Baseline;
    }
    internal static async Task Apply(SqlConnection c, SqlTransaction t, Guid importId, BrokerImportBinding binding,
        BrokerImportPlan plan, BrokerImportLegacyLedger baseline)
    {
        var prior = await Latest(c, t, binding.BindingId);
        var before = await BrokerAccountImportRepository.Ledger(c, t);
        if (prior is null && before.Fingerprint != binding.LegacyFingerprint)
            throw new InvalidOperationException("The linked ledger changed before the first trade synchronization.");
        if (prior is not null) ValidateBaseline(binding, before, prior);
        var projection = BrokerTradeProjector.Build(plan, baseline);
        if (prior is not null && prior.Projection.Trades.Any(old => projection.Trades.SingleOrDefault(x => x.OrderId == old.OrderId) != old))
            throw new InvalidOperationException("An already recorded broker order changed. Explicit correction is required.");
        var additions = projection.Trades.Where(x => !x.Legacy &&
            (prior is null || prior.Projection.Trades.All(p => p.OrderId != x.OrderId))).ToArray();
        // Only affected lifecycles are updated. Price marks, stops, highs, pick attribution and existing
        // recommendation protection remain owned by their original subsystems.
        foreach (var position in projection.Positions.Where(p => additions.Any(x => x.PositionId == p.PositionId)))
        {
            var parameters = new { position.PositionId, position.Symbol, EntryDate = position.EntryDate.ToDateTime(TimeOnly.MinValue),
                position.EntryPrice, Shares = position.Quantity, CostBasis = position.Cost, IsActive = position.Active };
            bool exists = before.Positions.Any(p => p.PositionId == position.PositionId);
            if (!exists)
                await c.ExecuteAsync("""
                    INSERT dbo.ActivePosition(PositionId,Symbol,EntryDate,EntryPrice,Shares,CostBasis,ExecutionMode,AccountLabel,IsActive,Notes)
                    VALUES(@PositionId,@Symbol,@EntryDate,@EntryPrice,@Shares,@CostBasis,N'Real',N'TFSA',@IsActive,
                      N'Broker-confirmed cumulative orders. Monitoring begins at enrollment; no strategy attribution.');
                    """, parameters, t);
            else
            {
                int updated = await c.ExecuteAsync("""
                    UPDATE dbo.ActivePosition SET Shares=@Shares,CostBasis=@CostBasis,EntryPrice=@EntryPrice,
                      IsActive=@IsActive,CurrentValue=CASE WHEN @IsActive=1 THEN CurrentPrice*@Shares ELSE 0 END,
                      UnrealizedPnL=CASE WHEN @IsActive=1 THEN CurrentPrice*@Shares-@CostBasis ELSE 0 END,
                      UnrealizedPnLPct=CASE WHEN @IsActive=1 AND @CostBasis>0 THEN (CurrentPrice*@Shares-@CostBasis)/@CostBasis ELSE 0 END,
                      LastUpdatedUtc=SYSUTCDATETIME()
                    WHERE PositionId=@PositionId AND Symbol=@Symbol AND ExecutionMode=N'Real' AND AccountLabel=N'TFSA';
                    """, parameters, t);
                if (updated != 1) throw new InvalidOperationException("The tracked broker position changed during synchronization.");
            }
        }
        foreach (var trade in additions)
            await c.ExecuteAsync("""
                INSERT dbo.TradeLog(TradeId,PositionId,Symbol,TradeType,TradeDate,Shares,Price,Amount,Commission,NetAmount,
                  ExecutionMode,AccountLabel,Reason,RealizedPnL,RealizedPnLPct,HoldingDays,Notes)
                VALUES(@TradeId,@PositionId,@Symbol,@Side,@TradeDate,@Quantity,@Price,@Gross,@Fees,@Net,
                  N'Real',N'TFSA',N'BrokerConfirmedOrder',@RealizedProfit,@RealizedProfitFraction,@HoldingDays,
                  N'Resolved cumulative broker order; trade time is last fill, not individual execution time. Source: BrokerTradeSync.');
                """, BrokerAccountImportRepository.TimestampParameters(new { trade.TradeId, trade.PositionId, trade.Symbol, trade.Side,
                    trade.Quantity, trade.Price, trade.Gross, trade.Fees, trade.Net, trade.RealizedProfit, trade.RealizedProfitFraction,
                    trade.HoldingDays }, ("TradeDate", Core.Trader.PaperTradingMonitor.ToToronto(trade.LastFilledAt.UtcDateTime))), t);
        var after = await BrokerAccountImportRepository.Ledger(c, t);
        if (after.Positions.Where(p => p.IsActive).Any(p => !projection.Positions.Any(x => x.PositionId == p.PositionId && x.Active && x.Quantity == p.Quantity && x.Cost == p.CostBasis)) ||
            projection.Positions.Where(p => p.Active).Any(p => !after.Positions.Any(x => x.PositionId == p.PositionId && x.IsActive && x.Quantity == p.Quantity && x.CostBasis == p.Cost)))
            throw new InvalidOperationException("Tracked holdings do not match the reconciled broker book. Synchronization rolled back.");
        var receipt = new BrokerTradeSyncReceipt(importId, binding.BindingId, prior?.ImportId, baseline, after.Fingerprint, projection);
        await c.ExecuteAsync("""
            INSERT dbo.BrokerTradeSync(ImportId,BindingId,PreviousImportId,ContentHash,ReceiptJson,SavedUtc)
            VALUES(@ImportId,@BindingId,@PreviousImportId,@ContentHash,@ReceiptJson,SYSUTCDATETIME());
            """, new { receipt.ImportId, receipt.BindingId, receipt.PreviousImportId,
                ContentHash = BrokerAccountImport.Hash(receipt), ReceiptJson = Json(receipt) }, t);
    }
}
