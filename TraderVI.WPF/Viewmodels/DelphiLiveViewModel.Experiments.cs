#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class DelphiLiveViewModel
{
    private DelphiLiveExperimentState? experiment;
    private DelphiLivePromotionScore? promotionScore;
    private DelphiLivePolicyDefinition? championPolicy;
    private string phaseText = "Engineering shakedown has not started";
    private string experimentText = "No persisted experiment protocol";
    private string promotionText = "No untouched promotion score";
    private string researchText = "No persisted research coverage";
    private string experimentCapital = "";
    private string experimentCurrency = "CAD";
    private string operatorReason = "";
    private string corporateSymbol = "";
    private string corporateFrom = "";
    private string corporateThrough = "";
    private DelphiLiveFamilyChoice selectedFamily = FamilyChoices[0];
    private DelphiLiveChallengerChoice? selectedChallenger;
    private DelphiLivePortfolioPerformanceSummary? selectedPerformance;
    private DelphiLiveDiagnosticScorecard? selectedDiagnostic;
    private string diagnosticCoverageText = "Select a persisted signal diagnostic to inspect its coverage and forward outcomes.";
    private bool rebuildingChoices;
    private bool loadingResearch;
    private string researchFrom = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Toronto")).ToString("yyyy-MM-dd");
    private string researchThrough = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Toronto")).ToString("yyyy-MM-dd");
    private string researchPeriodText = "Choose an inclusive date range (yyyy-MM-dd). Reports use saved evidence and load only on request.";

    public static IReadOnlyList<DelphiLiveFamilyChoice> FamilyChoices { get; } = new[]
    {
        new DelphiLiveFamilyChoice(DelphiLiveHypothesisFamily.RawMoveThreshold, "Raw move threshold", "Move in volatility-range units; 0.15, 0.25 or 0.35."),
        new DelphiLiveFamilyChoice(DelphiLiveHypothesisFamily.RelativeDeadband, "XIU relative deadband", "Stock minus XIU move in range units; 0.025, 0.05 or 0.10."),
        new DelphiLiveFamilyChoice(DelphiLiveHypothesisFamily.VolatilityRuler, "Volatility ruler", "Completed sessions used by the volatility ruler; 10 or 14.")
    };
    public ObservableCollection<DelphiLiveVariantChoice> VariantChoices { get; } = [];
    public ObservableCollection<DelphiLiveChallengerChoice> Challengers { get; } = [];
    public ObservableCollection<DelphiLiveCohortRow> Cohorts { get; } = [];
    public ObservableCollection<DelphiLiveResearchMetricRow> ResearchMetrics { get; } = [];
    public ObservableCollection<DelphiLiveRankingMetricRow> RankingMetrics { get; } = [];
    public ObservableCollection<DelphiLiveFillDiagnostic> FillDiagnostics { get; } = [];
    public ObservableCollection<DelphiLivePortfolioPerformanceSummary> PortfolioPerformance { get; } = [];
    public ObservableCollection<DelphiLiveDiagnosticScorecard> DiagnosticScorecards { get; } = [];
    public ObservableCollection<DelphiLiveDiagnosticMetricRow> DiagnosticForwardMetrics { get; } = [];
    public string DiagnosticCoverageText { get => diagnosticCoverageText; private set => Set(ref diagnosticCoverageText, value); }
    public DelphiLiveDiagnosticScorecard? SelectedDiagnostic
    {
        get => selectedDiagnostic;
        set
        {
            if (!Set(ref selectedDiagnostic, value)) return;
            Replace(DiagnosticForwardMetrics, value?.ForwardMetrics.Select(m =>
                new DelphiLiveDiagnosticMetricRow(m.Metric, m.EqualCohortMean, CoverageText(m.Coverage))) ?? []);
            DiagnosticCoverageText = value is null ? "Select a persisted signal diagnostic to inspect its coverage and forward outcomes." :
                $"Signal coverage: {CoverageText(value.SignalCoverage)} · confirmed entry absent {value.ConfirmedEntryAbsentCount} · " +
                $"missing observation changed the market judgment {value.MissingObservationChangedMarketJudgmentCount}";
            if (value is not null) SelectedDossier = FormatJson(DelphiLiveLedgerJson.Serialize(value));
        }
    }
    public DelphiLivePortfolioPerformanceSummary? SelectedPerformance
    {
        get => selectedPerformance;
        set { if (Set(ref selectedPerformance, value) && value is not null) SelectedDossier = FormatJson(DelphiLiveLedgerJson.Serialize(value)); }
    }
    public string PhaseText { get => phaseText; private set => Set(ref phaseText, value); }
    public string ExperimentText { get => experimentText; private set => Set(ref experimentText, value); }
    public string PromotionText { get => promotionText; private set => Set(ref promotionText, value); }
    public string ResearchText { get => researchText; private set => Set(ref researchText, value); }
    public string ResearchFrom { get => researchFrom; set { Set(ref researchFrom, value); OnPropertyChanged(nameof(CanLoadResearch)); } }
    public string ResearchThrough { get => researchThrough; set { Set(ref researchThrough, value); OnPropertyChanged(nameof(CanLoadResearch)); } }
    public string ResearchPeriodText { get => researchPeriodText; private set => Set(ref researchPeriodText, value); }
    public bool CanLoadResearch => schemaInstalled && !loadingResearch && TryDate(ResearchFrom, out var from) &&
        TryDate(ResearchThrough, out var through) && from <= through;
    public string OperatorReason { get => operatorReason; set { Set(ref operatorReason, value); RefreshCommandAvailability(); } }
    public string ExperimentCapital { get => experimentCapital; set { Set(ref experimentCapital, value); RefreshCommandAvailability(); } }
    public string ExperimentCurrency { get => experimentCurrency; set { Set(ref experimentCurrency, value); RefreshCommandAvailability(); } }
    public string CorporateSymbol { get => corporateSymbol; set { Set(ref corporateSymbol, value); RefreshCommandAvailability(); } }
    public string CorporateFrom { get => corporateFrom; set { Set(ref corporateFrom, value); RefreshCommandAvailability(); } }
    public string CorporateThrough { get => corporateThrough; set { Set(ref corporateThrough, value); RefreshCommandAvailability(); } }
    public DelphiLiveFamilyChoice SelectedFamily
    {
        get => selectedFamily;
        set { if (value is not null && Set(ref selectedFamily, value)) { RefreshVariantChoices(); OnPropertyChanged(nameof(FamilyHelp)); } }
    }
    public string FamilyHelp => SelectedFamily.Help;
    public DelphiLiveChallengerChoice? SelectedChallenger
    {
        get => selectedChallenger;
        set { Set(ref selectedChallenger, value); RefreshCommandAvailability(); }
    }
    private bool CanOperate => schemaInstalled && service.CalendarAvailable && !busy && !string.IsNullOrWhiteSpace(OperatorReason);
    public bool CanScheduleDiscovery => CanOperate && championPolicy is not null && experiment is { PendingBoundary: null } &&
        experiment.Phase is DelphiLiveExperimentPhase.EngineeringShakedown or DelphiLiveExperimentPhase.Completed or DelphiLiveExperimentPhase.Invalidated &&
        experiment.EngineeringCohorts.Count(c => c.IsClean) >= 10 && VariantChoices.Count(c => c.IsSelected) is >= 1 and <= 2 &&
        TryAmount(ExperimentCapital, out _) && IsCurrency(ExperimentCurrency);
    public bool CanScheduleUntouched => CanOperate && experiment is { Phase: DelphiLiveExperimentPhase.Discovery, PendingBoundary: null, Definition: not null } &&
        SelectedChallenger is not null && experiment.Definition.ChallengerPolicyVersionIds.All(id =>
            experiment.DiscoveryCohorts.Count(c => DelphiLiveExperimentPolicy.IsPaired(c, experiment.ChampionPolicyVersionId, id)) >= 30);
    public bool CanApprovePromotion => CanOperate && experiment is { Phase: DelphiLiveExperimentPhase.UntouchedConfirmation, PendingBoundary: null } &&
        promotionScore?.EligibleForHumanReview == true;
    public bool CanRecordMeasurementDefect => CanOperate && experiment is not null;
    public bool CanResumeCapitalReview => CanOperate && portfolios.FirstOrDefault(p => p.PortfolioId == SelectedPortfolio?.PortfolioId) is { } selected &&
        selected.Guards.CapitalReviewRequired && selected.Marks.LastOrDefault() is { Complete: true, Nav: not null };
    public bool CanRecordCorporateAction => CanOperate && !string.IsNullOrWhiteSpace(CorporateSymbol) && CorporateSymbol.Trim().Length <= 20 &&
        TryDate(CorporateFrom, out var from) && TryDate(CorporateThrough, out var through) && from <= through;
    public string DiscoveryReview => $"Schedule {SelectedFamily.Label}: {string.Join(", ", VariantChoices.Where(c => c.IsSelected).Select(c => c.Label))}?\n\n" +
        $"Each comparison portfolio starts with {ExperimentCapital} {ExperimentCurrency.Trim().ToUpperInvariant()} at the next regular session. " +
        "The current champion continues operating. Comparison portfolios have independent cash, positions and fills.\n\n" +
        $"Reason: {OperatorReason.Trim()}";

    public Task ScheduleDiscoveryAsync(CancellationToken ct = default) => RunOperatorAsync(CanScheduleDiscovery,
        token => service.ScheduleDiscoveryAsync(SelectedFamily.Family, VariantChoices.Where(c => c.IsSelected).Select(c => c.Value).ToArray(),
            decimal.Parse(ExperimentCapital, CultureInfo.CurrentCulture), ExperimentCurrency.Trim().ToUpperInvariant(), OperatorReason.Trim(), token), ct);
    public Task ScheduleUntouchedAsync(CancellationToken ct = default) => RunOperatorAsync(CanScheduleUntouched,
        token => service.ScheduleUntouchedAsync(SelectedChallenger!.PolicyId, OperatorReason.Trim(), token), ct);
    public Task ApprovePromotionAsync(CancellationToken ct = default) => RunOperatorAsync(CanApprovePromotion,
        token => service.ApprovePromotionAsync(OperatorReason.Trim(), token), ct);
    public Task RecordMeasurementDefectAsync(CancellationToken ct = default) => RunOperatorAsync(CanRecordMeasurementDefect,
        token => service.RecordMeasurementDefectAsync(OperatorReason.Trim(), token), ct);
    public Task ResumeCapitalReviewAsync(CancellationToken ct = default) => RunOperatorAsync(CanResumeCapitalReview,
        token => service.ResumeCapitalReviewAsync(SelectedPortfolio!.PortfolioId, OperatorReason.Trim(), token), ct);
    public Task RecordCorporateActionAsync(CancellationToken ct = default) => RunOperatorAsync(CanRecordCorporateAction,
        token => service.RecordCorporateActionAsync(CorporateSymbol.Trim().ToUpperInvariant(),
            DateOnly.ParseExact(CorporateFrom.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(CorporateThrough.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture), OperatorReason.Trim(), token), ct);
    public void ShowExperimentEvidence() => SelectedDossier = FormatJson(DelphiLiveLedgerJson.Serialize(new { experiment, promotionScore }));
    public void ShowResearchEvidence() => SelectedDossier = researchEvidence;
    private string researchEvidence = "No persisted research evidence";

    public async Task LoadResearchAsync(CancellationToken ct = default)
    {
        if (!CanLoadResearch) return;
        loadingResearch = true;
        OnPropertyChanged(nameof(CanLoadResearch));
        DateOnly from = DateOnly.ParseExact(ResearchFrom.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly through = DateOnly.ParseExact(ResearchThrough.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        ResearchPeriodText = $"Loading saved evidence for {from:yyyy-MM-dd} through {through:yyyy-MM-dd}…";
        try
        {
            // The report has its own busy flag; market monitoring keeps its schedule.
            ApplyResearch(await service.LoadResearchAsync(from, through, ct));
            ResearchPeriodText = $"Saved evidence from {from:yyyy-MM-dd} through {through:yyyy-MM-dd} · read at {TorontoTime(DateTime.UtcNow)} Toronto";
        }
        catch
        {
            ResearchPeriodText = $"The report for {from:yyyy-MM-dd} through {through:yyyy-MM-dd} could not be loaded. Any displayed rows are the previously saved report.";
            throw;
        }
        finally { loadingResearch = false; OnPropertyChanged(nameof(CanLoadResearch)); }
    }

    private async Task RunOperatorAsync(bool allowed, Func<CancellationToken, Task> command, CancellationToken ct)
    {
        if (!allowed) throw new InvalidOperationException("The selected command does not yet meet its persisted review requirements.");
        if (!gate.Wait(0)) return;
        SetBusy(true);
        try
        {
            await command(ct);
            Apply(await service.SnapshotAsync(ct));
            OperatorReason = "";
        }
        finally { SetBusy(false); gate.Release(); }
    }

    private void ApplyProtocol(DelphiLiveRuntimeSnapshot snapshot)
    {
        experiment = snapshot.Experiment;
        promotionScore = snapshot.PromotionScore;
        bool changed = championPolicy?.PolicyVersionId != snapshot.ChampionPolicy?.PolicyVersionId;
        championPolicy = snapshot.ChampionPolicy;
        if (changed) RefreshVariantChoices();
        int discoveryPairs = experiment?.Definition is { } definition ? definition.ChallengerPolicyVersionIds
            .Select(id => experiment.DiscoveryCohorts.Count(c => DelphiLiveExperimentPolicy.IsPaired(c, definition.ChampionPolicyVersionId, id))).DefaultIfEmpty(0).Min() : 0;
        Guid? measuredContender = experiment?.SelectedChallenger ??
            (experiment?.Definition?.ChallengerPolicyVersionIds.Contains(experiment.ChampionPolicyVersionId) == true ? experiment.ChampionPolicyVersionId : null);
        int untouchedPairs = measuredContender is Guid contender && experiment?.Definition is { } compared ? experiment.UntouchedCohorts.Count(c =>
            DelphiLiveExperimentPolicy.IsPaired(c, compared.ChampionPolicyVersionId, contender)) : 0;
        PhaseText = experiment is null ? "Engineering shakedown has not started" :
            $"{experiment.Phase} · clean engineering {experiment.EngineeringCohorts.Count(c => c.IsClean)}/10 · " +
            $"minimum paired discovery {discoveryPairs}/30 · paired untouched {untouchedPairs}/30 · " +
            $"clean baseline {experiment.BaselineCohorts.Count(c => c.IsClean)}/30";
        ExperimentText = experiment is null ? "No persisted experiment protocol" :
            $"Protocol {experiment.ProtocolId} · revision {experiment.Revision}\n" +
            (experiment.Definition is null ? "No comparison family selected" :
                $"{experiment.Definition.HypothesisFamily} · comparison capital {experiment.Definition.StartingCapital:N2} {experiment.Definition.Currency} · experiment {experiment.Definition.ExperimentId}") +
            (experiment.PendingBoundary is null ? "" : $"\nQueued {experiment.PendingBoundary.Kind}: {experiment.PendingBoundary.EffectiveSession:yyyy-MM-dd}") +
            $"\nLatest record: {experiment.LastReason}";
        PromotionText = promotionScore is null ? "No untouched promotion score. Promotion requires a frozen contender and mature paired evidence." :
            $"{promotionScore.Status} · paired discovery {promotionScore.DiscoveryCohorts}/30 · paired untouched {promotionScore.UntouchedCohorts}/30\n" +
            $"Mean daily improvement {Percent(promotionScore.MeanDailyImprovement)} · 95% interval [{Percent(promotionScore.Lower95)}, {Percent(promotionScore.Upper95)}]\n" +
            $"Maximum drawdown: champion {Percent(promotionScore.ChampionMaximumDrawdown)}, challenger {Percent(promotionScore.ChallengerMaximumDrawdown)} · " +
            $"worst-decile average: champion {Percent(promotionScore.ChampionWorstDecileAverage)}, challenger {Percent(promotionScore.ChallengerWorstDecileAverage)}\n" +
            $"Untouched regimes: {string.Join(", ", promotionScore.UntouchedRegimeCounts.Select(p => $"{p.Key} {p.Value}"))}\n" +
            string.Join(", ", promotionScore.FailureReasons);
        Guid? selected = SelectedChallenger?.PolicyId;
        Replace(Challengers, experiment?.Definition?.ChallengerPolicyVersionIds.Select(id => new DelphiLiveChallengerChoice(id,
            $"{id} · paired discovery {experiment.DiscoveryCohorts.Count(c => DelphiLiveExperimentPolicy.IsPaired(c, experiment.ChampionPolicyVersionId, id))}/30")) ?? []);
        SelectedChallenger = Challengers.FirstOrDefault(c => c.PolicyId == selected);
        var cohorts = new List<DelphiLiveCohortRow>();
        if (experiment is not null)
        {
            AddCohorts("Engineering", experiment.EngineeringCohorts);
            AddCohorts("Discovery", experiment.DiscoveryCohorts);
            AddCohorts("Untouched", experiment.UntouchedCohorts);
            AddCohorts("Baseline", experiment.BaselineCohorts);
        }
        Replace(Cohorts, cohorts.OrderByDescending(c => c.Date));
        ApplyResearch(snapshot.Research);
        if (!loadingResearch && snapshot.ResearchReadUtc.HasValue)
            ResearchPeriodText = $"Saved evidence from {snapshot.ResearchFrom:yyyy-MM-dd} through {snapshot.ResearchThrough:yyyy-MM-dd} · read at {TorontoTime(snapshot.ResearchReadUtc)} Toronto";
        RefreshCommandAvailability();

        void AddCohorts(string phase, IEnumerable<DelphiLiveCohortEvidence> evidence)
        {
            cohorts.AddRange(evidence.Select(c => new DelphiLiveCohortRow(c.SessionDate.ToString("yyyy-MM-dd"), phase, c.Regime,
                $"{c.UsableOperationalSlots}/{c.ExpectedOperationalSlots}", c.IsClean, c.FiveSessionResearchMature,
                string.Join(", ", new[] { c.HasHostGap ? "Host gap" : null, c.HasOverlappingCycle ? "Overlap" : null,
                    !c.StablePolicyIdentities ? "Policy changed" : null, !c.ReconstructibleDecisionsAndFills ? "Unreconstructible evidence" : null,
                    c.CorporateActionUnsupported ? "Corporate action unsupported" : null, c.CapitalChanged ? "Capital changed" : null }.Where(x => x is not null)))));
        }
    }

    private void ApplyResearch(DelphiLiveResearchPresentation? research)
    {
        ResearchText = research is null ? "No persisted research coverage" :
            $"Stock operational slots: {CoverageText(research.StockOperationalCoverage)}\nXIU operational slots: {CoverageText(research.XiuOperationalCoverage)}";
        researchEvidence = research is null ? "No persisted research evidence" : FormatJson(DelphiLiveLedgerJson.Serialize(research));
        Replace(ResearchMetrics, research?.Metrics.Select(m => new DelphiLiveResearchMetricRow(m.Horizon.ToString(), m.Metric,
            m.Coverage.ValidCount, m.Coverage.DegradedCount, m.Coverage.InvalidCount, m.Coverage.PendingCount, m.Coverage.NotApplicableCount,
            m.Coverage.ApplicableCount, Percent(m.Coverage.CompletionCoverage), Percent(m.Coverage.UsableCoverage), m.Coverage.Readiness.ToString(),
            string.Join("; ", m.FailureReasons.Select(p => $"{p.Key}: {p.Value}")))) ?? []);
        Replace(RankingMetrics, research?.Rankings.Select(m => new DelphiLiveRankingMetricRow(m.Scorecard.Lens.ToString(), m.Horizon.ToString(), m.Metric,
            Percent(m.Scorecard.DailyEqualCohortReturn), Percent(m.Scorecard.LiveEqualCohortReturn), Percent(m.Scorecard.IncrementalReturn),
            CoverageText(m.Scorecard.CohortCoverage))) ?? []);
        Replace(FillDiagnostics, research?.FillDiagnostics ?? []);
        Guid? performanceId = SelectedPerformance?.PortfolioId;
        Replace(PortfolioPerformance, research?.PortfolioStatistics ?? []);
        SelectedPerformance = PortfolioPerformance.FirstOrDefault(p => p.PortfolioId == performanceId);
        var diagnosticSelection = SelectedDiagnostic;
        Replace(DiagnosticScorecards, research?.DiagnosticScorecards ?? []);
        SelectedDiagnostic = DiagnosticScorecards.FirstOrDefault(d => diagnosticSelection is not null &&
            d.Category == diagnosticSelection.Category && d.Variant == diagnosticSelection.Variant &&
            d.Signal == diagnosticSelection.Signal && d.Horizon == diagnosticSelection.Horizon);
    }

    private void RefreshVariantChoices()
    {
        rebuildingChoices = true;
        decimal? current = championPolicy is null ? null : SelectedFamily.Family switch
        {
            DelphiLiveHypothesisFamily.RawMoveThreshold => championPolicy.SelectedRawMoveThreshold,
            DelphiLiveHypothesisFamily.RelativeDeadband => championPolicy.SelectedExcessMoveThreshold,
            _ => championPolicy.SelectedRulerSessions
        };
        decimal[] values = SelectedFamily.Family switch
        {
            DelphiLiveHypothesisFamily.RawMoveThreshold => [0.15m, 0.25m, 0.35m],
            DelphiLiveHypothesisFamily.RelativeDeadband => [0.025m, 0.05m, 0.10m],
            _ => [10m, 14m]
        };
        Replace(VariantChoices, values.Select(v => new DelphiLiveVariantChoice(v,
            v.ToString(CultureInfo.InvariantCulture) + (current == v ? " (current champion)" : ""), current.HasValue && current != v)));
        foreach (var choice in VariantChoices) choice.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DelphiLiveVariantChoice.IsSelected) || rebuildingChoices) return;
            int count = VariantChoices.Count(c => c.IsSelected);
            foreach (var item in VariantChoices) item.IsEnabled = item.IsContender && (item.IsSelected || count < 2);
            RefreshCommandAvailability();
        };
        rebuildingChoices = false;
        RefreshCommandAvailability();
    }
    private void RefreshCommandAvailability()
    {
        foreach (string property in new[] { nameof(CanScheduleDiscovery), nameof(CanScheduleUntouched), nameof(CanApprovePromotion),
            nameof(CanRecordMeasurementDefect), nameof(CanResumeCapitalReview), nameof(CanRecordCorporateAction) }) OnPropertyChanged(property);
        OnPropertyChanged(nameof(CanLoadResearch));
    }
    private static bool TryAmount(string value, out decimal amount) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture,
        out amount) && amount > 0m && decimal.Round(amount, 6) == amount;
    private static bool IsCurrency(string value) => value.Trim().Length == 3 && value.Trim().ToUpperInvariant().All(c => c is >= 'A' and <= 'Z');
    private static bool TryDate(string value, out DateOnly date) => DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    private static string Percent(decimal? value) => value?.ToString("P2", CultureInfo.CurrentCulture) ?? "—";
    private static string CoverageText(DelphiLiveMetricCoverage coverage) =>
        $"{coverage.ValidCount + coverage.DegradedCount}/{coverage.ApplicableCount} usable ({Percent(coverage.UsableCoverage)}) · completion {Percent(coverage.CompletionCoverage)} · {coverage.Readiness}";
}

