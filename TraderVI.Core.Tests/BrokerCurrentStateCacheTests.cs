#nullable enable
using Core.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerCurrentStateCacheTests
{
    [Fact]
    public async Task ExactHeadCanReuseVerifiedStateButAChangedHeadOrLostQualificationCannot()
    {
        var f = new BrokerManagedSessionTests.Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        var state = f.Store.State!;
        var key = new BrokerCurrentStateKey(state.BindingId, state.Revision, state.RevisionId, state.PreviousRevisionId,
            state.SessionId, state.Approval.Review.BaselineId, state.SavedAt, new string('A', 64));
        var cache = new BrokerCurrentStateCache(); var contents = new Dictionary<string, string> { ["original"] = "fixture content" };
        cache.Save(key, state, contents); contents["original"] = "changed caller dictionary";
        Assert.Same(state, cache.Read(key, qualified: true)!.State);
        Assert.Equal("fixture content", cache.Read(key, qualified: true)!.Contents["original"]);
        Assert.Null(cache.Read(key, qualified: false));
        Assert.Null(cache.Read(key, qualified: true)); // A cleared hold must requalify, never revive a discarded entry.
        foreach (var changed in new[] { key with { ContentHash = new string('B', 64) }, key with { RevisionId = Guid.NewGuid() },
            key with { Revision = key.Revision + 1 }, key with { SavedAt = key.SavedAt.AddTicks(1) }, key with { BaselineId = Guid.NewGuid() } })
        {
            cache.Save(key, state, contents); Assert.Null(cache.Read(changed, qualified: true));
        }
        Assert.Throws<InvalidDataException>(() => cache.Save(key with { SessionId = Guid.NewGuid() }, state, contents));
    }
    [Fact]
    public async Task CacheKeepsOnlyBoundedCurrentBindings()
    {
        var cache = new BrokerCurrentStateCache(); BrokerCurrentStateKey? first = null;
        for (int i = 0; i < 5; i++)
        {
            var f = new BrokerManagedSessionTests.Fixture(); await f.Workflow().ApproveAsync(f.Approval());
            var state = f.Store.State!;
            var key = new BrokerCurrentStateKey(state.BindingId, state.Revision, state.RevisionId, state.PreviousRevisionId,
                state.SessionId, state.Approval.Review.BaselineId, state.SavedAt, new string('A', 64));
            first ??= key; cache.Save(key, state, new Dictionary<string, string>());
            Assert.NotNull(cache.Read(key, qualified: true));
        }
        Assert.Null(cache.Read(first!, qualified: true));
    }
}
