#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveArchiveTreeTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(31)] [InlineData(32)] [InlineData(100_000)]
    public async Task UpdatingAndAppendingMatchesFullReconstructionWithLogarithmicReads(int count)
    {
        var leaves = Enumerable.Range(0, count).Select(i => DelphiLiveSnapshotCodec.Hash(i.ToString())).ToList();
        var full = DelphiLiveArchiveTree.Build(leaves);
        var nodes = full.Nodes.ToDictionary(p => p.Key, p => p.Value);
        var changes = new List<DelphiLiveArchiveTree.Change>();
        if (count > 0)
        {
            changes.Add(new(count / 2, leaves[count / 2], DelphiLiveSnapshotCodec.Hash("changed")));
            leaves[count / 2] = changes[0].ContentHash;
        }
        changes.Add(new(count, null, DelphiLiveSnapshotCodec.Hash("appended")));
        leaves.Add(changes[^1].ContentHash);
        int reads = 0;
        var result = await DelphiLiveArchiveTree.ApplyAsync(count, full.Root, changes, (key, _) =>
        { reads++; return Task.FromResult(nodes.GetValueOrDefault(key)); });
        result.Root.ShouldBe(DelphiLiveArchiveTree.Build(leaves).Root);
        reads.ShouldBeLessThanOrEqualTo(36);
        result.Nodes.Count.ShouldBeLessThanOrEqualTo(37);
        foreach (var pair in result.Nodes) nodes[pair.Key] = pair.Value;
        foreach (var change in changes)
            await DelphiLiveArchiveTree.VerifyLeafAsync(result.Count, result.Root, change.Ordinal, change.ContentHash,
                (key, _) => Task.FromResult(nodes.GetValueOrDefault(key)));
    }

    [Fact]
    public async Task MissingDamagedOrWrongSourcePathsCannotBeReused()
    {
        string[] leaves = [DelphiLiveSnapshotCodec.Hash("a"), DelphiLiveSnapshotCodec.Hash("b")];
        var tree = DelphiLiveArchiveTree.Build(leaves);
        var changes = new[] { new DelphiLiveArchiveTree.Change(1, leaves[1], DelphiLiveSnapshotCodec.Hash("c")) };
        await Should.ThrowAsync<InvalidDataException>(() => DelphiLiveArchiveTree.ApplyAsync(2, tree.Root, changes, (_, _) => Task.FromResult<string?>(null)));
        await Should.ThrowAsync<InvalidDataException>(() => DelphiLiveArchiveTree.ApplyAsync(2, tree.Root, changes, (_, _) => Task.FromResult<string?>("{}")));
        await Should.ThrowAsync<InvalidDataException>(() => DelphiLiveArchiveTree.ApplyAsync(2, tree.Root,
            [changes[0] with { Expected = leaves[0] }], (key, _) => Task.FromResult<string?>(tree.Nodes[key])));
        await Should.ThrowAsync<InvalidDataException>(() => DelphiLiveArchiveTree.ApplyAsync(2, tree.Root,
            [changes[0], changes[0]], (key, _) => Task.FromResult<string?>(tree.Nodes[key])));
    }

    [Fact]
    public void IncrementalArchiveRestoresTheExactLegacySnapshotAndRejectsRootAndLeafChanges()
    {
        var time = new DateTime(2026, 10, 21, 13, 30, 0, DateTimeKind.Utc);
        var state = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000m, "CAD", new(2026, 10, 21), time, time.AddDays(-1), "Fixture", "Test"));
        state = state with { Marks = [new(Guid.NewGuid(), new(2026, 10, 21), DelphiLivePortfolioMarkKind.Opening,
            time, true, 1000m, [], "Test")] };
        var package = DelphiLiveSnapshotCodec.Pack(state);
        var counts = DelphiLiveSnapshotCodec.ReadCounts(package.SnapshotJson);
        var roots = DelphiLiveSnapshotCodec.Collections.ToDictionary(name => name,
            name => DelphiLiveArchiveTree.BuildCollection(name, package.Items.Where(i => i.Collection == name).Select(i => i.ContentHash).ToArray()).Root);
        string root = DelphiLiveSnapshotCodec.CreateIncrementalRoot(package.SnapshotJson, counts, roots);
        var restored = DelphiLiveSnapshotCodec.Unpack(root, package.Items, package.Contents);
        DelphiLiveLedgerJson.Serialize(restored).ShouldBe(DelphiLiveLedgerJson.Serialize(state));
        Should.Throw<InvalidOperationException>(() => DelphiLiveSnapshotCodec.Unpack(root.Replace("1000", "1001"), package.Items, package.Contents));
        Should.Throw<InvalidOperationException>(() => DelphiLiveSnapshotCodec.Unpack(root,
            [package.Items[0] with { ContentHash = DelphiLiveSnapshotCodec.Hash("changed") }], package.Contents));
    }
}
