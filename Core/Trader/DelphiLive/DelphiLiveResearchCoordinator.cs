#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveResearchSessionEvidence(
    ImmutableArray<DelphiLiveExpectedResearchSlot> ExpectedSlots,
    ImmutableArray<DelphiLiveFiveMinuteBar> IntradayBars,
    ImmutableArray<DelphiLiveDailyBar> DailyBars,
    ImmutableArray<DateOnly> CanonicalSessionDates,
    string FrozenRunContextJson, bool HasHostGap, bool HasOverlappingCycle,
    bool StablePolicyIdentities, bool CorporateActionUnsupported)
{
    public ImmutableHashSet<string> CorporateActionSymbols { get; init; } = ImmutableHashSet<string>.Empty;
    public bool HasConflictingEvidence { get; init; }
    public ImmutableHashSet<string> ConflictingAnchors { get; init; } = ImmutableHashSet<string>.Empty;
}

public interface IDelphiLiveResearchEvidenceSource
{
    Task<DelphiLiveResearchSessionEvidence> ReadAsync(DelphiLiveSessionContext context,
        DateTime throughBarEndUtc, DateTime asOfUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DateOnly>> ReadFrozenDatesAsync(DateOnly through, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DateOnly>> ReadChangedSessionDatesAsync(DateTime asOfUtc, CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveResearchMetricSummary(DelphiLiveOutcomeHorizon Horizon, string Metric,
    DelphiLiveMetricCoverage Coverage, ImmutableDictionary<string, int> FailureReasons);
public sealed record DelphiLiveRankingMetricSummary(DelphiLiveOutcomeHorizon Horizon, string Metric,
    DelphiLiveRankingScorecard Scorecard);
public sealed record DelphiLiveFillDiagnostic(Guid PortfolioId, string Role, int AllFillCount,
    int EstimatedFillCount, decimal? EstimatedFillFraction, int ClosedTradeCount, int BidAskOnlyClosedTradeCount,
    decimal? OfficialRealizedReturn, decimal? BidAskOnlyRealizedReturn, decimal? Difference,
    decimal? OfficialNavReturn);
public sealed record DelphiLiveResearchPresentation(ImmutableArray<DelphiLiveResearchMetricSummary> Metrics,
    ImmutableArray<DelphiLiveRankingMetricSummary> Rankings, DelphiLiveMetricCoverage StockOperationalCoverage,
    DelphiLiveMetricCoverage XiuOperationalCoverage, ImmutableArray<DelphiLiveFillDiagnostic> FillDiagnostics)
{
    public ImmutableArray<DelphiLiveDiagnosticScorecard> DiagnosticScorecards { get; init; } = [];
    public ImmutableArray<DelphiLivePortfolioPerformanceSummary> PortfolioStatistics { get; init; } = [];
}

/// <summary>Persists checkpoint research without trading; later observations can mature labels but never repair operational slots.</summary>
public sealed class DelphiLiveResearchCoordinator(
    IDelphiLiveExperimentStore experiments, IDelphiLiveResearchStore research,
    IDelphiLiveResearchEvidenceSource evidenceSource, IDelphiLiveSessionContextStore sessions,
    IDelphiLiveLedgerStore ledgers, ITsxSessionCalendar calendar, IDelphiLiveClock clock,
    Func<string> currentCodeIdentity)
{
    private readonly DelphiLiveExperimentWorkflow protocol = new(experiments);

    public async Task CheckpointAsync(DelphiLiveSessionContext context, DateTime endpoint,
        IReadOnlyList<DelphiLiveStoredEvaluation> evaluations, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        if (endpoint.Kind != DateTimeKind.Utc || evaluations.Any(e => e.Input.SessionId != context.Session.SessionId || e.Input.BarEndUtc != endpoint))
            throw new ArgumentException("A research checkpoint must use one frozen session and endpoint.");
        var evidence = await evidenceSource.ReadAsync(context, endpoint, clock.UtcNow, cancellationToken);
        await RecordNewSlots(evidence.ExpectedSlots, context.Session.TradingDate, lease, cancellationToken);
        DateTime first = context.Bounds.OpenUtc.AddMinutes(20), cutoff = context.Bounds.CloseUtc.AddMinutes(-15);
        if (endpoint < first || endpoint >= cutoff) return;
        Guid champion = context.Assignments.Single(a => a.Role == DelphiLivePolicyRole.OperationalChampion).PolicyVersionId;
        var facts = evaluations.Where(e => e.Input.Policy.PolicyVersionId == champion && e.Result.RankCandidate is not null)
            .Select(e => new DelphiLiveRankingEvidence(e.Input.EvaluationId, e.Result.RankCandidate!, e.Result.ConfirmedLiveEligible)).ToList();
        // The daily control must include every frozen candidate even when its
        // evaluation is unavailable. Its rank never depends on live coverage.
        foreach (var candidate in context.Candidates.Values.Where(c => facts.All(f => f.Candidate.Symbol != c.Symbol)))
        {
            var setup = new DelphiLiveDailySetupQuality(context.Session.CalibrationRunId!.Value, candidate.CandidateId,
                context.Session.DailyStrategyVersionId!.Value, candidate.CommonComposite,
                candidate.SourceLenses.Select(l => new DelphiLiveSourceLensQuality(Enum.Parse<DelphiLiveSourceLens>(l.Lens),
                    l.Eligible, l.Published, l.Rank, l.RankingKey, l.FirstFailure ?? "PassedFrozenDailyLens", l.GateTraceJson)).ToImmutableArray());
            facts.Add(new(null, new(candidate.Symbol, new(DelphiLiveMomentumState.Neutral, DelphiLiveStrongTier.None,
                DelphiLiveNeutralDetail.None, 0, 0, 0, 0), null, setup, false), false));
        }
        foreach (DelphiLiveSourceLens lens in Enum.GetValues<DelphiLiveSourceLens>())
            await research.RecordRankingCheckpointAsync(DelphiLiveResearchScorecards.Snapshot(
                StableId($"ranking/{context.Session.SessionId:D}/{endpoint:O}/{lens}"), context.Session.SessionId,
                context.Session.TradingDate, endpoint, champion, lens, facts), lease, cancellationToken);
    }

    public async Task SessionClosedAsync(DelphiLiveSessionContext context, DelphiLiveLease lease,
        CancellationToken cancellationToken = default)
    {
        if (clock.UtcNow <= context.Bounds.CloseUtc) return;
        DateTime reviewCutoff = clock.UtcNow;
        var evidence = await evidenceSource.ReadAsync(context, context.Bounds.CloseUtc, reviewCutoff, cancellationToken);
        await RecordNewSlots(evidence.ExpectedSlots, context.Session.TradingDate, lease, cancellationToken);
        await MatureSession(context, evidence, reviewCutoff, lease, cancellationToken);
        var state = await experiments.LoadAsync(cancellationToken);
        if (state is null)
        {
            await research.RecordSessionReviewAsync(context.Session.SessionId, reviewCutoff, lease, cancellationToken);
            return;
        }
        var existing = AllCohorts(state).SingleOrDefault(c => c.SessionDate == context.Session.TradingDate);
        if (existing is not null)
        {
            bool mature = existing.FiveSessionResearchMature || await IsResearchMature(context.Session.TradingDate, cancellationToken);
            if (mature != existing.FiveSessionResearchMature || evidence.CorporateActionUnsupported && !existing.CorporateActionUnsupported ||
                evidence.HasConflictingEvidence && !existing.EvidenceConflict)
                await protocol.RecordCohortAsync(existing with { FiveSessionResearchMature = mature,
                    CorporateActionUnsupported = existing.CorporateActionUnsupported || evidence.CorporateActionUnsupported,
                    EvidenceConflict = existing.EvidenceConflict || evidence.HasConflictingEvidence }, clock.UtcNow, lease, cancellationToken);
            await research.RecordSessionReviewAsync(context.Session.SessionId, reviewCutoff, lease, cancellationToken);
            return;
        }
        // An absent session from an already-ended phase stays excluded; its
        // collection/outcome coverage remains visible. Never relabel it as a
        // fresh untouched cohort merely because recovery happens later.
        if (context.Bounds.OpenUtc < state.PhaseStartedUtc && context.Bounds.CloseUtc <= state.PhaseStartedUtc)
        {
            await research.RecordSessionReviewAsync(context.Session.SessionId, reviewCutoff, lease, cancellationToken);
            return;
        }
        var portfolios = await ledgers.GetPortfoliosForSessionAsync(context.Session.TradingDate, cancellationToken);
        var comparison = state.Definition is null
            ? portfolios.Where(p => p.PortfolioId == state.OperationalPortfolioId).ToArray()
            : portfolios.Where(p => p.ExperimentId == state.Definition.ExperimentId && p.Role != "OperationalChampion").ToArray();
        var returns = ImmutableDictionary.CreateBuilder<Guid, decimal?>();
        var drawdowns = ImmutableDictionary.CreateBuilder<Guid, decimal?>();
        bool reconstructible = comparison.Length > 0, capitalChanged = false;
        foreach (var portfolio in comparison)
        {
            var marks = portfolio.Marks.Where(m => m.TradingDate == context.Session.TradingDate).ToArray();
            var opening = marks.LastOrDefault(m => m.Kind == DelphiLivePortfolioMarkKind.Opening);
            var closing = marks.LastOrDefault(m => m.Kind == DelphiLivePortfolioMarkKind.Closing && m.BarEndUtc == context.Bounds.CloseUtc);
            bool complete = opening is { Complete: true, Nav: > 0m } && closing is { Complete: true, Nav: not null };
            returns[portfolio.PolicyVersionId] = complete ? CalculateDailyReturn(portfolio, context.Session.TradingDate,
                calendar.GetImmediatelyPrecedingSession(context.Session.TradingDate)) : null;
            // Include the entire aligned run through this close so a drawdown
            // spanning two session boundaries cannot disappear in aggregation.
            bool navPathComplete = Enumerable.Range(1, 78).All(n => marks.Any(m => m.BarEndUtc == context.Bounds.OpenUtc.AddMinutes(5 * n) && m.Complete));
            drawdowns[portfolio.PolicyVersionId] = complete && navPathComplete
                ? DelphiLiveResearchScorecards.MaximumCheckpointDrawdown(portfolio.StartingCapital,
                    portfolio.Marks.Where(m => m.TradingDate <= context.Session.TradingDate && m.Complete && m.Nav.HasValue).Select(m => m.Nav!.Value)) : null;
            reconstructible &= complete && ValidateReconstruction(portfolio);
            capitalChanged |= portfolio.Cash != portfolio.StartingCapital + portfolio.Fills.Sum(f =>
                (f.Side == DelphiLiveActionSide.Buy ? -1m : 1m) * f.Quantity * f.Price) ||
                state.Definition is not null && (portfolio.StartingCapital != state.Definition.StartingCapital || portfolio.Currency != state.Definition.Currency);
        }
        var slots = await research.ReadExpectedSlotsAsync(context.Session.TradingDate, context.Session.TradingDate, cancellationToken);
        var cohort = new DelphiLiveCohortEvidence(context.Session.TradingDate, calendar.GetSessionOrdinal(context.Session.TradingDate),
            ReadFrozenRegime(evidence.FrozenRunContextJson), slots.Count, slots.Count(s => s.OperationalUsable),
            evidence.HasHostGap, evidence.HasOverlappingCycle,
            evidence.StablePolicyIdentities && (state.Definition is null || state.Definition.CodeIdentity == currentCodeIdentity()),
            reconstructible, await IsResearchMature(context.Session.TradingDate, cancellationToken),
            evidence.CorporateActionUnsupported, capitalChanged, returns.ToImmutable(), drawdowns.ToImmutable())
            { EvidenceConflict = evidence.HasConflictingEvidence };
        await protocol.RecordCohortAsync(cohort, clock.UtcNow, lease, cancellationToken);
        await research.RecordSessionReviewAsync(context.Session.SessionId, reviewCutoff, lease, cancellationToken);
    }

    public async Task RecoverAndMatureAsync(DateOnly asOfDate, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        foreach (DateOnly date in await evidenceSource.ReadChangedSessionDatesAsync(clock.UtcNow, cancellationToken))
        {
            if (date > asOfDate) continue;
            var context = await sessions.ReadContextAsync(date, cancellationToken);
            if (context is not null && clock.UtcNow > context.Bounds.CloseUtc)
                await SessionClosedAsync(context, lease, cancellationToken);
        }
    }

    private async Task MatureSession(DelphiLiveSessionContext context, DelphiLiveResearchSessionEvidence evidence,
        DateTime now, DelphiLiveLease lease, CancellationToken token)
    {
        var slots = await research.ReadExpectedSlotsAsync(context.Session.TradingDate, context.Session.TradingDate, token);
        var prior = (await research.ReadLatestOutcomesAsync(context.Session.TradingDate, context.Session.TradingDate, token)).ToDictionary(o => o.SlotId);
        DateOnly matured = evidence.CanonicalSessionDates.LastOrDefault(d => calendar.GetSessionBounds(d).CloseUtc < now);
        foreach (var slot in slots.Where(s => !s.IsBenchmark))
        {
            var anchor = evidence.IntradayBars.SingleOrDefault(b => b.Symbol == slot.Symbol && b.EndUtc == slot.BarEndUtc);
            DelphiLiveOutcomeCalculationInput? input = anchor is null ? null : new()
            {
                OutcomeId = StableId($"outcome/{slot.SlotId:D}"), Anchor = anchor,
                XiuAnchor = evidence.IntradayBars.SingleOrDefault(b => b.Symbol == "XIU" && b.EndUtc == slot.BarEndUtc),
                SessionCloseUtc = context.Bounds.CloseUtc, AsOfUtc = now, MaturedThroughSession = matured,
                CanonicalSessionDates = evidence.CanonicalSessionDates,
                FutureIntradayBars = evidence.IntradayBars.Where(b => b.Symbol == slot.Symbol).ToArray(),
                FutureXiuIntradayBars = evidence.IntradayBars.Where(b => b.Symbol == "XIU").ToArray(),
                FutureDailyBars = evidence.DailyBars.Where(b => b.Symbol == slot.Symbol).ToArray(),
                FutureXiuDailyBars = evidence.DailyBars.Where(b => b.Symbol == "XIU").ToArray(),
                EvidenceBasket = context.Candidates.ContainsKey(slot.Symbol) ? DelphiLiveOutcomeEvidenceBasket.ModelGrade : DelphiLiveOutcomeEvidenceBasket.OutOfScopeValid,
                CorporateActionUnsupported = evidence.CorporateActionSymbols.Contains(slot.Symbol) || evidence.CorporateActionSymbols.Contains("XIU")
            };
            var next = DelphiLiveResearchScorecards.CalculateExpectedOutcome(slot, input, now, DelphiLivePolicyDefinition.Version1);
            if (input is null && evidence.ConflictingAnchors.Contains($"{slot.Symbol}/{slot.BarEndUtc:O}"))
                next = next with { MissingAnchorReason = DelphiLiveOutcomeReasons.ConflictingEvidence };
            if (!prior.TryGetValue(slot.SlotId, out var old) || DelphiLiveLedgerJson.Serialize(old.Outcome) != DelphiLiveLedgerJson.Serialize(next.Outcome) || old.MissingAnchorReason != next.MissingAnchorReason)
                await research.AppendOutcomeAsync(next, lease, token);
        }
    }

    private async Task<bool> IsResearchMature(DateOnly date, CancellationToken token)
    {
        var slots = (await research.ReadExpectedSlotsAsync(date, date, token)).Where(s => !s.IsBenchmark).ToArray();
        var results = (await research.ReadLatestOutcomesAsync(date, date, token)).ToDictionary(o => o.SlotId);
        DateOnly endpoint = date;
        for (int i = 0; i < 4; i++)
        {
            try { endpoint = calendar.GetNextSession(endpoint); }
            catch (InvalidOperationException) { return false; }
        }
        if (clock.UtcNow <= calendar.GetSessionBounds(endpoint).CloseUtc) return false;
        if (slots.Length == 0) return true; // observed all-cash policy cohort has no missing stock labels
        return new[] { "RawReturn", "ExcessReturn", "MaximumFavourableMovement", "MaximumAdverseMovement" }.All(metric =>
        {
            var coverage = DelphiLiveCoverageCalculator.Calculate(slots.Select(slot => MetricForSlot(slot, results.GetValueOrDefault(slot.SlotId),
                DelphiLiveOutcomeHorizon.Session5, metric).State), DelphiLivePolicyDefinition.Version1);
            return coverage.Readiness is DelphiLiveCoverageReadiness.Ready or DelphiLiveCoverageReadiness.Degraded;
        });
    }

    public async Task<DelphiLiveResearchPresentation> ReadPresentationAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default)
    {
        var slots = await research.ReadExpectedSlotsAsync(from, through, cancellationToken);
        var outcomes = (await research.ReadLatestOutcomesAsync(from, through, cancellationToken)).ToDictionary(o => o.SlotId);
        var checkpoints = await research.ReadRankingCheckpointsAsync(from, through, cancellationToken);
        var dates = await evidenceSource.ReadFrozenDatesAsync(through, cancellationToken);
        var expected = dates.Where(d => d >= from).SelectMany(d => Enumerable.Range(0, 71)
            .Select(i => (d, calendar.GetSessionBounds(d).OpenUtc.AddMinutes(20 + 5 * i)))).ToArray();
        var metricRows = ImmutableArray.CreateBuilder<DelphiLiveResearchMetricSummary>();
        var rankingRows = ImmutableArray.CreateBuilder<DelphiLiveRankingMetricSummary>();
        var policy = DelphiLivePolicyDefinition.Version1;
        foreach (var horizon in Enum.GetValues<DelphiLiveOutcomeHorizon>())
        foreach (string metric in new[] { "RawReturn", "XiuReturn", "ExcessReturn", "MaximumFavourableMovement", "MaximumAdverseMovement" })
        {
            var measures = slots.Where(s => !s.IsBenchmark).Select(s => MetricForSlot(s, outcomes.GetValueOrDefault(s.SlotId), horizon, metric)).ToArray();
            metricRows.Add(new(horizon, metric, DelphiLiveCoverageCalculator.Calculate(measures.Select(m => m.State), policy),
                measures.Where(m => m.State != DelphiLiveOutcomeMetricState.Valid).GroupBy(m => m.ReasonCode).ToImmutableDictionary(g => g.Key, g => g.Count())));
            if (metric is not ("RawReturn" or "ExcessReturn")) continue;
            foreach (var lens in Enum.GetValues<DelphiLiveSourceLens>())
            {
                var comparisons = checkpoints.Where(c => c.Lens == lens).Select(c =>
                {
                    var anchors = slots.Where(s => s.SessionId == c.SessionId && s.BarEndUtc == c.BarEndUtc && !s.IsBenchmark)
                        .ToDictionary(s => s.Symbol, s => MetricForSlot(s, outcomes.GetValueOrDefault(s.SlotId), horizon, metric));
                    bool applicable = IntradayMinutes(horizon) is not int minutes || c.BarEndUtc.AddMinutes(minutes) <= calendar.GetSessionBounds(c.TradingDate).CloseUtc;
                    return new DelphiLiveCheckpointComparison(c.TradingDate, c.BarEndUtc, lens,
                        DelphiLiveResearchScorecards.CalculateBasket(c.DailyTop5, anchors, policy, applicable),
                        DelphiLiveResearchScorecards.CalculateBasket(c.ConfirmedLiveTop5, anchors, policy, applicable));
                }).ToArray();
                rankingRows.Add(new(horizon, metric, DelphiLiveResearchScorecards.Aggregate(lens, comparisons, policy, expected)));
            }
        }
        var diagnosticSource = research as IDelphiLiveDiagnosticSource;
        var historical = diagnosticSource is not null
            ? await diagnosticSource.ReadPortfolioHistoryAsync(from, through, cancellationToken)
            : (await ledgers.GetPortfoliosForSessionAsync(through, cancellationToken)).Select(p => new DelphiLivePortfolioHistoryItem(p, null)).ToArray();
        var championEvaluations = diagnosticSource is null ? Array.Empty<DelphiLiveDiagnosticEvaluation>()
            : await diagnosticSource.ReadChampionEvaluationsAsync(from, through, cancellationToken);
        return new(metricRows.ToImmutable(), rankingRows.ToImmutable(),
            DelphiLiveCoverageCalculator.Calculate(slots.Where(s => !s.IsBenchmark).Select(s => s.OperationalUsable ? DelphiLiveOutcomeMetricState.Valid : DelphiLiveOutcomeMetricState.Invalid), policy),
            DelphiLiveCoverageCalculator.Calculate(slots.Where(s => s.IsBenchmark).Select(s => s.OperationalUsable ? DelphiLiveOutcomeMetricState.Valid : DelphiLiveOutcomeMetricState.Invalid), policy),
            historical.Select(h => FillDiagnostic(h.Portfolio, calendar.GetSessionBounds(LastReportSession(h, through)).CloseUtc)).ToImmutableArray())
        {
            DiagnosticScorecards = DelphiLiveDiagnosticScorecards.Calculate(slots.ToArray(), championEvaluations.ToArray(),
                outcomes.Values.ToArray(), calendar, policy),
            PortfolioStatistics = historical.Select(history => DelphiLivePortfolioScorecard.Calculate(history.Portfolio,
                LastReportSession(history, through), calendar)).ToImmutableArray()
        };
    }

    private DateOnly LastReportSession(DelphiLivePortfolioHistoryItem history, DateOnly through)
    {
        DateOnly cutoff = history.EndExclusiveTradingDate is DateOnly end && end <= through ? calendar.GetImmediatelyPrecedingSession(end) : through;
        return calendar.IsRegularSession(cutoff) ? cutoff : calendar.GetImmediatelyPrecedingSession(cutoff);
    }

    public static DelphiLiveFillDiagnostic FillDiagnostic(DelphiLivePortfolioSnapshot portfolio, DateTime? cutoffUtc = null)
    {
        var fills = portfolio.Fills.Where(f => !cutoffUtc.HasValue || f.FilledUtc <= cutoffUtc).ToArray();
        var trades = portfolio.Positions.Where(p => p.ClosedUtc.HasValue && (!cutoffUtc.HasValue || p.ClosedUtc <= cutoffUtc)).Select(p =>
        {
            var buy = fills.Single(f => f.ActionId == p.EntryActionId);
            var sell = fills.Single(f => f.ActionId == p.ExitActionId);
            return (Cost: buy.Price * buy.Quantity, Pnl: sell.Price * sell.Quantity - buy.Price * buy.Quantity,
                BidAsk: buy.Confidence != DelphiLiveFillConfidence.EstimatedFill && sell.Confidence != DelphiLiveFillConfidence.EstimatedFill);
        }).ToArray();
        var bidAsk = trades.Where(t => t.BidAsk).ToArray();
        decimal? official = trades.Length > 0 ? trades.Sum(t => t.Pnl) / trades.Sum(t => t.Cost) : null;
        decimal? diagnostic = bidAsk.Length > 0 ? bidAsk.Sum(t => t.Pnl) / bidAsk.Sum(t => t.Cost) : null;
        int estimated = fills.Count(f => f.Confidence == DelphiLiveFillConfidence.EstimatedFill);
        var close = portfolio.Marks.LastOrDefault(m => m.Kind == DelphiLivePortfolioMarkKind.Closing && (!cutoffUtc.HasValue || m.BarEndUtc == cutoffUtc));
        return new(portfolio.PortfolioId, portfolio.Role, fills.Length, estimated,
            fills.Length == 0 ? null : (decimal)estimated / fills.Length, trades.Length, bidAsk.Length,
            official, diagnostic, diagnostic - official, close is { Complete: true, Nav: not null } ? close.Nav / portfolio.StartingCapital - 1m : null);
    }

    public static decimal? CalculateDailyReturn(DelphiLivePortfolioSnapshot portfolio, DateOnly date, DateOnly priorCanonicalSession)
    {
        var closing = portfolio.Marks.LastOrDefault(m => m.TradingDate == date && m.Kind == DelphiLivePortfolioMarkKind.Closing);
        if (closing is not { Complete: true, Nav: not null }) return null;
        var previous = portfolio.Marks.LastOrDefault(m => m.TradingDate == priorCanonicalSession && m.Kind == DelphiLivePortfolioMarkKind.Closing);
        decimal? basis = date == portfolio.EffectiveSession ? portfolio.StartingCapital
            : previous is { Complete: true, Nav: > 0m } ? previous.Nav : null;
        return basis > 0m ? closing.Nav / basis - 1m : null;
    }

    public static string ReadFrozenRegime(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("regime", out var regime) || regime.ValueKind != JsonValueKind.Object ||
                !regime.TryGetProperty("isBothBearish", out var bearish) || bearish.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !regime.TryGetProperty("isAnyBenchmarkUptrend", out var bullish) || bullish.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                bearish.GetBoolean() && bullish.GetBoolean()) return "Unavailable";
            return bearish.GetBoolean() ? "Bearish" : bullish.GetBoolean() ? "Bullish" : "Mixed";
        }
        catch (JsonException) { return "Unavailable"; }
    }

    public static Guid StableId(string identity) => new(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
    private static int? IntradayMinutes(DelphiLiveOutcomeHorizon horizon) => horizon switch
    { DelphiLiveOutcomeHorizon.Minutes20 => 20, DelphiLiveOutcomeHorizon.Minutes60 => 60, DelphiLiveOutcomeHorizon.Minutes120 => 120, DelphiLiveOutcomeHorizon.Minutes180 => 180, _ => null };
    private DelphiLiveOutcomeMetric MetricForSlot(DelphiLiveExpectedResearchSlot slot, DelphiLiveResearchOutcomeRevision? revision,
        DelphiLiveOutcomeHorizon horizon, string metric)
    {
        if (IntradayMinutes(horizon) is int minutes && slot.BarEndUtc.AddMinutes(minutes) > calendar.GetSessionBounds(slot.TradingDate).CloseUtc)
            return DelphiLiveOutcomeMetric.NotApplicable();
        if (revision is null) return new(DelphiLiveOutcomeMetricState.Pending, null, "AwaitingOutcomeCalculation");
        var result = revision?.Outcome?.Horizons.SingleOrDefault(h => h.Horizon == horizon);
        if (result is null) return DelphiLiveOutcomeMetric.Invalid(string.IsNullOrWhiteSpace(revision?.MissingAnchorReason)
            ? "MissingExpectedAnchor" : revision.MissingAnchorReason);
        return metric switch { "RawReturn" => result.RawReturn, "XiuReturn" => result.XiuReturn,
            "ExcessReturn" => result.ExcessReturn, "MaximumFavourableMovement" => result.MaximumFavourableMovement,
            "MaximumAdverseMovement" => result.MaximumAdverseMovement, _ => throw new ArgumentException("Unknown research metric.") };
    }
    private async Task RecordNewSlots(ImmutableArray<DelphiLiveExpectedResearchSlot> slots, DateOnly date, DelphiLiveLease lease, CancellationToken token)
    {
        var existing = (await research.ReadExpectedSlotsAsync(date, date, token)).Select(s => s.SlotId).ToHashSet();
        var added = slots.Where(s => !existing.Contains(s.SlotId)).ToArray();
        if (added.Length > 0) await research.RecordExpectedSlotsAsync(added, lease, token);
    }
    private static IEnumerable<DelphiLiveCohortEvidence> AllCohorts(DelphiLiveExperimentState state) =>
        state.EngineeringCohorts.Concat(state.DiscoveryCohorts).Concat(state.UntouchedCohorts).Concat(state.BaselineCohorts);
    private static bool ValidateReconstruction(DelphiLivePortfolioSnapshot portfolio) => portfolio.Fills.All(fill =>
        portfolio.Actions.Any(a => a.Intent.ActionId == fill.ActionId && a.Status == "Filled" && a.Intent.DecisionPersistedUtc < fill.FilledUtc) &&
        portfolio.Quotes.Any(q => q.QuoteId == fill.QuoteObservationId && q.ActionId == fill.ActionId && q.Observation.ReceivedUtc == fill.FilledUtc));
}
