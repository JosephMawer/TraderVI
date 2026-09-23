#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveSnapshotCodecTests
{
    [Fact]
    public void CompleteDomainSnapshot_RoundTripsAndOldReaderFailsClosed()
    {
        var open = new DateTime(2026, 9, 17, 13, 30, 0, DateTimeKind.Utc);
        var state = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000m, "CAD", DateOnly.FromDateTime(open), open, open.AddDays(-1), "Fixture", "Test"));
        var id = Guid.NewGuid();
        state = state with { Positions = [new(id, "ABC", 2, 10.50m, open, Guid.NewGuid(),
            "{\"report\": \"é / \\\"quoted\\\"\\ntext\"}", DelphiLiveProfitProtectionState.Open(id, 10.50m))] };
        var package = DelphiLiveSnapshotCodec.Pack(state);
        var restored = DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents);
        DelphiLiveLedgerJson.Serialize(restored).ShouldBe(DelphiLiveLedgerJson.Serialize(state));
        Should.Throw<JsonException>(() => DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(package.SnapshotJson));
    }

    [Fact]
    public void RepeatedReports_AreSavedOnceAndCompactRootDoesNotAccumulateQuoteHistory()
    {
        var small = DelphiLiveSnapshotCodec.PackJson(Json(1));
        var large = DelphiLiveSnapshotCodec.PackJson(Json(1000));
        large.Contents.Values.Count(v => v.Contains(new string('x', 4096))).ShouldBe(1);
        large.Contents.Count.ShouldBe(1001);
        (large.SnapshotJson.Length - small.SnapshotJson.Length).ShouldBeLessThan(10);
        (large.Contents.Values.Sum(v => v.Length) / (double)Json(1000).Length).ShouldBeLessThan(0.1);
        DelphiLiveSnapshotCodec.Fingerprint(DelphiLiveSnapshotCodec.RestoreJson(large.SnapshotJson, large.Items, large.Contents))
            .ShouldBe(DelphiLiveSnapshotCodec.Fingerprint(Json(1000)));
    }

    [Fact]
    public void OlderOptionalPropertiesAndArrayOrderArePreservedWithoutInjectingDefaults()
    {
        string raw = Json(3);
        var package = DelphiLiveSnapshotCodec.PackJson(raw);
        var restored = DelphiLiveSnapshotCodec.RestoreJson(package.SnapshotJson, package.Items, package.Contents);
        JsonElement.DeepEquals(JsonDocument.Parse(raw).RootElement, JsonDocument.Parse(restored).RootElement).ShouldBeTrue();
        restored.ShouldNotContain("candidateStates");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("duplicate")]
    [InlineData("future")]
    [InlineData("version")]
    [InlineData("count")]
    public void CorruptOrWrongRevisionPartsFailClosed(string corruption)
    {
        var package = DelphiLiveSnapshotCodec.PackJson(Json(3));
        var contents = package.Contents.ToDictionary(x => x.Key, x => x.Value);
        var items = package.Items.ToList();
        var root = JsonNode.Parse(package.SnapshotJson)!;
        switch (corruption)
        {
            case "missing": contents.Remove(items[0].ContentHash); break;
            case "corrupt": contents[items[0].ContentHash] += " "; break;
            case "duplicate": items[1] = items[0]; break;
            case "future": items[0] = items[0] with { ContentHash = items[2].ContentHash }; break;
            case "version": root["$storage"]!["version"] = 99; break;
            case "count": root["$storage"]!["counts"]!["quotes"] = 2; break;
        }
        Should.Throw<InvalidOperationException>(() => DelphiLiveSnapshotCodec.RestoreJson(root.ToJsonString(), items, contents));
    }

    [Fact]
    public void FingerprintIgnoresObjectOrderButPreservesReportsAndArrayOrder()
    {
        DelphiLiveSnapshotCodec.Fingerprint("{\"b\":2,\"a\":1}").ShouldBe(DelphiLiveSnapshotCodec.Fingerprint("{\"a\":1,\"b\":2}"));
        DelphiLiveSnapshotCodec.Fingerprint("[1,2]").ShouldNotBe(DelphiLiveSnapshotCodec.Fingerprint("[2,1]"));
        DelphiLiveSnapshotCodec.Fingerprint("\"a b\"").ShouldNotBe(DelphiLiveSnapshotCodec.Fingerprint("\"ab\""));
    }

    private static string Json(int count)
    {
        var root = new JsonObject { ["portfolioId"] = "00000000-0000-0000-0000-000000000001", ["revision"] = count };
        foreach (string name in DelphiLiveSnapshotCodec.Collections) root[name] = new JsonArray();
        for (int i = 0; i < count; i++) root["quotes"]!.AsArray().Add(new JsonObject
            { ["quoteId"] = i, ["dossierJson"] = new string('x', 4096), ["value"] = 10.50m });
        return root.ToJsonString();
    }
}
