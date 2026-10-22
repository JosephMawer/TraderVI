#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineArchiveCodecTests
{
    [Fact]
    public void SharedNestedFinancialRecordsRestoreExactlyWithoutDuplicatingTheirPayloads()
    {
        var orderId = Guid.NewGuid(); var observed = new DateTimeOffset(2026, 10, 21, 15, 30, 0, TimeSpan.Zero);
        var source = new { Description = new string('x', 16_384), Cash = 1250.12m, OriginalOrderId = orderId, ObservedAt = observed };
        string json = JsonSerializer.Serialize(new { Revision = 10, Approval = source,
            Account = source, History = Enumerable.Repeat(source, 100).ToArray() });
        var packed = EngineArchiveCodec.Pack(json);
        string restored = EngineArchiveCodec.Restore(packed.Manifest, packed.Contents);
        Assert.Equal(DelphiLiveSnapshotCodec.Fingerprint(json), DelphiLiveSnapshotCodec.Fingerprint(restored));
        using var read = JsonDocument.Parse(restored);
        Assert.Equal(orderId, read.RootElement.GetProperty("Approval").GetProperty("OriginalOrderId").GetGuid());
        Assert.Equal(observed, read.RootElement.GetProperty("Approval").GetProperty("ObservedAt").GetDateTimeOffset());
        Assert.True(packed.Manifest.Length + packed.Contents.Values.Sum(v => v.Length) < json.Length / 20);
        Assert.All(EngineArchiveCodec.References(packed.Manifest), h => Assert.True(packed.Contents.ContainsKey(h)));
        var next = EngineArchiveCodec.Pack(JsonSerializer.Serialize(new { Revision = 11, Approval = source,
            Account = source, History = Enumerable.Repeat(source, 100).ToArray() }));
        Assert.Empty(next.Contents.Keys.Except(packed.Contents.Keys));
    }

    [Fact]
    public void RealRecommendationStateAndLegacyRecordsRoundTripWithOriginalIdentities()
    {
        string json = JsonSerializer.Serialize(TradingRecommendationTests.LiveInput());
        var packed = EngineArchiveCodec.Pack(json);
        Assert.Equal(json, EngineArchiveCodec.Restore(packed.Manifest, packed.Contents));
        Assert.Equal(json, EngineArchiveCodec.Restore(json, new Dictionary<string, string>()));
    }

    [Fact]
    public void MissingCorruptOrSwappedRecordsAndChangedRootsAreRejected()
    {
        var packed = EngineArchiveCodec.Pack(JsonSerializer.Serialize(new { Cash = 100m, Record = new { Report = new string('x', 1024) } }));
        var missing = packed.Contents.ToDictionary(p => p.Key, p => p.Value);
        missing.Remove(missing.Keys.First());
        Assert.ThrowsAny<Exception>(() => EngineArchiveCodec.Restore(packed.Manifest, missing));
        var corrupt = packed.Contents.ToDictionary(p => p.Key, p => p.Value);
        corrupt[corrupt.Keys.First()] += " ";
        Assert.ThrowsAny<Exception>(() => EngineArchiveCodec.Restore(packed.Manifest, corrupt));
        Assert.ThrowsAny<Exception>(() => EngineArchiveCodec.Restore(packed.Manifest.Replace("\"Cash\":100", "\"Cash\":101"), packed.Contents));
        var replaced = System.Text.Json.Nodes.JsonNode.Parse(packed.Manifest)!;
        replaced["Record"] = new System.Text.Json.Nodes.JsonObject { ["$engineRef"] = DelphiLiveSnapshotCodec.Hash("{}") };
        corrupt = packed.Contents.ToDictionary(p => p.Key, p => p.Value); corrupt[DelphiLiveSnapshotCodec.Hash("{}")] = "{}";
        Assert.ThrowsAny<Exception>(() => EngineArchiveCodec.Restore(replaced.ToJsonString(), corrupt));
    }

    [Fact]
    public void ReservedMarkersAndOversizedExpansionAreRejected()
    {
        Assert.ThrowsAny<Exception>(() => EngineArchiveCodec.Pack("{\"Value\":{\"$engineRef\":\"not-content\"}}"));
        Assert.ThrowsAny<Exception>(() => EngineArchiveCodec.Pack(JsonSerializer.Serialize(new { Value = new string('x', EngineArchiveCodec.MaximumExpandedBytes) })));
        var packed = EngineArchiveCodec.Pack("{\"Cash\":1}");
        Assert.ThrowsAny<Exception>(() => EngineArchiveCodec.Restore(packed.Manifest.Replace("\"version\":1", "\"version\":2"), packed.Contents));
    }

    [Fact]
    public async System.Threading.Tasks.Task BrokerAuthorityOrdersAndRecoveryStateSurviveSharedStorage()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        await f.Workflow().ApproveAsync(f.Approval());
        await f.Workflow().TickAsync(f.Store.State!.BindingId);
        var state = f.Store.State! with { LatestDecision = new(DateTime.UtcNow, DateTimeOffset.UtcNow,
            [new("TEST", DateTime.UtcNow, "SignalTooOld")], [], ["Original permission expired"], "NoSelection", null, null) };
        Assert.NotEmpty(state.Orders);
        string json = JsonSerializer.Serialize(state);
        var packed = EngineArchiveCodec.Pack(json);
        var restored = JsonSerializer.Deserialize<global::Core.Trader.Promotion.BrokerManagedSession>(EngineArchiveCodec.Restore(packed.Manifest, packed.Contents));
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        Assert.Equal(state.Approval.Fingerprint, restored!.Approval.Fingerprint);
        Assert.Equal(state.Orders.Select(o => o.RequestId), restored.Orders.Select(o => o.RequestId));
        Assert.NotNull(state.LatestDecision);
        var inline = EngineBrokerDiagnostics.From(state, false);
        var shared = EngineBrokerDiagnostics.From(restored, true);
        Assert.Equal(JsonSerializer.Serialize(inline with { StorageFormat = "Shared" }), JsonSerializer.Serialize(shared));
    }
}
