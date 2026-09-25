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
    public async Task<BrokerAccountSynchronization> SynchronizeAccountAsync(WealthsimpleTfsaAccount account, long revision, CancellationToken ct)
    {
        if (IsSubmitting) throw new InvalidOperationException("Finish the current order or reconciliation first.");
        reconciling = true; CommandManager.InvalidateRequerySuggested();
        try
        {
            var repository = new BrokerAccountImportRepository();
            var status = await repository.ReadTradeSyncStatusAsync(ct);
            async Task<BrokerAccountRead> Read(CancellationToken token)
            {
                var result = await reconciliation.ReadAsync(account, revision, token);
                if (!connection.IsConnected || connection.SessionRevision != revision)
                    throw new InvalidOperationException("Sign-in changed during trade synchronization. Refresh again.");
                return result;
            }
            var result = await new BrokerAccountSynchronizationWorkflow(new(repository)).RefreshAsync(Read, ct);
            if (!connection.IsConnected || connection.SessionRevision != revision)
                throw new InvalidOperationException("Sign-in changed during trade synchronization. Refresh again.");
            if (result.Saved is null)
                return result with { TradeSyncStatus = "Trade synchronization needs attention: " + string.Join(" ", result.Blocks) };
            if (!status.Installed)
                return result with { TradeSyncStatus = "Account history saved. Trade recording requires the reviewed BrokerTradeSync schema." };
            BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
            return result with { TradeSyncStatus = "Broker trades and recorded positions synchronized. Repeated order totals are applied only once." };
        }
        finally { reconciling = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
