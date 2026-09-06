#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveCollectionWorkflowTests
{
    private static readonly DateTime End = new(2026, 9, 8, 13, 35, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HostLeaseSurvivesCyclesAndDurableDispositionControlsCoverage()
    {
        var fixture = new Fixture();
        fixture.Store.SettledDisposition = "ConflictingDuplicate";
        var result = await fixture.Workflow.RunCycleAsync(fixture.Cycle, fixture.Targets, fixture.Lease);
        result.OperationalReceipts.ShouldBe(0);
        result.MissedTargets.ShouldBe(1);
        fixture.Leases.Releases.ShouldBe(0);
        fixture.Store.Events.ShouldBe(new[] { "Begin", "Receipt", "Complete" });
    }

    [Fact]
    public async Task LateResponseCannotCreateAnOperationalReceiptOrStartRemainingRequests()
    {
        var fixture = new Fixture();
        fixture.Source.Get = request =>
        {
            fixture.Clock.UtcNow = fixture.Cycle.DeadlineUtc;
            return Task.FromResult(fixture.Receipt(request));
        };
        var result = await fixture.Workflow.RunCycleAsync(fixture.Cycle,
            new[] { fixture.Targets[0], new DelphiLiveObservationTarget("ABC", DelphiLiveCollectionPriorityClass.ActiveCandidate, 0, true, false) }, fixture.Lease);
        result.AttemptedTargets.ShouldBe(1);
        result.LateResearchReceipts.ShouldBe(1);
        result.MissedTargets.ShouldBe(2);
        result.Status.ShouldBe("DeadlineExceeded");
    }

    [Fact]
    public async Task ConcurrentCycleFailsBeforeSecondProviderRequest()
    {
        var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Source.Get = async request =>
        {
            entered.SetResult();
            await release.Task;
            return fixture.Receipt(request);
        };
        Task first = fixture.Workflow.RunCycleAsync(fixture.Cycle, fixture.Targets, fixture.Lease);
        await entered.Task;
        await Should.ThrowAsync<InvalidOperationException>(() =>
            fixture.Workflow.RunCycleAsync(fixture.Cycle with { CycleId = Guid.NewGuid() }, fixture.Targets, fixture.Lease));
        release.SetResult();
        await first;
        fixture.Store.Events.ShouldBe(new[] { "Begin", "Receipt", "Complete" });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(300)]
    public async Task EarlyAndExpiredCyclesNeverRequestMarketData(int seconds)
    {
        var fixture = new Fixture();
        fixture.Clock.UtcNow = fixture.Cycle.ScheduledStartUtc.AddSeconds(seconds);
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.Workflow.RunCycleAsync(fixture.Cycle, fixture.Targets, fixture.Lease));
        fixture.Store.Events.Count.ShouldBe(0);
    }

    [Fact]
    public void ClaimedSuccessfulReceiptWithoutAnExactBarIsAMiss()
    {
        var fixture = new Fixture();
        var request = new DelphiLiveMarketDataRequest(fixture.Cycle.CycleId, "XIU", End.AddMinutes(-5), End,
            fixture.Cycle.DeadlineUtc, fixture.Clock.UtcNow, 1);
        DelphiLiveCollectionWorkflow.NormalizeReceipt(request,
            new(request, null, fixture.Clock.UtcNow, "OperationalOnTime"), fixture.Cycle.DeadlineUtc)
            .Disposition.ShouldBe("NoCompletedBar");
    }

    private sealed class Fixture
    {
        public FakeClock Clock { get; } = new();
        public FakeStore Store { get; } = new();
        public FakeLeases Leases { get; } = new();
        public FakeSource Source { get; } = new();
        public DelphiLiveCollectionCycle Cycle { get; } = new(Guid.NewGuid(), Guid.NewGuid(), End.AddMinutes(-5), End, End.AddMinutes(2), End.AddMinutes(7), 1, 1);
        public DelphiLiveLease Lease { get; } = new(Guid.NewGuid(), "test", 1, End, End.AddMinutes(15));
        public DelphiLiveObservationTarget[] Targets { get; } = [new("XIU", DelphiLiveCollectionPriorityClass.XiuBenchmark, 0, false, false)];
        public DelphiLiveCollectionWorkflow Workflow { get; }
        public Fixture()
        {
            Source.Get = request => Task.FromResult(Receipt(request));
            Workflow = new(Clock, Source, Store, Leases);
        }
        public DelphiLiveMarketDataReceipt Receipt(DelphiLiveMarketDataRequest request) =>
            new(request, new OhlcvBar(request.BarStartUtc, 100, 101, 99, 100, 50), Clock.UtcNow, "OperationalOnTime");
    }
    private sealed class FakeClock : IDelphiLiveClock { public DateTime UtcNow { get; set; } = End.AddMinutes(2); }
    private sealed class FakeSource : IDelphiLiveMarketDataSource
    {
        public Func<DelphiLiveMarketDataRequest, Task<DelphiLiveMarketDataReceipt>> Get { get; set; } = null!;
        public Task<DelphiLiveMarketDataReceipt> GetExactFiveMinuteBarAsync(DelphiLiveMarketDataRequest request, CancellationToken cancellationToken = default) => Get(request);
        public Task<DelphiLiveQuoteReceipt> GetQuoteAsync(DelphiLiveQuoteRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FakeStore : IDelphiLiveCycleStore
    {
        public List<string> Events { get; } = [];
        public string? SettledDisposition { get; set; }
        public Task BeginCycleAsync(DelphiLiveCollectionCycle cycle, IReadOnlyList<DelphiLiveObservationTarget> expectedTargets, CancellationToken cancellationToken = default) { Events.Add("Begin"); return Task.CompletedTask; }
        public Task<DelphiLiveMarketDataReceipt> RecordReceiptAsync(DelphiLiveMarketDataReceipt receipt, CancellationToken cancellationToken = default) { Events.Add("Receipt"); return Task.FromResult(receipt with { Disposition = SettledDisposition ?? receipt.Disposition }); }
        public Task CompleteCycleAsync(Guid cycleId, DateTime completedUtc, string status, CancellationToken cancellationToken = default) { Events.Add("Complete"); return Task.CompletedTask; }
    }
    private sealed class FakeLeases : IDelphiLiveLeaseStore
    {
        public int Releases { get; private set; }
        public Task<DelphiLiveLease?> TryAcquireAsync(string ownerId, DateTime acquiredUtc, DateTime expiresUtc, CancellationToken cancellationToken = default) => Task.FromResult<DelphiLiveLease?>(new(Guid.NewGuid(), ownerId, 1, acquiredUtc, expiresUtc));
        public Task<bool> TryRenewAsync(DelphiLiveLease lease, DateTime renewedUtc, DateTime expiresUtc, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReleaseAsync(DelphiLiveLease lease, DateTime releasedUtc, CancellationToken cancellationToken = default) { Releases++; return Task.CompletedTask; }
    }
}
