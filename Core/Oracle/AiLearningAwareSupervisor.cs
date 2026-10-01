#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle.Learning;

namespace Core.Oracle;

/// <summary>Supplies evaluated learning before every review and durably records its disposition.</summary>
public sealed class AiLearningAwareSupervisor(AiPortfolioReviewer reviewer, AiLearningJournal journal,
    TimeSpan refreshInterval, TimeProvider? time = null, IAiReviewStore? reviewStore = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    // A host invokes this on its existing tick. No timer or paid model call is created here.
    public AiLearningContext? RefreshLearningIfDue(string contextId, string accountMode, ImmutableArray<AiLearningScope> scopes) =>
        journal.Consult("supervisor", contextId, accountMode, scopes, clock.GetUtcNow(), refreshInterval);

    public async Task<AiPortfolioProposal> ReviewAsync(AiPortfolioPacket packet, string accountMode, CancellationToken ct = default)
        => (await ReviewWithEvidenceAsync(packet, accountMode, ct)).Proposal!;

    /// <summary>Returns the saved learning-enriched evidence so a host cannot overstate review freshness.</summary>
    public async Task<AiLearningReview> ReviewWithEvidenceAsync(AiPortfolioPacket packet, string accountMode, CancellationToken ct = default,
        bool requestTiming = false)
    {
        ct.ThrowIfCancellationRequested();
        if (reviewStore is not null && reviewStore.Scope.AccountMode != accountMode)
            throw new ArgumentException("Review storage must match the account mode.");
        // Freeze before reading learning or awaiting the model; the same packet is used for durable evidence.
        var frozen = JsonSerializer.Deserialize<AiPortfolioPacket>(JsonSerializer.Serialize(packet))!;
        AiPortfolioReviewer.ValidatePacket(frozen, clock.GetUtcNow());
        if (frozen.Learning != null || frozen.EvaluatedLessons.Length != 0)
            throw new ArgumentException("The learning module owns reviewed lesson inputs.");
        var scopes = frozen.Candidates.Select(c => new AiLearningScope(c.Id, c.StrategyVersion)).ToImmutableArray();
        var learning = journal.Consult("supervisor", frozen.PacketId.ToString("D"), accountMode,
            scopes, clock.GetUtcNow(), refreshInterval, force: true)!;
        frozen = frozen with { SchemaVersion = requestTiming ? 3 : 2, Learning = learning };
        var pending = new AiLearningReview(frozen.PacketId, learning.ConsultationId, frozen, null, "Pending", clock.GetUtcNow());
        journal.RecordReview(pending);
        // Persist the exact learning-enriched input before any paid request. A storage failure stops admission.
        if (reviewStore is not null) await reviewStore.SaveAsync(pending, ct);
        AiPortfolioProposal proposal;
        try { proposal = await reviewer.ReviewAsync(frozen, ct); }
        catch
        {
            // Preserve a rejected attempt even on cancellation. Raw model evidence is in the call journal.
            var rejected = new AiLearningReview(frozen.PacketId, learning.ConsultationId, frozen, null, "Rejected", clock.GetUtcNow());
            journal.RecordReview(rejected);
            if (reviewStore is not null) await reviewStore.SaveAsync(rejected, CancellationToken.None);
            throw;
        }
        var reviewed = new AiLearningReview(frozen.PacketId, learning.ConsultationId, frozen, proposal, "Reviewed", clock.GetUtcNow());
        journal.RecordReview(reviewed);
        if (reviewStore is not null) await reviewStore.SaveAsync(reviewed, ct);
        return reviewed;
    }
}
