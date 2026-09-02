#nullable enable

using Core.Db;
using Core.Runtime;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TraderVI.WPF.Viewmodels;

public sealed class DelphiViewModel : INotifyPropertyChanged
{
    private readonly DelphiPublishedRecommendationReader reader = new();
    private readonly DelphiWorkflow workflow = new();
    private bool isRunning;
    private string status = "Loading the latest saved recommendations…";
    private string recommendationDate = "—";
    private string savedAt = "—";
    private string topPick = "—";
    private string snapshotSource = "DETAILS UNAVAILABLE";
    private string recommendationAction = "—";
    private string recommendationMetrics = "—";
    private string recommendationReason = "—";
    private string allocation = "—";
    private string marketRegime = "—";
    private string breadthHeadline = "—";
    private string granvilleHeadline = "—";
    private string sectorHeadline = "—";
    private string tapeHeadline = "—";
    private string climaxHeadline = "—";
    private string overviewWarnings = "No detailed snapshot is available for this recommendation date.";
    private string overviewSummary = "No Delphi summary is available.";
    private string fullReport = "No Delphi report is available.";
    private Brush statusBrush = Brushes.SlateGray;
    private Brush recommendationBrush = Brushes.SlateGray;
    private DateTime? publishedPickDate;
    private DelphiPickRow? selectedPaperPick;
    private string paperShares = "1";
    private string paperFillPrice = "";
    private string selectedExecutionMode = TrackedExecutionMode.Ghost.ToStorageValue();
    private string realAccountLabel = "TFSA";
    private string paperEntryStatus = "Select a saved pick, then choose Ghost or Real tracking.";

    public ObservableCollection<DelphiPickRow> ContinuationPicks { get; } = [];
    public ObservableCollection<DelphiPickRow> BreakoutPicks { get; } = [];
    public ObservableCollection<DelphiSectorRow> Sectors { get; } = [];
    public ObservableCollection<DelphiUsIndexRow> UsIndices { get; } = [];
    public ObservableCollection<DelphiGranvilleRow> GranvilleIndicators { get; } = [];
    public ObservableCollection<DelphiMetricRow> MarketFacts { get; } = [];
    public ObservableCollection<DelphiMetricRow> StrategyThresholds { get; } = [];
    public ObservableCollection<DelphiMetricRow> Models { get; } = [];
    public ObservableCollection<DelphiMetricRow> UniverseDiagnostics { get; } = [];
    public ObservableCollection<DelphiMetricRow> RsObvDiagnostics { get; } = [];
    public ObservableCollection<DelphiSignalRow> BestPickSignals { get; } = [];
    public ObservableCollection<DelphiGateRow> BestPickGates { get; } = [];
    public ObservableCollection<DelphiObvRow> ObvSymbols { get; } = [];

