#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Oracle.Learning;
using Core.Oracle.Llm;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiLearningWorkflowTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-learning-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 23, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly ImmutableArray<AiLearningScope> Scopes = [new("breakout", "v1")];
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly Clock clock = new(Now);
    private AiLearningJournal NewJournal() => new(directory, initializeNew: true);
    private static AiEvaluatedLesson Lesson(string mode = "paper") => new(Guid.NewGuid(), Scopes[0], mode,
        "Treat this evaluated observation as advisory.", "synthetic/evaluation/1", "Mixed outcomes; no proven edge.",
        ["synthetic/outcome/1", "synthetic/outcome/2"], Now.AddMinutes(-10), Now.AddHours(1));
    private static AiLearningSnapshot Snapshot(params AiEvaluatedLesson[] lessons) =>
        new(Guid.NewGuid(), 1, Now, Now.AddHours(1), lessons.ToImmutableArray());
    private static AiPortfolioPacket Packet() => new(Guid.NewGuid(), 1, Now, Now.AddMinutes(30), null,
        [new("breakout", "v1", "DailyShadow", true, "Synthetic portfolio", [new("stop", true, "Keep protection")])],
        [new("observation", Now, "Synthetic evidence")], []);

    [Fact]
    public async Task SupervisorUsesVersionedLessonsAndCoordinatorRechecksTheirRecordedDisposition()
    {
        using var journal = NewJournal();
        var lesson = Lesson();
        journal.Publish(Snapshot(lesson), Now);
        var fake = new FakeClient(request =>
        {
            Assert.Equal("tradervi_portfolio_proposal_v2", request.OutputSchema!.Name);
            var packet = JsonSerializer.Deserialize<AiPortfolioPacket>(request.UserPrompt, Json)!;
            Assert.Equal(2, packet.SchemaVersion);
            Assert.Equal(lesson.LessonId, Assert.Single(packet.Learning!.Lessons).LessonId);
            Assert.Equal("Pending", journal.ReadReview(packet.PacketId).Status);
            return Response(packet);
        });
        var packet = Packet();
        var supervisor = Supervisor(journal, fake);
        await supervisor.ReviewAsync(packet, "paper");
        var saved = journal.ReadReview(packet.PacketId);
        Assert.Equal("Reviewed", saved.Status);
        Assert.Equal("apply", Assert.Single(saved.Proposal!.LessonAssessments!).Disposition);
        var coordinator = new AiPortfolioExecutionCoordinator(journal, Interval, clock);
        var preparation = coordinator.Prepare(packet.PacketId, "paper", null);
        Assert.Equal("Current", preparation.LearningCheck.Status);
        Assert.Single(preparation.LearningCheck.Assessments);
        Assert.Equal(new[] { "supervisor", "execution" }, journal.ReadConsultations().Select(c => c.Consumer));
        Assert.Single(journal.ReadExecutionChecks());
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public void PeriodicConsultationIsIndependentPerConsumerAndSurvivesRestart()
    {
        using (var journal = NewJournal())
        {
            journal.Publish(Snapshot(Lesson()), Now);
            Assert.NotNull(journal.Consult("supervisor", "account", "paper", Scopes, Now, Interval));
            Assert.NotNull(journal.Consult("execution", "account", "paper", Scopes, Now, Interval));
        }
        using var reopened = new AiLearningJournal(directory);
        Assert.Null(reopened.Consult("supervisor", "account", "paper", Scopes, Now.AddMinutes(4), Interval));
        Assert.Null(reopened.Consult("execution", "account", "paper", Scopes, Now.AddMinutes(4), Interval));
        Assert.NotNull(reopened.Consult("supervisor", "account", "paper", Scopes, Now.AddMinutes(5), Interval));
        Assert.NotNull(reopened.Consult("execution", "account", "paper", Scopes, Now.AddMinutes(5), Interval));
        Assert.Equal(4, reopened.ReadConsultations().Length);
    }

    [Fact]
    public void ChangedSnapshotOrExpiredLessonRefreshesBeforeInterval()
    {
        using var journal = NewJournal();
        var first = Lesson() with { ExpiresUtc = Now.AddMinutes(1) };
        journal.Publish(Snapshot(first), Now);
        Assert.Single(journal.Consult("execution", "account", "paper", Scopes, Now, Interval)!.Lessons);
        Assert.Empty(journal.Consult("execution", "account", "paper", Scopes, Now.AddMinutes(1), Interval)!.Lessons);
        journal.Publish(Snapshot(Lesson()) with { Version = 2, PublishedUtc = Now.AddMinutes(2) }, Now.AddMinutes(2));
        Assert.Equal(2, journal.Consult("execution", "account", "paper", Scopes, Now.AddMinutes(2), Interval)!.SnapshotVersion);
    }

    [Fact]
    public void ConsultationSeparatesPaperLiveAndExactStrategyVersion()
    {
        using var journal = NewJournal();
        var paper = Lesson();
        var live = Lesson("live");
        var old = Lesson() with { Scope = new("breakout", "v0") };
        journal.Publish(Snapshot(paper, live, old), Now);
        Assert.Equal(paper.LessonId, Assert.Single(journal.Consult("supervisor", "account", "paper", Scopes, Now, Interval)!.Lessons).LessonId);
        Assert.Equal(live.LessonId, Assert.Single(journal.Consult("execution", "account", "live", Scopes, Now, Interval)!.Lessons).LessonId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("no-reason")]
    [InlineData("execute")]
    public async Task MissingOrInventedLessonAssessmentsCannotCompleteAReview(string failure)
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(Lesson()), Now);
        var fake = new FakeClient(request =>
        {
            var packet = JsonSerializer.Deserialize<AiPortfolioPacket>(request.UserPrompt, Json)!;
            var proposal = Proposal(packet);
            var assessment = proposal.LessonAssessments![0];
            proposal = proposal with { LessonAssessments = failure switch
            {
                "missing" => null,
                "duplicate" => [assessment, assessment],
                "unknown" => [assessment with { LessonId = Guid.NewGuid() }],
                "no-reason" => [assessment with { Reason = "" }],
                _ => [assessment with { Disposition = "execute" }]
            } };
            return JsonSerializer.Serialize(proposal, Json);
        });
        var packet = Packet();
        await Assert.ThrowsAsync<InvalidDataException>(() => Supervisor(journal, fake).ReviewAsync(packet, "paper"));
        Assert.Equal("Rejected", journal.ReadReview(packet.PacketId).Status);
        Assert.Throws<InvalidDataException>(() => new AiPortfolioExecutionCoordinator(journal, Interval, clock).Prepare(packet.PacketId, "paper", null));
    }

    [Fact]
    public async Task LearningChangeAfterReviewRequiresNewReviewWithoutAnotherModelCall()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(Lesson()), Now);
        var fake = ValidClient();
        var packet = Packet();
        await Supervisor(journal, fake).ReviewAsync(packet, "paper");
        clock.Now = Now.AddMinutes(1);
        journal.Publish(Snapshot(Lesson()) with { Version = 2, PublishedUtc = clock.Now }, clock.Now);
        var result = new AiPortfolioExecutionCoordinator(journal, Interval, clock).Prepare(packet.PacketId, "paper", null);
        Assert.Equal("ReviewRequired", result.LearningCheck.Status);
        Assert.Null(result.Proposal);
        Assert.Empty(result.LearningCheck.Assessments);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task ExpiredDecisionOrLessonCannotPassExecutionPreparation()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(Lesson() with { ExpiresUtc = Now.AddMinutes(1) }), Now);
        var packet = Packet();
        await Supervisor(journal, ValidClient()).ReviewAsync(packet, "paper");
        clock.Now = Now.AddMinutes(1);
        Assert.Equal("ReviewRequired", new AiPortfolioExecutionCoordinator(journal, Interval, clock)
            .Prepare(packet.PacketId, "paper", null).LearningCheck.Status);
    }

    [Fact]
    public async Task ModelFailureIsRecordedAndCannotBeRetriedUnderTheSameIdentity()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(), Now);
        var fake = new FakeClient(_ => throw new OperationCanceledException());
        var packet = Packet();
        var supervisor = Supervisor(journal, fake);
        await Assert.ThrowsAsync<OperationCanceledException>(() => supervisor.ReviewAsync(packet, "paper"));
        await Assert.ThrowsAsync<InvalidDataException>(() => supervisor.ReviewAsync(packet, "paper"));
        Assert.Equal("Rejected", journal.ReadReview(packet.PacketId).Status);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task NoSnapshotOrUnversionedInjectedLessonsNeverReachTheModel()
    {
        using var journal = NewJournal();
        var fake = ValidClient();
        var supervisor = Supervisor(journal, fake);
        await Assert.ThrowsAsync<InvalidDataException>(() => supervisor.ReviewAsync(Packet(), "paper"));
        journal.Publish(Snapshot(), Now);
        await Assert.ThrowsAsync<ArgumentException>(() => supervisor.ReviewAsync(Packet() with { EvaluatedLessons = ["Unreviewed claim"] }, "paper"));
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task SavedReviewCannotBeRedirectedToAnotherModeOrAssignment()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(), Now);
        var packet = Packet();
        await Supervisor(journal, ValidClient()).ReviewAsync(packet, "paper");
        var coordinator = new AiPortfolioExecutionCoordinator(journal, Interval, clock);
        Assert.Throws<InvalidDataException>(() => coordinator.Prepare(packet.PacketId, "live", null));
        Assert.Throws<InvalidDataException>(() => coordinator.Prepare(packet.PacketId, "paper", "other"));
        Assert.Empty(journal.ReadExecutionChecks());
    }

    [Fact]
    public async Task MutatingReturnedProposalCannotRewriteTheRecordedDecision()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(Lesson()), Now);
        var packet = Packet();
        var proposal = await Supervisor(journal, ValidClient()).ReviewAsync(packet, "paper");
        proposal.LessonAssessments![0] = proposal.LessonAssessments[0] with { Disposition = "execute" };
        var read = journal.ReadReview(packet.PacketId);
        read.Packet.Candidates[0] = read.Packet.Candidates[0] with { Eligible = false };
        Assert.Equal("Current", new AiPortfolioExecutionCoordinator(journal, Interval, clock).Prepare(packet.PacketId, "paper", null).LearningCheck.Status);
    }

    [Fact]
    public void EvaluatedLessonPublicationRequiresProvenanceAndCannotRewriteHistory()
    {
        using var journal = NewJournal();
        var lesson = Lesson();
        Assert.Throws<InvalidDataException>(() => journal.Publish(Snapshot(lesson with { OutcomeReferences = [] }), Now));
        Assert.Throws<InvalidDataException>(() => journal.Publish(Snapshot(lesson with { EvaluationReference = "" }), Now));
        journal.Publish(Snapshot(lesson), Now);
        Assert.Throws<InvalidOperationException>(() => journal.Publish(Snapshot(lesson), Now));
        Assert.Throws<InvalidOperationException>(() => journal.Publish(Snapshot(lesson with { Summary = "Rewritten" }) with { Version = 2 }, Now));
        Assert.Throws<InvalidDataException>(() => journal.Publish(Snapshot(lesson) with { Version = 2, PublishedUtc = Now.AddMinutes(1) }, Now));
        Assert.Throws<InvalidDataException>(() => journal.Publish(Snapshot(lesson) with { Version = 2, PublishedUtc = Now.AddMinutes(-1) }, Now));
    }

    [Fact]
    public void CorruptionInterruptedSaveExclusiveOwnerAndLostHistoryFailClosed()
    {
        using (var journal = NewJournal())
        {
            journal.Publish(Snapshot(Lesson()), Now);
            Assert.Throws<IOException>(() => new AiLearningJournal(directory));
            File.WriteAllText(Path.Combine(directory, "ai-learning.json.pending"), "interrupted");
            Assert.Throws<InvalidDataException>(() => journal.Consult("execution", "account", "paper", Scopes, Now, Interval));
        }
        Assert.Throws<InvalidDataException>(() => new AiLearningJournal(directory));
        File.Delete(Path.Combine(directory, "ai-learning.json.pending"));
        string path = Path.Combine(directory, "ai-learning.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Mixed outcomes", "Altered outcomes"));
        Assert.Throws<InvalidDataException>(() => new AiLearningJournal(directory));
        File.Delete(path);
        Assert.Throws<InvalidDataException>(() => new AiLearningJournal(directory));
        Assert.Throws<InvalidOperationException>(() => new AiLearningJournal(directory, initializeNew: true));
    }

    [Fact]
    public void StaleSnapshotAndBackwardClockAreUnavailable()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(), Now);
        journal.Consult("execution", "account", "paper", Scopes, Now, Interval);
        Assert.Throws<InvalidOperationException>(() => journal.Consult("execution", "account", "paper", Scopes, Now.AddSeconds(-1), Interval));
        Assert.Throws<InvalidDataException>(() => journal.Consult("execution", "account", "paper", Scopes, Now.AddHours(1), Interval));
    }

    [Fact]
    public async Task CompletedReviewAndItsAssessmentsSurviveRestart()
    {
        var packet = Packet();
        using (var journal = NewJournal())
        {
            journal.Publish(Snapshot(Lesson()), Now);
            await Supervisor(journal, ValidClient()).ReviewAsync(packet, "paper");
        }
        using var reopened = new AiLearningJournal(directory);
        Assert.Equal("Current", new AiPortfolioExecutionCoordinator(reopened, Interval, clock)
            .Prepare(packet.PacketId, "paper", null).LearningCheck.Status);
        Assert.Single(reopened.ReadExecutionChecks());
    }

    [Fact]
    public async Task PendingReviewAfterRestartCannotRepeatItsModelCall()
    {
        var packet = Packet();
        using (var journal = NewJournal())
        {
            journal.Publish(Snapshot(), Now);
            var learning = journal.Consult("supervisor", packet.PacketId.ToString("D"), "paper", Scopes, Now, Interval, true)!;
            journal.RecordReview(new(packet.PacketId, learning.ConsultationId,
                packet with { SchemaVersion = 2, Learning = learning }, null, "Pending", Now));
        }
        using var reopened = new AiLearningJournal(directory);
        var fake = ValidClient();
        await Assert.ThrowsAsync<InvalidDataException>(() => Supervisor(reopened, fake).ReviewAsync(packet, "paper"));
        Assert.Equal(0, fake.Calls);
        Assert.Equal("Pending", reopened.ReadReview(packet.PacketId).Status);
    }

    [Fact]
    public async Task SnapshotChangeBetweenConsultationAndRecordingCannotYieldCurrentCheck()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(Lesson()), Now);
        var packet = Packet();
        await Supervisor(journal, ValidClient()).ReviewAsync(packet, "paper");
        var learning = journal.Consult("execution", packet.PacketId.ToString("D"), "paper", Scopes, Now, Interval, true)!;
        var review = journal.ReadReview(packet.PacketId);
        journal.Publish(Snapshot() with { Version = 2, PublishedUtc = Now }, Now);
        Assert.Throws<InvalidDataException>(() => journal.RecordExecutionCheck(new(Guid.NewGuid(), packet.PacketId,
            learning.ConsultationId, "Current", "Synthetic check", review.Proposal!.LessonAssessments!.ToImmutableArray(), Now)));
        Assert.Empty(journal.ReadExecutionChecks());
    }

    [Fact]
    public async Task LearningExpirationDuringModelCallRejectsTheSavedReview()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(Lesson() with { ExpiresUtc = Now.AddMinutes(1) }), Now);
        var packet = Packet();
        var fake = new FakeClient(request =>
        {
            clock.Now = Now.AddMinutes(1);
            return Response(JsonSerializer.Deserialize<AiPortfolioPacket>(request.UserPrompt, Json)!);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Supervisor(journal, fake).ReviewAsync(packet, "paper"));
        Assert.Equal("Rejected", journal.ReadReview(packet.PacketId).Status);
    }

    [Fact]
    public void BothWorkflowConsumersExposePeriodicConsultationWithoutModelCalls()
    {
        using var journal = NewJournal();
        journal.Publish(Snapshot(), Now);
        var fake = ValidClient();
        var supervisor = Supervisor(journal, fake);
        var coordinator = new AiPortfolioExecutionCoordinator(journal, Interval, clock);
        Assert.NotNull(supervisor.RefreshLearningIfDue("account", "paper", Scopes));
        Assert.NotNull(coordinator.RefreshLearningIfDue("account", "paper", Scopes));
        Assert.Null(supervisor.RefreshLearningIfDue("account", "paper", Scopes));
        Assert.Null(coordinator.RefreshLearningIfDue("account", "paper", Scopes));
        clock.Now = Now.AddMinutes(5);
        Assert.NotNull(supervisor.RefreshLearningIfDue("account", "paper", Scopes));
        Assert.NotNull(coordinator.RefreshLearningIfDue("account", "paper", Scopes));
        Assert.Equal(0, fake.Calls);
    }

    private AiLearningAwareSupervisor Supervisor(AiLearningJournal journal, FakeClient fake) =>
        new(new AiPortfolioReviewer(fake, clock), journal, Interval, clock);
    private static AiPortfolioProposal Proposal(AiPortfolioPacket packet) => new(packet.PacketId, "select", "breakout", "v1",
        "Synthetic rationale", ["observation"], [], "Evidence changes", ["New unproven hypothesis"],
        packet.Learning!.Lessons.Select(l => new AiLessonAssessment(l.LessonId, "apply", "It informed the comparison.")).ToArray());
    private static string Response(AiPortfolioPacket packet) => JsonSerializer.Serialize(Proposal(packet), Json);
    private static FakeClient ValidClient() => new(request => Response(JsonSerializer.Deserialize<AiPortfolioPacket>(request.UserPrompt, Json)!));
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FakeClient(Func<LlmRequest, string> respond) : ILlmClient
    {
        public int Calls;
        public string Provider => "synthetic";
        public string Model => "synthetic";
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        { Calls++; return Task.FromResult(new LlmResponse(respond(request), new(0, 0, 0), TimeSpan.Zero, Provider, Model)); }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
