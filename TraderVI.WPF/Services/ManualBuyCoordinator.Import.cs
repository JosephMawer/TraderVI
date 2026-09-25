#nullable enable
using Core.Db;
using Core.WSTrade.Accounts;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TraderVI.WPF.Views;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    private BrokerImportWindow? importWindow;
    public async Task ImportHistoryAsync(Window owner, WealthsimpleTfsaAccount account, Func<bool> isCurrentAccount)
    {
        if (importWindow is not null) { importWindow.Activate(); return; }
        if (IsSubmitting || !isCurrentAccount()) return;
        var dialog = importWindow = new BrokerImportWindow(owner, account.DisplayName);
        var imports = new BrokerImportWorkflow(new BrokerAccountImportRepository());
        var cancellation = new CancellationTokenSource();
        var revision = connection.SessionRevision;
        BrokerImportPlan? reviewed = null;
        Guid requestId = Guid.NewGuid();
        bool closed = false, working = false;
        void CheckContext()
        {
            if (!connection.IsConnected || connection.SessionRevision != revision || !isCurrentAccount())
                throw new InvalidOperationException("The selected account or login changed. Close this window and open the current account's import again.");
        }
        async Task<BrokerAccountReconciliation> Read(CancellationToken ct)
        {
            CheckContext(); var result = await reconciliation.RefreshAsync(account, revision, ct); CheckContext(); return result;
        }
        async Task Run(Func<Task> action)
        {
            if (working || IsSubmitting || closed) return;
            working = true; reconciling = true; dialog.SetBusy(true, "Checking broker and saved account history…"); CommandManager.InvalidateRequerySuggested();
            try { CheckContext(); await action(); }
            catch (OperationCanceledException) { if (!closed) dialog.Failed("Import stopped. Refresh to inspect saved history before retrying."); }
            catch (InvalidOperationException error) { if (!closed) dialog.Failed(error.Message); }
            catch { if (!closed) dialog.Failed("The import could not complete. Refresh to inspect saved history before retrying. No broker orders were submitted."); }
            finally
            {
                working = false; reconciling = false;
                if (!closed) dialog.SetBusy(false); else cancellation.Dispose();
                CommandManager.InvalidateRequerySuggested();
            }
        }
        async Task Refresh()
        {
            reviewed = null; requestId = Guid.NewGuid();
            var plan = await imports.PreviewAsync(await Read(cancellation.Token), cancellation.Token); CheckContext();
            if (plan.PreviousRequestId.HasValue && plan.CanImport)
            {
                var saved = await imports.ApplyAsync(plan, requestId, Environment.UserName, Read, cancellation.Token);
                BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
                if (!closed) dialog.Present(saved.Plan, saved);
            }
            else { reviewed = plan; if (!closed) dialog.Present(plan); }
        }
        dialog.RefreshRequested += async (_, _) => await Run(Refresh);
        dialog.ApplyRequested += async (_, _) => await Run(async () =>
        {
            if (reviewed is null) return;
            var saved = await imports.ApplyAsync(reviewed, requestId, Environment.UserName, Read, cancellation.Token);
            BrokerLedgerChanged?.Invoke(this, EventArgs.Empty);
            reviewed = null; if (!closed) dialog.Present(saved.Plan, saved);
        });
        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (connection.SessionRevision != revision || !connection.IsConnected)
            { cancellation.Cancel(); if (!closed) dialog.Failed("Login changed. Close this window and open the current account's import again."); }
        }
        connection.PropertyChanged += Changed;
        dialog.Closed += (_, _) =>
        {
            closed = true; cancellation.Cancel(); connection.PropertyChanged -= Changed;
            if (!working) cancellation.Dispose(); importWindow = null;
        };
        dialog.Show(); await Run(Refresh);
    }
}