    public bool IsRunning { get => isRunning; private set => Set(ref isRunning, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string RecommendationDate { get => recommendationDate; private set => Set(ref recommendationDate, value); }
    public string SavedAt { get => savedAt; private set => Set(ref savedAt, value); }
    public string TopPick { get => topPick; private set => Set(ref topPick, value); }
    public string SnapshotSource { get => snapshotSource; private set => Set(ref snapshotSource, value); }
    public string RecommendationAction { get => recommendationAction; private set => Set(ref recommendationAction, value); }
    public string RecommendationMetrics { get => recommendationMetrics; private set => Set(ref recommendationMetrics, value); }
    public string RecommendationReason { get => recommendationReason; private set => Set(ref recommendationReason, value); }
    public string Allocation { get => allocation; private set => Set(ref allocation, value); }
    public string MarketRegime { get => marketRegime; private set => Set(ref marketRegime, value); }
    public string BreadthHeadline { get => breadthHeadline; private set => Set(ref breadthHeadline, value); }
    public string GranvilleHeadline { get => granvilleHeadline; private set => Set(ref granvilleHeadline, value); }
    public string SectorHeadline { get => sectorHeadline; private set => Set(ref sectorHeadline, value); }
    public string TapeHeadline { get => tapeHeadline; private set => Set(ref tapeHeadline, value); }
    public string ClimaxHeadline { get => climaxHeadline; private set => Set(ref climaxHeadline, value); }
    public string OverviewWarnings { get => overviewWarnings; private set => Set(ref overviewWarnings, value); }
    public string OverviewSummary { get => overviewSummary; private set => Set(ref overviewSummary, value); }
    public string FullReport { get => fullReport; private set => Set(ref fullReport, value); }
    public Brush StatusBrush { get => statusBrush; private set => Set(ref statusBrush, value); }
    public Brush RecommendationBrush { get => recommendationBrush; private set => Set(ref recommendationBrush, value); }
    public int ContinuationCount => ContinuationPicks.Count;
    public int BreakoutCount => BreakoutPicks.Count;
    public DelphiPickRow? SelectedPaperPick { get => selectedPaperPick; private set => Set(ref selectedPaperPick, value); }
    public string PaperShares { get => paperShares; set => Set(ref paperShares, value); }
    public string PaperFillPrice { get => paperFillPrice; set => Set(ref paperFillPrice, value); }
    public IReadOnlyList<string> ExecutionModeOptions { get; } =
        [TrackedExecutionMode.Ghost.ToStorageValue(), TrackedExecutionMode.Real.ToStorageValue()];
    public string SelectedExecutionMode { get => selectedExecutionMode; set => Set(ref selectedExecutionMode, value); }
    public string RealAccountLabel { get => realAccountLabel; set => Set(ref realAccountLabel, value); }
    public string PaperEntryStatus { get => paperEntryStatus; private set => Set(ref paperEntryStatus, value); }

    public void SelectPaperPick(DelphiPickRow? pick)
    {
        SelectedPaperPick = pick;
        PaperEntryStatus = pick is null
            ? "Select a saved pick, then choose Ghost or Real tracking."
            : $"Selected {pick.Lens} #{pick.Rank}: {pick.Symbol}";
    }

    public bool TryBuildPaperEntry(
        out DelphiPickRow? pick,
        out int shares,
        out decimal fillPrice,
        out TrackedExecutionMode executionMode,
        out string? accountLabel,
        out string error)
    {
        pick = SelectedPaperPick;
        shares = 0;
        fillPrice = 0m;
        executionMode = SelectedExecutionMode == TrackedExecutionMode.Real.ToStorageValue()
            ? TrackedExecutionMode.Real
            : TrackedExecutionMode.Ghost;
        accountLabel = null;
        if (pick is null)
        {
            error = "Select a Continuation or Breakout pick first.";
            return false;
        }
        if (!int.TryParse(PaperShares, NumberStyles.Integer, CultureInfo.CurrentCulture, out shares) || shares <= 0)
        {
            error = "Shares must be a positive whole number.";
            return false;
        }
        bool parsedPrice = decimal.TryParse(
                PaperFillPrice,
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.CurrentCulture,
                out fillPrice) ||
            decimal.TryParse(
                PaperFillPrice,
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.InvariantCulture,
                out fillPrice);
        if (!parsedPrice || fillPrice <= 0m)
        {
            error = "Fill price must be a positive amount.";
            return false;
        }
        try
        {
            accountLabel = TrackedExecutionModeContract.NormalizeAccountLabel(
                executionMode,
                executionMode == TrackedExecutionMode.Real ? RealAccountLabel : null);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
        error = "";
        return true;
    }

    public async Task<PaperTradeEntryResult> OpenPaperPositionAsync(
        DelphiPickRow pick,
        int shares,
        decimal fillPrice,
        TrackedExecutionMode executionMode,
        string? accountLabel)
    {
        PaperEntryStatus = $"Opening {executionMode.ToStorageValue()} position for {pick.Symbol}…";
        try
        {
            PaperTradeEntryResult result = await new PaperTradeEntryWorkflow()
                .OpenAsync(pick.PickId, shares, fillPrice, executionMode, accountLabel);
            PaperEntryStatus = result.Message;
            return result;
        }
        catch (Exception ex)
        {
            PaperEntryStatus = $"Paper entry failed · {ex.Message}";
            throw;
        }
    }

    public bool HasRecommendationsFor(DateTime date) => publishedPickDate == date.Date;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return;

        Status = "Reading the latest saved Delphi session…";
        StatusBrush = Brushes.SlateGray;
        try
        {
            await LoadLatestAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Refresh cancelled";
        }
        catch (Exception ex)
        {
            Status = $"Saved Delphi session unavailable · {ex.GetType().Name}: {ex.Message}";
            StatusBrush = Brushes.IndianRed;
        }
    }

    public async Task RunOfficialAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Status = "Official Delphi evaluation running · please keep TraderVI open…";
        StatusBrush = Brushes.Goldenrod;
        FullReport = "Delphi is evaluating the local market snapshot. Results will appear here when it finishes.";

        using var output = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            DelphiWorkflowRunResult result = await Task.Run(
                () => workflow.RunAsync(new DelphiWorkflowOptions(), output, cancellationToken),
                cancellationToken);

            if (result.Presentation is not null)
                ApplyPresentation(result.Presentation);
            else
                FullReport = result.SummaryReport ?? output.ToString();

            if (result.Succeeded)
            {
                if (result.ContinuationPickCount > 0)
                {
                    await LoadLatestAsync(cancellationToken, updateStatus: false);
                }
                else
                {
                    publishedPickDate = null;
                    ContinuationPicks.Clear();
                    BreakoutPicks.Clear();
                    RecommendationDate = result.RecommendationDate.ToString("MMM d, yyyy");
                    SavedAt = "No picks published";
                    TopPick = "—";
                    NotifyCounts();
                }
                Status = $"Official Delphi run completed · {result.ContinuationPickCount} continuation and {result.BreakoutPickCount} breakout picks";
                StatusBrush = Brushes.MediumSeaGreen;
            }
            else
            {
                Status = $"Delphi did not publish recommendations · {result.Status}";
                StatusBrush = Brushes.Goldenrod;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Delphi run cancelled";
            StatusBrush = Brushes.Goldenrod;
            FullReport = output.ToString();
        }
        catch (Exception ex)
        {
            Status = $"Delphi failed · {ex.GetType().Name}: {ex.Message}";
            StatusBrush = Brushes.IndianRed;
            string diagnostics = output.ToString();
            FullReport = string.IsNullOrWhiteSpace(diagnostics) ? ex.Message : diagnostics;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task LoadLatestAsync(CancellationToken cancellationToken, bool updateStatus = true)
    {
        DelphiPublishedRecommendations? published = await reader.LoadLatestAsync(cancellationToken);
        SelectPaperPick(null);
        ContinuationPicks.Clear();
        BreakoutPicks.Clear();

        if (published is null)
        {
            publishedPickDate = null;
            RecommendationDate = "None saved";
            SavedAt = "—";
            TopPick = "—";
            ApplyPresentation(null);
            NotifyCounts();
            if (updateStatus)
                Status = "No saved Delphi recommendations were found";
            return;
        }

        publishedPickDate = published.PickDate.Date;
        foreach (DailyPickInfo pick in published.Continuation)
            ContinuationPicks.Add(DelphiPickRow.Create(pick));
        foreach (DailyPickInfo pick in published.Breakout)
            BreakoutPicks.Add(DelphiPickRow.Create(pick));

        RecommendationDate = published.PickDate.ToString("MMM d, yyyy");
        DateTime savedUtc = DateTime.SpecifyKind(published.LatestCreatedUtc, DateTimeKind.Utc);
        SavedAt = published.LatestCreatedUtc == DateTime.MinValue
            ? "—"
            : savedUtc.ToLocalTime().ToString("MMM d · HH:mm:ss");
        TopPick = published.Continuation.Count > 0
            ? published.Continuation[0].Symbol
            : published.Breakout.Count > 0 ? published.Breakout[0].Symbol : "—";
        ApplyPresentation(published.Presentation);
        NotifyCounts();

        if (updateStatus)
        {
            Status = published.Presentation?.IsReconstructed == true
                ? "Showing saved picks with a date-aligned legacy evidence reconstruction · Delphi was not run"
                : "Showing the saved Delphi session · Delphi was not run";
            StatusBrush = published.Presentation is null ? Brushes.Goldenrod : Brushes.MediumSeaGreen;
        }
    }

    private void ApplyPresentation(DelphiPresentationSnapshot? snapshot)
    {
        ClearPresentationCollections();
        if (snapshot is null)
        {
            SnapshotSource = "DETAILS UNAVAILABLE";
            RecommendationAction = "—";
            RecommendationMetrics = "—";
            RecommendationReason = "No matching OfficialPaper calibration run was found.";
            Allocation = "—";
            MarketRegime = BreadthHeadline = GranvilleHeadline = SectorHeadline = TapeHeadline = ClimaxHeadline = "—";
            OverviewWarnings = "No detailed snapshot is available for this recommendation date.";
            OverviewSummary = "No Delphi summary is available.";
            FullReport = "No Delphi report is available.";
            RecommendationBrush = Brushes.SlateGray;
            return;
        }

        SnapshotSource = snapshot.IsReconstructed ? "RECONSTRUCTED LEGACY EVIDENCE" : "CAPTURED IMMUTABLE SNAPSHOT";
        DelphiRecommendationPresentation recommendation = snapshot.Recommendation;
        RecommendationAction = recommendation.HasTrade
            ? $"{recommendation.Direction.ToUpperInvariant()} {recommendation.Symbol}"
            : "NO TRADE";
        RecommendationMetrics = recommendation.HasTrade
            ? $"Composite {recommendation.CompositeScore:P1}  ·  Edge {recommendation.DirectionEdge:+0.0%;-0.0%}  ·  Breakout {recommendation.BreakoutProbability:P0}"
            : "No candidate passed the complete decision pipeline";
        RecommendationReason = recommendation.Reason;
        Allocation = recommendation.HasTrade
            ? $"{recommendation.SuggestedSize:C2} · {recommendation.AllocationPercent:P1}"
            : "—";
        RecommendationBrush = recommendation.HasTrade ? Brushes.MediumSeaGreen : Brushes.Goldenrod;

        MarketRegime = snapshot.Regime is null
            ? "Unavailable"
            : $"{snapshot.Regime.Label} · XIU 20d {snapshot.Regime.XiuReturn20d:+0.00%;-0.00%}";
        BreadthHeadline = snapshot.Breadth is null
            ? "Unavailable"
            : $"{snapshot.Breadth.Advancers} up / {snapshot.Breadth.Decliners} down · score {snapshot.Breadth.BreadthScore:+0.00;-0.00}";
        GranvilleHeadline = snapshot.Granville is null
            ? "Unavailable"
            : $"Net {snapshot.Granville.NetPoints:+0;-0;0} · {snapshot.Granville.BullishCount} bull / {snapshot.Granville.BearishCount} bear · adj {snapshot.Granville.CompositeAdjustment:+0.0%;-0.0%;0.0%}";
        int positiveSectors = snapshot.Sectors.Count(sector => sector.PercentChange > 0);
        SectorHeadline = snapshot.Sectors.Count == 0 ? "Unavailable" : $"{positiveSectors} of {snapshot.Sectors.Count} positive";
        TapeHeadline = snapshot.MarketTape?.XiuVolumeRatio20 is decimal ratio
            ? $"XIU {snapshot.MarketTape.XiuReturn1d:+0.00%;-0.00%} · volume {ratio:0.00}×"
            : "Unavailable";
        ClimaxHeadline = snapshot.Climax is null ? "Unavailable" : $"CLX {snapshot.Climax.Clx:+0;-0;0} · {snapshot.Climax.Regime}";

        OverviewWarnings = BuildWarnings(snapshot);
        OverviewSummary = snapshot.SummaryReport;
        FullReport = $"{snapshot.SummaryReport.Trim()}\n\n{snapshot.DiagnosticReport.Trim()}";

        foreach (DelphiSectorPresentation sector in snapshot.Sectors.OrderByDescending(item => item.PercentChange))
            Sectors.Add(DelphiSectorRow.Create(sector));
        foreach (DelphiUsIndexPresentation index in snapshot.UsIndices)
            UsIndices.Add(DelphiUsIndexRow.Create(index));
        if (snapshot.Granville is not null)
            foreach (DelphiGranvilleIndicatorPresentation indicator in snapshot.Granville.Indicators)
                GranvilleIndicators.Add(DelphiGranvilleRow.Create(indicator));

        AddMarketFacts(snapshot);
        AddDiagnostics(snapshot);
    }

    private void AddMarketFacts(DelphiPresentationSnapshot snapshot)
    {
        if (snapshot.Regime is not null)
        {
            MarketFacts.Add(new("XIU MA50 > MA200", YesNo(snapshot.Regime.XiuUptrend)));
            MarketFacts.Add(new("XIU 20-day return", snapshot.Regime.XiuReturn20d.ToString("+0.00%;-0.00%")));
            MarketFacts.Add(new("SPY uptrend", YesNo(snapshot.Regime.SpyUptrend)));
            MarketFacts.Add(new("SPY 20-day positive", YesNo(snapshot.Regime.Spy20dPositive)));
            MarketFacts.Add(new("Volatility normal", YesNo(snapshot.Regime.VolatilityNormal)));
        }
        if (snapshot.Breadth is not null)
        {
            MarketFacts.Add(new("A/D date", snapshot.Breadth.Date.ToString("yyyy-MM-dd")));
            MarketFacts.Add(new("Daily plurality", snapshot.Breadth.DailyPlurality.ToString("+#;-#;0")));
            MarketFacts.Add(new("Cumulative A/D", snapshot.Breadth.CumulativeDifferential.ToString("+#,0;-#,0;0")));
            MarketFacts.Add(new("A/D slope (20d)", snapshot.Breadth.Slope20d.ToString("+0.0;-0.0;0.0")));
            MarketFacts.Add(new("Above A/D SMA50", YesNo(snapshot.Breadth.AboveSma50)));
            MarketFacts.Add(new("Bearish divergence", YesNo(snapshot.Breadth.BearishDivergence)));
        }
        if (snapshot.Granville is not null)
        {
            DelphiGranvillePresentation granville = snapshot.Granville;
            if (granville.LeadershipActiveBreadthRequired > 0)
            {
                MarketFacts.Add(new("Leadership history", $"{granville.LeadershipHistoryDays:N0} stored days"));
                MarketFacts.Add(new("Leadership mover coverage",
                    $"{granville.LeadershipActiveBreadthDays:N0} / {granville.LeadershipActiveBreadthRequired:N0} contiguous"));
                MarketFacts.Add(new("Leadership mover status",
                    granville.LeadershipActiveBreadthDays >= granville.LeadershipActiveBreadthRequired
                        ? "Available"
                        : "N/A — neutral/no-data"));
            }
            else
            {
                MarketFacts.Add(new("Leadership coverage", "Not captured (legacy snapshot)"));
            }
        }
        if (snapshot.MarketTape is not null)
        {
            MarketFacts.Add(new("XIU close / previous", $"{Money(snapshot.MarketTape.XiuClose)} / {Money(snapshot.MarketTape.XiuPreviousClose)}"));
            MarketFacts.Add(new("XIU volume", snapshot.MarketTape.XiuVolume?.ToString("N0") ?? "—"));
            MarketFacts.Add(new("Prior volume SMA20", snapshot.MarketTape.XiuVolumeSma20Prior?.ToString("N0") ?? "—"));
            MarketFacts.Add(new("Light volume", YesNo(snapshot.MarketTape.IsLightVolume)));
        }
        if (snapshot.Climax is not null)
        {
            MarketFacts.Add(new("CLX up / down", $"{snapshot.Climax.UpBreakouts} / {snapshot.Climax.DownBreakouts}"));
            MarketFacts.Add(new("CLX coverage", $"{snapshot.Climax.Covered} / {snapshot.Climax.BasketSize}"));
            MarketFacts.Add(new("CLX verdict", snapshot.Climax.Description));
        }
        if (snapshot.Weighting is not null)
        {
            MarketFacts.Add(new("Weighting coverage", $"{snapshot.Weighting.ConstituentsObserved} / {snapshot.Weighting.ConstituentsRequired}"));
            MarketFacts.Add(new("Weighting XIU return", snapshot.Weighting.XiuReturn.ToString("+0.00%;-0.00%;0.00%")));
            MarketFacts.Add(new("Weighting score B / C", $"{snapshot.Weighting.ScoreB:+0.000;-0.000;0.000} / {snapshot.Weighting.ScoreC:+0.000;-0.000;0.000}"));
            MarketFacts.Add(new("Narrow advance warning", YesNo(snapshot.Weighting.Triggered)));
            MarketFacts.Add(new("Weighting degraded", YesNo(snapshot.Weighting.Degraded)));
            MarketFacts.Add(new("Top contributors", snapshot.Weighting.TopContributors.Count == 0
                ? "—"
                : string.Join(", ", snapshot.Weighting.TopContributors)));
        }
    }

    private void AddDiagnostics(DelphiPresentationSnapshot snapshot)
    {
        DelphiStrategyPresentation strategy = snapshot.Strategy;
        StrategyThresholds.Add(new("Strategy", $"{strategy.VersionName} · {strategy.Description}"));
        StrategyThresholds.Add(new("Minimum composite", strategy.MinimumComposite.ToString("P0")));
        StrategyThresholds.Add(new("Minimum P(up)", strategy.MinimumUpProbability.ToString("P0")));
        StrategyThresholds.Add(new("Minimum breakout", strategy.MinimumBreakoutProbability.ToString("P0")));
        StrategyThresholds.Add(new("Maximum P(down)", strategy.MaximumDownProbability.ToString("P0")));
        StrategyThresholds.Add(new("Minimum edge", strategy.MinimumDirectionEdge.ToString("P0")));
        StrategyThresholds.Add(new("Breadth veto", strategy.BreadthVetoThreshold.ToString("+0.00;-0.00")));
        StrategyThresholds.Add(new("Stop loss", strategy.StopLossPercent.ToString("P0")));
        StrategyThresholds.Add(new("Maximum positions", strategy.MaximumPositions.ToString()));
        foreach (string model in strategy.PatternSignals)
            Models.Add(new("Rule signal", model));
        foreach (string model in strategy.ProfitModels)
            Models.Add(new("ML model", model));

        DelphiUniversePresentation universe = snapshot.Universe;
        UniverseDiagnostics.Add(new("Discovered / evaluated", $"{universe.Discovered:N0} / {universe.Loaded:N0}"));
        UniverseDiagnostics.Add(new("Skipped history", universe.SkippedHistory.ToString("N0")));
        UniverseDiagnostics.Add(new("Skipped stale", universe.SkippedStaleHistory.ToString("N0")));
        UniverseDiagnostics.Add(new("Skipped price ceiling", universe.SkippedPriceCeiling.ToString("N0")));
        UniverseDiagnostics.Add(new("Skipped price floor", universe.SkippedPriceFloor.ToString("N0")));
        UniverseDiagnostics.Add(new("Skipped low volume", universe.SkippedLowVolume.ToString("N0")));
        UniverseDiagnostics.Add(new("Skipped lev/inv ETP", universe.SkippedLeveragedEtp.ToString("N0")));
        UniverseDiagnostics.Add(new("Liquidity floor", $"{universe.MinimumPrice:C2} · {universe.MinimumVolume20d:N0} shares"));

        DelphiRelativeStrengthPresentation rs = snapshot.RelativeStrength;
        RsObvDiagnostics.Add(new("RS computed", rs.Computed.ToString("N0")));
        RsObvDiagnostics.Add(new("Raw sector rows min / max", $"{NullableNumber(rs.SectorBarsMinimum)} / {NullableNumber(rs.SectorBarsMaximum)}"));
        RsObvDiagnostics.Add(new("RS bars required", NullableNumber(rs.BarsRequired)));
        RsObvDiagnostics.Add(new("Full canonical coverage", $"{NullableNumber(rs.FullCoverageCount)} / {rs.Computed:N0}"));
        RsObvDiagnostics.Add(new("Fallback to XIU", NullableNumber(rs.FallbackToXiu)));
        RsObvDiagnostics.Add(new("Date-alignment gaps", NullableNumber(rs.AlignmentGapCount)));
        RsObvDiagnostics.Add(new("Gap symbols", StableSymbols(rs.AlignmentGapSymbols)));
        RsObvDiagnostics.Add(new("Composite null", NullableNumber(rs.CompositeNull)));
        DelphiObvPresentation obv = snapshot.Obv;
        RsObvDiagnostics.Add(new("OBV rising / falling", $"{NullableNumber(obv.Rising)} / {NullableNumber(obv.Falling)}"));
        RsObvDiagnostics.Add(new("OBV doubtful / unknown", $"{NullableNumber(obv.Doubtful)} / {NullableNumber(obv.Indeterminate)}"));
        RsObvDiagnostics.Add(new("OBV window / tilt", $"{NullableNumber(obv.BreakoutWindow)} / {(obv.SignalWeight.HasValue ? obv.SignalWeight.Value.ToString("0.00") : "—")}"));

        foreach (DelphiObvSymbolPresentation item in obv.PublishedSymbols)
            ObvSymbols.Add(DelphiObvRow.Create(item));
        foreach (DelphiSignalPresentation signal in snapshot.BestPickSignals)
            BestPickSignals.Add(DelphiSignalRow.Create(signal));
        foreach (DelphiGatePresentation gate in snapshot.BestPickGates)
            BestPickGates.Add(DelphiGateRow.Create(gate));
    }

    private static string BuildWarnings(DelphiPresentationSnapshot snapshot)
    {
        List<string> warnings = [];
        if (snapshot.IsReconstructed)
            warnings.Add("This is a date-aligned reconstruction of a run that predates the complete presentation snapshot.");
        if (snapshot.Regime?.BothBearish == true)
            warnings.Add("Both XIU and SPY regime evidence is bearish.");
        if (snapshot.Breadth?.BearishDivergence == true)
            warnings.Add("A/D breadth shows bearish divergence from the benchmark.");
        if (snapshot.Granville is { NetPoints: < 0 })
            warnings.Add($"Granville is net bearish ({snapshot.Granville.NetPoints} points).");
        if (snapshot.Granville is { LeadershipActiveBreadthRequired: <= 0 })
        {
            warnings.Add("Leadership mover coverage was not captured; mover-dependent Granville evidence cannot be verified.");
        }
        else if (snapshot.Granville is { } granville
                 && granville.LeadershipActiveBreadthDays < granville.LeadershipActiveBreadthRequired)
        {
            warnings.Add(
                $"Leadership movers are unavailable ({granville.LeadershipActiveBreadthDays}/{granville.LeadershipActiveBreadthRequired} contiguous observations); mover-dependent Granville evidence is neutral/no-data.");
        }
        if (snapshot.Weighting?.Triggered == true)
            warnings.Add("Granville weighting flags a narrow market advance.");
        if (snapshot.Weighting?.Degraded == true)
            warnings.Add("Granville weighting coverage was degraded for this run.");
        if (snapshot.Universe.SkippedStaleHistory > 0)
            warnings.Add($"{snapshot.Universe.SkippedStaleHistory} symbol(s) failed the strict freshness rule.");
        if (snapshot.RelativeStrength.FallbackToXiu is > 0)
            warnings.Add($"{snapshot.RelativeStrength.FallbackToXiu} relative-strength rows fell back to XIU.");
        if (snapshot.RelativeStrength.AlignmentGapCount is > 0)
        {
            string symbols = StableSymbols(snapshot.RelativeStrength.AlignmentGapSymbols);
            string symbolDetail = symbols == "—" ? string.Empty : $" ({symbols})";
            warnings.Add($"{snapshot.RelativeStrength.AlignmentGapCount} relative-strength row(s) had date-alignment gaps{symbolDetail}; coverage is degraded and metrics requiring those sessions are unavailable.");
        }
        if (snapshot.RelativeStrength.CompositeNull is > 0)
            warnings.Add($"{snapshot.RelativeStrength.CompositeNull} relative-strength composites were unavailable.");
        return warnings.Count == 0
            ? "No major warning was emitted by the captured Delphi evidence."
            : string.Join(Environment.NewLine, warnings.Select(warning => $"• {warning}"));
    }

    private void ClearPresentationCollections()
    {
        Sectors.Clear(); UsIndices.Clear(); GranvilleIndicators.Clear(); MarketFacts.Clear();
        StrategyThresholds.Clear(); Models.Clear(); UniverseDiagnostics.Clear(); RsObvDiagnostics.Clear();
        BestPickSignals.Clear(); BestPickGates.Clear(); ObvSymbols.Clear();
    }

    private void NotifyCounts()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContinuationCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BreakoutCount)));
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string Money(decimal? value) => value?.ToString("C2") ?? "—";
    private static string NullableNumber(int? value) => value?.ToString("N0") ?? "—";
    private static string StableSymbols(IReadOnlyList<string>? symbols) =>
        symbols is { Count: > 0 }
            ? string.Join(", ", symbols.OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase))
            : "—";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record DelphiPickRow(
    Guid PickId, DateTime PickDate, string Lens,
    int Rank, string Symbol, string Direction, string Composite, string UpProbability,
    string BreakoutProbability, string VolumeExpansion, string ExpectedReturn,
    string SuggestedSize, string Notes)
{
    public static DelphiPickRow Create(DailyPickInfo pick) => new(
        pick.PickId, pick.PickDate, pick.Lens,
        pick.Rank, pick.Symbol, pick.Direction, pick.CompositeScore.ToString("P1"),
        FormatPercent(pick.DirectionProb), FormatPercent(pick.BreakoutProb),
        FormatPercent(pick.VolExpansionProb), FormatPercent(pick.ExpectedReturn),
        pick.SuggestedSize?.ToString("C0") ?? "—", pick.Notes ?? "");

    private static string FormatPercent(double? value) => value?.ToString("P1") ?? "—";
}

