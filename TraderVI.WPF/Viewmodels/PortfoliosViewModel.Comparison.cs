#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class PortfoliosViewModel
{
    private readonly Dictionary<string, IReadOnlyList<PortfolioClosingObservation>> closingHistory = [];
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private int selectedView, selectedPeriod = 2, selectedScope;
    private bool isRefreshing;
    private string primaryStrategyName = "Not loaded", primaryStrategyIdentity = "Refresh to read the daily assignment.";
    private string trackedStrategyName = "Not loaded", comparisonDataStatus = "Saved account history has not been loaded.";
    private string refreshedText = "Not refreshed";
    public ObservableCollection<PortfolioComparisonRow> ComparisonRows { get; } = [];
    public IReadOnlyList<string> Periods { get; } = ["Last 30 days", "Last 90 days", "All saved history"];
    public IReadOnlyList<string> Scopes { get; } = ["Paper strategies", "All accounts"];
    public int SelectedView { get => selectedView; set => Set(ref selectedView, value); }
    public int SelectedPeriod { get => selectedPeriod; set { if (Set(ref selectedPeriod, value)) RebuildComparison(); } }
    public int SelectedScope { get => selectedScope; set { if (Set(ref selectedScope, value)) RebuildComparison(); } }
    public bool IsRefreshing { get => isRefreshing; private set { Set(ref isRefreshing, value); OnPropertyChanged(nameof(CanRefresh)); } }
    public bool CanRefresh => !IsRefreshing;
    public string PrimaryStrategyName => primaryStrategyName;
    public string PrimaryStrategyIdentity => primaryStrategyIdentity;
    public string TrackedStrategyName => trackedStrategyName;
    public string RefreshedText => refreshedText;
    public string ComparisonDataStatus => comparisonDataStatus;
    public PortfolioComparisonRow? SelectedComparison
    {
        get => ComparisonRows.FirstOrDefault(r => r.Source.StableCode == SelectedPortfolio?.StableCode);
        set { if (value is not null) SelectedPortfolio = value.Source; }
    }
    public bool CanReview => SelectedComparison?.IsPaperStrategy == true &&
        (SelectedPortfolio?.SystemPortfolioId.HasValue == true || SelectedPortfolio?.DelphiLivePortfolioId.HasValue == true);
    public bool HasComparisonRows => ComparisonRows.Count > 0;
    public string WindowText { get; private set; } = "No saved closing dates";
    public string ReviewExplanation => !CanReview
        ? "Select a funded paper strategy in Compare to review it against the current recommendation source. Real and operator-selected holdings are reference accounts."
        : "This paper account is the selected challenger. Its history may span earlier rule assignments. A matching paper account for the complete current recommendation strategy is not yet identified, so a return advantage cannot be established.";
    public string PromotionStatus => "Promotion unavailable: a common strategy-to-Trading route is not implemented. Review assignments in Settings; this page does not change the primary strategy.";
    public string MetricNotes => "Period return uses matching saved closing dates and costs already recorded by each paper engine. Maximum closing decline is the largest observed peak-to-trough fall in this window; it excludes intraday moves. Missing values are not zero. Account history can span earlier rules. These figures do not certify promotion readiness.";
    public string PrimaryPerformanceText => "Unavailable — no paper account is verified as the complete current recommendation strategy. Real-account performance is not substituted.";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken);
        IsRefreshing = true;
        try { await RefreshCoreAsync(cancellationToken); }
        catch
        {
            comparisonDataStatus = "Refresh failed — displayed data may be from the previous refresh. Retry before reviewing.";
            OnPropertyChanged(nameof(ComparisonDataStatus));
            throw;
        }
        finally { IsRefreshing = false; refreshGate.Release(); }
    }

    private async Task RefreshComparisonAsync(CancellationToken token)
    {
        closingHistory.Clear();
        var warnings = new List<string>();
        if (generation is not null && schemaInstalled)
        {
            try
            {
                var history = await new PortfolioHistoryReader().ReadShadowAsync(generation.GenerationId, token);
                foreach (var row in Portfolios.Where(r => r.SystemPortfolioId.HasValue))
                    if (history.TryGetValue(row.SystemPortfolioId!.Value, out var points)) closingHistory[row.StableCode] = points;
            }
            catch (Microsoft.Data.SqlClient.SqlException) { warnings.Add("Daily Shadow closing history unavailable; refresh to retry."); }
        }
        foreach (var account in liveAccounts.Values)
            closingHistory[$"DelphiLive:{account.Snapshot.PortfolioId:N}"] = account.Snapshot.Marks
                .Where(m => m.Kind == DelphiLivePortfolioMarkKind.Closing)
                .Select(m => new PortfolioClosingObservation(m.TradingDate,
                    m.Complete && m.Reason != "CorporateActionUnsupported" ? m.Nav : null)).ToArray();
        try
        {
            var active = await new StrategyVersionRepository().GetActiveVersion();
            primaryStrategyName = active?.VersionName ?? "No active daily strategy";
            primaryStrategyIdentity = active is null ? "No saved daily assignment was found."
                : $"Daily models and gates · {active.VersionId:D}";
        }
        catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            primaryStrategyName = "Assignment unavailable";
            primaryStrategyIdentity = "The current daily strategy could not be verified. Refresh to retry.";
            warnings.Add(primaryStrategyIdentity);
        }
        comparisonDataStatus = warnings.Count > 0 ? string.Join(" ", warnings)
            : "Saved paper-account closes · observed dates only · rule histories and fill assumptions can differ";
        refreshedText = $"Read {DateTime.Now:MMM d, HH:mm:ss} local";
        NotifyComparisonHeader();
        RebuildComparison();
    }

    // Presentation-only input for isolated fixture checks. No repository or operational calls.
    public void ApplyComparisonHistory(IReadOnlyDictionary<string, IReadOnlyList<PortfolioClosingObservation>> history,
        string primaryName, string primaryIdentity, string trackedName)
    {
        closingHistory.Clear();
        foreach (var item in history) closingHistory[item.Key] = item.Value;
        primaryStrategyName = primaryName; primaryStrategyIdentity = primaryIdentity; trackedStrategyName = trackedName;
        comparisonDataStatus = "Illustrative fixture data · no database or market service used";
        refreshedText = "Fixture preview";
        NotifyComparisonHeader();
        RebuildComparison();
    }

    private void NotifyComparisonHeader()
    {
        foreach (string name in new[] { nameof(PrimaryStrategyName), nameof(PrimaryStrategyIdentity), nameof(TrackedStrategyName), nameof(ComparisonDataStatus), nameof(RefreshedText) })
            OnPropertyChanged(name);
    }

    private void RebuildComparison()
    {
        // Scope filters visibility, not measurement: dates stay fixed when reference accounts are shown.
        var dates = PortfolioComparison.Window(closingHistory.Values.SelectMany(x => x),
            SelectedPeriod switch { 0 => 30, 1 => 90, _ => (int?)null });
        WindowText = dates.Length == 0 ? "No saved closing dates"
            : $"{dates[0]:dd MMM yyyy} – {dates[^1]:dd MMM yyyy} · {dates.Length} observed dates";
        string? selectedCode = SelectedPortfolio?.StableCode;
        Replace(ComparisonRows, Portfolios.Where(r => SelectedScope == 1 || r.SystemPortfolioId.HasValue || r.IsDelphiLive)
            .Select(r => new PortfolioComparisonRow(r, PortfolioComparison.Calculate(
                closingHistory.GetValueOrDefault(r.StableCode) ?? [], dates))));
        SelectedPortfolio = ComparisonRows.FirstOrDefault(r => r.Source.StableCode == selectedCode)?.Source
            ?? ComparisonRows.FirstOrDefault(r => r.Source.SystemPortfolioId.HasValue || r.Source.DelphiLivePortfolioId.HasValue)?.Source
            ?? ComparisonRows.FirstOrDefault()?.Source;
        OnPropertyChanged(nameof(WindowText));
        OnPropertyChanged(nameof(HasComparisonRows));
        NotifyComparisonSelection();
    }

    private void NotifyComparisonSelection()
    {
        OnPropertyChanged(nameof(SelectedComparison));
        OnPropertyChanged(nameof(CanReview));
        OnPropertyChanged(nameof(ReviewExplanation));
    }
}

