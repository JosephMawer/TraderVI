#nullable enable

using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class DelphiLiveViewModel : INotifyPropertyChanged
{
    private readonly DelphiLiveDesktopService service;
    private readonly SemaphoreSlim gate = new(1, 1);
    private string status = "Delphi Live is inactive";
    private string coverage = "No session evidence loaded";
    private string warning = "";
    private string capital = "";
    private string currency = "CAD";
    private string reason = "";
    private bool schemaInstalled;
    private bool busy;
    private bool activationRequested;
    private bool hasPortfolio;
    private DelphiLiveObservationRow? selectedObservation;
    private DelphiLivePortfolioRow? selectedPortfolio;
    private DelphiLiveActionRow? selectedAction;
    private DelphiLiveQuoteRow? selectedQuote;
    private string selectedDossier = "Select an observation or action to inspect its persisted deterministic evidence.";
    private string policyText = "DelphiLivePolicyV1 · installed inactive";
    private IReadOnlyList<DelphiLivePortfolioSnapshot> portfolios = Array.Empty<DelphiLivePortfolioSnapshot>();

    public DelphiLiveViewModel(DelphiLiveDesktopService service)
    {
        this.service = service;
        RefreshVariantChoices();
    }

    public ObservableCollection<DelphiLiveObservationRow> Opportunities { get; } = [];
    public ObservableCollection<DelphiLiveObservationRow> AllObservations { get; } = [];
    public ObservableCollection<DelphiLivePortfolioRow> Portfolios { get; } = [];
    public ObservableCollection<DelphiLivePositionRow> Positions { get; } = [];
    public ObservableCollection<DelphiLiveActionRow> Actions { get; } = [];
    public ObservableCollection<DelphiLiveFillRow> Fills { get; } = [];
    public ObservableCollection<DelphiLiveQuoteRow> Quotes { get; } = [];

    public string Status { get => status; private set => Set(ref status, value); }
    public string Coverage { get => coverage; private set => Set(ref coverage, value); }
    public string Warning { get => warning; private set => Set(ref warning, value); }
    public string PolicyText { get => policyText; private set => Set(ref policyText, value); }
    public string StartingCapital { get => capital; set { Set(ref capital, value); OnPropertyChanged(nameof(CanActivate)); } }
    public string Currency { get => currency; set { Set(ref currency, value); OnPropertyChanged(nameof(CanActivate)); } }
    public string ActivationReason { get => reason; set { Set(ref reason, value); OnPropertyChanged(nameof(CanActivate)); } }
    public bool CanActivate => schemaInstalled && service.CalendarAvailable && !busy && !activationRequested && !hasPortfolio &&
        !Status.Contains("queued", StringComparison.OrdinalIgnoreCase) &&
        TryCapital(out _) && Currency.Trim().Length == 3 && Currency.Trim().ToUpperInvariant().All(c => c >= 'A' && c <= 'Z') &&
        !string.IsNullOrWhiteSpace(ActivationReason);
    public bool CanRefresh => !busy;
    public string SelectedDossier { get => selectedDossier; private set => Set(ref selectedDossier, value); }
    public DelphiLiveObservationRow? SelectedObservation
    {
        get => selectedObservation;
        set { if (Set(ref selectedObservation, value) && value is not null) SelectedDossier = value.EvidenceJson; }
    }
    public DelphiLiveActionRow? SelectedAction
    {
        get => selectedAction;
        set { if (Set(ref selectedAction, value) && value is not null) SelectedDossier = value.DossierJson; }
    }
    public DelphiLiveQuoteRow? SelectedQuote
    {
        get => selectedQuote;
        set { if (Set(ref selectedQuote, value) && value is not null) SelectedDossier = value.EvidenceJson; }
    }
    public DelphiLivePortfolioRow? SelectedPortfolio
    {
        get => selectedPortfolio;
        set { if (Set(ref selectedPortfolio, value)) { ShowPortfolio(); RefreshCommandAvailability(); } }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => UpdateAsync(false, cancellationToken);
    public Task TickAsync(CancellationToken cancellationToken = default) => UpdateAsync(true, cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => service.StopAsync(cancellationToken);

    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanActivate || !TryCapital(out decimal amount))
            throw new InvalidOperationException("Activation requires installed storage, a reviewed calendar, positive simulation capital, currency, and an operator reason.");
        if (!gate.Wait(0))
            return;
        SetBusy(true);
        try
        {
            await service.ActivateAsync(amount, Currency.Trim().ToUpperInvariant(), ActivationReason.Trim(), cancellationToken);
            activationRequested = true;
            Apply(await service.SnapshotAsync(cancellationToken));
            Status = "Activation recorded for the next regular session";
        }
        finally { SetBusy(false); gate.Release(); }
    }

    private async Task UpdateAsync(bool tick, CancellationToken cancellationToken)
    {
        if (!gate.Wait(0))
            return;
        SetBusy(true);
        try
        {
            schemaInstalled = await service.HasSchemaAsync(cancellationToken);
            if (!schemaInstalled)
            {
                Status = "Delphi Live storage is not installed";
                Warning = "Delphi Live remains inactive. Its reviewed manual migrations have not been applied.";
                Coverage = "No operational session has started";
                return;
            }
            Apply(tick ? await service.TickAsync(cancellationToken) : await service.SnapshotAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Status = "Delphi Live needs attention";
            Warning = $"{exception.GetType().Name}: {exception.Message}";
        }
        finally { SetBusy(false); gate.Release(); }
    }

    private void Apply(DelphiLiveRuntimeSnapshot snapshot)
    {
        Status = snapshot.Status;
        portfolios = snapshot.Portfolios;
        ApplyProtocol(snapshot);
        hasPortfolio = portfolios.Count > 0;
        Warning = string.Join(Environment.NewLine, snapshot.Warnings
            .Concat(service.CalendarAvailable ? Array.Empty<string>() : new[] { service.CalendarWarning ?? "The reviewed TSX calendar is unavailable; activation is blocked." })
            .Distinct());
        int normal = snapshot.Evaluations.Count(e => e.Result.NextState.Confidence.State == DelphiLiveDataConfidenceState.Normal);
        Coverage = $"Session {snapshot.TradingDate:yyyy-MM-dd} · last checkpoint {TorontoTime(snapshot.LastCheckpointUtc)} · " +
            $"{normal}/{snapshot.Evaluations.Count} latest policy observations have Normal confidence";
        Guid? championId = portfolios.FirstOrDefault(p => p.Role == "OperationalChampion")?.PolicyVersionId;
        PolicyText = championId.HasValue
            ? $"Operational Champion {championId} · daily and live identities remain separate"
            : "DelphiLivePolicyV1 · no active Operational Champion portfolio";

        string? selection = SelectedObservation?.SelectionKey;
        var ordered = snapshot.Evaluations.OrderBy(e => e.Input.Policy.PolicyVersionId)
            .ThenBy(e => e.Result.RankCandidate, DelphiLiveRankingComparer.Instance)
            .ThenBy(e => e.Input.Stock.Symbol, StringComparer.OrdinalIgnoreCase).ToArray();
        var rows = ordered.SelectMany((e, index) =>
        {
            var matching = portfolios.Where(p => p.PolicyVersionId == e.Input.Policy.PolicyVersionId).ToArray();
            return matching.Length == 0 ? new[] { ObservationRow(e, index + 1, null) } :
                matching.Select(p => ObservationRow(e, index + 1, p));
        }).ToArray();
        Replace(AllObservations, rows);
        Replace(Opportunities, rows.Where(r => r.Role == "OperationalChampion" && r.PolicyVersionId == championId && r.Activity == "Active" && !r.IsHeld && !r.IsExitPending)
            .Select((row, index) => row with { Rank = index + 1 }));
        if (selection is not null) SelectedObservation = rows.FirstOrDefault(r => r.SelectionKey == selection);

        Guid? portfolioSelection = SelectedPortfolio?.PortfolioId;
        Replace(Portfolios, portfolios.Select(p => new DelphiLivePortfolioRow(p.PortfolioId, p.Role, p.Currency,
            p.Cash, p.OpenPositions.Count(), p.Marks.LastOrDefault(m => m.Complete)?.Nav,
            p.Guards.CapitalReviewRequired ? "Capital review required" : p.Guards.DailyBuyingPaused ? "Daily buying paused" :
            p.Marks.LastOrDefault() is { Complete: false } incomplete ? incomplete.Reason : "Protecting and observing",
            p.PolicyVersionId.ToString(), p.Revision)));
        SelectedPortfolio = Portfolios.FirstOrDefault(p => p.PortfolioId == portfolioSelection) ?? Portfolios.FirstOrDefault();
        ShowPortfolio();
        OnPropertyChanged(nameof(CanActivate));
    }

    private static DelphiLiveObservationRow ObservationRow(DelphiLiveStoredEvaluation stored, int rank, DelphiLivePortfolioSnapshot? portfolio)
    {
        var input = stored.Input;
        var result = stored.Result;
        bool held = portfolio?.OpenPositions.Any(p => p.Symbol == input.Stock.Symbol) == true;
        bool pendingSell = portfolio?.PendingActions.Any(a => a.Intent.Symbol == input.Stock.Symbol && a.Intent.Side == DelphiLiveActionSide.Sell) == true;
        bool pendingBuy = portfolio?.PendingActions.Any(a => a.Intent.Symbol == input.Stock.Symbol && a.Intent.Side == DelphiLiveActionSide.Buy) == true;
        var ownLifecycle = portfolio?.CandidateStates.GetValueOrDefault(input.Stock.Symbol)?.Lifecycle ??
            DelphiLiveLifecycleSnapshot.NewSession(input.DailySetup is not null);
        var lifecycle = pendingSell ? DelphiLiveRecommendationState.ExitPending : held ? DelphiLiveRecommendationState.Held :
            pendingBuy ? DelphiLiveRecommendationState.BuyPending : ownLifecycle.State;
        bool inScope = input.DailySetup is not null || held || portfolio?.Positions.Any(p => p.Symbol == input.Stock.Symbol &&
            p.ClosedUtc.HasValue && DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(p.ClosedUtc.Value,
                TimeZoneInfo.FindSystemTimeZoneById("America/Toronto"))) == input.Stock.SessionDate) == true;
        bool active = inScope && !held && !pendingSell && lifecycle != DelphiLiveRecommendationState.Dismissed &&
            (lifecycle is DelphiLiveRecommendationState.WarmingUp or DelphiLiveRecommendationState.Emerging or
                DelphiLiveRecommendationState.EntryEligible or DelphiLiveRecommendationState.BuyPending ||
             result.NextState.Momentum.State is DelphiLiveMomentumState.Strong or DelphiLiveMomentumState.StrongWithConflict or
                DelphiLiveMomentumState.PositiveNudge or DelphiLiveMomentumState.PositiveNudgeWithConflict);
        string sources = input.DailySetup is null
            ? input.IsSessionCarryCandidate ? "Session carry · no current daily rank" : "Observed holding · no current daily thesis"
            : string.Join(" · ", input.DailySetup.SourceLenses.Where(l => l.SelectedSource).Select(l => $"{l.Lens} #{l.Rank}"));
        string Family(DelphiLiveSignalFamily family) => result.NextState.FamilyJudgments.Single(f => f.Family == family).State.ToString();
        return new($"{portfolio?.PortfolioId}/{input.Policy.PolicyVersionId}/{input.Stock.Symbol}", input.Policy.PolicyVersionId,
            rank, input.Stock.Symbol, sources, input.DailySetup?.CommonDelphiComposite,
            result.NextState.Momentum.StrongTier == DelphiLiveStrongTier.None ? result.NextState.Momentum.State.ToString() :
                $"{result.NextState.Momentum.State}/{result.NextState.Momentum.StrongTier}",
            result.NextState.Confidence.State.ToString(), lifecycle.ToString(),
            active ? "Active" : "Quiet", Family(DelphiLiveSignalFamily.Persistence),
            Family(DelphiLiveSignalFamily.PriceMovement), Family(DelphiLiveSignalFamily.VolumeSupport), Family(DelphiLiveSignalFamily.PriceStructure),
            result.NextState.PersistenceScore, TorontoTime(input.BarEndUtc), held, pendingSell,
            FormatEvidence(stored, portfolio, ownLifecycle), inScope ? ownLifecycle.ReasonCode : "Observe only · outside this portfolio's entry scope")
            { Role = portfolio?.Role ?? "Shared research", PortfolioId = portfolio?.PortfolioId };
    }

    private void ShowPortfolio()
    {
        var portfolio = portfolios.FirstOrDefault(p => p.PortfolioId == SelectedPortfolio?.PortfolioId);
        Replace(Positions, portfolio?.Positions.Select(p => new DelphiLivePositionRow(p.Symbol, p.Quantity, p.AveragePurchasePrice,
            p.Protection.FloorPrice, p.Protection.Stage.ToString(), p.ClosedUtc.HasValue ? "Closed" : "Held", TorontoTime(p.OpenedUtc))) ?? []);
        Replace(Actions, portfolio?.Actions.Reverse().Select(a => new DelphiLiveActionRow(a.Intent.Symbol, a.Intent.Side.ToString(),
            a.Status, a.PrimaryReason, a.TerminalReason ?? "", a.AttemptCount, TorontoTime(a.Intent.DecisionUtc),
            FormatJson(a.DossierJson))) ?? []);
        Replace(Fills, portfolio?.Fills.Reverse().Select(f => new DelphiLiveFillRow(f.Symbol, f.Side.ToString(), f.Quantity,
            f.Price, f.Confidence.ToString(), f.Field.ToString(), TorontoTime(f.FilledUtc))) ?? []);
        Replace(Quotes, portfolio?.Quotes.Reverse().Select(q => new DelphiLiveQuoteRow(q.Observation.Symbol, q.Purpose,
            q.Observation.Bid, q.Observation.Ask, q.Observation.Price, q.Disposition,
            TorontoTime(q.Observation.RequestStartedUtc), TorontoTime(q.Observation.ReceivedUtc),
            FormatJson(DelphiLiveLedgerJson.Serialize(q)))) ?? []);
    }

    private static string FormatEvidence(DelphiLiveStoredEvaluation stored, DelphiLivePortfolioSnapshot? portfolio,
        DelphiLiveLifecycleSnapshot ownLifecycle) => JsonSerializer.Serialize(new
    {
        stored.Input.EvaluationId, stored.Input.SessionId, stored.Input.BarEndUtc, stored.Input.EvaluatedUtc,
        dailySetup = stored.Input.DailySetup, policy = stored.Input.Policy, stored.ContinuityEpoch,
        raw = stored.Result.RawValues, derived = stored.Result.DerivedFacts,
        families = stored.Result.NextState.FamilyJudgments, momentum = stored.Result.NextState.Momentum,
        confidence = stored.Result.NextState.Confidence, sharedResearchLifecycle = stored.Result.Lifecycle,
        portfolioId = portfolio?.PortfolioId, portfolioRole = portfolio?.Role, portfolioLifecycle = ownLifecycle,
        safety = stored.Result.Safety, stored.Result.Counterfactuals
    }, new JsonSerializerOptions { WriteIndented = true });
    private static string FormatJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
    private static string TorontoTime(DateTime? utc) => utc.HasValue
        ? TimeZoneInfo.ConvertTimeFromUtc(utc.Value, TimeZoneInfo.FindSystemTimeZoneById("America/Toronto")).ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        : "—";
    private bool TryCapital(out decimal amount) => decimal.TryParse(StartingCapital, NumberStyles.Number,
        CultureInfo.CurrentCulture, out amount) && amount > 0m && decimal.Round(amount, 6) == amount;
    private void SetBusy(bool value) { busy = value; OnPropertyChanged(nameof(CanActivate)); OnPropertyChanged(nameof(CanRefresh)); RefreshCommandAvailability(); }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items) { target.Clear(); foreach (var item in items) target.Add(item); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record DelphiLiveObservationRow(string SelectionKey, Guid PolicyVersionId, int Rank, string Symbol,
    string DailySource, decimal? DailyComposite, string Momentum, string Confidence, string Lifecycle, string Activity,
    string Persistence, string PriceMovement, string VolumeSupport, string PriceStructure, int? PersistenceScore,
    string Checkpoint, bool IsHeld, bool IsExitPending, string EvidenceJson, string Reason)
{
    public string Role { get; init; } = "";
    public Guid? PortfolioId { get; init; }
}
public sealed record DelphiLivePortfolioRow(Guid PortfolioId, string Role, string Currency, decimal Cash,
    int Holdings, decimal? LastCompleteNav, string Guard, string Policy, long Revision);
public sealed record DelphiLivePositionRow(string Symbol, int Quantity, decimal AverageCost, decimal? ProfitFloor,
    string Protection, string State, string Opened);
public sealed record DelphiLiveActionRow(string Symbol, string Side, string State, string Reason, string TerminalReason,
    int Attempts, string DecisionTime, string DossierJson);
public sealed record DelphiLiveFillRow(string Symbol, string Side, int Quantity, decimal Price, string Confidence, string Field, string Time);
public sealed record DelphiLiveQuoteRow(string Symbol, string Purpose, decimal? Bid, decimal? Ask, decimal? Price,
    string Disposition, string Requested, string Received, string EvidenceJson);