public sealed record DelphiLiveFamilyChoice(DelphiLiveHypothesisFamily Family, string Label, string Help);
public sealed record DelphiLiveChallengerChoice(Guid PolicyId, string Label);
public sealed record DelphiLiveCohortRow(string Date, string Phase, string Regime, string OperationalSlots, bool Clean, bool Mature, string Exclusions);
public sealed record DelphiLiveResearchMetricRow(string Horizon, string Metric, int Valid, int Degraded, int Invalid, int Pending,
    int NotApplicable, int Applicable, string Completion, string Usable, string Readiness, string Reasons);
public sealed record DelphiLiveRankingMetricRow(string Lens, string Horizon, string Metric, string Daily, string Live, string Incremental, string Coverage);
public sealed record DelphiLiveDiagnosticMetricRow(string Metric, decimal? Mean, string Coverage);
public sealed class DelphiLiveVariantChoice(decimal value, string label, bool isContender) : INotifyPropertyChanged
{
    private bool selected;
    private bool enabled = isContender;
    public decimal Value { get; } = value;
    public string Label { get; } = label;
    public bool IsContender { get; } = isContender;
    public bool IsSelected
    {
        get => selected;
        set { if (selected == value || value && !IsEnabled) return; selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public bool IsEnabled
    {
        get => enabled;
        set { if (enabled == value) return; enabled = value; PropertyChanged?.Invoke(this, new(nameof(IsEnabled))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
