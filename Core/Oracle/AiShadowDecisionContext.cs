#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Gates;
using Core.Trader.Promotion;

namespace Core.Oracle;

public sealed record AiShadowDailyCandidate(Guid CandidateId, string Symbol, int Rank, string Direction,
    bool Eligible, double RankingKey, string? FirstFailedGate, ImmutableArray<GateTraceEntry> Gates,
    SystemShadowRuntimeCandidate? Decision);

/// <summary>Saved candidate-account evidence, never the managed account's money or order permission.</summary>
public sealed record AiShadowSavedContext(Guid PortfolioId, Guid SessionId, DateOnly TradingDate,
    Guid PolicyId, SystemShadowPolicyConfig Policy, string Lens, int MaximumPositions,
    PaperComparisonAccount? Comparison, Guid RunId, Guid DailyStrategyId, StrategyConfig DailyConfiguration,
    ImmutableArray<TradingModelIdentity> Models, DateOnly MarketDataAsOf, DateTime PublishedUtc,
    DateTime EffectiveUtc, DateTime PolicyAssignedUtc, string RunPurpose, string AuditState,
    ImmutableArray<AiShadowDailyCandidate> Candidates, ImmutableArray<PortfolioDailyObservation> History);

public interface IAiShadowDecisionSource
{
    Task<AiShadowSavedContext?> ReadAsync(Guid portfolioId, DateOnly date, CancellationToken ct = default);
}

/// <summary>Read-only composition. Native freshness settings and SQL behavior require separate qualification.</summary>
public sealed class AiShadowDecisionContextReader(IAiShadowDecisionSource source,
    ReviewedTsxSessionCalendar calendar, TimeSpan maximumAge, TimeProvider? clock = null)
{
    public async Task<AiStrategyDecisionContext> ReadAsync(AiPortfolioCatalogEntry choice, DateOnly date,
        CancellationToken ct = default)
    {
        var saved = await source.ReadAsync(choice.PaperAccountId, date, ct);
        ct.ThrowIfCancellationRequested();
        // Do not refresh evidence timestamps, or test freshness against the clock before a slow read.
        return AiShadowDecisionContext.Build(choice, saved, calendar, date, maximumAge,
            (clock ?? TimeProvider.System).GetUtcNow());
    }
}

