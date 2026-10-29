#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class IntradayCollectorTests
{
    private static readonly DateTime Open = new(2026, 10, 28, 13, 30, 0, DateTimeKind.Utc);
    private static readonly CollectorSubscriptionSnapshot Subscriptions = new(Open, Open.AddHours(6.5), ["NEW"], ["HELD"], ["EXIT"]);
    private static readonly CollectorLimits Limits = new(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(20), 8, 100);

    [Fact]
    public async Task PreservesHeldAndExitSubscriptionsWithoutCandidatesAndDoesNotRepeatSlots()
    {
        var rig = new Rig();
        var subscriptions = Subscriptions with { Candidates = [] };
        Assert.Equal("StartupTransportCooldown", (await rig.Worker.TickAsync(subscriptions)).State);
        rig.Now = Open.AddMinutes(2).AddSeconds(5);
        Assert.Equal(3, (await rig.Worker.TickAsync(subscriptions)).Saved);
        Assert.Equal(new[] { "XIU", "HELD", "EXIT" }, rig.Source.Reads.ToArray());
        Assert.Equal(0, (await rig.Worker.TickAsync(subscriptions)).Attempted);
        rig.Now = rig.Now.AddMinutes(1);
        Assert.Equal(3, (await rig.Worker.TickAsync(subscriptions)).Saved);
    }

    [Fact]
    public async Task ASlowReadDoesNotCauseCatchupRequestsOrSkipTheOriginalMinuteIdentity()
    {
        var rig = new Rig(); rig.Now = Open.AddMinutes(2).AddSeconds(5);
        rig.Source.AfterRead = () => rig.Now = rig.Now.AddSeconds(45);
        var tick = await rig.Worker.TickAsync(Subscriptions);
        Assert.Equal(1, tick.Attempted);
        Assert.Equal("PartialCollection", tick.State);
        Assert.Equal(Open.AddMinutes(2), Assert.Single(rig.Store.Saved).ExpectedEndUtc);
        Assert.Empty(rig.Store.Saved[0].Bars);
    }

    [Fact]
    public async Task FailedReadsAreRecordedWithoutRetryAndBudgetSurvivesWorkerRestart()
    {
        var rig = new Rig(); rig.Source.Fail = true; rig.Now = Open.AddMinutes(2).AddSeconds(5);
        var first = await rig.Worker.TickAsync(Subscriptions);
        Assert.Equal(4, first.Failed);
        Assert.All(rig.Store.Saved, b => Assert.Equal("SourceReadFailed", b.FailureCode));
        var restarted = new IntradayCollector(rig.Store, rig.Source, Limits with { MaximumRequestsPerSession = 4 }, () => rig.Now);
        rig.Now = rig.Now.AddMinutes(1);
        Assert.Equal(0, (await restarted.TickAsync(Subscriptions)).Attempted);
        Assert.Equal(4, rig.Source.Reads.Count);
    }

    [Fact]
    public async Task PersistedAttemptPrecedesRequestAndLostWriterCannotSave()
    {
        var rig = new Rig(); rig.Now = Open.AddMinutes(2).AddSeconds(5);
        rig.Source.AfterRead = () => rig.Store.CurrentOwner = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Worker.TickAsync(Subscriptions));
        Assert.Single(rig.Store.Attempts);
        Assert.Empty(rig.Store.Saved);
        Assert.Single(rig.Source.Reads);
    }

    [Fact]
    public async Task AnotherOwnerClosedSessionAndOversizedUnionNeverCallSource()
    {
        var rig = new Rig(); rig.Now = Open.AddMinutes(2).AddSeconds(5); rig.Store.DenyLease = true;
        Assert.Equal("OtherCollectorOwnsLease", (await rig.Worker.TickAsync(Subscriptions)).State);
        rig.Now = Subscriptions.SessionCloseUtc.Add(Limits.DeadlineOffset);
        Assert.Equal("OutsideSession", (await rig.Worker.TickAsync(Subscriptions)).State);
        rig.Now = Open.AddMinutes(2).AddSeconds(5);
        var small = new IntradayCollector(rig.Store, rig.Source, Limits with { MaximumSymbols = 2 }, () => rig.Now);
        Assert.Equal("SubscriptionBudgetExceeded", (await small.TickAsync(Subscriptions)).State);
        Assert.Empty(rig.Source.Reads);
    }

    [Fact]
    public async Task ClosingMinuteIsCollectedWithinOriginalDeadlineWithoutInventingAnAfterHoursEndpoint()
    {
        var rig = new Rig(); rig.Now = Subscriptions.SessionCloseUtc.AddSeconds(5);
        var result = await rig.Worker.TickAsync(Subscriptions);
        Assert.Equal(4, result.Saved);
        Assert.All(rig.Store.Saved, b => Assert.Equal(Subscriptions.SessionCloseUtc, b.ExpectedEndUtc));
        Assert.Equal("NoNewRequestAdmitted", (await rig.Worker.TickAsync(Subscriptions)).State);
        rig.Now = Subscriptions.SessionCloseUtc.Add(Limits.DeadlineOffset);
        Assert.Equal("OutsideSession", (await rig.Worker.TickAsync(Subscriptions)).State);
        rig.Now = Subscriptions.SessionCloseUtc.AddMinutes(1).AddSeconds(5);
        Assert.Equal("OutsideSession", (await rig.Worker.TickAsync(Subscriptions)).State);
        Assert.Equal(4, rig.Source.Reads.Count);
    }

    [Fact]
    public async Task ExpiredEndpointReportsNoCoverageInsteadOfCollected()
    {
        var rig = new Rig(); rig.Now = Open.AddMinutes(2).AddSeconds(45);
        Assert.Equal("MinuteDeadlineElapsed", (await rig.Worker.TickAsync(Subscriptions)).State);
        Assert.Empty(rig.Source.Reads);
    }

    [Fact]
    public async Task WakesForSubsecondEligibilityRatherThanSkippingTheLastAdmissionWindow()
    {
        var rig = new Rig();
        var one = Subscriptions with { Candidates = [], Holdings = [], PendingExits = [] };
        rig.Now = Open.AddMinutes(2).AddMilliseconds(44200);
        Assert.Equal(1, (await rig.Worker.TickAsync(one)).Saved);
        rig.Now = Open.AddMinutes(3).AddMilliseconds(43950);
        var waiting = await rig.Worker.TickAsync(one);
        Assert.Equal(0, waiting.Attempted);
        Assert.Equal(TimeSpan.FromMilliseconds(250), waiting.PollDelay(rig.Now));
        rig.Now += waiting.PollDelay(rig.Now);
        Assert.Equal(1, (await rig.Worker.TickAsync(one)).Saved);
        Assert.Equal(2, rig.Source.Reads.Count);
        Assert.Equal(TimeSpan.FromMinutes(1), rig.Source.Starts[1] - rig.Source.Starts[0]);
    }

    [Fact]
    public async Task CompletedSlotIsNotReservedAgainWithinMinuteButNewMinuteStillRuns()
    {
        var rig = new Rig(); rig.Now = Open.AddMinutes(2).AddSeconds(5);
        await rig.Worker.TickAsync(Subscriptions);
        int reservations = rig.Store.Reservations;
        rig.Now = Open.AddMinutes(2).AddSeconds(44);
        await rig.Worker.TickAsync(Subscriptions);
        Assert.Equal(reservations, rig.Store.Reservations);
        rig.Now = Open.AddMinutes(3).AddSeconds(5);
        Assert.Equal(4, (await rig.Worker.TickAsync(Subscriptions)).Saved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FullSessionThirtyFourSymbolsRetainsRateAndDeadlineWithVariableLatency(bool scheduled)
    {
        var rig = new Rig(Limits with { MaximumSymbols = 34, MaximumRequestsPerSession = 20000 });
        var subscriptions = Subscriptions with
        { Candidates = Enumerable.Range(0, 31).Select(i => "C" + i).ToImmutableArray() };
        rig.Store.BeforeRenew = () => rig.Now = rig.Now.AddMilliseconds(2);
        rig.Store.BeforeReserve = () => rig.Now = rig.Now.AddMilliseconds(4);
        rig.Store.AfterSave = () => rig.Now = rig.Now.AddMilliseconds(4);
        rig.Source.AfterRead = () => rig.Now = rig.Now.AddMilliseconds(60 + rig.Source.Reads.Count % 7 * 10);
        rig.Now = Open.AddMinutes(2).AddSeconds(5);
        var stop = Subscriptions.SessionCloseUtc.AddSeconds(45);
        while (rig.Now < stop)
        {
            var result = await rig.Worker.TickAsync(subscriptions);
            // File/status work and timer overshoot are not free in the native host.
            rig.Now = rig.Now.AddMilliseconds(3);
            rig.Now += scheduled ? result.PollDelay(rig.Now) : TimeSpan.FromSeconds(1);
            rig.Now = rig.Now.AddMilliseconds(1 + rig.Source.Reads.Count % 5);
        }
        var counts = rig.Store.Saved.GroupBy(b => b.ExpectedEndUtc).Select(g => g.Count()).ToArray();
        Assert.Equal(389, counts.Length); // Minute endpoints 2 through 390, including close.
        if (scheduled) Assert.All(counts, count => Assert.Equal(34, count));
        else Assert.Contains(counts, count => count < 34); // Same workload reproduces old polling drift.
        foreach (var group in rig.Store.Saved.GroupBy(b => b.Symbol))
        {
            var requests = group.OrderBy(b => b.FetchStartedUtc).ToArray();
            for (int i = 1; i < requests.Length; i++)
                Assert.True(requests[i].FetchStartedUtc - requests[i - 1].FetchStartedUtc >= TimeSpan.FromMinutes(1));
        }
        if (scheduled)
            Assert.All(rig.Store.Saved, b => Assert.True(b.ReceivedUtc < b.ExpectedEndUtc.Add(Limits.DeadlineOffset)));
        else
            Assert.Contains(rig.Store.Saved, b => b.ReceivedUtc >= b.ExpectedEndUtc.Add(Limits.DeadlineOffset));
        Assert.Equal(rig.Store.Saved.Count, rig.Store.Attempts.Select(a => (a.Symbol, a.ExpectedEndUtc)).Distinct().Count());
    }

    [Fact]
    public void PollDelayPreservesFailureBackoffAndBoundsOverdueWakeups()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), new CollectorTickResult("CollectorAttention", 0, 0, 0)
            { NextPollUtc = Open }.PollDelay(Open));
        Assert.Equal(TimeSpan.FromMilliseconds(1), new CollectorTickResult("Collected", 1, 1, 0)
            { NextPollUtc = Open }.PollDelay(Open));
        Assert.Equal(TimeSpan.FromSeconds(1), new CollectorTickResult("Collected", 1, 1, 0)
            { NextPollUtc = Open.AddMinutes(1) }.PollDelay(Open));
    }

    [Fact]
    public async Task OverlappingTicksAndStopAreSerialized()
    {
        var rig = new Rig(); rig.Now = Open.AddMinutes(2).AddSeconds(5);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Source.Wait = async () => { entered.TrySetResult(); await release.Task; };
        var first = rig.Worker.TickAsync(Subscriptions); await entered.Task;
        Assert.Equal("Busy", (await rig.Worker.TickAsync(Subscriptions)).State);
        var stop = rig.Worker.StopAsync(); Assert.False(stop.IsCompleted);
        release.SetResult(); await first; await stop;
        Assert.Null(rig.Store.CurrentOwner);
    }

    [Theory]
    [InlineData("2026-10-28T13:31:00Z", false)]
    [InlineData("2026-10-28T13:31:00", true)]
    public async Task SourceRequiresExplicitTimezoneAndRetainsRawReceipt(string timestamp, bool invalid)
    {
        var now = Open.AddMinutes(2).AddSeconds(5);
        string raw = "{\"data\":{\"getTimeSeriesData\":[{\"dateTime\":\"" + timestamp + "\",\"open\":10,\"high\":11,\"low\":9,\"close\":10,\"volume\":100}]}}";
        var handler = new Handler(raw);
        using var source = new TmxCollectorSource(handler, () => now);
        var receipt = await source.FetchAsync(new(Guid.NewGuid(), TmxCollectorSource.Contract, "XIU", Open, Open.AddHours(6.5), Open.AddMinutes(2), now),
            Limits.DeadlineOffset, CancellationToken.None);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(invalid, receipt.FailureCode is not null);
        Assert.Equal(invalid ? 0 : 1, receipt.Bars.Length);
        Assert.Contains("dateTime", receipt.RawResponseJson);
    }

    private sealed class Rig
    {
        public DateTime Now = Open.AddSeconds(5);
        public Store Store { get; }
        public Source Source { get; }
        public IntradayCollector Worker { get; }
        public Rig(CollectorLimits? limits = null) { Store = new(() => Now); Source = new(Store, () => Now); Worker = new(Store, Source, limits ?? Limits, () => Now); }
    }
    private sealed class Store(Func<DateTime> clock) : IIntradayCollectorStore
    {
        public Guid? CurrentOwner; public bool DenyLease;
        public Action? BeforeRenew, BeforeReserve, AfterSave; public int Reservations;
        public List<CollectorAttempt> Attempts = []; public List<DelphiLiveCollectorBatch> Saved = [];
        public Task<MarketCollectorLease?> AcquireAsync(string source, Guid owner, CancellationToken ct)
        {
            if (DenyLease) return Task.FromResult<MarketCollectorLease?>(null);
            CurrentOwner = owner; return Task.FromResult<MarketCollectorLease?>(new(source, owner, 1, clock().AddMinutes(2)));
        }
        public Task<bool> RenewAsync(MarketCollectorLease lease, CancellationToken ct) { BeforeRenew?.Invoke(); return Task.FromResult(CurrentOwner == lease.OwnerId); }
        public Task ReleaseAsync(MarketCollectorLease lease, CancellationToken ct) { if (CurrentOwner == lease.OwnerId) CurrentOwner = null; return Task.CompletedTask; }
        public Task<CollectorAttempt?> ReserveAsync(MarketCollectorLease lease, CollectorSubscriptionSnapshot s, string symbol, DateTime end, int budget, CancellationToken ct)
        {
            Reservations++; BeforeReserve?.Invoke();
            if (Attempts.Count >= budget || Attempts.Any(a => a.Symbol == symbol && a.ExpectedEndUtc == end)) return Task.FromResult<CollectorAttempt?>(null);
            var attempt = new CollectorAttempt(Guid.NewGuid(), lease.SourceContract, symbol, s.SessionOpenUtc, s.SessionCloseUtc, end, clock());
            Attempts.Add(attempt); return Task.FromResult<CollectorAttempt?>(attempt);
        }
        public Task SaveAsync(MarketCollectorLease lease, DelphiLiveCollectorBatch batch, CancellationToken ct)
        {
            if (CurrentOwner != lease.OwnerId) throw new InvalidOperationException("Fenced out");
            Saved.Add(batch); AfterSave?.Invoke(); return Task.CompletedTask;
        }
    }
    private sealed class Source(Store store, Func<DateTime> clock) : IIntradayCollectorSource
    {
        public string SourceContract => "SyntheticV1";
        public List<string> Reads = []; public bool Fail; public Action? AfterRead; public Func<Task>? Wait;
        public List<DateTime> Starts = [];
        public async Task<DelphiLiveCollectorBatch> FetchAsync(CollectorAttempt a, TimeSpan offset, CancellationToken ct)
        {
            Assert.Contains(a, store.Attempts); Reads.Add(a.Symbol); var started = clock(); Starts.Add(started);
            if (Wait is not null) await Wait();
            if (Fail) throw new InvalidOperationException("Synthetic failure");
            AfterRead?.Invoke();
            return new(a.RequestId, a.SourceContract, a.Symbol, a.SessionOpenUtc, a.SessionCloseUtc, a.ExpectedEndUtc, offset,
                started, clock(), clock(), [], "{}", null);
        }
    }
    private sealed class Handler(string raw) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(raw) }); }
    }
}
