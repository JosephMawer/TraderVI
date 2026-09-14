#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

/// <summary>SELECT-only manual TFSA guard. Uses existing pause and account-financial protection.</summary>
public sealed class ManualBuySafetyRepository : IManualBuySafetySource
{
    public async Task<IManualBuySafetyLease> AcquireAsync(CancellationToken ct)
    {
        var connection = await Open(ct);
        SqlTransaction? transaction = null;
        try
        {
            transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            await Fence(connection, transaction, "TFSA", false);
            if (!await Installed(connection, transaction) || !await TradingControlRepository.InstalledAsync(connection, transaction))
                return new Lease(connection, transaction, "Trading controls and account protection are unavailable. Manual buying is blocked.");
            string? reason = null;
            if (await TradingControlRepository.IsPausedAsync(connection, transaction, EngineStrategySettings.Tracked))
                reason = "New buys are paused in Trading or globally. Resume the applicable control before buying.";
            var memory = TradingRecommendationMemory.Reconcile(await Batch(connection, "TFSA", transaction), await Report(connection, "TFSA", transaction));
            if (memory.CapitalReviewRequired) reason = "The TFSA has an active capital-review hold.";
            var today = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
            if (memory.TradingDate == today && memory.DailyLossHold) reason = "The TFSA has an active daily-loss hold.";
            return new Lease(connection, transaction, reason);
        }
        catch
        {
            transaction?.Dispose(); await connection.DisposeAsync(); throw;
        }
    }
    private sealed class Lease(SqlConnection connection, SqlTransaction transaction, string? reason) : IManualBuySafetyLease
    {
        public string? BlockReason => reason;
        public async ValueTask DisposeAsync()
        {
            try { await transaction.RollbackAsync(); }
            finally { await transaction.DisposeAsync(); await connection.DisposeAsync(); }
        }
    }
}