public sealed record DelphiSectorRow(
    string Sector, string Symbol, string Price, string Change, string PercentChange, Brush ChangeBrush)
{
    public static DelphiSectorRow Create(DelphiSectorPresentation item) => new(
        item.SectorName, item.Symbol, item.Price.ToString("N2"),
        item.PriceChange.ToString("+0.00;-0.00;0.00"),
        item.PercentChange.ToString("+0.00;-0.00;0.00") + "%",
        DelphiRowBrushes.Delta((double)item.PercentChange));
}

public sealed record DelphiUsIndexRow(
    string Symbol, string Date, string Close, string Return1d, string Return5d, Brush ReturnBrush)
{
    public static DelphiUsIndexRow Create(DelphiUsIndexPresentation item) => new(
        item.Symbol, item.Date.ToString("yyyy-MM-dd"), item.Close.ToString("N2"),
        item.Return1d.ToString("+0.00%;-0.00%;0.00%"),
        item.Return5d.ToString("+0.00%;-0.00%;0.00%"), DelphiRowBrushes.Delta(item.Return1d));
}

public sealed record DelphiGranvilleRow(
    string Number, string Category, string Name, string Signal, string Points, string Description, Brush SignalBrush)
{
    public static DelphiGranvilleRow Create(DelphiGranvilleIndicatorPresentation item) => new(
        item.IndicatorNumber.ToString("D2"), item.Category, item.Name, item.Signal,
        item.Points.ToString("+0;-0;0"), item.Description,
        item.Signal.Contains("Bullish", StringComparison.OrdinalIgnoreCase)
            ? Brushes.MediumSeaGreen
            : item.Signal.Contains("Bearish", StringComparison.OrdinalIgnoreCase)
                ? Brushes.IndianRed : Brushes.SlateGray);
}

