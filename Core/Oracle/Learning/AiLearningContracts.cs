#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Core.Oracle.Learning;

public sealed record AiLearningScope(string PortfolioId, string StrategyVersion);
public sealed record AiEvaluatedLesson(Guid LessonId, AiLearningScope Scope, string AccountMode,
    string Summary, string EvaluationReference, string EvaluationResult,
    ImmutableArray<string> OutcomeReferences, DateTimeOffset EvaluatedUtc, DateTimeOffset ExpiresUtc);
public sealed record AiLearningSnapshot(Guid SnapshotId, long Version, DateTimeOffset PublishedUtc,
    DateTimeOffset ExpiresUtc, ImmutableArray<AiEvaluatedLesson> Lessons);
public sealed record AiLessonAssessment(Guid LessonId, string Disposition, string Reason);
public sealed record AiLearningContext(Guid ConsultationId, Guid SnapshotId, long SnapshotVersion,
    string AccountMode, DateTimeOffset PublishedUtc, DateTimeOffset ExpiresUtc, ImmutableArray<AiEvaluatedLesson> Lessons);
public sealed record AiLearningConsultation(Guid ConsultationId, string Consumer, string ContextId,
    string AccountMode, ImmutableArray<AiLearningScope> Scopes, DateTimeOffset ConsultedUtc,
    Guid SnapshotId, long SnapshotVersion, ImmutableArray<Guid> LessonIds);
public sealed record AiLearningReview(Guid ReviewId, Guid ConsultationId, AiPortfolioPacket Packet,
    AiPortfolioProposal? Proposal, string Status, DateTimeOffset RecordedUtc);
public sealed record AiExecutionLearningCheck(Guid CheckId, Guid ReviewId, Guid ConsultationId,
    string Status, string Reason, ImmutableArray<AiLessonAssessment> Assessments, DateTimeOffset RecordedUtc);

/// <summary>Validation of provenance and scope, not statistical qualification of a lesson's claims.</summary>
public static class AiLearningContracts
{
    public static void Validate(AiLearningSnapshot snapshot)
    {
        if (snapshot.SnapshotId == Guid.Empty || snapshot.Version <= 0 || snapshot.ExpiresUtc <= snapshot.PublishedUtc ||
            snapshot.Lessons.IsDefault || snapshot.Lessons.Length > 50 ||
            snapshot.Lessons.Select(l => l?.LessonId).Distinct().Count() != snapshot.Lessons.Length)
            throw new InvalidDataException("Learning snapshot has invalid identity, lifetime or lesson count.");
        foreach (var lesson in snapshot.Lessons)
        {
            if (lesson is null || lesson.LessonId == Guid.Empty || !ValidScope(lesson.Scope) ||
                !ValidMode(lesson.AccountMode) || !Text(lesson.Summary, 4000) ||
                !Text(lesson.EvaluationReference, 1024) || !Text(lesson.EvaluationResult, 4000) ||
                lesson.OutcomeReferences.IsDefaultOrEmpty || lesson.OutcomeReferences.Length > 100 ||
                lesson.OutcomeReferences.Any(r => !Text(r, 1024)) ||
                lesson.OutcomeReferences.Distinct(StringComparer.Ordinal).Count() != lesson.OutcomeReferences.Length ||
                lesson.EvaluatedUtc > snapshot.PublishedUtc || lesson.ExpiresUtc <= snapshot.PublishedUtc ||
                lesson.ExpiresUtc > snapshot.ExpiresUtc)
                throw new InvalidDataException("Learning lesson lacks valid evaluated evidence, scope or lifetime.");
        }
    }

    public static bool ValidScope(AiLearningScope? scope) => scope is not null &&
        Text(scope.PortfolioId, 256) && Text(scope.StrategyVersion, 256);
    public static bool ValidMode(string mode) => mode is "paper" or "live";
    internal static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;

    public static void ValidateAssessments(AiLearningContext context, AiLessonAssessment[]? assessments)
    {
        if (assessments is null || assessments.Length != context.Lessons.Length ||
            assessments.Any(a => a is null || a.Disposition is not ("apply" or "disregard" or "not_applicable") || !Text(a.Reason, 4000)) ||
            !assessments.Select(a => a.LessonId).Order().SequenceEqual(context.Lessons.Select(l => l.LessonId).Order()))
            throw new InvalidDataException("Every consulted lesson requires exactly one explained assessment.");
    }
}
