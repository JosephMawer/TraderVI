#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

/// <summary>
/// Reuses only fully verified immutable domain values. Callers must still read all revision-scoped
/// parts from SQL on every load; exact fresh bytes, not a revision or a hash alone, establish a hit.
/// </summary>
internal sealed class DelphiLiveSnapshotReadCache(int capacity = 4, long maximumBytes = 256 * 1024 * 1024)
{
    private sealed record Entry(string Root, ImmutableArray<DelphiLiveSnapshotItem> Items,
        ImmutableDictionary<string, string> Contents, DelphiLivePortfolioSnapshot Snapshot, long Bytes);
    private readonly object gate = new();
    private readonly LinkedList<Entry> entries = new();
    private long retainedBytes;

    public DelphiLivePortfolioSnapshot Read(string root, IReadOnlyList<DelphiLiveSnapshotItem> items,
        IReadOnlyDictionary<string, string> contents)
    {
        // Retain copies of collection membership. The supplied collections may be mutable even
        // though each value and every reachable domain record are immutable.
        var stableItems = items.ToImmutableArray();
        var stableContents = contents.ToImmutableDictionary(StringComparer.Ordinal);
        lock (gate)
        {
            for (var node = entries.First; node is not null; node = node.Next)
            {
                var entry = node.Value;
                if (!string.Equals(root, entry.Root, StringComparison.Ordinal) ||
                    !stableItems.SequenceEqual(entry.Items) || stableContents.Count != entry.Contents.Count ||
                    stableContents.Any(pair => !entry.Contents.TryGetValue(pair.Key, out var value) ||
                        !string.Equals(pair.Value, value, StringComparison.Ordinal))) continue;
                entries.Remove(node);
                entries.AddFirst(node);
                return entry.Snapshot;
            }
        }
        // Never admit an unvalidated payload, including after a changed/missing/corrupt part.
        var snapshot = DelphiLiveSnapshotCodec.Unpack(root, stableItems, stableContents);
        // Conservative retained-size allowance includes raw and decoded strings, records and
        // collection nodes. Oversized snapshots remain readable without entering this cache.
        long bytes = 8L * root.Length + stableItems.Length * 2048L +
            stableContents.Sum(pair => 8L * (pair.Key.Length + pair.Value.Length) + 2048L);
        if (capacity > 0 && bytes <= maximumBytes)
        {
            lock (gate)
            {
                // Keep only the most recently verified revision of a portfolio. Concurrent
                // misses and frequent append-only revisions must not retain duplicate histories.
                for (var node = entries.First; node is not null;)
                {
                    var next = node.Next;
                    if (node.Value.Snapshot.PortfolioId == snapshot.PortfolioId)
                    { retainedBytes -= node.Value.Bytes; entries.Remove(node); }
                    node = next;
                }
                while (entries.Count >= capacity || retainedBytes + bytes > maximumBytes)
                {
                    retainedBytes -= entries.Last!.Value.Bytes;
                    entries.RemoveLast();
                }
                entries.AddFirst(new Entry(root, stableItems, stableContents, snapshot, bytes));
                retainedBytes += bytes;
            }
        }
        return snapshot;
    }
}
