#nullable enable
using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradingRecommendationWorkflowTests
{
    [Fact]
    public async Task MissingApprovalDoesNotFetchQuotesOrPublish()
    {
        var store = new FencedStore(null); var quotes = new HeldQuotes([]);
        Assert.Null(await new TradingRecommendationWorkflow(store, quotes).EvaluateAsync("TFSA"));
        Assert.False(quotes.Started.Task.IsCompleted); Assert.Empty(store.History);
    }
    [Fact]
    public async Task FailedPublicationReleasesFenceAndRetryDoesNotAdvanceHistoryOrLoseProtection()
    {
        var store = new FencedStore(TradingRecommendationTests.Input()) { FailPublicationOnce = true };
        var workflow = new TradingRecommendationWorkflow(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.EvaluateAsync("TFSA"));
        Assert.Empty(store.History); Assert.Equal(1, store.Disposals);
        var saved = await workflow.EvaluateAsync("TFSA");
        Assert.Single(store.History); Assert.Equal(saved, store.History[0]); Assert.Equal(2, store.Disposals);
    }
    [Fact]
    public async Task ConcurrentPromotionWaitsForOldEvaluationPublicationThenNextEvaluationUsesNewAssignment()
    {
        var input = TradingRecommendationTests.LiveInput(); var store = new FencedStore(input);
        var quotes = new HeldQuotes(input.Quotes);
        var evaluating = new TradingRecommendationWorkflow(store, quotes).EvaluateAsync("TFSA");
        await quotes.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var next = input.Assignment with { PromotionId = Guid.NewGuid(), PriorPromotionId = input.Assignment.PromotionId };
        var promoting = store.ActivateAsync(next);
        Assert.False(promoting.IsCompleted); Assert.Equal(input.Assignment.PromotionId, store.Input!.Assignment.PromotionId);
        quotes.Continue.SetResult();
        var old = await evaluating; await promoting;
        Assert.Equal(input.Assignment.PromotionId, old!.PromotionId);
        var current = await new TradingRecommendationWorkflow(store).EvaluateAsync("TFSA");
        Assert.Equal(next.PromotionId, current!.PromotionId); Assert.Equal(old.BatchId, current.PriorBatchId);
        Assert.Equal(2, store.History.Count);
    }
    [Fact]
    public async Task QuoteFailureDisposesEvaluationWithoutChangingAssignmentOrPublishingPartialAdvice()
    {
        var input = TradingRecommendationTests.LiveInput(); var store = new FencedStore(input); var quotes = new BrokenQuotes();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TradingRecommendationWorkflow(store, quotes).EvaluateAsync("TFSA"));
        Assert.Equal(input.Assignment, store.Input!.Assignment); Assert.Empty(store.History); Assert.Equal(1, store.Disposals);
    }
    [Fact]
    public async Task DifferentAccountDoesNotBorrowAnExistingApproval()
    {
        var store = new FencedStore(TradingRecommendationTests.Input());
        Assert.Null(await new TradingRecommendationWorkflow(store).EvaluateAsync("RRSP")); Assert.Empty(store.History);
    }
    private sealed class HeldQuotes(ImmutableArray<TradingQuote> quotes) : ITradingRecommendationQuoteSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ImmutableArray<TradingQuote>> ReadAsync(TradingRecommendationInput input, CancellationToken token)
        { Started.TrySetResult(); await Continue.Task.WaitAsync(token); return quotes; }
    }
    private sealed class BrokenQuotes : ITradingRecommendationQuoteSource
    { public Task<ImmutableArray<TradingQuote>> ReadAsync(TradingRecommendationInput input, CancellationToken token) => throw new InvalidOperationException("synthetic source failure"); }
    // An executable contract for the store's consequential fence. SQL lock behavior is separately reviewed and SSDT-built, not exercised against a database here.
    private sealed class FencedStore(TradingRecommendationInput? input) : ITradingRecommendationStore
    {
        private readonly SemaphoreSlim fence = new(1, 1);
        public TradingRecommendationInput? Input { get; private set; } = input;
        public List<TradingRecommendationBatch> History { get; } = [];
        public int Disposals { get; private set; }
        public bool FailPublicationOnce { get; set; }
        public async Task ActivateAsync(TradingStrategyAssignment assignment)
        { await fence.WaitAsync(); try { Input = Input! with { Assignment = assignment }; } finally { fence.Release(); } }
        public async Task<ITradingRecommendationSession?> BeginAsync(string account, CancellationToken token)
        {
            await fence.WaitAsync(token);
            if (Input?.Assignment.AccountLabel != account) { fence.Release(); return null; }
            var snapshot = Input with { BatchId = Guid.NewGuid(), PreviousMemory = History.Count == 0 ? TradingRecommendationMemory.Empty : History[^1].Result.Memory };
            return new Session(this, snapshot);
        }
        private sealed class Session(FencedStore store, TradingRecommendationInput snapshot) : ITradingRecommendationSession
        {
            public TradingRecommendationInput Input => snapshot;
            public Task<TradingRecommendationBatch> PublishAsync(TradingRecommendationInput input, TradingRecommendationResult result, CancellationToken token)
            {
                if (store.FailPublicationOnce) { store.FailPublicationOnce = false; throw new InvalidOperationException("synthetic publication failure"); }
                Assert.Equal(snapshot.Assignment, store.Input!.Assignment);
                var batch = new TradingRecommendationBatch(input.BatchId, input.Assignment.AccountLabel, input.Assignment.PromotionId, input.Account?.SnapshotId,
                    store.History.Count == 0 ? null : store.History[^1].BatchId, input.EvaluatedUtc, "fixture", result);
                store.History.Add(batch); return Task.FromResult(batch);
            }
            public ValueTask DisposeAsync() { store.Disposals++; store.fence.Release(); return ValueTask.CompletedTask; }
        }
    }
}
