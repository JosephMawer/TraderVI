#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

/// <summary>New content/item versions only, derived from an already verified immutable source revision.</summary>
public sealed record DelphiLiveSnapshotDelta(ImmutableArray<DelphiLiveSnapshotItem> Items,
    ImmutableDictionary<string, string> Contents)
{
    public static DelphiLiveSnapshotDelta Create(IReadOnlyList<DelphiLiveSnapshotItem> priorItems,
        IReadOnlyDictionary<string, string> priorContents, DelphiLiveSnapshotPackage next)
    {
        var prior = priorItems.ToDictionary(i => (i.Collection, i.Ordinal), i => i.ContentHash);
        var changed = next.Items.Where(i => !prior.TryGetValue((i.Collection, i.Ordinal), out var hash) || hash != i.ContentHash).ToImmutableArray();
        var contents = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var item in next.Contents)
        {
            if (priorContents.TryGetValue(item.Key, out var old))
            {
                if (!string.Equals(old, item.Value, StringComparison.Ordinal))
                    throw new InvalidOperationException("An existing immutable content identity changed.");
            }
            else
            {
                if (DelphiLiveSnapshotCodec.Hash(item.Value) != item.Key)
                    throw new InvalidOperationException("New snapshot content does not match its fingerprint.");
                contents.Add(item.Key, item.Value);
            }
        }
        return new(changed, contents.ToImmutable());
    }
}
