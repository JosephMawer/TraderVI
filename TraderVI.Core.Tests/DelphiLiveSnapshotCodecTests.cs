#nullable enable
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveSnapshotCodecTests
{
    private readonly ITestOutputHelper output;
    public DelphiLiveSnapshotCodecTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ReopenedHistoryChecksDoNotExpandReportsAndStillRejectEveryChangedField()
    {
        string report = new('x', 16_384);
        var state = State(1000, report) with { Cash = 979m };
        var next = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(DelphiLiveLedgerJson.Serialize(state))
            with { Revision = state.Revision + 1 };
        long before = GC.GetAllocatedBytesForCurrentThread();
        DelphiLiveLedgerIntegrity.ValidateTransition(state, next);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Reopened 1000quote prefix validation: {bytes:N0} allocated bytes.");
        Assert.True(bytes < 16_384_000, $"Prefix check allocated {bytes} bytes.");
        Reject(next.Quotes[0] with { DossierJson = report + " " });
        Reject(next.Quotes[0] with { Disposition = "rewritten" });
        Reject(next.Quotes[0] with { Observation = next.Quotes[0].Observation with { Price = 10.51m } });
        // Numeric representation was part of the original exact serialized comparison too.
        Reject(next.Quotes[0] with { Observation = next.Quotes[0].Observation with { Price = 10.500m } });
        Should.Throw<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(state,
            next with { Actions = next.Actions.SetItem(0, next.Actions[0] with { SupportingReasons = ["changed"] }) }));
        void Reject(DelphiLiveLedgerQuote quote) => Should.Throw<InvalidOperationException>(() =>
            DelphiLiveLedgerIntegrity.ValidateTransition(state, next with { Quotes = next.Quotes.SetItem(0, quote) }));
    }

    [Fact]
    public void TypedPackingPreservesEveryOriginalContentHashAndFullFingerprint()
    {
        var state = State(40, "é <>& / \"quoted\"\n😀");
        var original = DelphiLiveSnapshotCodec.PackJson(DelphiLiveLedgerJson.Serialize(state));
        var direct = DelphiLiveSnapshotCodec.Pack(state);
        direct.FullHash.ShouldBe(original.FullHash);
        direct.SnapshotJson.ShouldBe(original.SnapshotJson);
        direct.Items.ShouldBe(original.Items);
        direct.Contents.OrderBy(p => p.Key).ShouldBe(original.Contents.OrderBy(p => p.Key));
        DelphiLiveSnapshotCodec.Validate(direct.SnapshotJson, direct.Items, direct.Contents);
        DelphiLiveLedgerJson.Serialize(DelphiLiveSnapshotCodec.Unpack(direct.SnapshotJson, direct.Items, direct.Contents))
            .ShouldBe(DelphiLiveLedgerJson.Serialize(state));
    }

    [Fact]
    public void TypedPackingAvoidsExpandedReportBuffersWhileRetainingOriginalVerification()
    {
        var state = State(1000, new string('x', 16_384));
        _ = DelphiLiveSnapshotCodec.Pack(state);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var expanded = DelphiLiveSnapshotCodec.PackJson(DelphiLiveLedgerJson.Serialize(state));
        long expandedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        long expandedMs = watch.ElapsedMilliseconds;
        before = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
        var direct = DelphiLiveSnapshotCodec.Pack(state);
        long directBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"1000 quotes / 16KiB report: expanded pack {expandedMs}ms/{expandedBytes:N0} bytes; direct pack {watch.ElapsedMilliseconds}ms/{directBytes:N0} bytes.");
        Assert.True(directBytes < expandedBytes / 3, $"Direct {directBytes} versus expanded {expandedBytes} bytes.");
        direct.FullHash.ShouldBe(expanded.FullHash);
        direct.Items.ShouldBe(expanded.Items);
        DelphiLiveSnapshotCodec.Validate(direct.SnapshotJson, direct.Items, direct.Contents);
    }

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
        Should.Throw<InvalidOperationException>(() => DelphiLiveSnapshotCodec.Unpack(root.ToJsonString(), items, contents));
        Should.Throw<InvalidOperationException>(() => DelphiLiveSnapshotCodec.Validate(root.ToJsonString(), items, contents));
    }

    [Fact]
    public void FingerprintIgnoresObjectOrderButPreservesReportsAndArrayOrder()
    {
        DelphiLiveSnapshotCodec.Fingerprint("{\"b\":2,\"a\":1}").ShouldBe(DelphiLiveSnapshotCodec.Fingerprint("{\"a\":1,\"b\":2}"));
        DelphiLiveSnapshotCodec.Fingerprint("[1,2]").ShouldNotBe(DelphiLiveSnapshotCodec.Fingerprint("[2,1]"));
        DelphiLiveSnapshotCodec.Fingerprint("\"a b\"").ShouldNotBe(DelphiLiveSnapshotCodec.Fingerprint("\"ab\""));
    }

    [Fact]
    public void DirectReadPreservesAllCollectionsAndSharesExactReportsAcrossRecordTypes()
    {
        var state = State(8, "{\"report\":\"é / \\\"quoted\\\"\\nline <>& 😀\"}");
        var package = DelphiLiveSnapshotCodec.Pack(state);
        var restored = DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents);
        DelphiLiveLedgerJson.Serialize(restored).ShouldBe(DelphiLiveLedgerJson.Serialize(state));
        string report = restored.Positions[0].OriginalEntryDossierJson;
        Assert.Same(report, restored.SessionOpeningPositions[0].OriginalEntryDossierJson);
        Assert.Same(report, restored.Actions[0].DossierJson);
        Assert.All(restored.Quotes, quote => Assert.Same(report, quote.DossierJson));
        // Materialization must not change the supplied compact payload or its contents.
        DelphiLiveSnapshotCodec.Validate(package.SnapshotJson, package.Items, package.Contents);
    }

    [Fact]
    public void DirectReadPreservesLegacyAndMissingOptionalFields()
    {
        var raw = JsonNode.Parse(DelphiLiveLedgerJson.Serialize(State(2, "report")))!.AsObject();
        raw.Remove("candidateStates");
        raw.Remove("dailySourceRevisionId");
        raw.Remove("comparisonAccount");
        raw["positions"]![0]!.AsObject().Remove("originalEntryDossierJson");
        raw["quotes"]![0]!["dossierJson"] = null;
        string json = raw.ToJsonString();
        var expected = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(json);
        var legacy = DelphiLiveSnapshotCodec.Unpack(json, [], new Dictionary<string, string>());
        DelphiLiveLedgerJson.Serialize(legacy).ShouldBe(DelphiLiveLedgerJson.Serialize(expected));
        var package = DelphiLiveSnapshotCodec.PackJson(json);
        var restored = DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents);
        DelphiLiveLedgerJson.Serialize(restored).ShouldBe(DelphiLiveLedgerJson.Serialize(expected));
    }

    [Fact]
    public void StreamingValidationMatchesOriginalFingerprintIncludingEscapesAndNumberSpellings()
    {
        var root = JsonNode.Parse(Json(4))!.AsObject();
        root["unknownOptional"] = JsonNode.Parse("""{"z":[1e-07,-0,10.5000,null,true],"a":"é <>& / \"\n😀"}""");
        string raw = root.ToJsonString();
        var package = DelphiLiveSnapshotCodec.PackJson(raw);
        string originalHash = OriginalFingerprint(raw);
        package.FullHash.ShouldBe(originalHash);
        // Use an independently computed old-format hash so a changed writer cannot
        // silently pass by computing the same wrong hash in both Pack and Unpack.
        var compact = JsonNode.Parse(package.SnapshotJson)!;
        compact["$storage"]!["fullHash"] = originalHash;
        DelphiLiveSnapshotCodec.Validate(compact.ToJsonString(), package.Items, package.Contents);
        OriginalFingerprint(DelphiLiveSnapshotCodec.RestoreJson(compact.ToJsonString(), package.Items, package.Contents))
            .ShouldBe(originalHash);
        // Raw reconstruction preserves unknown properties; typed reads still reject them.
        Should.Throw<JsonException>(() => DelphiLiveSnapshotCodec.Unpack(compact.ToJsonString(), package.Items, package.Contents));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptReportOrRootFingerprintFailsBeforeReturningAnyDomainSnapshot(bool alterRoot)
    {
        var package = DelphiLiveSnapshotCodec.Pack(State(2, "original report"));
        var contents = package.Contents.ToDictionary(x => x.Key, x => x.Value);
        var root = JsonNode.Parse(package.SnapshotJson)!;
        if (alterRoot) root["cash"] = 999m;
        else
        {
            string reportHash = contents.Single(x => x.Value == "\"original report\"").Key;
            contents[reportHash] = "\"changed report\"";
        }
        Should.Throw<InvalidOperationException>(() => DelphiLiveSnapshotCodec.Unpack(root.ToJsonString(), package.Items, contents));
        Should.Throw<InvalidOperationException>(() => DelphiLiveSnapshotCodec.Validate(root.ToJsonString(), package.Items, contents));
    }

    [Fact]
    public void RepeatedLargeReportsDoNotAllocateFullHistoryBuffersOrPerQuoteStrings()
    {
        var package = DelphiLiveSnapshotCodec.Pack(State(1000, new string('x', 16_384)));
        _ = DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents);
        _ = ExpandedRead(package);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var expanded = ExpandedRead(package);
        long expandedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var direct = DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents);
        long directBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"1000 quotes / 16 KiB report: expanded read {expandedBytes:N0} bytes; direct read {directBytes:N0} bytes.");
        direct.Quotes.Length.ShouldBe(expanded.Quotes.Length);
        Assert.All(direct.Quotes, quote => Assert.Same(direct.Quotes[0].DossierJson, quote.DossierJson));
        Assert.True(directBytes < expandedBytes / 4,
            $"Direct read allocated {directBytes:N0} bytes versus {expandedBytes:N0} expanded bytes.");
        GC.KeepAlive(expanded);
        GC.KeepAlive(direct);
    }

    [Fact]
    public void RepeatedReportValidationRetainsIndependentFullFingerprint()
    {
        string report = string.Concat(Enumerable.Repeat("é <>& \\\"quoted\\\"\n😀", 2048));
        var state = State(1000, report);
        var package = DelphiLiveSnapshotCodec.Pack(state);
        DelphiLiveSnapshotCodec.Validate(package.SnapshotJson, package.Items, package.Contents);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3; i++)
            DelphiLiveSnapshotCodec.Validate(package.SnapshotJson, package.Items, package.Contents);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Three repeated-report validations: {watch.ElapsedMilliseconds} ms; {allocated:N0} allocated bytes.");
        package.FullHash.ShouldBe(OriginalFingerprint(DelphiLiveLedgerJson.Serialize(state)));
    }

    [Fact]
    public void VerifiedReadCacheRequiresAllFreshBytesAndCopiesMembership()
    {
        var package = DelphiLiveSnapshotCodec.Pack(State(8, "original report"));
        var items = package.Items.ToList();
        var contents = package.Contents.ToDictionary(p => p.Key, p => p.Value);
        var cache = new DelphiLiveSnapshotReadCache();
        var first = cache.Read(package.SnapshotJson, items, contents);
        Assert.Same(first, cache.Read(package.SnapshotJson, items, contents));
        string key = items[0].ContentHash;
        contents[key] += " "; // Semantically identical JSON still violates its stored content hash.
        Should.Throw<InvalidOperationException>(() => cache.Read(package.SnapshotJson, items, contents));
        contents[key] = package.Contents[key];
        contents.Remove(key);
        Should.Throw<InvalidOperationException>(() => cache.Read(package.SnapshotJson, items, contents));
        contents[key] = package.Contents[key];
        items.RemoveAt(0);
        Should.Throw<InvalidOperationException>(() => cache.Read(package.SnapshotJson, items, contents));
        var changed = JsonNode.Parse(package.SnapshotJson)!;
        changed["cash"] = 1;
        Should.Throw<InvalidOperationException>(() => cache.Read(changed.ToJsonString(), package.Items, contents));
        Assert.Same(first, cache.Read(package.SnapshotJson, package.Items, package.Contents));
    }

    [Fact]
    public void VerifiedReadCacheIsBoundedAndSeparatesAccountsAndRevisions()
    {
        var a = DelphiLiveSnapshotCodec.Pack(State(2, "a"));
        var b = DelphiLiveSnapshotCodec.Pack(State(2, "b"));
        var cache = new DelphiLiveSnapshotReadCache(capacity: 1);
        var first = cache.Read(a.SnapshotJson, a.Items, a.Contents);
        var second = cache.Read(b.SnapshotJson, b.Items, b.Contents);
        first.PortfolioId.ShouldNotBe(second.PortfolioId);
        Assert.NotSame(first, cache.Read(a.SnapshotJson, a.Items, a.Contents));
        var disabled = new DelphiLiveSnapshotReadCache(maximumBytes: 1);
        Assert.NotSame(disabled.Read(a.SnapshotJson, a.Items, a.Contents),
            disabled.Read(a.SnapshotJson, a.Items, a.Contents));
        var changed = DelphiLiveSnapshotCodec.Pack(first with { Revision = first.Revision + 1 });
        cache.Read(changed.SnapshotJson, changed.Items, changed.Contents).Revision.ShouldBe(first.Revision + 1);
    }

    [Fact]
    public void VerifiedReadCacheAvoidsRepeatedExpandedHistoryWork()
    {
        var package = DelphiLiveSnapshotCodec.Pack(State(1000, new string('x', 8192)));
        var cache = new DelphiLiveSnapshotReadCache();
        var verified = cache.Read(package.SnapshotJson, package.Items, package.Contents);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) Assert.Same(verified, cache.Read(package.SnapshotJson, package.Items, package.Contents));
        output.WriteLine($"100 exact fresh-payload cache reads: {watch.ElapsedMilliseconds} ms.");
        DelphiLiveLedgerJson.Serialize(verified).ShouldBe(DelphiLiveLedgerJson.Serialize(StateCopy()));
        DelphiLivePortfolioSnapshot StateCopy() => DelphiLiveSnapshotCodec.Unpack(package.SnapshotJson, package.Items, package.Contents);
    }

    [Fact]
    public async System.Threading.Tasks.Task VerifiedReadCacheHandlesConcurrentMissesAndRejectsChangedOrdinals()
    {
        var package = DelphiLiveSnapshotCodec.Pack(State(8, "shared report"));
        var cache = new DelphiLiveSnapshotReadCache(capacity: 2);
        var results = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            System.Threading.Tasks.Task.Run(() => cache.Read(package.SnapshotJson, package.Items, package.Contents))));
        foreach (var result in results) result.PortfolioId.ShouldBe(results[0].PortfolioId);
        var changedItems = package.Items.ToList();
        changedItems[0] = changedItems[0] with { Ordinal = 999 };
        Should.Throw<InvalidOperationException>(() => cache.Read(package.SnapshotJson, changedItems, package.Contents));
    }

    [Fact]
    public void VerifiedReadCacheAdmitsLargeHistoryWithinBoundedAllowance()
    {
        // The actual current source contains >15,000 distinct small immutable item contents.
        // A small-fixture-only benchmark must not hide a budget that bypasses production loads.
        var package = DelphiLiveSnapshotCodec.Pack(State(16000, new string('x', 1024)));
        var cache = new DelphiLiveSnapshotReadCache();
        var first = cache.Read(package.SnapshotJson, package.Items, package.Contents);
        Assert.Same(first, cache.Read(package.SnapshotJson, package.Items, package.Contents));
        var next = DelphiLiveSnapshotCodec.Pack(first with { Revision = first.Revision + 1 });
        var updated = cache.Read(next.SnapshotJson, next.Items, next.Contents);
        Assert.Same(updated, cache.Read(next.SnapshotJson, next.Items, next.Contents));
        Assert.NotSame(first, cache.Read(package.SnapshotJson, package.Items, package.Contents));
    }

    private static DelphiLivePortfolioSnapshot ExpandedRead(DelphiLiveSnapshotPackage package) =>
        DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(
            DelphiLiveSnapshotCodec.RestoreJson(package.SnapshotJson, package.Items, package.Contents));

    private static DelphiLivePortfolioSnapshot State(int quoteCount, string report)
    {
        var open = new DateTime(2026, 9, 17, 13, 30, 0, DateTimeKind.Utc);
        var state = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "OperationalChampion", null, 1000m, "CAD", DateOnly.FromDateTime(open), open, open.AddDays(-1), "Fixture", "Test"));
        var id = Guid.NewGuid();
        var action = Guid.NewGuid();
        var position = new DelphiLiveLedgerPosition(id, "ABC", 2, 10.50m, open, action,
            report, DelphiLiveProfitProtectionState.Open(id, 10.50m));
        return state with
        {
            Revision = 7, CurrentSession = DateOnly.FromDateTime(open), DailySourceRevisionId = Guid.NewGuid(),
            Positions = [position], SessionOpeningPositions = [position],
            Actions = [new(new(action, Guid.NewGuid(), Guid.NewGuid(), "ABC", DelphiLiveActionSide.Buy,
                open, open, open.AddHours(1), 2, 21m), id, null, DateOnly.FromDateTime(open), Guid.NewGuid(),
                "Fixture", report, "Filled", 1, Guid.NewGuid(), open, null, ["Preserve"])],
            Fills = [new(Guid.NewGuid(), action, id, Guid.NewGuid(), "ABC", DelphiLiveActionSide.Buy, 2, 10.50m,
                DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.SideSpecific, open, DateOnly.FromDateTime(open))],
            Quotes = Enumerable.Range(0, quoteCount).Select(i => new DelphiLiveLedgerQuote(Guid.NewGuid(), action, id,
                "Protection", Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), "ABC", i + 1, open, open.AddSeconds(1),
                    10.50m, 10.49m, 10.51m, "Fixture"), "Observed", report)).ToImmutableArray(),
            Marks = [new(Guid.NewGuid(), DateOnly.FromDateTime(open), DelphiLivePortfolioMarkKind.Closing, open.AddHours(6.5),
                true, 1000m, [new(id, "ABC", 2, 10.50m, open)], "Fixture")]
        };
    }

    private static string OriginalFingerprint(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, document.RootElement);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));

        static void Write(Utf8JsonWriter writer, JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
                writer.WriteEndObject();
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (var child in element.EnumerateArray()) Write(writer, child);
                writer.WriteEndArray();
            }
            else element.WriteTo(writer);
        }
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
