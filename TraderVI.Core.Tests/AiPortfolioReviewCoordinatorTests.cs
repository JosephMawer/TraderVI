#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Db;
using Core.Oracle;
using Core.Oracle.Learning;
using Core.Oracle.Llm;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiPortfolioReviewCoordinatorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-coordinator-" + Guid.NewGuid().ToString("N"));
    private readonly Clock clock = new(AiReviewSchedulerTests.Open);
    private readonly Guid portfolio = Guid.NewGuid();
    private static readonly DateOnly Date = AiReviewSchedulerTests.Date;
    private readonly ReviewedTsxSessionCalendar calendar = AiReviewSchedulerTests.Calendar();
    private readonly List<IDisposable> resources = [];
    private AiReviewScheduleScope Scope => new(portfolio, portfolio, "paper");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private T Own<T>(T value) where T : IDisposable { resources.Add(value); return value; }

    private async Task<Fixture> Setup()
    {
        var policy = new AiPortfolioRiskPolicy(calendar);
        var riskJournal = Own(new AiPortfolioRiskJournal(Path.Combine(directory, "risk"),
            new(portfolio, portfolio, AiRiskAccountMode.Paper), policy, true));
        var at = clock.Now.UtcDateTime;
        var snapshot = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), portfolio,
            DelphiLivePolicyDefinition.Version1.PolicyVersionId, "OperationalChampion", null, 12345m, "CAD",
            Date, at, at.AddDays(-1), "Test", "Synthetic"));
        snapshot = snapshot with { Revision = 1, CurrentSession = Date, UpdatedUtc = at,
            Marks = [new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, at, true, 12345m, [], "Complete")] };
        var book = new Book(snapshot);
        var risk = new PaperAiPortfolioRiskReader(book,
            new DelphiLiveAiPortfolioRiskMonitor(riskJournal, policy, TimeSpan.FromMinutes(5)), clock);
        Assert.True((await risk.StartAsync(Date, false, [])).Accepted);
        var strategy = TradingStrategyResolutionTests.Strategy();
        var definition = DelphiLivePolicyDefinition.Version1;
        var catalog = new Catalog(new(clock.Now, clock.Now.AddMinutes(5), [
            new(Guid.NewGuid(), "Shadow", "DailyShadow", strategy, null, []),
            new(Guid.NewGuid(), "Live", "DelphiLive", strategy with {
                Family = TradingStrategyFamily.DelphiLive, ShadowPolicy = null, LivePolicy = definition,
                PolicyVersionId = definition.PolicyVersionId, Selection = CompleteTradingStrategy.LiveSelection,
                MaximumHoldings = definition.MaximumHoldings }, null, []),
            new(Guid.NewGuid(), "Undefined", "DailyShadow", null, null, ["No definition"])
        ]));
        var shadow = new Shadow();
        var live = new Live();
        AiPaperReviewState state = new(null, []);
        var inputs = new AiPaperPortfolioReviewInputs(catalog,
            new(shadow, calendar, TimeSpan.FromMinutes(5), clock), new(live, calendar, TimeSpan.FromMinutes(5), clock),
            risk, riskJournal, () => state with { IndependentBlocks = [.. state.IndependentBlocks] }, clock);
        var learning = Own(new AiLearningJournal(Path.Combine(directory, "learning"), initializeNew: true));
        learning.Publish(new(Guid.NewGuid(), 1, clock.Now, clock.Now.AddDays(1), []), clock.Now);
        var schedule = Own(new AiReviewScheduleJournal(Path.Combine(directory, "schedule"), Scope, calendar, Date,
            new(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(195), TimeSpan.FromMinutes(5)), clock.Now, true));
        var store = new Store(Scope);
        var client = new Client();
        var coordinator = new AiPortfolioReviewCoordinator(schedule, inputs, client, learning, store, TimeSpan.FromMinutes(5), clock);
        return new(coordinator, inputs, catalog, book, shadow, live, client, store, schedule, learning, riskJournal,
            value => state = value);
    }

    [Fact]
    public async Task GathersBothEnginesAndActualFundsThenSavesTheExactModelInputAndAnswer()
    {
        var f = await Setup();
        f.Client.BeforeResponse = _ => Assert.Equal("Pending", Assert.Single(f.Store.Records).Status);
        var result = await f.Coordinator.TickAsync();
        Assert.Equal("wait", result.Proposal!.Action);
        Assert.Equal(1, f.Shadow.Reads);
        Assert.Equal(1, f.Live.Reads);
        Assert.Equal(3, f.Store.Records[0].Packet.Candidates.Length);
        Assert.All(f.Store.Records[0].Packet.Candidates, c => Assert.False(c.Eligible));
        var sent = JsonSerializer.Deserialize<AiPortfolioPacket>(f.Client.Last!.UserPrompt, Json)!;
        Assert.Equal(JsonSerializer.Serialize(sent), JsonSerializer.Serialize(f.Store.Records[0].Packet));
        Assert.Equal(new[] { "Pending", "Reviewed" }, f.Store.Records.Select(r => r.Status));
        Assert.Equal(result.Proposal.PacketId, f.Client.Last.CallId);
        Assert.Equal(result.Proposal.PacketId, f.Store.Records[1].ReviewId);
        using var account = JsonDocument.Parse(sent.Evidence.Single(e => e.Id == "managed-account").Description);
        Assert.Equal(12345m, account.RootElement.GetProperty("SavedBook").GetProperty("Cash").GetDecimal());
        Assert.Equal(TimeSpan.FromMinutes(7), result.Schedule.Interval.Applied);
        Assert.Null((await f.Coordinator.TickAsync()).Proposal);
        Assert.Equal(1, f.Client.Calls);
        Assert.Equal(1, f.Catalog.Reads);
        Assert.Single(f.Risk.ReadHistory()); // Reviews neither create a new round nor mark old prices fresh.
    }

    [Fact]
    public async Task ExpiredAccountEvidenceStopsTheExistingPathBeforeTheModel()
    {
        var f = await Setup();
        clock.Now = clock.Now.AddMinutes(6);
        f.Catalog.Value = f.Catalog.Value with { AsOfUtc = clock.Now, ExpiresUtc = clock.Now.AddMinutes(5) };
        var result = await f.Coordinator.TickAsync();
        Assert.Equal("DataUnavailable", result.Schedule.ReviewBlock);
        Assert.Null(result.Proposal);
        Assert.Equal(0, f.Client.Calls);
        Assert.Empty(f.Store.Records);
        Assert.Single(f.Risk.ReadHistory());
    }

    [Fact]
    public async Task ChangedActivePortfolioDuringCollectionDoesNotMixContexts()
    {
        var f = await Setup();
        f.Book.OnRead = () => f.SetState(new(f.Catalog.Value.Entries[0].Id, []));
        var result = await f.Coordinator.TickAsync();
        Assert.Equal("DataUnavailable", result.Schedule.ReviewBlock);
        Assert.Equal(0, f.Client.Calls);
    }

    [Fact]
    public async Task FailedPendingSavePreventsPaidCall()
    {
        var f = await Setup();
        f.Store.FailStatus = "Pending";
        await Assert.ThrowsAsync<IOException>(() => f.Coordinator.TickAsync());
        Assert.Equal(0, f.Client.Calls);
        Assert.Equal("Failed", f.Schedule.Read(clock.Now).ReviewBlock);
    }

    [Fact]
    public async Task FailedFinalSaveCanBeRepairedFromTheJournalWithoutAnotherCall()
    {
        var f = await Setup();
        f.Store.FailStatus = "Reviewed";
        await Assert.ThrowsAsync<IOException>(() => f.Coordinator.TickAsync());
        var id = f.Schedule.Read(clock.Now).LastAttempt!.Request.Id;
        Assert.Equal("Reviewed", f.Learning.ReadReview(id).Status);
        Assert.Equal("Failed", f.Schedule.Read(clock.Now).ReviewBlock);
        f.Store.FailStatus = null;
        await f.Coordinator.PersistReviewAsync(id);
        Assert.Equal("Reviewed", f.Store.Records.Last().Status);
        Assert.Equal(1, f.Client.Calls);
        Assert.Equal("Failed", f.Schedule.Read(clock.Now).ReviewBlock); // Saving history grants no fresh permission.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrCancelledAnswersAreSavedAsRejected(bool cancelled)
    {
        var f = await Setup();
        using var cancellation = new CancellationTokenSource();
        if (cancelled) f.Client.BeforeResponse = _ => cancellation.Cancel();
        else f.Client.Invalid = true;
        if (cancelled) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Coordinator.TickAsync(cancellation.Token));
        else Assert.Null((await f.Coordinator.TickAsync()).Proposal);
        Assert.Equal(new[] { "Pending", "Rejected" }, f.Store.Records.Select(r => r.Status));
        Assert.Null(f.Store.Records.Last().Proposal);
    }

    [Fact]
    public async Task RestartCanRepairASavedAnswerWithoutRepeatingThePaidCall()
    {
        var f = await Setup();
        f.Store.FailStatus = "Reviewed";
        await Assert.ThrowsAsync<IOException>(() => f.Coordinator.TickAsync());
        var id = f.Schedule.Read(clock.Now).LastAttempt!.Request.Id;
        f.Schedule.Dispose(); resources.Remove(f.Schedule);
        f.Learning.Dispose(); resources.Remove(f.Learning);
        clock.Now = clock.Now.AddSeconds(1);
        var learning = Own(new AiLearningJournal(Path.Combine(directory, "learning")));
        var schedule = Own(new AiReviewScheduleJournal(Path.Combine(directory, "schedule"), Scope, calendar, Date,
            new(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(195), TimeSpan.FromMinutes(5)), clock.Now));
        var coordinator = new AiPortfolioReviewCoordinator(schedule, f.Inputs, f.Client, learning, f.Store, TimeSpan.FromMinutes(5), clock);
        f.Store.FailStatus = null;
        await coordinator.PersistReviewAsync(id);
        Assert.Equal("Reviewed", f.Store.Records.Last().Status);
        Assert.Equal(1, f.Client.Calls);
        Assert.Null((await coordinator.TickAsync()).Proposal);
        Assert.Equal(1, f.Client.Calls);
    }

    [Fact]
    public async Task EventDuringResultSaveIsRetainedAndTheOldAnswerIsNotReturnedAsCurrent()
    {
        var f = await Setup();
        f.Store.OnSave = review =>
        {
            if (review.Status == "Reviewed")
                f.Schedule.RequestEvent(AiReviewSchedulerTests.Event("changed", clock.Now), clock.Now);
        };
        var result = await f.Coordinator.TickAsync();
        Assert.Equal("Reviewed", f.Store.Records.Last().Status);
        Assert.Equal("PendingEvents", result.Schedule.ReviewBlock);
        Assert.Null(result.Proposal);
        Assert.Equal(1, result.Schedule.PendingEvents);
    }

    [Fact]
    public async Task ConcurrentTicksDoNotDuplicateCollectionOrCalls()
    {
        var f = await Setup();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.Wait = ready.Task;
        var pending = f.Coordinator.TickAsync();
        Assert.True((await f.Coordinator.TickAsync()).Schedule.InFlight);
        Assert.Equal(1, f.Client.Calls);
        Assert.Equal(1, f.Catalog.Reads);
        ready.SetResult();
        Assert.NotNull((await pending).Proposal);
    }

    [Fact]
    public async Task ScopeMismatchCannotComposeAReviewer()
    {
        var f = await Setup();
        Assert.Throws<ArgumentException>(() => new AiPortfolioReviewCoordinator(f.Schedule, f.Inputs, f.Client,
            f.Learning, new Store(Scope with { AccountScopeId = Guid.NewGuid() }), TimeSpan.FromMinutes(5), clock));
    }

    [Fact]
    public async Task RepositorySerializationPreservesEvidenceAndRejectsAlteredIdentity()
    {
        var f = await Setup();
        await f.Coordinator.TickAsync();
        var repository = new AiPortfolioReviewRepository(Scope);
        var review = f.Store.Records.Last();
        Assert.Equal(JsonSerializer.Serialize(review), repository.Serialize(review));
        Assert.Throws<InvalidDataException>(() => repository.Serialize(review with { ReviewId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => repository.Serialize(review with { Status = "Pending" }));
        Assert.Throws<InvalidDataException>(() => new AiPortfolioReviewRepository(Scope with { PortfolioId = Guid.NewGuid() }).Serialize(review));
        Assert.Throws<InvalidDataException>(() => new AiPortfolioReviewRepository(Scope with { AccountScopeId = Guid.NewGuid() }).Serialize(review));
    }

    private sealed record Fixture(AiPortfolioReviewCoordinator Coordinator, AiPaperPortfolioReviewInputs Inputs,
        Catalog Catalog, Book Book, Shadow Shadow, Live Live, Client Client, Store Store, AiReviewScheduleJournal Schedule,
        AiLearningJournal Learning, AiPortfolioRiskJournal Risk, Action<AiPaperReviewState> SetState);
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Catalog(AiPortfolioCatalog value) : IAiPortfolioCatalogReader
    {
        public AiPortfolioCatalog Value = value;
        public int Reads;
        public Task<AiPortfolioCatalog> ReadAsync(CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(Value); }
    }
    private sealed class Book(DelphiLivePortfolioSnapshot value) : IDelphiLivePortfolioReader
    {
        public Action? OnRead;
        public Task<DelphiLivePortfolioSnapshot?> LoadPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken = default)
        { OnRead?.Invoke(); Assert.Equal(value.PortfolioId, portfolioId); return Task.FromResult<DelphiLivePortfolioSnapshot?>(value); }
    }
    private sealed class Shadow : IAiShadowDecisionSource
    {
        public int Reads;
        public Task<AiShadowSavedContext?> ReadAsync(Guid portfolioId, DateOnly date, CancellationToken ct = default)
        { Reads++; return Task.FromResult<AiShadowSavedContext?>(null); }
    }
    private sealed class Live : IAiLiveDecisionSource
    {
        public int Reads;
        public Task<AiLiveSavedContext?> ReadAsync(Guid portfolioId, DateOnly date, CancellationToken ct = default)
        { Reads++; return Task.FromResult<AiLiveSavedContext?>(null); }
    }
    private sealed class Store(AiReviewScheduleScope scope) : IAiReviewStore
    {
        public AiReviewScheduleScope Scope => scope;
        public readonly List<AiLearningReview> Records = [];
        public string? FailStatus;
        public Action<AiLearningReview>? OnSave;
        public Task SaveAsync(AiLearningReview review, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailStatus == review.Status) throw new IOException("Synthetic save failure");
            _ = new AiPortfolioReviewRepository(Scope).Serialize(review);
            OnSave?.Invoke(review);
            Records.Add(JsonSerializer.Deserialize<AiLearningReview>(JsonSerializer.Serialize(review))!);
            return Task.CompletedTask;
        }
    }
    private sealed class Client : ILlmClient
    {
        public int Calls;
        public bool Invalid;
        public Action<LlmRequest>? BeforeResponse;
        public Task? Wait;
        public LlmRequest? Last;
        public string Provider => "synthetic";
        public string Model => "synthetic";
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        {
            Calls++; Last = request; BeforeResponse?.Invoke(request);
            if (Wait is not null) await Wait;
            ct.ThrowIfCancellationRequested();
            var packet = JsonSerializer.Deserialize<AiPortfolioPacket>(request.UserPrompt, Json)!;
            var proposal = new AiPortfolioProposal(packet.PacketId, "wait", null, null, "Synthetic review",
                ["managed-account"], [], "New data", [], [], new(7, "Synthetic cadence"));
            return new(Invalid ? "{}" : JsonSerializer.Serialize(proposal, Json), new(0, 0, 0), TimeSpan.Zero, Provider, Model);
        }
    }
    public void Dispose()
    {
        foreach (var resource in resources.AsEnumerable().Reverse()) resource.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
