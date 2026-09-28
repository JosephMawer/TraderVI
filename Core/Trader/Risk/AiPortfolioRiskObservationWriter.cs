#nullable enable
using System;
using System.Collections.Immutable;

namespace Core.Trader.Risk;

public sealed record AiRiskObservationResult(bool Accepted, bool Advanced,
    AiRiskJournalSnapshot Snapshot, PortfolioRiskAssessment Assessment)
{
    public override string ToString() => "AI risk observation result (not order authority)";
}

/// <summary>Shared persist-before-assess boundary. Source adapters qualify evidence; no runtime or order calls.</summary>
internal sealed class AiPortfolioRiskObservationWriter(AiPortfolioRiskJournal journal, AiPortfolioRiskPolicy policy)
{
    internal const string RejectedObservation = "PortfolioValuationRejected";
    private readonly object sync = new();

    internal AiRiskObservationResult Apply(Func<PortfolioRiskValuation?, PortfolioRiskValuation> qualify,
        DateTimeOffset now, bool hasFreshAiReview, ImmutableArray<string> independentBlocks, bool initialize)
    {
        if (independentBlocks.IsDefault)
            throw new ArgumentException("Independent checks must be supplied.", nameof(independentBlocks));
        lock (sync)
        {
            // Storage failures propagate. Never turn unreadable/uncertain history into empty state.
            var current = journal.ReadCurrent();
            PortfolioRiskValuation value;
            try
            {
                if (initialize != (current.State is null))
                    throw new InvalidOperationException("Explicit initialization and an existing round cannot be interchanged.");
                value = qualify(current.State?.LastValuation);
                if (current.State is { } prior) _ = policy.Observe(prior, value, now);
                else _ = policy.Start(value, now);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
            {
                return Result(journal.ReadCurrent(), false, false, now, hasFreshAiReview, independentBlocks.Add(RejectedObservation));
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
                // Includes competing transitions. Return current protection, never old allowance.
                return Result(journal.ReadCurrent(), false, false, now, hasFreshAiReview,
                    independentBlocks.Add(RejectedObservation));
            }
            return Result(saved, true, saved.Revision != current.Revision, now, hasFreshAiReview, independentBlocks);
        }
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
