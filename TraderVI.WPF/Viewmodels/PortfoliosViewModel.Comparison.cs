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
    private readonly DisplayRefreshFlight refreshFlight = new();
    private int selectedView, selectedPeriod = 2;
    private bool includeReferenceAccounts;
    private bool isRefreshing;
    private string primaryStrategyName = "Not loaded", primaryStrategyIdentity = "Refresh to read the daily assignment.";
    private string trackedStrategyName = "Not loaded", comparisonDataStatus = "Saved account history has not been loaded.";
    private string refreshedText = "Not refreshed";
    public ObservableCollection<PortfolioComparisonRow> ComparisonRows { get; } = [];
    public IReadOnlyList<string> Periods { get; } = ["Last 30 days", "Last 90 days", "All saved history"];
    public string TradingAccountLabel { get; set; } = "TFSA";
    public string TradingSourceLabel => $"Main Trading strategy · {TradingAccountLabel}   ";
    public string PortfolioListSummary => $"{ComparisonRows.Count(r => r.IsPaperStrategy)} paper portfolios · " +
        $"{ComparisonRows.Count(r => r.Source.GateProfile == PaperGateProfile.PortfolioRulesOnlyV1)} skip daily gates · " +
        "all in this list";
    public int SelectedView { get => selectedView; set => Set(ref selectedView, value); }
    public int SelectedPeriod { get => selectedPeriod; set { if (Set(ref selectedPeriod, value)) RebuildComparison(); } }
    public bool IncludeReferenceAccounts { get => includeReferenceAccounts; set { if (Set(ref includeReferenceAccounts, value)) RebuildComparison(); } }
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
    public bool CanReview => SelectedPortfolio?.ComparisonPairId is null && SelectedComparison?.IsPaperStrategy == true &&
        (SelectedPortfolio?.SystemPortfolioId.HasValue == true || SelectedPortfolio?.DelphiLivePortfolioId.HasValue == true);
    public bool HasComparisonRows => ComparisonRows.Count > 0;
    public string WindowText { get; private set; } = "No saved closing dates";
    public string ReviewExplanation => SelectedPortfolio?.ComparisonPairId is not null
        ? "This account belongs to a fixed-gate paper comparison. Its paired results remain separate from recommendation approval."
        : !CanReview
        ? "Select a funded paper strategy in Account directory to review it against the current recommendation source. Operator-reported accounts remain reference-only."
        : "This is the selected paper strategy. Its history may span earlier rule assignments. Prepare the complete review to see the exact rules being proposed; the chart alone cannot establish a return advantage for that version.";
    public string PromotionStatus => "Prepare a review to see the complete strategy, exact versions, supporting paper history and account requirements. Performance evidence is advisory; approval is your explicit decision.";
    public string MetricNotes => "Period return uses matching saved closing dates and costs already recorded by each paper engine. Maximum closing decline is the largest observed peak-to-trough fall in this window; it excludes intraday moves. Missing values are not zero. Account history can span earlier rules. These figures do not certify promotion readiness.";
    public string PrimaryPerformanceText => "Version-specific performance is not established by these lifetime account returns. The approved source identifies its supporting paper account; review policy and daily-input histories before comparing.";

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        refreshFlight.RunAsync(() => RefreshOnceAsync(cancellationToken), cancellationToken);

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        using var timing = Core.Runtime.PaperCycleTimings.Start(Core.Runtime.PaperCycleStage.PortfolioDisplay);
        IsRefreshing = true;
        try { await RefreshCoreAsync(cancellationToken); }
        catch
        {
            comparisonDataStatus = "Refresh failed — displayed data may be from the previous refresh. Retry before reviewing.";
            OnPropertyChanged(nameof(ComparisonDataStatus));
            throw;
        }
        finally { IsRefreshing = false; }
    }

    private async Task RefreshComparisonAsync(CancellationToken token)
    {
        closingHistory.Clear();
        dailyHistory.Clear();
        var warnings = new List<string>();
        if (schemaInstalled)
        {
            try
            {
                foreach (Guid generationId in Portfolios.Where(r => r.SystemGenerationId.HasValue).Select(r => r.SystemGenerationId!.Value).Distinct())
                {
                    var history = await Task.Run(() => new PortfolioHistoryReader().ReadShadowDaysAsync(generationId, token), token);
                    foreach (var row in Portfolios.Where(r => r.SystemPortfolioId.HasValue))
                        if (history.TryGetValue(row.SystemPortfolioId!.Value, out var days))
                        {
                            dailyHistory[row.StableCode] = days;
                            closingHistory[row.StableCode] = days.Where(d => d.IsClosing)
                                .Select(d => new PortfolioClosingObservation(d.Date, d.Value)).ToArray();
                        }
                }
            }
            catch (Microsoft.Data.SqlClient.SqlException) { warnings.Add("Daily Shadow closing history unavailable; refresh to retry."); }
        }
        foreach (var account in liveAccounts.Values)
        {
            dailyHistory[$"DelphiLive:{account.Snapshot.PortfolioId:N}"] = PortfolioDailyPerformance.FromLiveMarks(account.Snapshot.Marks);
            closingHistory[$"DelphiLive:{account.Snapshot.PortfolioId:N}"] = account.Snapshot.Marks
                .Where(m => m.Kind == DelphiLivePortfolioMarkKind.Closing)
                .Select(m => new PortfolioClosingObservation(m.TradingDate,
                    m.Complete && m.Reason != "CorporateActionUnsupported" ? m.Nav : null)).ToArray();
        }
        try
        {
            var active = await new TradingPromotionRepository().ReadAssignmentAsync(TradingAccountLabel,token);
            primaryStrategyName = active?.Strategy.Name ?? "No approved Trading strategy";
            primaryStrategyIdentity = active is null ? $"No saved Trading approval was found for {TradingAccountLabel}."
                : $"Complete version {active.Strategy.VersionId:D} · effective {active.EffectiveUtc:yyyy-MM-dd HH:mm:ss} UTC · paper account {active.PaperAccountId:D}";
        }
        catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            primaryStrategyName = "Assignment unavailable";
            primaryStrategyIdentity = "The current Trading approval could not be verified. Refresh to retry.";
            warnings.Add(primaryStrategyIdentity);
        }
        comparisonDataStatus = warnings.Count > 0 ? string.Join(" ", warnings)
            : "Saved paper-account valuations · day gains use session openings; period returns use matching closes";
        refreshedText = $"Display read {PaperTradingMonitor.ToToronto(DateTime.UtcNow):MMM d, HH:mm:ss} Toronto";
        NotifyComparisonHeader();
        RebuildDailyDates();
        RebuildComparison();
    }

    // Presentation-only input for isolated fixture checks. No repository or operational calls.
    public void ApplyComparisonHistory(IReadOnlyDictionary<string, IReadOnlyList<PortfolioClosingObservation>> history,
        string primaryName, string primaryIdentity, string trackedName,
        IReadOnlyDictionary<string, IReadOnlyList<PortfolioDailyObservation>>? days = null)
    {
        closingHistory.Clear();
        dailyHistory.Clear();
        foreach (var item in history) closingHistory[item.Key] = item.Value;
        if (days is not null) foreach (var item in days) dailyHistory[item.Key] = item.Value;
        primaryStrategyName = primaryName; primaryStrategyIdentity = primaryIdentity; trackedStrategyName = trackedName;
        comparisonDataStatus = "Illustrative fixture data · no database or market service used";
        refreshedText = "Fixture preview";
        NotifyComparisonHeader();
        RebuildDailyDates();
        RebuildComparison();
    }

    private void NotifyComparisonHeader()
    {
        foreach (string name in new[] { nameof(TradingSourceLabel), nameof(PrimaryStrategyName), nameof(PrimaryStrategyIdentity), nameof(TrackedStrategyName), nameof(ComparisonDataStatus), nameof(RefreshedText) })
            OnPropertyChanged(name);
    }

    private void RebuildComparison()
    {
        // All paper accounts share one comparison window. Reference visibility cannot change its dates.
        var dates = PortfolioComparison.Window(closingHistory.Values.SelectMany(x => x),
            SelectedPeriod switch { 0 => 30, 1 => 90, _ => (int?)null });
        WindowText = dates.Length == 0 ? "No saved closing dates"
            : $"{dates[0]:dd MMM yyyy} – {dates[^1]:dd MMM yyyy} · {dates.Length} observed dates";
        string? selectedCode = SelectedPortfolio?.StableCode;
        var displayed = Portfolios.Where(r => IncludeReferenceAccounts || r.SystemPortfolioId.HasValue || r.IsDelphiLive);
        void ApplyRows()
        {
            StableDisplayRows.Apply(ComparisonRows, displayed
                .Select(r => new PortfolioComparisonRow(r, PortfolioComparison.Calculate(
                    closingHistory.GetValueOrDefault(r.StableCode) ?? [], dates))
                    { Day = PortfolioDailyPerformance.Calculate(dailyHistory.GetValueOrDefault(r.StableCode) ?? [], SelectedDay) })
                .OrderBy(r => r.StrategyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Source.ComparisonPairId).ThenBy(r => r.Source.GateProfile).ThenBy(r => r.StableCode, StringComparer.Ordinal),
                (left, right) => left.HasSameValues(right));
            SelectedPortfolio = ComparisonRows.FirstOrDefault(r => r.Source.StableCode == selectedCode)?.Source
                ?? ComparisonRows.FirstOrDefault(r => r.Source.SystemPortfolioId.HasValue || r.Source.DelphiLivePortfolioId.HasValue)?.Source
                ?? ComparisonRows.FirstOrDefault()?.Source;
        }
        if (IsRefreshing) ApplyRefreshedSelection(ApplyRows);
        else ApplyRows();
        OnPropertyChanged(nameof(WindowText));
        OnPropertyChanged(nameof(HasComparisonRows));
        OnPropertyChanged(nameof(PortfolioListSummary));
        NotifyComparisonSelection();
    }

    private void NotifyComparisonSelection()
    {
        OnPropertyChanged(nameof(SelectedComparison));
        OnPropertyChanged(nameof(CanReview));
        OnPropertyChanged(nameof(ReviewExplanation));
        NotifyWorkspace();
    }
}

