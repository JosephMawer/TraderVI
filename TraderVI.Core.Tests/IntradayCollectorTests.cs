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
        rig.Now = Subscriptions.SessionCloseUtc;
        Assert.Equal("OutsideSession", (await rig.Worker.TickAsync(Subscriptions)).State);
        rig.Now = Open.AddMinutes(2).AddSeconds(5);
        var small = new IntradayCollector(rig.Store, rig.Source, Limits with { MaximumSymbols = 2 }, () => rig.Now);
        Assert.Equal("SubscriptionBudgetExceeded", (await small.TickAsync(Subscriptions)).State);
        Assert.Empty(rig.Source.Reads);
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
        public Rig() { Store = new(() => Now); Source = new(Store, () => Now); Worker = new(Store, Source, Limits, () => Now); }
    }
    private sealed class Store(Func<DateTime> clock) : IIntradayCollectorStore
    {
        public Guid? CurrentOwner; public bool DenyLease;
        public List<CollectorAttempt> Attempts = []; public List<DelphiLiveCollectorBatch> Saved = [];
        public Task<MarketCollectorLease?> AcquireAsync(string source, Guid owner, CancellationToken ct)
        {
            if (DenyLease) return Task.FromResult<MarketCollectorLease?>(null);
            CurrentOwner = owner; return Task.FromResult<MarketCollectorLease?>(new(source, owner, 1, clock().AddMinutes(2)));
        }
        public Task<bool> RenewAsync(MarketCollectorLease lease, CancellationToken ct) => Task.FromResult(CurrentOwner == lease.OwnerId);
        public Task ReleaseAsync(MarketCollectorLease lease, CancellationToken ct) { if (CurrentOwner == lease.OwnerId) CurrentOwner = null; return Task.CompletedTask; }
        public Task<CollectorAttempt?> ReserveAsync(MarketCollectorLease lease, CollectorSubscriptionSnapshot s, string symbol, DateTime end, int budget, CancellationToken ct)
        {
            if (Attempts.Count >= budget || Attempts.Any(a => a.Symbol == symbol && a.ExpectedEndUtc == end)) return Task.FromResult<CollectorAttempt?>(null);
            var attempt = new CollectorAttempt(Guid.NewGuid(), lease.SourceContract, symbol, s.SessionOpenUtc, s.SessionCloseUtc, end, clock());
            Attempts.Add(attempt); return Task.FromResult<CollectorAttempt?>(attempt);
        }
        public Task SaveAsync(MarketCollectorLease lease, DelphiLiveCollectorBatch batch, CancellationToken ct)
        {
            if (CurrentOwner != lease.OwnerId) throw new InvalidOperationException("Fenced out");
            Saved.Add(batch); return Task.CompletedTask;
        }
    }
    private sealed class Source(Store store, Func<DateTime> clock) : IIntradayCollectorSource
    {
        public string SourceContract => "SyntheticV1";
        public List<string> Reads = []; public bool Fail; public Action? AfterRead; public Func<Task>? Wait;
        public async Task<DelphiLiveCollectorBatch> FetchAsync(CollectorAttempt a, TimeSpan offset, CancellationToken ct)
        {
            Assert.Contains(a, store.Attempts); Reads.Add(a.Symbol); var started = clock();
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
