#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveWorkingCheckpointTests
{
    [Fact]
    public async Task UpdatingAnOpenPositionAfterPruningPreservesItsOriginalArchiveOrdinal()
    {
        var full = DelphiLiveWorkingStateTests.History(1_000);
        Guid positionId = Guid.NewGuid(), actionId = Guid.NewGuid();
        var position = new DelphiLiveLedgerPosition(positionId, "BBB", 1, 10m, full.UpdatedUtc, actionId, "{}",
            DelphiLiveProfitProtectionState.Open(positionId, 10m));
        var fill = new DelphiLiveLedgerFill(Guid.NewGuid(), actionId, positionId, Guid.NewGuid(), "BBB", DelphiLiveActionSide.Buy,
            1, 10m, DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.SideSpecific, full.UpdatedUtc, full.CurrentSession!.Value);
        full = full with { Positions = full.Positions.Add(position), Fills = full.Fills.Add(fill), Cash = full.Cash - 10m };
        var package = DelphiLiveSnapshotCodec.Pack(full);
        var qualified = DelphiLiveWorkingCheckpoint.Qualify(full, package);
        var all = package.Contents.ToDictionary(p => p.Key, p => p.Value);
        foreach (var pair in qualified.NewContents) all[pair.Key] = pair.Value;
        var currentContents = DelphiLiveSnapshotCodec.Pack(qualified.Snapshot).Contents;
        var small = qualified.Checkpoint.Restore(package.SnapshotJson, currentContents);
        small.Positions.Length.ShouldBe(2);
        var changedPosition = position with { Protection = position.Protection with { HighestCompletedFiveMinuteClose = 11m } };
        var next = small with { Revision = small.Revision + 1, Positions = small.Positions.SetItem(1, changedPosition) };
        int reads = 0;
        var prepared = await qualified.Checkpoint.AdvanceAsync(small, next, currentContents, (key, _) =>
        { reads++; return Task.FromResult(all.GetValueOrDefault(key)); }, CancellationToken.None);
        reads.ShouldBeLessThanOrEqualTo(10);
        var changed = prepared.ChangedItems.Single();
        changed.Collection.ShouldBe("positions"); changed.Ordinal.ShouldBe(1_000);
        foreach (var pair in prepared.NewContents) all[pair.Key] = pair.Value;
        var items = package.Items.Select(i => i.Collection == "positions" && i.Ordinal == 1_000 ? changed : i).ToArray();
        var restored = DelphiLiveSnapshotCodec.Unpack(prepared.Archive.SnapshotJson, items, all);
        DelphiLiveLedgerIntegrity.ValidateTransition(full, restored);
        restored.Positions.Take(1_000).Select(p => p.PositionId).ShouldBe(full.Positions.Take(1_000).Select(p => p.PositionId));
        restored.Positions[^1].Protection.HighestCompletedFiveMinuteClose.ShouldBe(11m);
        restored.Cash.ShouldBe(full.Cash);
    }

    [Fact]
    public async Task LongArchiveAppendAndDayRolloverReconstructExactFullHistoryWithBoundedWork()
    {
        var now = new DateTime(2026, 10, 22, 14, 0, 0, DateTimeKind.Utc);
        var full = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000m, "CAD", new(2026, 1, 1), now.AddYears(-1), now.AddYears(-2), "Fixture", "Test"))
            with { CurrentSession = new(2026, 10, 22), UpdatedUtc = now };
        full = full with { Marks = Enumerable.Range(0, 10_000).Select(i => new DelphiLiveLedgerMark(Guid.NewGuid(),
            new(2026, 10, 21), DelphiLivePortfolioMarkKind.Checkpoint, now.AddDays(-1).AddSeconds(i), true, 1000m, [], "Old")).ToImmutableArray() };
        var package = DelphiLiveSnapshotCodec.Pack(full);
        var qualified = DelphiLiveWorkingCheckpoint.Qualify(full, package);
        var contents = package.Contents.Concat(qualified.NewContents).ToDictionary(p => p.Key, p => p.Value);
        var archiveItems = package.Items.ToDictionary(i => (i.Collection, i.Ordinal));
        var checkpoint = qualified.Checkpoint;
        string archiveRoot = package.SnapshotJson;
        for (int day = 0; day < 3; day++)
        {
            var small = checkpoint.Restore(archiveRoot, contents);
            small.Marks.Length.ShouldBe(1);
            var mark = new DelphiLiveLedgerMark(Guid.NewGuid(), new DateOnly(2026, 10, 22).AddDays(day),
                DelphiLivePortfolioMarkKind.Checkpoint, now.AddDays(day), true, 1000m, [], "New");
            var next = small with { Revision = small.Revision + 1, UpdatedUtc = now.AddDays(day),
                CurrentSession = mark.TradingDate, Marks = small.Marks.Add(mark) };
            full = full with { Revision = next.Revision, UpdatedUtc = next.UpdatedUtc, CurrentSession = next.CurrentSession,
                Marks = full.Marks.Add(mark) };
            int reads = 0;
            var prepared = await checkpoint.AdvanceAsync(small, next, contents, (key, _) =>
            { reads++; return Task.FromResult(contents.GetValueOrDefault(key)); }, CancellationToken.None);
            reads.ShouldBe(0);
            prepared.ChangedItems.Length.ShouldBe(1);
            prepared.NewContents.Count.ShouldBe(1);
            foreach (var item in prepared.ChangedItems) archiveItems[(item.Collection, item.Ordinal)] = item;
            foreach (var pair in prepared.NewContents) contents[pair.Key] = pair.Value;
            archiveRoot = prepared.Archive.SnapshotJson;
            checkpoint = prepared.Checkpoint;
            var reopened = DelphiLiveSnapshotCodec.Unpack(archiveRoot, archiveItems.Values.ToArray(), contents);
            DelphiLiveLedgerJson.Serialize(reopened).ShouldBe(DelphiLiveLedgerJson.Serialize(full));
            checkpoint.Restore(archiveRoot, contents).Cash.ShouldBe(full.Cash);
        }
        Should.Throw<InvalidDataException>(() => (checkpoint with { Totals = checkpoint.Totals with { ArchivedCashNet = 1m } }).Restore(archiveRoot, contents));
        Should.Throw<InvalidDataException>(() => checkpoint.Restore(package.SnapshotJson, contents));
        var corrupt = contents.ToDictionary(p => p.Key, p => p.Value);
        corrupt[checkpoint.Items[0].ContentHash] += " ";
        Should.Throw<InvalidOperationException>(() => checkpoint.Restore(archiveRoot, corrupt));
    }
}