public static class AiShadowDecisionContext
{
    public static AiStrategyDecisionContext Build(AiPortfolioCatalogEntry choice, AiShadowSavedContext? saved,
        ReviewedTsxSessionCalendar calendar, DateOnly date, TimeSpan maximumAge, DateTimeOffset now)
    {
        choice.Validate();
        if (maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumAge));
        var strategy = choice.Strategy;
        if (strategy?.Family != TradingStrategyFamily.DailyShadow)
            throw new ArgumentException("A defined Daily Shadow choice is required.");
        var bounds = calendar.GetSessionBounds(date);
        if (saved is null) return Unavailable(choice, now, "No saved Shadow session context");
        if (saved.PortfolioId != choice.PaperAccountId || saved.SessionId == Guid.Empty || saved.TradingDate != date ||
            saved.PolicyId != strategy.PolicyVersionId || saved.Policy != strategy.ShadowPolicy ||
            saved.Comparison != choice.Comparison || CompleteTradingStrategy.ShadowSelectionPrefix + saved.Lens != strategy.Selection ||
            saved.MaximumPositions != strategy.MaximumHoldings || saved.RunId == Guid.Empty ||
            saved.DailyStrategyId != strategy.Daily.StrategyVersionId || !strategy.Daily.Matches(saved.DailyConfiguration, saved.Models) ||
            saved.MarketDataAsOf != calendar.GetImmediatelyPrecedingSession(date) ||
            saved.RunPurpose != "OfficialPaper" || saved.AuditState != "Valid" ||
            !Utc(saved.PublishedUtc) || !Utc(saved.EffectiveUtc) || !Utc(saved.PolicyAssignedUtc) ||
            saved.PublishedUtc > saved.EffectiveUtc || saved.EffectiveUtc > now || saved.PolicyAssignedUtc > now ||
            saved.Candidates.IsDefault || saved.History.IsDefault)
            throw new InvalidDataException("Saved Shadow identity, source or policy differs from the catalogue.");
        if (saved.Candidates.Any(c => c is null || c.CandidateId == Guid.Empty || string.IsNullOrWhiteSpace(c.Symbol) ||
                c.Rank < 1 || c.Rank > saved.MaximumPositions || c.Direction is not ("Buy" or "Hold" or "Sell") ||
                !double.IsFinite(c.RankingKey) || c.Gates.IsDefaultOrEmpty ||
                c.Gates.Any(g => string.IsNullOrWhiteSpace(g.GateName)) ||
                c.Gates.Select(g => g.GateName).Distinct(StringComparer.Ordinal).Count() != c.Gates.Length) ||
            saved.Candidates.Select(c => c.CandidateId).Distinct().Count() != saved.Candidates.Length ||
            saved.Candidates.Select(c => c.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Candidates.Length ||
            saved.Candidates.Select(c => c.Rank).Distinct().Count() != saved.Candidates.Length)
            throw new InvalidDataException("Saved Shadow candidates or gate traces are ambiguous.");

        var rules = ImmutableArray.CreateBuilder<AiPortfolioRule>();
        rules.Add(new("source-validity", true, "Exact current publication, model, policy and gate-profile identities; missing/conflicting data cannot be waived."));
        rules.Add(new("stock-protection", true, JsonSerializer.Serialize(new { saved.Policy.HardLossFraction, saved.Policy.TrailingLossFraction,
            Requirement = "Preserve established stops; never loosen or remove them." })));
        rules.Add(new("account-limits", true, $"Maximum {saved.MaximumPositions} strategy holdings; whole shares, actual managed cash and shared risk allowance must be rechecked."));
        rules.Add(new("shadow-entry-guidance", false, "Ordinary Shadow entry needs a completed five-minute close at or above the prior daily and five-minute closes. A saved paper decision is guidance, not managed-account entry confirmation."));
        rules.Add(new("shadow-rotation-guidance", false, "Ordinary Shadow considers losing unqualified incumbents for Session-2 rotation and exits unprofitable holdings at Session-2 close."));
        var signals = ImmutableArray.CreateBuilder<object>();
        var decisionTimes = ImmutableArray.CreateBuilder<DateTime>();
        bool complete = true;
        var boundary = new[] { saved.EffectiveUtc, saved.PolicyAssignedUtc, bounds.OpenUtc }.Max();
        foreach (var candidate in saved.Candidates.OrderBy(c => c.Rank))
        {
            bool admitted = PaperGateComparison.Admits(saved.Comparison?.Profile ?? PaperGateProfile.NormalV1,
                true, candidate.Rank, candidate.Eligible && candidate.Direction == "Buy", saved.MaximumPositions);
            var decision = candidate.Decision;
            if (decision is not null && (decision.CandidateTrackingId == Guid.Empty || decision.SessionId != saved.SessionId ||
                decision.CalibrationCandidateId != candidate.CandidateId || decision.Symbol != candidate.Symbol ||
                decision.Rank != candidate.Rank || decision.PreviousSessionClose <= 0 ||
                decision.State is not ("Pending" or "Qualified" or "Entered" or "Held" or "NoEntry" or "Blocked" or "Exited") ||
                decision.LastEvaluatedUtc is { } at && (!Utc(at) || at > now || at < boundary)))
                throw new InvalidDataException("Shadow decision does not match the adopted candidate or causal boundary.");
            if (admitted)
            {
                if (decision?.LastEvaluatedUtc is not { } observed) complete = false;
                else
                {
                    decisionTimes.Add(observed);
                    if (decision.ReasonCode is not ("Qualified" or "BelowPreviousSessionClose" or "FallingFromPreviousFiveMinuteClose")) complete = false;
                }
            }
            foreach (var gate in candidate.Gates)
            {
                if (!DiscretionaryGate(gate.GateName) && !gate.Passed) complete = false;
                rules.Add(new($"daily:{candidate.CandidateId:D}:{gate.GateName}", !DiscretionaryGate(gate.GateName),
                    $"{candidate.Symbol}: {gate.GateName} recorded {(gate.Passed ? "pass" : "fail")}. {gate.Reason} " +
                    "Only recorded gates were evaluated; a missing gate is not a pass."));
            }
            signals.Add(new { candidate.CandidateId, candidate.Symbol, candidate.Rank, candidate.RankingKey,
                candidate.Direction, candidate.Eligible, candidate.FirstFailedGate, candidate.Gates,
                GateProfile = (saved.Comparison?.Profile ?? PaperGateProfile.NormalV1).ToString(), AdmittedByPaperProfile = admitted,
                Decision = decision, Scope = "Candidate paper account only; no managed-account action or re-entry authority" });
        }
        // Without dated intraday decisions this adapter supplies disclosure, not selectable fresh context.
        var observedUtc = decisionTimes.Count == 0 ? new DateTimeOffset(saved.PublishedUtc) : new DateTimeOffset(decisionTimes.Max());
        var expiry = decisionTimes.Count == 0 ? now : new DateTimeOffset(decisionTimes.Min()).Add(maximumAge);
        if (expiry > bounds.CloseUtc) expiry = bounds.CloseUtc;
        bool usable = complete && decisionTimes.Count > 0 && expiry > now && now >= bounds.OpenUtc && now < bounds.CloseUtc;
        if (saved.History.Any(h => h.Date > date || h.IsClosing && h.AsOfUtc is null || h.AsOfUtc is { } at && (!Utc(at) || at > now)))
            throw new InvalidDataException("Performance history contains future or undated-zone evidence.");
        var history = saved.History.Select(h => h.Date).Distinct().Order().Select(d =>
            PortfolioDailyPerformance.Calculate(saved.History, d)).ToImmutableArray();
        var evidence = ImmutableArray.Create(
            new AiMarketEvidence("shadow-source", saved.PublishedUtc, JsonSerializer.Serialize(new { saved.PortfolioId,
                saved.SessionId, saved.RunId, saved.DailyStrategyId, saved.MarketDataAsOf, saved.PublishedUtc, saved.EffectiveUtc,
                saved.PolicyId, saved.PolicyAssignedUtc, saved.Comparison, Usable = usable,
                Limitation = "Saved paper guidance only; current managed-account checks and post-closure entry confirmation remain required." })),
            new AiMarketEvidence("shadow-signals", observedUtc, JsonSerializer.Serialize(signals.ToImmutable())),
            new AiMarketEvidence("shadow-performance", saved.History.Where(h => h.AsOfUtc.HasValue)
                .Select(h => new DateTimeOffset(h.AsOfUtc!.Value)).DefaultIfEmpty(observedUtc).Max(), JsonSerializer.Serialize(new {
                Scope = "Candidate paper account daily observations in the requested history window; may span earlier policies. Descriptive only, not strategy superiority or available funds.",
                Days = history, MissingDates = "Unobserved dates are omitted, never zero returns; incomplete sessions retain null returns." })));
        // History can be recorded later than the last signal, but never extends the signal's expiry.
        return new(choice.Id, choice.Version, evidence.Max(e => e.ObservedUtc), expiry, usable,
            rules.ToImmutable(), evidence);
    }

    private static bool Utc(DateTime time) => time.Kind == DateTimeKind.Utc;
    private static bool DiscretionaryGate(string name) => name is "Regime" or "Breadth" or "Granville" or
        "DownProbability" or "Setup" or "TrendConfirmation" or "Direction" or "Composite";
    private static AiStrategyDecisionContext Unavailable(AiPortfolioCatalogEntry choice, DateTimeOffset now, string reason) =>
        new(choice.Id, choice.Version, now, now, false, [], [new("shadow-unavailable", now, reason)]);
}
