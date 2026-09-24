#nullable enable
using Core.Db;
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    public event EventHandler? BrokerLedgerChanged;
    public async Task<string> SynchronizeTradesAsync(WealthsimpleTfsaAccount account, long revision, CancellationToken ct)
    {
        if (IsSubmitting) return "Trade synchronization is waiting for the current order or reconciliation.";
        reconciling = true; CommandManager.InvalidateRequerySuggested();
        try
        {
            var repository = new BrokerAccountImportRepository();
            var status = await repository.ReadTradeSyncStatusAsync(ct);
            if (!status.Installed) return "Trade synchronization needs migration 040 and an updated app installation. No trade records changed.";
            var state = await repository.ReadAsync(ct);
            if (state.Binding is null) return "Link this TFSA using Import account history before automatic trade synchronization.";
            async Task<BrokerAccountReconciliation> Read(CancellationToken token)
            {
                var result = await reconciliation.RefreshAsync(account, revision, token);
                if (!connection.IsConnected || connection.SessionRevision != revision)
                    throw new InvalidOperationException("Sign-in changed during trade synchronization. Refresh again.");
                return result;
            }
            var observation = await Read(ct);
            var plan = BrokerAccountImport.Preview(observation, state, DateTimeOffset.UtcNow);
            if (!plan.CanImport) return "Trade synchronization needs attention: " + string.Join(" ", plan.Blocks);
            // Freshly rechecked identical facts need no new SQL snapshot or cumulative trade application.
            if (state.Latest is { } latest && status.LatestImportId == latest.RequestId &&
                BrokerAccountImport.FinancialFingerprint(latest.Plan.Observation) == BrokerAccountImport.FinancialFingerprint(observation))
                return "Broker trades and recorded positions agree. No new completed orders.";
            await new BrokerImportWorkflow(repository).SynchronizeAsync(plan, Guid.NewGuid(), Environment.UserName, ct);
            BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
            return "Broker trades saved; recorded positions and trade history updated.";
        }
        finally { reconciling = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
