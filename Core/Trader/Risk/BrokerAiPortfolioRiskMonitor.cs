#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Trader.Promotion;

namespace Core.Trader.Risk;

/// <summary>
/// Producer-owned reconciled account facts, never a display balance or a net-deposit delta.
/// BindingId is the journal's account scope. Native collection/qualification remains external.
/// </summary>
public sealed record BrokerAiRiskObservation(Guid PortfolioId, DateOnly TradingDate,
    BrokerRiskBoundaryBook Book, BrokerRiskNoFlowEvidence NoFlows)
{
    public override string ToString() => "Broker AI risk observation (financial data omitted)";
}

public sealed record AiRiskObservationResult(bool Accepted, bool Advanced,
    AiRiskJournalSnapshot Snapshot, PortfolioRiskAssessment Assessment)
{
    public override string ToString() => "AI risk observation result (not order authority)";
}

/// <summary>
/// Offline adapter from the existing reconciled broker book/no-flow contracts to durable AI risk.
/// Exact contemporaneous marks only; opening/saved-price and broker-graph contracts are not substituted.
/// No collection, model calls or orders. The host must qualify the producer and maximum evidence age,
/// pass all independent holds, and block new risk if storage throws. Existing protection stays separate.
/// </summary>
public sealed class BrokerAiPortfolioRiskMonitor
{
    public const string ValuationContract = "BrokerAiExactBookMarksV1";
    public const string RejectedObservation = "PortfolioValuationRejected";
    private readonly AiPortfolioRiskJournal journal;
    private readonly AiPortfolioRiskPolicy policy;
    private readonly TimeSpan maximumEvidenceAge;
    private readonly object sync = new();

    public BrokerAiPortfolioRiskMonitor(AiPortfolioRiskJournal journal, AiPortfolioRiskPolicy policy,
        TimeSpan maximumEvidenceAge)
    {
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        if (journal.Scope.Mode != AiRiskAccountMode.Live || maximumEvidenceAge <= TimeSpan.Zero)
            throw new ArgumentException("A live account journal and explicitly qualified evidence age are required.");
        this.maximumEvidenceAge = maximumEvidenceAge;
    }

    // Initialization is deliberately separate from monitoring: a missing round is never reset on a poll.
    public AiRiskObservationResult Start(BrokerAiRiskObservation? observation, DateTimeOffset now,
        bool hasFreshAiReview, ImmutableArray<string> independentBlocks) =>
        Process(observation, now, hasFreshAiReview, independentBlocks, initialize: true);

    public AiRiskObservationResult Observe(BrokerAiRiskObservation? observation, DateTimeOffset now,
        bool hasFreshAiReview, ImmutableArray<string> independentBlocks) =>
        Process(observation, now, hasFreshAiReview, independentBlocks, initialize: false);

