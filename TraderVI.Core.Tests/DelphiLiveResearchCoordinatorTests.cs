#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
namespace TraderVI.Core.Tests;

public sealed class DelphiLiveResearchCoordinatorTests
{
    private static readonly DateOnly Date = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly DelphiLiveLease Lease = new(Guid.NewGuid(), "test", 1, Open, Open.AddHours(12));

    [Fact]
    public async Task EmptyWatchlistStillPersistsBothFiveCashSlotBasketsAtExactCheckpoint()
    {
        var store = new Research(); var source = new Source(); var clock = new Clock(Open.AddMinutes(22));
        var coordinator = new DelphiLiveResearchCoordinator(new Experiments(), store, source, null!, null!, Calendar(), clock, () => "test");
        await coordinator.CheckpointAsync(Context(), Open.AddMinutes(20), [], Lease);
        store.Checkpoints.Count.ShouldBe(2);
        store.Checkpoints.All(c => c.BarEndUtc == Open.AddMinutes(20)).ShouldBeTrue();
        store.Checkpoints.All(c => c.DailyTop5.Length == 5 && c.ConfirmedLiveTop5.All(s => s.Symbol is null)).ShouldBeTrue();
        source.LastThrough.ShouldBe(Open.AddMinutes(20));
    }

    [Fact]
    public async Task MissingAnchorsPersistExpectedSlotsAndReasonWithoutStockOutcomeForXiu()
    {
        var slot = new DelphiLiveExpectedResearchSlot(Guid.NewGuid(), SessionId, Date, Open.AddMinutes(20), "AAA", false, null, "MissedDeadline", false);
        var xiu = slot with { SlotId = Guid.NewGuid(), Symbol = "XIU", IsBenchmark = true };
        var source = new Source { Evidence = Evidence() with { ExpectedSlots = [slot, xiu],
            HasConflictingEvidence = true, ConflictingAnchors = ImmutableHashSet.Create($"AAA/{slot.BarEndUtc:O}") } };
        var store = new Research(); var clock = new Clock(Open.AddHours(7));
        source.AfterRead = () => clock.UtcNow = clock.UtcNow.AddSeconds(5);
        var coordinator = new DelphiLiveResearchCoordinator(new Experiments(), store, source, null!, null!, Calendar(), clock, () => "test");
        DateTime cutoff = clock.UtcNow;
        await coordinator.SessionClosedAsync(Context(), Lease);
        store.Slots.Count.ShouldBe(2);
        store.Outcomes.Single().SlotId.ShouldBe(slot.SlotId);
        store.Outcomes.Single().Outcome.ShouldBeNull();
        store.Outcomes.Single().MissingAnchorReason.ShouldBe(DelphiLiveOutcomeReasons.ConflictingEvidence);
        store.Reviews.Single().ShouldBe(cutoff); // late arrivals after read cutoff must trigger next refresh
        await coordinator.SessionClosedAsync(Context(), Lease);
        store.Outcomes.Count.ShouldBe(1); // unchanged missing result is not a second observation
        store.Slots.Count.ShouldBe(2);
    }