public sealed record PortfolioComparisonRow(PortfolioOverviewRow Source, PortfolioPeriodResult Period)
{
    public bool HasSameValues(PortfolioComparisonRow other) => Source == other.Source && Day == other.Day &&
        Period with { Points = other.Period.Points } == other.Period && Period.Points.SequenceEqual(other.Period.Points);
    public PortfolioDailyResult Day { get; init; } = PortfolioDailyPerformance.Calculate([], null);
    public string DayGainText => Day.Gain.HasValue ? $"{Day.Gain.Value:+#,##0.00;-#,##0.00;0.00} {Source.Currency}" : "—";
    public string DayReturnText => Percent(Day.Return);
    public string DayColor => Day.Gain is > 0m ? "#55CBB3" : Day.Gain is < 0m ? "#F49B97" : "#B4C2D4";
    public string DayStatus => !IsPaperStrategy ? "Reference only" : !Day.Gain.HasValue ? "Unavailable" : Day.IsClosing ? "Close" : "Partial";
    public string DaySummary => $"{DayReturnText} · {DayStatus}";
    public string DayOpeningText => Money(Day.OpeningValue);
    public string DayValueText => Money(Day.Value);
    public string DayEvidence => !IsPaperStrategy ? "Reference account · daily cash-flow history unavailable" : Day.Evidence +
        (Day.AsOfUtc.HasValue ? $" · {TradingAccountOverview.Local(Day.AsOfUtc.Value)}" : "");
    public override string ToString() => DisplayName;
    public string StableCode => Source.StableCode;
    public string DisplayName => Source.DisplayName;
    public string StrategyName => string.IsNullOrWhiteSpace(Source.GateStrategyName) ? Source.StrategyName
        : Source.GateStrategyName.Replace("DelphiLive", "Delphi Live").Replace("Top3", " · 3 positions").Replace("Top5", " · 5 positions");
    public string GateProfileText => Source.GateProfile switch
    {
        PaperGateProfile.NormalV1 => "Normal",
        PaperGateProfile.PortfolioRulesOnlyV1 => "Portfolio rules only",
        _ => IsPaperStrategy ? "Assigned strategy" : "Reference account"
    };
    public string DailyGatesText => Source.GateProfile switch
    {
        PaperGateProfile.NormalV1 => "Applied",
        PaperGateProfile.PortfolioRulesOnlyV1 => "Skipped",
        _ => IsPaperStrategy ? "Per strategy" : "—"
    };
    public string GateProfileColor => Source.GateProfile == PaperGateProfile.PortfolioRulesOnlyV1 ? "#91C6FF" : "#ADBBCC";
    public string GateProfileExplanation => Source.GateProfile switch
    {
        PaperGateProfile.NormalV1 => "Normal: daily entry gates apply. Portfolio entry conditions, data checks and risk limits also apply.",
        PaperGateProfile.PortfolioRulesOnlyV1 => "Portfolio rules only: daily entry gates are skipped. Portfolio entry conditions, data checks and risk limits still apply.",
        _ => IsPaperStrategy ? "Admission follows this account's assigned strategy and current source. Inspect its account activity for the actual admission state." : "Operator-reported reference account; not an independent strategy portfolio."
    };
    public string PairContext => Source.ComparisonPairId is Guid pair ? $"Fixed matched pair · {pair.ToString("N")[..8]}" : "";
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
    public string HistoryScope => Source.ComparisonPairId.HasValue
        ? "Fresh account with fixed paired rules. Compare matching observed dates within its named pair; missing coverage remains unavailable."
        : "Performance belongs to this account's saved history, including prior rule assignments. It is not isolated evidence for the current strategy version.";
    private string Money(decimal? value) => value.HasValue ? $"{value.Value:N2} {Source.Currency}" : "—";
    private static string Percent(decimal? value) => value.HasValue ? value.Value.ToString("+0.00%;-0.00%;0.00%", CultureInfo.CurrentCulture) : "—";
}
