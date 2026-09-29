using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Oracle.Learning;
using Core.Oracle.Llm;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiScheduledPortfolioReviewerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-review-schedule-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new(AiReviewSchedulerTests.Open);
    private readonly AiReviewScheduler scheduler = AiReviewSchedulerTests.NewSchedule();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private AiLearningJournal Journal()
    {
        var journal = new AiLearningJournal(directory, initializeNew: true);
        journal.Publish(new(Guid.NewGuid(), 1, clock.Now, clock.Now.AddDays(1), ImmutableArray<AiEvaluatedLesson>.Empty), clock.Now);
        return journal;
    }

    private AiPortfolioPacket Packet() => new(Guid.NewGuid(), 1, clock.Now, clock.Now.AddMinutes(5), null,
        [new("synthetic", "v1", "DailyShadow", true, "Synthetic candidate", [])],
        [new("fixture", clock.Now, "Synthetic current evidence")], []);

    private AiScheduledPortfolioReviewer Runner(AiLearningJournal journal, ILlmClient client,
        Func<AiReviewRequest, CancellationToken, Task<AiPortfolioPacket>> prepare = null) =>
        new(scheduler, new(new AiPortfolioReviewer(client, clock), journal, TimeSpan.FromMinutes(5), clock),
            prepare ?? ((_, _) => Task.FromResult(Packet())), "paper", clock);

    private static LlmResponse Response(LlmRequest request)
    {
        var packet = JsonSerializer.Deserialize<AiPortfolioPacket>(request.UserPrompt, Json)!;
        var proposal = new AiPortfolioProposal(packet.PacketId, "wait", null, null, "Synthetic wait",
            ["fixture"], [], "New evidence", [], []);
        return new(JsonSerializer.Serialize(proposal, Json), new(0, 0, 0), TimeSpan.Zero, "synthetic", "synthetic");
    }

    [Fact]
    public async Task TickUsesExistingLearningReviewerAndSavesOneReviewWithAdmittedIdentity()
    {
        using var journal = Journal();
        var client = new Client((r, _) => Task.FromResult(Response(r)));
        AiReviewRequest captured = null;
        var runner = Runner(journal, client, (request, _) => { captured = request; return Task.FromResult(Packet()); });
        var result = await runner.TickAsync();
        Assert.Equal(1, client.Calls);
        Assert.Equal("SessionStart", Assert.Single(captured.Reasons));
        Assert.Equal(captured.Id, result.Proposal!.PacketId);
        Assert.Equal("Reviewed", journal.ReadReview(captured.Id).Status);
        Assert.Contains(journal.ReadReview(captured.Id).Packet.Evidence, e => e.Id == "schedule-request/" + captured.Id);
        Assert.Null(result.Schedule.ReviewBlock);
        Assert.Null((await runner.TickAsync()).Proposal);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task SlowPreparationCannotAdmitConcurrentTicksOrCallsCloseTogether()
    {
        using var journal = Journal();
        var client = new Client((r, _) => Task.FromResult(Response(r)));
        var ready = new TaskCompletionSource<AiPortfolioPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparations = 0;
        var runner = Runner(journal, client, (_, _) => ++preparations == 1 ? ready.Task : Task.FromResult(Packet()));
        var pending = runner.TickAsync();
        Assert.True((await runner.TickAsync()).Schedule.InFlight);
        Assert.Equal(0, client.Calls);
        clock.Now = clock.Now.AddMinutes(3);
        ready.SetResult(Packet());
        Assert.NotNull((await pending).Proposal);
        scheduler.RequestEvent(AiReviewSchedulerTests.Event("new", clock.Now), clock.Now);
        Assert.Null((await runner.TickAsync()).Proposal);
        Assert.Equal(1, client.Calls);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.NotNull((await runner.TickAsync()).Proposal);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task EventsDuringPaidReviewAreNotConsumedByItsOlderProposal()
    {
        using var journal = Journal();
        var ready = new TaskCompletionSource<LlmResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        LlmRequest call = null;
        var client = new Client((r, _) => { call = r; return ready.Task; });
        var runner = Runner(journal, client);
        var pending = runner.TickAsync();
        clock.Now = clock.Now.AddSeconds(30);
        scheduler.RequestEvent(AiReviewSchedulerTests.Event("closed", clock.Now, AiReviewEventKind.HoldingClosed), clock.Now);
        ready.SetResult(Response(call));
        var result = await pending;
        Assert.Null(result.Proposal);
        Assert.Equal("PendingEvents", result.Schedule.ReviewBlock);
        Assert.Equal(1, result.Schedule.PendingEvents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryBeforeOrDuringCallCannotReturnUsableProposal(bool duringCall)
    {
        using var journal = Journal();
        var client = new Client((r, _) => { clock.Now = clock.Now.AddMinutes(6); return Task.FromResult(Response(r)); });
        var runner = Runner(journal, client, (_, _) => Task.FromResult(duringCall ? Packet() : Packet() with { ExpiresUtc = clock.Now }));
        var result = await runner.TickAsync();
        Assert.Equal(duringCall ? 1 : 0, client.Calls);
        Assert.Null(result.Proposal);
        Assert.Equal("DataUnavailable", result.Schedule.ReviewBlock);
    }

    [Fact]
    public async Task PreparationCannotRunAReviewPastClose()
    {
        using var journal = Journal();
        var client = new Client((r, _) => Task.FromResult(Response(r)));
        var runner = Runner(journal, client, (_, _) =>
        {
            clock.Now = AiReviewSchedulerTests.Close;
            return Task.FromResult(Packet());
        });
        Assert.Null((await runner.TickAsync()).Proposal);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task SessionCloseDuringProviderCallRejectsResultInExistingJournal()
    {
        using var journal = Journal();
        clock.Now = AiReviewSchedulerTests.Close.AddSeconds(-30);
        var client = new Client((r, _) => { clock.Now = AiReviewSchedulerTests.Close; return Task.FromResult(Response(r)); });
        var result = await Runner(journal, client).TickAsync();
        Assert.Null(result.Proposal);
        Assert.Equal("Rejected", journal.ReadReview(result.Schedule.LastAttempt!.Request.Id).Status);
    }

    [Fact]
    public async Task SharedBudgetRejectionDoesNotRetryOnEveryTickOrClearOnFundingAlone()
    {
        using var journal = Journal();
        using var spend = new AiCallJournal(Path.Combine(directory, "spend"), initializeNew: true);
        var policy = new AiSpendPolicy(1.5m, 1.2m, clock.Now.AddDays(1), MonthlyLimitCad: 1m);
        // Another role's outstanding cost counts against the same monthly allowance.
        spend.Reserve(Guid.NewGuid(), policy, clock.Now, "synthetic prior role", 1000, 4096);
        var blocked = true;
        var client = new Client((r, _) =>
        {
            if (blocked) spend.Reserve(r.CallId!.Value, policy, clock.Now, "synthetic scheduled review", 30000, 4096);
            return Task.FromResult(Response(r));
        });
        var runner = Runner(journal, client);
        var first = await runner.TickAsync();
        Assert.Equal("BudgetUnavailable", first.Schedule.ReviewBlock);
        Assert.Null(first.Proposal);
        Assert.Single(spend.ReadAll());
        Assert.Null((await runner.TickAsync()).Proposal);
        Assert.Equal(1, client.Calls);
        blocked = false; // Simulate restored availability; scheduling has not granted a fresh review.
        Assert.Equal("BudgetUnavailable", scheduler.Read(clock.Now).ReviewBlock);
        clock.Now = clock.Now.AddMinutes(1);
        var recovered = await runner.TickAsync();
        Assert.NotNull(recovered.Proposal);
        Assert.NotEqual(first.Schedule.LastAttempt!.Request.Id, recovered.Proposal.PacketId);
        Assert.Null(recovered.Schedule.ReviewBlock);
        Assert.Single(spend.ReadAll()); // Recovery did not reset the existing spending ledger.
    }

    [Fact]
    public async Task ProviderFailureIsVisibleAndRequiresANewFreshReview()
    {
        using var journal = Journal();
        var broken = true;
        var client = new Client((r, _) => broken ? throw new HttpRequestException("synthetic failure") : Task.FromResult(Response(r)));
        var runner = Runner(journal, client);
        await Assert.ThrowsAsync<HttpRequestException>(() => runner.TickAsync());
        Assert.Equal("Failed", scheduler.Read(clock.Now).ReviewBlock);
        broken = false;
        Assert.Null((await runner.TickAsync()).Proposal);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.NotNull((await runner.TickAsync()).Proposal);
    }

    [Fact]
    public async Task ReviewFreshnessCannotOutliveItsLearningSnapshot()
    {
        using var journal = Journal();
        journal.Publish(new(Guid.NewGuid(), 2, clock.Now, clock.Now.AddMinutes(1), ImmutableArray<AiEvaluatedLesson>.Empty), clock.Now);
        var client = new Client((r, _) => Task.FromResult(Response(r)));
        Assert.NotNull((await Runner(journal, client).TickAsync()).Proposal);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal("FreshReviewRequired", scheduler.Read(clock.Now).ReviewBlock);
    }

    [Fact]
    public async Task TriggerEvidenceIsSavedAndIncludedInTheModelPacket()
    {
        using var journal = Journal();
        scheduler.RequestEvent(AiReviewSchedulerTests.Event("entry", clock.Now), clock.Now);
        var client = new Client((r, _) =>
        {
            var input = JsonSerializer.Deserialize<AiPortfolioPacket>(r.UserPrompt, Json)!;
            Assert.Equal("Review requested: EntrySignal; source evidence: synthetic/entry.",
                input.Evidence.Single(e => e.Id == "schedule-event/entry").Description);
            return Task.FromResult(Response(r));
        });
        var result = await Runner(journal, client).TickAsync();
        Assert.Contains(journal.ReadReview(result.Proposal!.PacketId).Packet.Evidence, e => e.Id == "schedule-event/entry");
    }

    [Fact]
    public async Task NewEventDuringPreparationDefersTheCallAndRetainsBothEvents()
    {
        using var journal = Journal();
        scheduler.RequestEvent(AiReviewSchedulerTests.Event("first", clock.Now), clock.Now);
        var client = new Client((r, _) => Task.FromResult(Response(r)));
        var runner = Runner(journal, client, (_, _) =>
        {
            scheduler.RequestEvent(AiReviewSchedulerTests.Event("second", clock.Now), clock.Now);
            return Task.FromResult(Packet());
        });
        var result = await runner.TickAsync();
        Assert.Equal(0, client.Calls);
        Assert.Equal(2, result.Schedule.PendingEvents);
        Assert.Equal("DataUnavailable", result.Schedule.ReviewBlock);
    }

    [Fact]
    public async Task CancellationRetainsRequestAndReleasesSingleFlightWithoutImmediateRetry()
    {
        using var journal = Journal();
        using var cancellation = new CancellationTokenSource();
        var client = new Client((r, _) => { cancellation.Cancel(); return Task.FromResult(Response(r)); });
        var runner = Runner(journal, client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.TickAsync(cancellation.Token));
        var status = scheduler.Read(clock.Now);
        Assert.False(status.InFlight);
        Assert.Equal("Cancelled", status.ReviewBlock);
        Assert.Null((await runner.TickAsync()).Proposal);
        Assert.Equal(1, client.Calls);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Client(Func<LlmRequest, CancellationToken, Task<LlmResponse>> respond) : ILlmClient
    {
        public int Calls;
        public string Provider => "synthetic";
        public string Model => "synthetic";
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        { Calls++; return respond(request, ct); }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