    private static DelphiLiveSessionContext Context() => new(new(SessionId, Date, null, null, "NoValidDelphiRun", Open, ["XIU"]),
        new(Date, Open, Open.AddHours(6.5)), [new(Guid.NewGuid(), DelphiLivePolicyDefinition.Version1.PolicyVersionId, DelphiLivePolicyRole.OperationalChampion, Date)],
        new Dictionary<Guid, DelphiLivePolicyDefinition> { [DelphiLivePolicyDefinition.Version1.PolicyVersionId] = DelphiLivePolicyDefinition.Version1 },
        new Dictionary<string, DelphiLiveFrozenCandidate>(), new Dictionary<string, DelphiLiveFrozenBaseline>());
    private static ReviewedTsxSessionCalendar Calendar() => new(new("tests", "reviewed test calendar", Date.AddDays(-1), Date.AddDays(6),
        Enumerable.Range(-1, 7).Select(i => Date.AddDays(i)).ToArray()));
    private static DelphiLiveResearchSessionEvidence Evidence() => new([], [], [], [Date, Date.AddDays(1), Date.AddDays(2), Date.AddDays(3), Date.AddDays(4)], "{}", false, false, true, false);
    private sealed class Clock(DateTime now) : IDelphiLiveClock { public DateTime UtcNow { get; set; } = now; }
    private sealed class Source : IDelphiLiveResearchEvidenceSource
    {
        public DelphiLiveResearchSessionEvidence Evidence = DelphiLiveResearchCoordinatorTests.Evidence();
        public DateTime LastThrough; public Action? AfterRead;
        public Task<DelphiLiveResearchSessionEvidence> ReadAsync(DelphiLiveSessionContext context, DateTime throughBarEndUtc, DateTime asOfUtc, CancellationToken cancellationToken = default)
        { LastThrough = throughBarEndUtc; AfterRead?.Invoke(); return Task.FromResult(Evidence); }
        public Task<IReadOnlyList<DateOnly>> ReadFrozenDatesAsync(DateOnly through, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DateOnly>>([Date]);
        public Task<IReadOnlyList<DateOnly>> ReadChangedSessionDatesAsync(DateTime asOfUtc, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DateOnly>>([Date]);
    }
    private sealed class Experiments : IDelphiLiveExperimentStore
    {
        public Task<DelphiLiveExperimentState?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<DelphiLiveExperimentState?>(null);
        public Task<DelphiLiveExperimentState> CommitAsync(long expectedRevision, DelphiLiveExperimentState next, Guid commandId, string eventKind, DelphiLiveLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DelphiLiveExperimentState> ApplyBoundaryAsync(long expectedRevision, DelphiLiveExperimentState next, DelphiLiveExperimentBoundaryPlan plan, DateOnly tradingDate, DateTime sessionOpenUtc, DelphiLiveLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Research : IDelphiLiveResearchStore
    {
        public List<DelphiLiveExpectedResearchSlot> Slots = []; public List<DelphiLiveResearchOutcomeRevision> Outcomes = [];
        public List<DelphiLiveRankingCheckpoint> Checkpoints = []; public List<DateTime> Reviews = [];
        public Task RecordExpectedSlotsAsync(IReadOnlyCollection<DelphiLiveExpectedResearchSlot> slots, DelphiLiveLease lease, CancellationToken cancellationToken = default) { Slots.AddRange(slots); return Task.CompletedTask; }
        public Task RecordRankingCheckpointAsync(DelphiLiveRankingCheckpoint checkpoint, DelphiLiveLease lease, CancellationToken cancellationToken = default) { Checkpoints.Add(checkpoint); return Task.CompletedTask; }
        public Task AppendOutcomeAsync(DelphiLiveResearchOutcomeRevision revision, DelphiLiveLease lease, CancellationToken cancellationToken = default) { Outcomes.Add(revision); return Task.CompletedTask; }
        public Task RecordSessionReviewAsync(Guid sessionId, DateTime reviewedUtc, DelphiLiveLease lease, CancellationToken cancellationToken = default) { Reviews.Add(reviewedUtc); return Task.CompletedTask; }
        public Task<IReadOnlyList<DelphiLiveExpectedResearchSlot>> ReadExpectedSlotsAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DelphiLiveExpectedResearchSlot>>(Slots);
        public Task<IReadOnlyList<DelphiLiveResearchOutcomeRevision>> ReadLatestOutcomesAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DelphiLiveResearchOutcomeRevision>>(Outcomes.GroupBy(o => o.SlotId).Select(g => g.Last()).ToArray());
        public Task<IReadOnlyList<DelphiLiveRankingCheckpoint>> ReadRankingCheckpointsAsync(DateOnly from, DateOnly through, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DelphiLiveRankingCheckpoint>>(Checkpoints);
    }
}
