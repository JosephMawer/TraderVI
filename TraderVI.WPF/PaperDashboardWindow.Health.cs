#nullable enable
using Core.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF;

public partial class PaperDashboardWindow
{
    private EngineIntegrityStatus? healthIntegrity;
    private DateTimeOffset? healthIntegrityObserved;
    private DateTimeOffset nextHealthRead;
    private bool readingHealth, healthReadFailed;
    private DashboardCollectorOverview? healthCollector;
    private DashboardDailyPrep? healthPrep;
    private DateTimeOffset? healthCollectorRead, healthPrepRead;
    private EngineHealthSnapshot? healthSnapshot;

    private async Task RefreshHealthAsync(bool force = false)
    {
        if (readingHealth || shutdown.IsCancellationRequested || !force && DateTimeOffset.UtcNow < nextHealthRead) return;
        readingHealth = true;
        try
        {
            // Existing read-only RPC. Does not run an audit, recover state or change holds.
            healthIntegrity = await engineControl.CallAsync<EngineIntegrityStatus>(EngineOperation.IntegrityStatus, ct: shutdown.Token);
            healthIntegrityObserved = DateTimeOffset.UtcNow;
            healthReadFailed = false;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch { healthReadFailed = true; }
        finally
        {
            nextHealthRead = DateTimeOffset.UtcNow.AddSeconds(30);
            readingHealth = false;
            if (!shutdown.IsCancellationRequested) RenderHealth();
        }
    }

    private void RenderHealth()
    {
        if (HealthSummaryText == null || manualBuys == null) return;
        var now = DateTimeOffset.UtcNow;
        var signals = new List<EngineHealthIssue>();
        void Add(string id, string category, string domain, string title, string explanation, string impact, string next,
            DateTimeOffset? at, string evidence = "Read-only presentation of the latest available source.") =>
            signals.Add(new(id, id, category, domain, title, explanation, impact, next, evidence, null, at, "Dashboard." + domain));
        if (healthCollector is { } collector && EngineHealthSnapshot.Fresh(healthCollectorRead, now))
        {
            var category = collector.State is "Unavailable" or "Unverified" or "Stale" or "Partial" or "Deadline elapsed" or "Needs attention" or "Stopped" ? "Warning" : "Status";
            Add("collector", category, "Collector", "Collector · " + collector.State, collector.Detail,
                "This describes the collection cycle; it does not establish fresh prices for every stock.",
                category == "Warning" ? "Inspect the collector receipt and its operating logs. Do not infer that restarting the trading engine will repair the collector." : "No intervention is indicated by this cycle status alone.", healthCollectorRead);
        }
        else Add("collector", "Warning", "Collector", "Collector status unverified", "No current local status read is available.", "Collection health cannot be established here.", "Check the configured collector receipt path and process.", healthCollectorRead);
        if (healthPrep is { } prep && EngineHealthSnapshot.Fresh(healthPrepRead, now))
        {
            bool attention = prep.Color is "#FF7D87" || prep.Label.EndsWith("Attention", StringComparison.Ordinal) || prep.Label.EndsWith("Unverified", StringComparison.Ordinal);
            Add("daily-prep", attention ? "Warning" : "Status", "Daily prep", "Daily prep · " + prep.Label, prep.Detail,
                "Preparation is for the displayed trading session. An older run does not qualify a later session.",
                attention ? "Inspect the nightly receipt and flagged stage before deciding whether a rerun or repair is needed." :
                    prep.Label.EndsWith("Ready", StringComparison.Ordinal) ? "Preparation is reported complete for this session. Execution still applies its own checks." :
                    "Pending preparation before the session opens is expected. Check again after the scheduled run.", healthPrepRead);
        }
        else Add("daily-prep", "Warning", "Daily prep", "Daily prep status unverified", "No current local status read is available.", "Session preparation cannot be established here.", "Inspect the nightly receipt and reviewed trading calendar.", healthPrepRead);
        var account = TradingRecommendationsTabView.BrokerAccountDisplay;
        if (!wealthsimple.IsConnected)
            Add("broker-connection", "Warning", "Wealthsimple", "Wealthsimple is not connected", "The dashboard has no verified connected session.",
                "Current holdings and broker facts cannot be verified through this session.", "Open Wealthsimple from the bottom bar and inspect the connection.", null);
        else if (account != null)
        {
            bool warning = account.FooterHoldingsFlag.Length > 0;
            Add("holdings", warning ? "Warning" : "Status", "Wealthsimple", warning ? "Holdings are stale or unverified" : "Connected · holdings snapshot available",
                account.FooterHoldingsDetail, "A connected session alone does not make retained holdings current.",
                warning ? "Trading → Main account → Refresh Wealthsimple. Refresh may synchronize verified broker history into the linked local ledger." : "Use the dated holdings snapshot on Main account; normal freshness checks still apply.", account.Snapshot?.RetrievedAt);
        }
        if (account?.ManagedStrategy is { } strategy && (strategy.CanResumeBuying || strategy.BuyingMode.StartsWith("New buys stopped", StringComparison.Ordinal)))
            Add("buying-mode", "Status", "Real account", "Saved buying mode · new buys stopped", strategy.BuyingMode,
                "Existing exit rules are retained, subject to their independent checks.", "Review the saved buying controls in Trading → Account tools. Resuming is an explicit operator action.", null, strategy.Status);
        healthSnapshot = EngineHealthSnapshot.Build(latestEngineStatus, healthIntegrity, healthIntegrityObserved, healthReadFailed, manualBuys.ExecutionStatus, now, signals);
        HealthSummaryText.Text = healthSnapshot.Summary;
        HealthCoverageText.Text = healthSnapshot.Coverage;
        HealthRestrictions.Items = healthSnapshot.Restrictions;
        HealthWarnings.Items = healthSnapshot.Warnings;
        HealthWaiting.Items = healthSnapshot.Waiting;
        HealthHistory.Items = healthSnapshot.History;
        HealthRestrictionsEmpty.Text = healthSnapshot.Restrictions.Any() ? "" : "No restrictions returned by the available sources. Check verification gaps below before relying on this.";
        HealthWarningsEmpty.Text = healthSnapshot.Warnings.Any() ? "" : "No warnings returned by the available sources.";
        HealthHistoryEmpty.Text = healthSnapshot.History.Count > 0 ? "" : "No history receipts have been loaded.";
    }

    private async void RefreshHealth_Click(object sender, RoutedEventArgs e)
    {
        HealthRefreshButton.IsEnabled = false;
        try { await RefreshEngineStatusAsync(); await RefreshLocalStatusAsync(); await RefreshHealthAsync(force: true); }
        finally { HealthRefreshButton.IsEnabled = true; }
    }
    private void CopyHealth_Click(object sender, RoutedEventArgs e)
    {
        RenderHealth();
        if (healthSnapshot == null) return;
        try { Clipboard.SetText(healthSnapshot.ToDiagnosticJson()); HealthCopyStatus.Text = "Diagnostic snapshot copied. No action was run."; }
        catch { HealthCopyStatus.Text = "Clipboard unavailable. Try copying again."; }
    }
}
