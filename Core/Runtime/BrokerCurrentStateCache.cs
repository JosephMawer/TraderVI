#nullable enable
using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Core.Runtime;

public sealed record BrokerCurrentStateKey(Guid BindingId, long Revision, Guid RevisionId, Guid? PreviousRevisionId,
    Guid SessionId, Guid BaselineId, DateTimeOffset SavedAt, string ContentHash);

/// <summary>Process-local verified current state; callers must independently verify continuity/guards/holds before every reuse.</summary>
public sealed class BrokerCurrentStateCache
{
    public sealed record Entry(BrokerCurrentStateKey Key, BrokerManagedSession State, ImmutableDictionary<string, string> Contents);
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    public Entry? Read(BrokerCurrentStateKey key, bool qualified)
    {
        lock (gate)
        {
            if (!qualified || !entries.TryGetValue(key.BindingId, out var entry) || entry.Key != key)
            { entries.Remove(key.BindingId); return null; }
            return entry;
        }
    }
    public void Save(BrokerCurrentStateKey key, BrokerManagedSession state, IReadOnlyDictionary<string, string> contents)
    {
        if (state.BindingId != key.BindingId || state.Revision != key.Revision || state.RevisionId != key.RevisionId ||
            state.PreviousRevisionId != key.PreviousRevisionId || state.SessionId != key.SessionId || state.SavedAt != key.SavedAt ||
            state.Approval.Review.BaselineId != key.BaselineId || !CompleteTradingStrategy.IsHash(key.ContentHash))
            throw new InvalidDataException("Cached broker state differs from its verified revision identity.");
        lock (gate)
        {
            entries.Remove(key.BindingId);
            // At most four current bindings and 64 MiB of serialized referenced content. No revision history is cached.
            long size = contents.Values.Sum(s => 2L * s.Length);
            if (size > 64L * 1024 * 1024) return;
            while (entries.Count >= 4 || entries.Values.Sum(e => e.Contents.Values.Sum(s => 2L * s.Length)) + size > 64L * 1024 * 1024)
                entries.Remove(entries.Keys.First());
            entries.Add(key.BindingId, new(key, state, contents.ToImmutableDictionary(StringComparer.Ordinal)));
        }
    }
}
