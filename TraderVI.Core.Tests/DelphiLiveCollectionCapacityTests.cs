#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveCollectionCapacityTests
{
    [Fact]
    public async Task FullDisjointDailyUnionAndHeldSymbolsShareOneRequestPerSymbolAcrossPolicies()
    {
        var fixture = new Fixture(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100));
        var result = await fixture.Workflow.RunCycleAsync(fixture.Cycle, fixture.Targets, fixture.Lease);

        result.ExpectedTargets.ShouldBe(61); // Two disjoint top-25 lenses, ten holdings, XIU.
        result.AttemptedTargets.ShouldBe(61);
        result.OperationalReceipts.ShouldBe(61);
        result.MissedTargets.ShouldBe(0);
        fixture.Requests.Select(r => r.Symbol).Distinct().Count().ShouldBe(61);
        fixture.Store.Expected.Count.ShouldBe(61);
        fixture.Store.Receipts.Sum(r => r.ProviderAttemptCount).ShouldBe(183);
        fixture.Store.Receipts.Sum(r => r.ProviderRequestCount).ShouldBe(122);
        fixture.Leases.ReleaseCount.ShouldBe(0);
    }

    [Fact]
    public async Task PersistenceCrossingDeadlineLeavesFullDenominatorAndStopsFurtherRequests()
    {
        var fixture = new Fixture(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        var result = await fixture.Workflow.RunCycleAsync(fixture.Cycle, fixture.Targets, fixture.Lease);

        result.ExpectedTargets.ShouldBe(61);
        result.AttemptedTargets.ShouldBe(50);
        result.OperationalReceipts.ShouldBe(49);
        result.LateResearchReceipts.ShouldBe(1);
        result.MissedTargets.ShouldBe(12); // One persisted late, eleven never attempted.
        result.Status.ShouldBe("DeadlineExceeded");
        fixture.Store.Expected.Count.ShouldBe(61);
        fixture.Requests.All(r => r.RequestStartedUtc < fixture.Cycle.DeadlineUtc).ShouldBeTrue();
        fixture.Store.Receipts[^1].ReceivedUtc.ShouldBe(fixture.Cycle.DeadlineUtc.AddSeconds(-1));
        fixture.Store.Receipts[^1].Disposition.ShouldBe("LateResearchOnly");
        fixture.Clock.UtcNow.ShouldBe(fixture.Cycle.DeadlineUtc);
        fixture.Requests[0].Symbol.ShouldBe("H00"); // Pending protection precedes candidate work.
        fixture.Store.Receipts.Sum(r => r.ProviderAttemptCount).ShouldBe(150);
        fixture.Store.Receipts.Sum(r => r.ProviderRequestCount).ShouldBe(100);
    }

    private sealed class Fixture
    {
        private static readonly DateTime End = new(2026, 9, 8, 13, 35, 0, DateTimeKind.Utc);
        public FakeClock Clock { get; } = new() { UtcNow = End.AddMinutes(2) };
        public FakeLeases Leases { get; } = new();
        public List<DelphiLiveMarketDataRequest> Requests { get; } = [];
        public FakeStore Store { get; }
        public DelphiLiveCollectionWorkflow Workflow { get; }
        public DelphiLiveCollectionCycle Cycle { get; } = new(Guid.NewGuid(), Guid.NewGuid(), End.AddMinutes(-5),
            End, End.AddMinutes(2), End.AddMinutes(7), 1, 1);
        public DelphiLiveLease Lease { get; } = new(Guid.NewGuid(), "capacity-fixture", 1, End, End.AddMinutes(15));
        public DelphiLiveObservationTarget[] Targets { get; }

        public Fixture(TimeSpan providerDuration, TimeSpan persistenceDuration)
        {
            var candidates = Enumerable.Range(0, 50).Select(n => new DelphiLiveObservationTarget($"C{n:00}",
                DelphiLiveCollectionPriorityClass.ActiveCandidate, n, true, false));
            var holdings = Enumerable.Range(0, 10).Select(n => new DelphiLiveObservationTarget($"H{n:00}",
                n == 0 ? DelphiLiveCollectionPriorityClass.PendingProtectiveSell : DelphiLiveCollectionPriorityClass.HeldSymbol,
                n, false, true));
            var union = candidates.Concat(holdings).Append(new("XIU", DelphiLiveCollectionPriorityClass.XiuBenchmark, 0, false, false)).ToArray();
            Targets = union.Concat(union).Concat(union).ToArray(); // Three policy consumers, shared facts.
            Store = new(Clock, persistenceDuration);
            Workflow = new(Clock, new FakeSource(request =>
            {
                Store.Expected.Count.ShouldBe(61); // Complete durable denominator exists before first request.
                Requests.Add(request);
                Clock.UtcNow += providerDuration;
                return new(request, new OhlcvBar(request.BarStartUtc, 100, 101, 99, 100, 50), Clock.UtcNow, "OperationalOnTime")
                {
                    ProviderAttemptCount = 3, ProviderRequestCount = 2,
                    ProviderFetchStartedUtc = request.RequestStartedUtc
                };
            }), Store, Leases);
        }
    }

    private sealed class FakeClock : IDelphiLiveClock { public DateTime UtcNow { get; set; } }
    private sealed class FakeSource(Func<DelphiLiveMarketDataRequest, DelphiLiveMarketDataReceipt> get) : IDelphiLiveMarketDataSource
    {
        public Task<DelphiLiveMarketDataReceipt> GetExactFiveMinuteBarAsync(DelphiLiveMarketDataRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(get(request));
        public Task<DelphiLiveQuoteReceipt> GetQuoteAsync(DelphiLiveQuoteRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FakeStore(FakeClock clock, TimeSpan persistenceDuration) : IDelphiLiveCycleStore
    {
        public IReadOnlyList<DelphiLiveObservationTarget> Expected { get; private set; } = [];
        public List<DelphiLiveMarketDataReceipt> Receipts { get; } = [];
        public Task BeginCycleAsync(DelphiLiveCollectionCycle cycle, IReadOnlyList<DelphiLiveObservationTarget> expectedTargets,
            CancellationToken cancellationToken = default) { Expected = expectedTargets; return Task.CompletedTask; }
        public Task<DelphiLiveMarketDataReceipt> RecordReceiptAsync(DelphiLiveMarketDataReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            clock.UtcNow += persistenceDuration;
            var durable = clock.UtcNow >= receipt.Request.DeadlineUtc ? receipt with { Disposition = "LateResearchOnly" } : receipt;
            Receipts.Add(durable);
            return Task.FromResult(durable);
        }
        public Task CompleteCycleAsync(Guid cycleId, DateTime completedUtc, string status,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FakeLeases : IDelphiLiveLeaseStore
    {
        public int ReleaseCount { get; private set; }
        public Task<DelphiLiveLease?> TryAcquireAsync(string ownerId, DateTime acquiredUtc, DateTime expiresUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryRenewAsync(DelphiLiveLease lease, DateTime renewedUtc, DateTime expiresUtc,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReleaseAsync(DelphiLiveLease lease, DateTime releasedUtc, CancellationToken cancellationToken = default)
        { ReleaseCount++; return Task.CompletedTask; }
    }
}
