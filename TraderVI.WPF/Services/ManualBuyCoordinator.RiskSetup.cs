#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TraderVI.WPF.Services;

public static class BrokerRiskSetupCommands
{
    public static RoutedUICommand Open { get; } = new("Account risk setup", "BrokerRiskSetup", typeof(BrokerRiskSetupCommands));
}
public sealed partial class ManualBuyCoordinator
{
    private Window? riskWindow;
    public async Task RiskSetupAsync(Window owner, WealthsimpleTfsaAccount account, Func<bool> currentAccount)
    {
        if (riskWindow is not null) { riskWindow.Activate(); return; }
        if (IsSubmitting || !currentAccount()) return;
        var window = riskWindow = new Window { Owner = owner, Title = "TFSA account risk setup", Width = 780, Height = 620,
            MinWidth = 580, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new Thickness(24) };
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16, Margin = new Thickness(0, 16, 0, 16) };
        body.Children.Add(new TextBlock { Text = "Establish continuous account risk tracking", FontSize = 24, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Setup preserves known references, holds and position protection. A new reference starts at the verified observation; it does not claim a historical opening or lifetime high. Strategy execution needs its separate review.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(summary);
        var refresh = new Button { Content = "Refresh verified account and references", Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(12) };
        var save = new Button { Content = "Save these reviewed initial references", IsEnabled = false, Padding = new Thickness(12) };
        body.Children.Add(refresh); body.Children.Add(save);
        window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        using var lifetime = new CancellationTokenSource();
        bool closed = false, busy = false;
        long revision = connection.SessionRevision;
        var store = new BrokerAccountRiskBaselineRepository();
        var setup = new BrokerAccountRiskBaselineWorkflow(store);
        BrokerRiskBaselineRequest? request = null;
        void Check()
        {
            if (closed || !currentAccount() || !connection.IsConnected || connection.SessionRevision != revision)
                throw new InvalidOperationException("The account or login changed. Close this window and prepare current account setup again.");
        }
        async Task Run(Func<Task> action)
        {
            if (busy || IsSubmitting || closed) return;
            busy = true; reconciling = true; refresh.IsEnabled = save.IsEnabled = false;
            try { Check(); await action(); }
            catch (OperationCanceledException) { if (!closed) summary.Text = "Setup stopped. Reopen to inspect saved references; do not initialize a replacement."; }
            catch (InvalidOperationException error) { if (!closed) summary.Text = error.Message; }
            catch { if (!closed) summary.Text = "Setup could not be verified. Reopen to recover saved references. Existing account history remains authoritative."; }
            finally { busy = false; reconciling = false; if (!closed) { refresh.IsEnabled = true; save.IsEnabled = request is not null; } }
        }
        async Task Refresh()
        {
            request = null; summary.Text = "Refreshing linked broker history and preserved risk state…";
            var imports = new BrokerImportWorkflow(new BrokerAccountImportRepository());
            async Task<BrokerAccountReconciliation> Read(CancellationToken ct) { Check(); return await reconciliation.RefreshAsync(account, revision, ct); }
            var plan = await imports.PreviewAsync(await Read(lifetime.Token), lifetime.Token); Check();
            if (!plan.CanImport || plan.PreviousRequestId is null) { summary.Text = "Complete the existing reviewed account-history link first.\n" + string.Join("\n", plan.Blocks); return; }
            await imports.ApplyAsync(plan, Guid.NewGuid(), Environment.UserName, Read, lifetime.Token); Check();
            var state = await store.ReadAsync(lifetime.Token); Check();
            if (state.RiskHistory.ExistingBaseline is { } existing)
            {
                summary.Text = $"Initial references are already saved (request {existing.BaselineId:D}).\nSaved: {existing.EstablishedAt.LocalDateTime:g}\n" +
                    $"Initial daily reference: {existing.SessionReference.Value:N2} CAD ({existing.SessionReference.Source})\n" +
                    $"Initial drawdown reference: {existing.DrawdownReference.Value:N2} CAD ({existing.DrawdownReference.Source})\n" +
                    "Daily continuation may have newer values. Repeated setup cannot replace these initial references or release holds.";
                return;
            }
            var preview = await setup.PreviewAsync(Guid.NewGuid(), lifetime.Token); Check();
            var baseline = preview.Preparation.Baseline;
            if (baseline is null) { summary.Text = string.Join("\n", preview.Preparation.Blocks); return; }
            summary.Text = $"Verified observation: {baseline.ObservationAt.LocalDateTime:g}\n" +
                $"Daily reference: {baseline.SessionReference.Value:N2} CAD ({baseline.SessionReference.Source})\n" +
                $"Drawdown reference: {baseline.DrawdownReference.Value:N2} CAD ({baseline.DrawdownReference.Source})\n" +
                $"Daily loss hold: {baseline.CarriedMemory.DailyLossHold} · Capital review hold: {baseline.CarriedMemory.CapitalReviewRequired}\n" +
                $"Preserved position-protection records: {baseline.CarriedMemory.Symbols.Count}\n" +
                (preview.Installed ? "Review these facts, then save explicitly." : "Reviewed migration 035 must be installed separately before saving.");
            if (preview.Installed && preview.Preparation.CanEstablish) request = new(baseline, Environment.UserName);
        }
        refresh.Click += async (_, _) => await Run(Refresh);
        save.Click += async (_, _) => await Run(async () =>
        {
            if (request is null) return;
            var saved = await setup.ApplyAsync(request, lifetime.Token); Check();
            request = null; summary.Text = $"Initial risk references saved. Request: {saved.Baseline.BaselineId:D}.\n" +
                "Daily/cash-flow source qualification and explicit strategy activation remain separate. No order was submitted.";
        });
        window.Closed += (_, _) => { closed = true; lifetime.Cancel(); riskWindow = null; };
        window.ShowDialog();
        await Task.CompletedTask;
    }
}
