using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveSnapshotDeltaTests
{
    [Fact]
    public void OneNewMarkWritesOneItemRegardlessOfArchivedMarkCount()
    {
        var open = new DateTime(2026, 10, 21, 13, 30, 0, DateTimeKind.Utc);
        var state = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000, "CAD", new(2026, 10, 21), open, open.AddDays(-1), "test", "fixture"));
        DelphiLiveLedgerMark Mark(int i) => new(Guid.NewGuid(), new(2026, 10, 20), DelphiLivePortfolioMarkKind.Checkpoint,
            open.AddDays(-1).AddSeconds(i), true, 1000, [], "Fixture");
        state = state with { Marks = Enumerable.Range(0, 10000).Select(Mark).ToImmutableArray() };
        var prior = DelphiLiveSnapshotCodec.Pack(state);
        var next = state with { Revision = 1, UpdatedUtc = open, Marks = state.Marks.Add(Mark(10001)) };
        DelphiLiveLedgerIntegrity.ValidateTransition(state, next);
        var package = DelphiLiveSnapshotCodec.Pack(next);
        var delta = DelphiLiveSnapshotDelta.Create(prior.Items, prior.Contents, package);
        Assert.Single(delta.Items); Assert.Single(delta.Contents);
        Assert.True(delta.Contents.Values.Sum(s => s.Length) < 1000);
        var items = prior.Items.Concat(delta.Items).GroupBy(i => (i.Collection, i.Ordinal)).Select(g => g.Last()).ToArray();
        var contents = prior.Contents.Concat(delta.Contents).ToDictionary(p => p.Key, p => p.Value);
        var restored = DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, items, contents);
        Assert.Equal(DelphiLiveLedgerJson.Serialize(next), DelphiLiveLedgerJson.Serialize(restored));
    }
    [Fact]
    public void ExistingContentCannotChangeBehindItsIdentity()
    {
        string content = "{\"value\":1}"; string hash = DelphiLiveSnapshotCodec.Hash(content);
        var prior = new Dictionary<string, string> { [hash] = content };
        var corrupt = new DelphiLiveSnapshotPackage("{}", "unused", new Dictionary<string, string> { [hash] = "{\"value\":2}" }, []);
        Assert.Throws<InvalidOperationException>(() => DelphiLiveSnapshotDelta.Create([], prior, corrupt));
        Assert.Throws<InvalidOperationException>(() => DelphiLiveSnapshotDelta.Create([], new Dictionary<string, string>(), corrupt));
    }
}
