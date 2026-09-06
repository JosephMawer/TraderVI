#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

/// <summary>Host-neutral session orchestration. All I/O boundaries are injected.</summary>
public sealed class DelphiLiveMonitorWorkflow
{
    private readonly IDelphiLiveClock clock;
    private readonly ITsxSessionCalendar calendar;
    private readonly IDelphiLiveSessionContextStore sessions;
    private readonly IDelphiLiveEvaluationStore evaluations;
    private readonly IDelphiLiveCollectionRuntimeStore collection;
    private readonly IDelphiLiveLedgerStore ledger;
    private readonly IDelphiLiveHoldingSource holdings;
    private readonly IDelphiLiveNotifier notifier;
    private readonly DelphiLiveCollectionWorkflow collector;
    private readonly DelphiLiveActionWorkflow actions;
    private readonly TimeSpan? hostTickCadence;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string owner = $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid():N}";
    private DelphiLiveLease? lease;
    private DelphiLiveSessionContext? context;
    private DelphiLiveCollectionRecovery? recovery;
    private DateTime? nextEnd;
    private DateTime continuityStart;
    private bool sessionProtectionStarted;
    private PreOpenHeartbeat? preOpenHeartbeat;
    private DateOnly? warningSession;
    private readonly List<string> warnings = [];
    private static readonly IComparer<DelphiLiveRankCandidate?> RankComparer =
        Comparer<DelphiLiveRankCandidate?>.Create(DelphiLiveRankingComparer.Instance.Compare);
    private sealed record PreOpenHeartbeat(DateOnly TradingDate, DateTime CompletedUtc, DateTime NextExpectedWakeUtc);

    public Func<DateOnly, DateTime, DelphiLiveLease, CancellationToken, Task>? ApplySessionBoundaryAsync { get; set; }
    public Func<DelphiLiveSessionContext, DateTime, IReadOnlyList<DelphiLiveStoredEvaluation>, DelphiLiveLease, CancellationToken, Task>? PersistResearchCheckpointAsync { get; set; }
    public Func<DelphiLiveSessionContext, DelphiLiveLease, CancellationToken, Task>? PersistSessionResearchAsync { get; set; }
    public Func<DateOnly, CancellationToken, Task<IReadOnlyList<string>>>? GetCorporateActionSymbolsAsync { get; set; }

    public DelphiLiveMonitorWorkflow(IDelphiLiveClock clock, ITsxSessionCalendar calendar,
        IDelphiLiveSessionContextStore sessions, IDelphiLiveEvaluationStore evaluations,
        IDelphiLiveCollectionRuntimeStore collection, IDelphiLiveLedgerStore ledger,
        IDelphiLiveHoldingSource holdings, IDelphiLiveMarketDataSource source, IDelphiLiveNotifier notifier,
        TimeSpan? hostTickCadence = null)
    {
        this.clock = clock; this.calendar = calendar; this.sessions = sessions;
        this.evaluations = evaluations; this.collection = collection; this.ledger = ledger;
        this.holdings = holdings; this.notifier = notifier;
        if (hostTickCadence.HasValue && hostTickCadence.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(hostTickCadence));
        // This describes the host's wake schedule, not a trading threshold.
        // An unknown cadence cannot attest to liveness across market open.
        this.hostTickCadence = hostTickCadence;
        collector = new(clock, source, collection, collection);
        actions = new(ledger, source, clock);
    }

    public async Task<DelphiLiveRuntimeSnapshot> SnapshotAsync(CancellationToken ct = default)
    {
        DateOnly date = LocalDate(clock.UtcNow);
        var frozen = await sessions.GetFrozenSessionAsync(date, ct);
        var values = frozen is null ? Array.Empty<DelphiLiveStoredEvaluation>() :
            await evaluations.GetLatestSnapshotAsync(frozen.SessionId, ct);
        var portfolios = await ledger.GetPortfoliosForSessionAsync(date, ct);
        return new(portfolios.Count == 0 ? "Inactive" : frozen is null ? "Awaiting session" : frozen.Status,
            date, frozen?.SessionId, values.Count == 0 ? null : values.Max(v => v.Input.BarEndUtc),
            values, portfolios, warnings.ToArray());
    }

    public async Task<DelphiLiveRuntimeSnapshot> TickAsync(CancellationToken ct = default)
    {
        if (!await gate.WaitAsync(0, ct)) return await SnapshotAsync(ct);
        try
        {
            DateTime now = clock.UtcNow;
            DateOnly date = LocalDate(now);
            if (!calendar.IsRegularSession(date)) return await SnapshotAsync(ct);
            var bounds = calendar.GetSessionBounds(date);
            if (now < bounds.OpenUtc)
            {
                bool active = (await sessions.GetAssignmentsForSessionAsync(date, ct)).Count > 0;
                var snapshot = await SnapshotAsync(ct);
                DateTime completed = clock.UtcNow;
                preOpenHeartbeat = active && hostTickCadence is TimeSpan cadence && completed >= now && completed < bounds.OpenUtc
                    ? new(date, completed, completed.Add(cadence)) : null;
                return snapshot;
            }
            // Capture liveness before initialization or collection I/O. A
            // pending wake may arrive within its own cadence interval, but
            // a missed following wake invalidates the opening attestation.
            bool armedAtOpen = WasArmedAtOpen(date, bounds.OpenUtc, now);
            if (now >= bounds.CloseUtc.AddMinutes(7))
            {
                if (context?.Session.TradingDate == date && lease is not null)
                {
                    await collection.FinishSessionAsync(context.Session.SessionId, lease, false, ct);
                    if (PersistSessionResearchAsync is not null) await PersistSessionResearchAsync(context, lease, ct);
                }
                return await SnapshotAsync(ct);
            }
            var assignments = await sessions.GetAssignmentsForSessionAsync(date, ct);
            if (assignments.Count == 0) return await SnapshotAsync(ct);
            if (lease is null)
            {
                lease = await collection.TryAcquireAsync(owner, now, now.AddMinutes(15), ct);
                if (lease is null)
                {
                    AddWarning("Another Delphi Live host owns the durable monitoring lease.");
                    return await SnapshotAsync(ct);
                }
            }
            if (!await collection.TryRenewAsync(lease, now, now.AddMinutes(15), ct))
                throw new InvalidOperationException("Delphi Live host lease was lost; no further actions are permitted.");
            if (context?.Session.TradingDate != date)
            {
                if (context is not null) await collection.FinishSessionAsync(context.Session.SessionId, lease, false, ct);
                if (ApplySessionBoundaryAsync is not null) await ApplySessionBoundaryAsync(date, bounds.OpenUtc, lease, ct);
                assignments = await sessions.GetAssignmentsForSessionAsync(date, ct);
                await sessions.FreezeSessionAsync(new(date, bounds.OpenUtc,
                    calendar.GetImmediatelyPrecedingSession(date), assignments), ct);
                context = await sessions.ReadContextAsync(date, ct) ?? throw new InvalidOperationException("Frozen session is unavailable.");
                if (warningSession != date) { warnings.Clear(); warningSession = date; }
                recovery = await collection.RecoverSessionAsync(context.Session.SessionId, lease, ct,
                    wasArmedAtSessionOpen: armedAtOpen);
                nextEnd = Endpoints(bounds).FirstOrDefault(e => e.AddMinutes(2) >= clock.UtcNow);
                if (nextEnd == default(DateTime)) nextEnd = null;
                bool fromOpen = !recovery.HostGapObserved && armedAtOpen;
                // After resume, the first fresh bar supplies the endpoint for
                // four later intervals, not a pre-resume opening price.
                continuityStart = fromOpen ? bounds.OpenUtc : nextEnd ?? bounds.CloseUtc;
                sessionProtectionStarted = false;
                if (recovery.HostGapObserved) AddWarning("Host coverage gap: this session cannot support clean shakedown or promotion.");
            }
            var portfolios = await ledger.GetPortfoliosForSessionAsync(date, ct);
            // Opening protection runs once immediately, before the first 09:37
            // collection. Later protection is first in each scheduled cycle.
            if (!sessionProtectionStarted)
            {
                Guid openingCycle = Guid.NewGuid();
                foreach (var portfolio in portfolios)
                    await actions.ProtectHoldingsAsync(new(portfolio.PortfolioId, openingCycle, date,
                        bounds.OpenUtc, bounds.CloseUtc, true, recovery!.HostGapObserved) { SessionId = context.Session.SessionId },
                        context.Policies[portfolio.PolicyVersionId], lease, ct);
                sessionProtectionStarted = true;
            }
            if (nextEnd is not DateTime end || clock.UtcNow < end.AddMinutes(2)) return await SnapshotAsync(ct);
            if (clock.UtcNow >= end.AddMinutes(7))
            {
                recovery = await collection.RecoverSessionAsync(context.Session.SessionId, lease, ct);
                nextEnd = Endpoints(bounds).Where(e => e.AddMinutes(2) >= clock.UtcNow).Cast<DateTime?>().FirstOrDefault();
                continuityStart = nextEnd ?? bounds.CloseUtc;
                AddWarning("A scheduled cycle was missed; confirmation must remature from fresh evidence.");
                return await SnapshotAsync(ct);
            }
            Guid cycleId = Guid.NewGuid();
            var previous = await evaluations.GetLatestSnapshotAsync(context.Session.SessionId, ct);
            foreach (var portfolio in portfolios)
            {
                bool warming = end < continuityStart.AddMinutes(20) ||
                    portfolio.OpenPositions.Any(position => !previous.Any(p => p.Input.Policy.PolicyVersionId == portfolio.PolicyVersionId &&
                        p.Input.Stock.Symbol == position.Symbol && p.ContinuityEpoch == recovery!.EpochNumber && p.Result.FamiliesMature));
                await actions.ProtectHoldingsAsync(new(portfolio.PortfolioId, cycleId, date,
                    bounds.OpenUtc, bounds.CloseUtc, warming) { SessionId = context.Session.SessionId },
                    context.Policies[portfolio.PolicyVersionId], lease, ct);
            }
            var tracked = await holdings.GetObservedHoldingsAsync(ct);
            var corporateSymbols = GetCorporateActionSymbolsAsync is null ? Array.Empty<string>() :
                await GetCorporateActionSymbolsAsync(date, ct);
            portfolios = await ledger.GetPortfoliosForSessionAsync(date, ct);
            context = await sessions.SynchronizeObservationSetAsync(context.Session.SessionId, end, lease, portfolios, ct);
            var targets = PlanTargets(context, previous, tracked, portfolios, end);
            var cycle = new DelphiLiveCollectionCycle(cycleId, context.Session.SessionId, end.AddMinutes(-5), end,
                end.AddMinutes(2), end.AddMinutes(7), lease.FencingToken, recovery!.EpochNumber);
            var collected = await collector.RunCycleAsync(cycle, targets, lease, ct);
            foreach (var warning in collected.Warnings) AddWarning(warning);
            var bars = await collection.GetSessionBarsAsync(context.Session.SessionId, end, ct);
            var saved = new List<DelphiLiveStoredEvaluation>();
            // Every policy judgment is durable before any cycle action is considered.
            foreach (var assignment in context.Assignments)
            {
                var policy = context.Policies[assignment.PolicyVersionId];
                var rolePortfolios = portfolios.Where(p => p.PolicyVersionId == policy.PolicyVersionId).ToArray();
                foreach (string symbol in targets.Where(t => t.Symbol != "XIU").Select(t => t.Symbol))
                {
                    var prior = previous.SingleOrDefault(e => e.Input.Policy.PolicyVersionId == policy.PolicyVersionId && e.Input.Stock.Symbol == symbol);
                    if (prior?.Input.BarEndUtc >= end) { saved.Add(prior); continue; }
                    context.Candidates.TryGetValue(symbol, out var candidate);
                    bool carry = candidate is null && rolePortfolios.SelectMany(p => p.Positions).Any(p => p.Symbol == symbol &&
                        (p.ClosedUtc is null || LocalDate(p.ClosedUtc.Value) == date));
                    var state = prior?.Result.NextState ?? DelphiLiveEvaluationState.Initial(candidate is not null);
                    if (prior is not null && prior.ContinuityEpoch != recovery.EpochNumber)
                        state = state with
                        {
                            Lifecycle = DelphiLiveLifecyclePolicy.AfterProcessContinuityGap(state.Lifecycle, candidate is not null),
                            ResearchLifecycle = DelphiLiveLifecyclePolicy.AfterProcessContinuityGap(state.ResearchLifecycle, candidate is not null)
                        };
                    if (prior is not null)
                    {
                        int omitted = (int)((end - prior.Input.BarEndUtc).Ticks / policy.BarInterval.Ticks) - 1;
                        for (int i = 0; i < omitted; i++) state = state with { Confidence = DelphiLiveDataConfidencePolicy.Advance(state.Confidence, true, false) };
                    }
                    var stockBars = bars.Where(b => b.Symbol == symbol).ToArray();
                    var xiuBars = bars.Where(b => b.Symbol == "XIU").ToArray();
                    var baseline = context.Baselines[symbol];
                    var input = new DelphiLiveEvaluationInput
                    {
                        EvaluationId = Guid.NewGuid(), SessionId = context.Session.SessionId, BarEndUtc = end,
                        EvaluatedUtc = clock.UtcNow, Stock = new(symbol, date, bounds.OpenUtc, continuityStart, stockBars),
                        Xiu = new("XIU", date, bounds.OpenUtc, continuityStart, xiuBars), Policy = policy,
                        VolatilityRulers = baseline.Rulers, PreviousState = state, PreviousStockSessionClose = baseline.PreviousClose,
                        PreviousXiuSessionClose = context.Baselines["XIU"].PreviousClose,
                        DailySetup = candidate is null ? null : DailySetup(context, candidate), IsSessionCarryCandidate = carry,
                        // Shared market and research judgments cannot inherit
                        // any portfolio's ownership, pending actions or exits.
                        ExactPairPersistedOnTime = OnTime(stockBars, end) && OnTime(xiuBars, end), IsHeld = false,
                        AveragePurchasePrice = null, ProfitProtection = null,
                        HasPendingBuy = false, HasPendingSell = false,
                        MedianFullDayVolume20 = MedianVolume(baseline)
                    };
                    var result = DelphiLiveEvaluationEngine.Evaluate(input);
                    await evaluations.PersistAsync(input, result, recovery.EpochNumber, lease, ct);
                    saved.Add(new(input, result, recovery.EpochNumber));
                }
            }
            if (PersistResearchCheckpointAsync is not null) await PersistResearchCheckpointAsync(context, end, saved, lease, ct);
            DelphiLivePortfolioSnapshot? operationalAfterActions = null;
            foreach (var portfolio in portfolios)
            {
                var currentPortfolio = await ledger.LoadPortfolioAsync(portfolio.PortfolioId, ct) ?? throw new InvalidOperationException("Portfolio disappeared.");
                var policy = context.Policies[portfolio.PolicyVersionId];
                var ordered = saved.Where(e => e.Input.Policy.PolicyVersionId == policy.PolicyVersionId)
                    .OrderBy(e => e.Result.RankCandidate, RankComparer).ToArray();
                var candidates = new List<DelphiLiveActionCandidate>();
                var candidateStates = currentPortfolio.CandidateStates;
                var portfolioDossiers = new List<DelphiLiveDecisionDossier>();
                for (int index = 0; index < ordered.Length; index++)
                {
                    var value = ordered[index];
                    var originalPosition = currentPortfolio.Positions.Where(p => p.Symbol == value.Input.Stock.Symbol).OrderByDescending(p => p.OpenedUtc).FirstOrDefault();
                    if (!IsWithinOwnEntryScope(currentPortfolio, value.Input.Stock.Symbol, date, value.Input.DailySetup is not null)) continue;
                    var owned = currentPortfolio.OpenPositions.SingleOrDefault(p => p.Symbol == value.Input.Stock.Symbol);
                    candidateStates.TryGetValue(value.Input.Stock.Symbol, out var candidateState);
                    var priorLifecycle = candidateState?.Lifecycle ?? DelphiLiveLifecycleSnapshot.NewSession(value.Input.DailySetup is not null);
                    if (candidateState is not null && candidateState.ContinuityEpoch != recovery.EpochNumber)
                        priorLifecycle = DelphiLiveLifecyclePolicy.AfterProcessContinuityGap(priorLifecycle, value.Input.DailySetup is not null);
                    bool pendingBuy = currentPortfolio.PendingActions.Any(a => a.Intent.Symbol == value.Input.Stock.Symbol && a.Intent.Side == DelphiLiveActionSide.Buy);
                    bool pendingSell = currentPortfolio.PendingActions.Any(a => a.Intent.Symbol == value.Input.Stock.Symbol && a.Intent.Side == DelphiLiveActionSide.Sell);
                    var safetyInput = value.Result.SafetyInput with
                    {
                        IsHeld = owned is not null, AveragePurchasePrice = owned?.AveragePurchasePrice,
                        ProfitProtection = owned?.Protection,
                        PreviousValidMomentumWasStrongWeakening = priorLifecycle.LastScheduledBarEndUtc == end - policy.BarInterval &&
                            priorLifecycle.ConsecutiveStrongWeakeningObservations > 0
                    };
                    var safety = DelphiLiveSafetyPolicy.Evaluate(safetyInput, policy);
                    var lifecycle = DelphiLiveLifecyclePolicy.Advance(priorLifecycle, new(end, value.Result.FamiliesMature,
                        value.Result.ObservationIsValid, value.Result.NextState.Confidence, value.Result.NextState.Momentum,
                        safety.EntrySafetyVetoActive, owned is not null, pendingBuy, pendingSell, value.Input.DailySetup is not null));
                    if (candidateState?.EvaluationId == value.Input.EvaluationId)
                        lifecycle = lifecycle with { Snapshot = candidateState.Lifecycle,
                            MayCreateBuyDecision = candidateState.Lifecycle.State == DelphiLiveRecommendationState.EntryEligible && !pendingBuy && !pendingSell && owned is null };
                    var ownState = new DelphiLivePortfolioCandidateState(lifecycle.Snapshot, recovery.EpochNumber, value.Input.EvaluationId, clock.UtcNow);
                    candidateStates = candidateStates.SetItem(value.Input.Stock.Symbol, ownState);
                    if (value.Result.CurrentStockObservationId is not Guid evidenceId) continue;
                    var ownInput = value.Input with
                    {
                        PreviousState = value.Input.PreviousState with { Lifecycle = priorLifecycle },
                        IsHeld = owned is not null, HasPendingBuy = pendingBuy, HasPendingSell = pendingSell,
                        AveragePurchasePrice = owned?.AveragePurchasePrice, ProfitProtection = owned?.Protection
                    };
                    DateTime? confirmationStart = lifecycle.Snapshot.ConsecutiveStrongObservations > 0
                        ? end - TimeSpan.FromTicks(policy.BarInterval.Ticks * (lifecycle.Snapshot.ConsecutiveStrongObservations - 1)) : null;
                    var ownResult = value.Result with
                    {
                        NextState = value.Result.NextState with { Lifecycle = lifecycle.Snapshot }, Lifecycle = lifecycle,
                        ConfirmationStartedBarEndUtc = confirmationStart, SafetyInput = safetyInput, Safety = safety
                    };
                    var attribution = context.Candidates.TryGetValue(value.Input.Stock.Symbol, out var daily) ? Lenses(daily) : [];
                    var dossier = DelphiLiveEvaluationEngine.CreateDossier(ownInput, ownResult, Guid.NewGuid(), clock.UtcNow,
                        "Observe", "Evaluated", attribution, OriginalThesis(originalPosition));
                    portfolioDossiers.Add(dossier);
                    bool eligible = context.Session.CalibrationRunId is not null && value.Result.ObservationIsValid &&
                        value.Result.FamiliesMature && value.Result.NextState.Confidence.AllowsNewRisk &&
                        value.Result.NextState.Momentum.IsEntryEligibleStrong && !safety.EntrySafetyVetoActive &&
                        lifecycle.MayCreateBuyDecision;
                    candidates.Add(new(value.Input.Stock.Symbol, value.Result.EvaluationId, evidenceId, index + 1,
                        eligible, confirmationStart, value.Result.NextState.Confidence,
                        safetyInput, DelphiLiveDecisionDossierBuilder.Serialize(dossier, policy)));
                }
                currentPortfolio = await ledger.CommitAsync(currentPortfolio.Revision,
                    currentPortfolio with { Revision = currentPortfolio.Revision + 1, UpdatedUtc = clock.UtcNow, CandidateStates = candidateStates },
                    new[] { new DelphiLiveLedgerEvent(Guid.NewGuid(), "PortfolioCandidateStatesEvaluated", clock.UtcNow,
                        DelphiLiveLedgerJson.Serialize(new { cycleId, candidates = candidateStates, dossiers = portfolioDossiers })) }, lease, ct);
                var marks = Marks(currentPortfolio.OpenPositions, bars, end, false);
                var openingPositions = currentPortfolio.CurrentSession == date ? currentPortfolio.SessionOpeningPositions : currentPortfolio.OpenPositions;
                var opening = Marks(openingPositions, bars, bounds.OpenUtc.AddMinutes(5), true);
                var completedPortfolio = await actions.RunCycleAsync(new(currentPortfolio.PortfolioId, cycleId, date, bounds.OpenUtc, bounds.CloseUtc,
                    end, ReviewedTsxSessionCalendar.At(date, policy.EntryCutoff), marks, opening, candidates,
                    CorporateActionUnsupported: currentPortfolio.SessionOpeningPositions.Concat(currentPortfolio.OpenPositions)
                        .Any(p => corporateSymbols.Contains(p.Symbol, StringComparer.OrdinalIgnoreCase)))
                    { SessionId = context.Session.SessionId }, policy, lease, ct);
                if (portfolio.Role == "OperationalChampion") operationalAfterActions = completedPortfolio;
            }
            Guid champion = context.Assignments.Single(a => a.Role == DelphiLivePolicyRole.OperationalChampion).PolicyVersionId;
            foreach (var value in saved.Where(e => e.Input.Policy.PolicyVersionId == champion))
            {
                if (value.Result.NextState.Confidence.State == DelphiLiveDataConfidenceState.MonitoringLost &&
                    (portfolios.Any(p => p.Role == "OperationalChampion" && p.OpenPositions.Any(position => position.Symbol == value.Input.Stock.Symbol)) ||
                     tracked.Any(h => h.Symbol == value.Input.Stock.Symbol)))
                    await notifier.NotifyAsync(new("Urgent", "MonitoringLost", "Holding monitoring lost; quote protection remains active.", Symbol: value.Input.Stock.Symbol), ct);
                else if (operationalAfterActions?.CandidateStates.GetValueOrDefault(value.Input.Stock.Symbol)?.Lifecycle.State is
                        DelphiLiveRecommendationState.EntryEligible or DelphiLiveRecommendationState.BuyPending &&
                    portfolios.SingleOrDefault(p => p.Role == "OperationalChampion")?.CandidateStates.GetValueOrDefault(value.Input.Stock.Symbol)?.Lifecycle.State is not
                        (DelphiLiveRecommendationState.EntryEligible or DelphiLiveRecommendationState.BuyPending) &&
                    value.Result.NextState.Confidence.AllowsNewRisk)
                    await notifier.NotifyAsync(new("Information", "EntryEligible", "Fresh strong confirmation completed.", Symbol: value.Input.Stock.Symbol), ct);
            }
            nextEnd = end < bounds.CloseUtc ? end.AddMinutes(5) : null;
            if (nextEnd is null)
            {
                await collection.FinishSessionAsync(context.Session.SessionId, lease, false, ct);
                if (PersistSessionResearchAsync is not null) await PersistSessionResearchAsync(context, lease, ct);
            }
            return await SnapshotAsync(ct);
        }
        catch
        {
            AddWarning("Monitoring interrupted; persisted evidence and pending protection are retained for recovery.");
            if (context is not null && lease is not null)
            {
                try { await collection.FinishSessionAsync(context.Session.SessionId, lease, true, CancellationToken.None); } catch { }
            }
            context = null; recovery = null; nextEnd = null; sessionProtectionStarted = false; preOpenHeartbeat = null;
            if (lease is not null)
            {
                try { await collection.ReleaseAsync(lease, clock.UtcNow, CancellationToken.None); } catch { }
                lease = null;
            }
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (lease is not null)
            {
                try
                {
                    if (context is not null)
                    {
                        await collection.FinishSessionAsync(context.Session.SessionId, lease, true, ct);
                        if (clock.UtcNow >= calendar.GetSessionBounds(context.Session.TradingDate).CloseUtc && PersistSessionResearchAsync is not null)
                            await PersistSessionResearchAsync(context, lease, ct);
                    }
                }
                finally { await collection.ReleaseAsync(lease, clock.UtcNow, ct); }
            }
            lease = null; context = null; recovery = null; nextEnd = null; sessionProtectionStarted = false; preOpenHeartbeat = null;
        }
        finally { gate.Release(); }
    }

    public async Task ExecuteOperatorCommandAsync(Func<DelphiLiveLease, CancellationToken, Task> command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await gate.WaitAsync(ct);
        DelphiLiveLease? commandLease = null;
        bool temporary = false;
        try
        {
            DateTime now = clock.UtcNow;
            if (lease is not null)
            {
                if (!await collection.TryRenewAsync(lease, now, now.AddMinutes(15), ct))
                    throw new InvalidOperationException("The active host lease was lost; the operator command was not applied.");
                commandLease = lease;
            }
            else
            {
                commandLease = await collection.TryAcquireAsync(owner, now, now.AddMinutes(15), ct)
                    ?? throw new InvalidOperationException("Another host owns Delphi Live; the operator command was not applied.");
                temporary = true;
            }
            await command(commandLease, ct);
        }
        finally
        {
            try { if (temporary && commandLease is not null) await collection.ReleaseAsync(commandLease, clock.UtcNow, CancellationToken.None); }
            finally { gate.Release(); }
        }
    }

    private bool WasArmedAtOpen(DateOnly date, DateTime openUtc, DateTime observedUtc) =>
        observedUtc == openUtc || hostTickCadence is TimeSpan cadence && preOpenHeartbeat is { } heartbeat &&
        heartbeat.TradingDate == date && heartbeat.CompletedUtc < openUtc && heartbeat.NextExpectedWakeUtc >= openUtc &&
        observedUtc >= heartbeat.CompletedUtc && observedUtc < heartbeat.NextExpectedWakeUtc.Add(cadence);

    private static IReadOnlyList<DelphiLiveObservationTarget> PlanTargets(DelphiLiveSessionContext context,
        IReadOnlyList<DelphiLiveStoredEvaluation> previous, IReadOnlyList<DelphiLiveObservedHolding> held,
        IReadOnlyList<DelphiLivePortfolioSnapshot> portfolios, DateTime end)
    {
        Guid champion = context.Assignments.Single(a => a.Role == DelphiLivePolicyRole.OperationalChampion).PolicyVersionId;
        var ordered = previous.Where(p => p.Input.Policy.PolicyVersionId == champion)
            .OrderBy(p => p.Result.RankCandidate, RankComparer).Select((value, index) => (value, index)).ToDictionary(p => p.value.Input.Stock.Symbol);
        return context.Session.Symbols.Where(symbol => !context.ObservationMembership.TryGetValue(symbol, out var membership) ||
            (membership.RequiredFromBarEndUtc <= end && membership.RequiredThroughBarEndUtc >= end)).Select(symbol =>
        {
            bool holding = held.Any(h => h.Symbol == symbol) || portfolios.SelectMany(p => p.OpenPositions).Any(p => p.Symbol == symbol);
            bool current = context.Candidates.TryGetValue(symbol, out var daily);
            ordered.TryGetValue(symbol, out var prior);
            bool active = prior.value is null || prior.value.Result.Lifecycle.PresentationActivity == DelphiLivePresentationActivity.Active;
            return new DelphiLiveObservationTarget(symbol,
                holding ? DelphiLiveCollectionPriorityClass.HeldSymbol : symbol == "XIU" ? DelphiLiveCollectionPriorityClass.XiuBenchmark :
                active ? DelphiLiveCollectionPriorityClass.ActiveCandidate : DelphiLiveCollectionPriorityClass.QuietOrDismissedCandidate,
                prior.value is null ? daily?.BestSourceRank ?? int.MaxValue : prior.index,
                current, !current && portfolios.SelectMany(p => p.Positions).Any(p => p.Symbol == symbol));
        }).ToArray();
    }

    private static DelphiLiveDailySetupQuality DailySetup(DelphiLiveSessionContext context, DelphiLiveFrozenCandidate candidate) =>
        new(context.Session.CalibrationRunId!.Value, candidate.CandidateId, context.Session.DailyStrategyVersionId!.Value,
            candidate.CommonComposite, candidate.SourceLenses.Select(l => new DelphiLiveSourceLensQuality(
                Enum.Parse<DelphiLiveSourceLens>(l.Lens), l.Eligible, l.Published, l.Rank, l.RankingKey,
                l.FirstFailure ?? "Published", l.GateTraceJson)).ToImmutableArray());

    private static DelphiLiveDossierLensAttribution[] Lenses(DelphiLiveFrozenCandidate candidate) => candidate.SourceLenses.Select(l =>
        new DelphiLiveDossierLensAttribution(l.LensEvaluationId, l.Lens, l.Rank!.Value, l.RankingKey!.Value,
            l.Eligible, l.Published, l.FirstFailure, l.GateTraceJson)).ToArray();

    private static DelphiLiveOriginalEntryThesis? OriginalThesis(DelphiLiveLedgerPosition? position)
    {
        if (position is null) return null;
        var dossier = DelphiLiveLedgerJson.Deserialize<DelphiLiveDecisionDossier>(position.OriginalEntryDossierJson);
        return dossier.OriginalEntryThesis ?? new(dossier.DecisionId, dossier.CalibrationRunId!.Value,
            dossier.CalibrationCandidateId!.Value, dossier.DailyStrategyVersionId!.Value, dossier.SourceLenses);
    }

    private static List<DelphiLivePositionMark> Marks(IEnumerable<DelphiLiveLedgerPosition> positions,
        IReadOnlyList<DelphiLiveFiveMinuteBar> bars, DateTime end, bool opening)
    {
        var marks = new List<DelphiLivePositionMark>();
        foreach (var position in positions)
        {
            var exact = bars.SingleOrDefault(b => b.Symbol == position.Symbol && b.EndUtc == end && b.Disposition == DelphiLiveEvidenceDisposition.OperationalOnTime);
            if (exact is not null) marks.Add(new(position.PositionId, position.Symbol, position.Quantity, opening ? exact.Open : exact.Close, end));
        }
        return marks;
    }

    private static bool OnTime(IEnumerable<DelphiLiveFiveMinuteBar> bars, DateTime end) => bars.Any(b => b.EndUtc == end && b.Disposition == DelphiLiveEvidenceDisposition.OperationalOnTime);
    internal static bool IsWithinOwnEntryScope(DelphiLivePortfolioSnapshot portfolio, string symbol, DateOnly date, bool currentlySelected) =>
        currentlySelected || portfolio.Positions.Any(p => p.Symbol == symbol && (p.ClosedUtc is null || LocalDate(p.ClosedUtc.Value) == date));
    private static decimal? MedianVolume(DelphiLiveFrozenBaseline baseline)
    {
        var values = baseline.Bars.Where(b => baseline.CanonicalDates.TakeLast(20).Contains(b.SessionDate)).Select(b => (decimal)b.Volume).Order().ToArray();
        return values.Length == 20 ? (values[9] + values[10]) / 2m : null;
    }
    private static IEnumerable<DateTime> Endpoints(DelphiLiveSessionBounds bounds)
    { for (DateTime end = bounds.OpenUtc.AddMinutes(5); end <= bounds.CloseUtc; end = end.AddMinutes(5)) yield return end; }
    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, ReviewedTsxSessionCalendar.Toronto));
    private void AddWarning(string warning) { if (!warnings.Contains(warning)) warnings.Add(warning); }
}