    private AiRiskObservationResult Process(BrokerAiRiskObservation? observation, DateTimeOffset now,
        bool hasFreshAiReview, ImmutableArray<string> independentBlocks, bool initialize)
    {
        if (independentBlocks.IsDefault)
            throw new ArgumentException("Independent checks must be supplied.", nameof(independentBlocks));
        lock (sync)
        {
            // Storage failures propagate. Never turn an unreadable/uncertain journal into empty state.
            var current = journal.ReadCurrent();
            PortfolioRiskValuation value;
            try
            {
                if (initialize != (current.State is null))
                    throw new InvalidOperationException("Explicit initialization and an existing round cannot be interchanged.");
                value = Qualify(observation, current.State?.LastValuation, now);
                if (current.State is { } prior) _ = policy.Observe(prior, value, now);
                else _ = policy.Start(value, now);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
            {
                return Result(current, false, false, now, hasFreshAiReview, independentBlocks.Add(RejectedObservation));
            }

            // A retry cannot extend expiry or manufacture another fresh observation/history entry.
            if (value == current.State?.LastValuation)
                return Result(current, true, false, now, hasFreshAiReview, independentBlocks);

            AiRiskJournalSnapshot saved;
            try
            {
                saved = journal.Apply(new(value.EvidenceId, current.Revision,
                    initialize ? AiRiskTransitionKind.Start : AiRiskTransitionKind.Observe, value), now);
            }
            catch (InvalidOperationException)
            {
                // Includes a competing transition. Re-read current protection, never return old allowance.
                return Result(journal.ReadCurrent(), false, false, now, hasFreshAiReview,
                    independentBlocks.Add(RejectedObservation));
            }
            return Result(saved, true, saved.Revision != current.Revision, now, hasFreshAiReview, independentBlocks);
        }
    }

    private PortfolioRiskValuation Qualify(BrokerAiRiskObservation? observation,
        PortfolioRiskValuation? previous, DateTimeOffset now)
    {
        if (observation is null || observation.Book is not { } book || observation.NoFlows is not { } flows ||
            observation.PortfolioId != journal.Scope.PortfolioId || book.BindingId != journal.Scope.AccountScopeId ||
            flows.BindingId != book.BindingId || book.EvidenceId == Guid.Empty || flows.EvidenceId == Guid.Empty ||
            book.At == default || book.VerifiedAt < book.At || book.VerifiedAt > now || book.Cash < 0m ||
            book.Holdings.IsDefault || flows.From == default || flows.Through != book.At || flows.From > flows.Through ||
            flows.ObservedAt < flows.Through || flows.ObservedAt > now ||
            !CompleteTradingStrategy.IsHash(flows.SourceFingerprint))
            throw new InvalidOperationException("Complete owned book and identified no-flow coverage are required.");

        // Initial capital is captured at this instant. Later observations cover the complete gap,
        // including overnight. An exact retry must retain its original source bytes below.
        bool retry = previous?.EvidenceId == book.EvidenceId;
        if (!retry && flows.From != (previous?.At ?? book.At))
            throw new InvalidOperationException("Cash-flow coverage must start at the saved valuation boundary.");
        if (book.Holdings.Any(h => h is null || h.PositionId == Guid.Empty ||
                string.IsNullOrWhiteSpace(h.Symbol) || h.Quantity <= 0 || h.SavedPrice is not > 0m ||
                h.SavedPriceEvidenceId is null || h.SavedPriceEvidenceId == Guid.Empty || h.SavedPriceAt != book.At) ||
            book.Holdings.Select(h => h.PositionId).Distinct().Count() != book.Holdings.Length ||
            book.Holdings.Select(h => h.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != book.Holdings.Length ||
            book.Holdings.Select(h => h.SavedPriceEvidenceId).Distinct().Count() != book.Holdings.Length)
            throw new InvalidOperationException("Every actual holding needs one identified exact-time price.");

        decimal value = book.Cash + book.Holdings.Sum(h => h.Quantity * h.SavedPrice!.Value);
        DateTimeOffset validUntil = book.At.Add(maximumEvidenceAge);
        if (validUntil < now) throw new InvalidOperationException("Portfolio evidence is stale.");
        // Store the source, not just caller-asserted booleans. Stable ordering makes a reordered book
        // an exact retry; any changed quantity, mark or reconciliation evidence changes provenance.
        var canonical = observation with
        {
            Book = book with { Holdings = book.Holdings.OrderBy(h => h.PositionId).ToImmutableArray() }
        };
        string json = JsonSerializer.Serialize(canonical);
        var source = new PortfolioRiskValuationSource(ValuationContract, json,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
        return new(observation.PortfolioId, book.EvidenceId, observation.TradingDate,
            book.At, validUntil, value, true, true) { Source = source };
    }

    private AiRiskObservationResult Result(AiRiskJournalSnapshot snapshot, bool accepted, bool advanced,
        DateTimeOffset now, bool hasFreshAiReview, ImmutableArray<string> blocks)
    {
        var assessment = snapshot.State is { } state
            ? policy.Assess(state, now, hasFreshAiReview, blocks)
            : new PortfolioRiskAssessment(PortfolioRiskProfile.AiContinuousRounds, 0m, false,
                blocks.Add("RiskRoundNotInitialized"));
        return new(accepted, advanced, snapshot, assessment);
    }
}