public sealed record DelphiMetricRow(string Label, string Value);

public sealed record DelphiSignalRow(string Name, string Score, string Hint, string Notes, Brush HintBrush)
{
    public static DelphiSignalRow Create(DelphiSignalPresentation item) => new(
        item.Name, item.Score.ToString("0.000"), item.Hint, item.Notes,
        item.Hint == "Buy" ? Brushes.MediumSeaGreen : item.Hint == "Sell" ? Brushes.IndianRed : Brushes.SlateGray);
}

public sealed record DelphiGateRow(string Name, string Result, string Reason, Brush ResultBrush)
{
    public static DelphiGateRow Create(DelphiGatePresentation item) => new(
        item.Name, item.Passed ? "PASS" : "BLOCK", item.Reason,
        item.Passed ? Brushes.MediumSeaGreen : Brushes.IndianRed);
}

public sealed record DelphiObvRow(string Symbol, string Trend, string Designation, string AsOf, string Pivots, string Tilt)
{
    public static DelphiObvRow Create(DelphiObvSymbolPresentation item) => new(
        item.Symbol, item.Trend, item.Designation, item.AsOf.ToString("yyyy-MM-dd"),
        item.PivotCount.ToString(), item.Tilt.ToString("+0.00;-0.00;0.00"));
}

internal static class DelphiRowBrushes
{
    public static Brush Delta(double value) =>
        value > 0 ? Brushes.MediumSeaGreen : value < 0 ? Brushes.IndianRed : Brushes.SlateGray;
}
