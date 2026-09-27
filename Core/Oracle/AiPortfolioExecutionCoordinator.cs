#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Oracle.Learning;

namespace Core.Oracle;

public sealed record AiPortfolioExecutionPreparation(AiPortfolioProposal? Proposal, AiExecutionLearningCheck LearningCheck);

/// <summary>
/// Learning-aware preparation only. No order transport, strategy assignment or protective-exit path.
/// The established execution workflow must separately validate current account, policy and authority.
/// </summary>
public sealed class AiPortfolioExecutionCoordinator(AiLearningJournal journal, TimeSpan refreshInterval, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public AiLearningContext? RefreshLearningIfDue(string contextId, string accountMode, ImmutableArray<AiLearningScope> scopes) =>
        journal.Consult("execution", contextId, accountMode, scopes, clock.GetUtcNow(), refreshInterval);

    public AiPortfolioExecutionPreparation Prepare(Guid reviewId, string accountMode, string? currentActivePortfolioId)
    {
        var review = journal.ReadReview(reviewId);
        if (review.Status != "Reviewed" || review.Proposal is null || review.Packet.Learning is not { } previous ||
            previous.AccountMode != accountMode || review.Packet.ActivePortfolioId != currentActivePortfolioId)
            throw new InvalidDataException("Execution preparation requires the recorded review and unchanged account/assignment context.");
        var now = clock.GetUtcNow();
        var scopes = review.Packet.Candidates.Select(c => new AiLearningScope(c.Id, c.StrategyVersion)).ToImmutableArray();
        var current = journal.Consult("execution", reviewId.ToString("D"), accountMode, scopes, now, refreshInterval, force: true)!;
        bool same = current.SnapshotId == previous.SnapshotId && current.SnapshotVersion == previous.SnapshotVersion &&
            current.Lessons.Select(l => l.LessonId).SequenceEqual(previous.Lessons.Select(l => l.LessonId));
        string reason = "The supervisor assessed the same evaluated lessons; execution policy and risk still require validation.";
        try
        {
            AiPortfolioReviewer.ValidatePacket(review.Packet, now);
            _ = AiPortfolioReviewer.ParseAndValidate(JsonSerializer.Serialize(review.Proposal, Json), review.Packet);
        }
        catch (InvalidDataException) { same = false; reason = "Decision evidence or its learning context is no longer valid; obtain a new review."; }
        if (!same) reason = "Learning or decision evidence changed since the supervisor review; obtain a new review.";
        var assessments = same ? review.Proposal.LessonAssessments!.ToImmutableArray() : [];
        var check = new AiExecutionLearningCheck(Guid.NewGuid(), reviewId, current.ConsultationId,
            same ? "Current" : "ReviewRequired", reason, assessments, clock.GetUtcNow());
        journal.RecordExecutionCheck(check);
        return new(same ? review.Proposal : null, check);
    }
}