public sealed record PortfolioComparisonRow(PortfolioOverviewRow Source, PortfolioPeriodResult Period)
{
    public override string ToString() => DisplayName;
    public string StableCode => Source.StableCode;
    public string DisplayName => Source.DisplayName;
    public string StrategyName => Source.StrategyName;
    public bool IsPaperStrategy => Source.SystemPortfolioId.HasValue || Source.IsDelphiLive;
    public string Mode => Source.IsDelphiLive ? "Paper · intraday" : Source.SystemPortfolioId.HasValue ? "Paper · daily" : Source.Execution;
    public string PeriodReturnText => Percent(Period.Return);
    public string ClosingDrawdownText => Percent(Period.ObservedDrawdown);
    public string Evidence => IsPaperStrategy ? Period.Evidence : "Reference only · no comparable return history";
    public string ValueText => Money(Source.NetAssetValue);
    public string CashText => Money(Source.Cash);
    public string RealizedText => Money(Source.RealizedProfitLoss);
    public string UnrealizedText => Money(Source.UnrealizedProfitLoss);
    public string OpenText => Source.OpenPositions?.ToString(CultureInfo.CurrentCulture) ?? "—";
    public string ReturnColor => Period.Return is > 0m ? "#55CBB3" : Period.Return is < 0m ? "#F49B97" : "#B4C2D4";
    public string AccountIdentity => Source.SystemPortfolioId?.ToString("D") ?? Source.DelphiLivePortfolioId?.ToString("D") ?? Source.StableCode;
    public string ExecutionAssumptions => Source.IsDelphiLive
        ? "Independent intraday paper ledger. Saved bid/ask or explicitly estimated fills; fees follow its assigned policy."
        : Source.SystemPortfolioId.HasValue ? "Independent daily Shadow account. Whole shares; simulated costs follow its assigned strategy."
        : "Operator-reported reference account. No cash-flow-adjusted strategy return is available.";
    public string HistoryScope => "Performance belongs to this account's saved history, including prior rule assignments. It is not isolated evidence for the current strategy version.";
    private string Money(decimal? value) => value.HasValue ? $"{value.Value:N2} {Source.Currency}" : "—";
    private static string Percent(decimal? value) => value.HasValue ? value.Value.ToString("+0.00%;-0.00%;0.00%", CultureInfo.CurrentCulture) : "—";
}
