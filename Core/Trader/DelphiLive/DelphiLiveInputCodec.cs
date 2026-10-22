#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveInputPackage(string InputJson, IReadOnlyDictionary<string, string> Contents);

/// <summary>Exact immutable input fragments shared across checkpoints and consumers. No latest-row joins.</summary>
public static class DelphiLiveInputCodec
{
    public const string Marker = "$evidence";
    private static readonly string[] SharedFields = ["policy", "volatilityRulers", "dailySetup"];

    public static DelphiLiveInputPackage Pack(DelphiLiveEvaluationInput input)
    {
        string full = DelphiLiveLedgerJson.Serialize(input);
        var root = JsonNode.Parse(full)!.AsObject();
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        JsonObject Reference(JsonNode value)
        {
            string json = value.ToJsonString(DelphiLiveLedgerJson.Options);
            string hash = DelphiLiveSnapshotCodec.Hash(json);
            contents.TryAdd(hash, json);
            return new() { ["contentHash"] = hash };
        }
        foreach (string field in SharedFields)
            if (root[field] is { } value) root[field] = Reference(value);
        foreach (string series in new[] { "stock", "xiu" })
        {
            var bars = root[series]!["bars"]!.AsArray();
            for (int i = 0; i < bars.Count; i++) bars[i] = Reference(bars[i]!);
        }
        root[Marker] = new JsonObject { ["version"] = 1, ["fullHash"] = DelphiLiveSnapshotCodec.Fingerprint(full) };
        return new(root.ToJsonString(DelphiLiveLedgerJson.Options), contents);
    }

    public static IReadOnlyList<string> References(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        if (!root.ContainsKey(Marker)) return [];
        ValidateMarker(root);
        return Nodes(root).Select(ReadHash).Distinct(StringComparer.Ordinal).ToArray();
    }

    public static DelphiLiveEvaluationInput Read(string json, IReadOnlyDictionary<string, string> contents)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        if (!root.ContainsKey(Marker)) return DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationInput>(json);
        ValidateMarker(root);
        string expected = root[Marker]!["fullHash"]!.GetValue<string>();
        JsonNode Restore(JsonNode reference)
        {
            string hash = ReadHash(reference);
            if (!contents.TryGetValue(hash, out var value) || DelphiLiveSnapshotCodec.Hash(value) != hash)
                throw new InvalidDataException("Shared input evidence is missing or its checksum failed.");
            return JsonNode.Parse(value) ?? throw new InvalidDataException("Shared input evidence is empty.");
        }
        foreach (string field in SharedFields)
            if (root[field] is { } value) root[field] = Restore(value);
        foreach (string series in new[] { "stock", "xiu" })
        {
            var bars = root[series]!["bars"]!.AsArray();
            for (int i = 0; i < bars.Count; i++) bars[i] = Restore(bars[i]!);
        }
        root.Remove(Marker);
        string restored = root.ToJsonString(DelphiLiveLedgerJson.Options);
        if (DelphiLiveSnapshotCodec.Fingerprint(restored) != expected)
            throw new InvalidDataException("Shared input manifest does not match its original evidence.");
        return DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationInput>(restored);
    }

    private static IEnumerable<JsonNode> Nodes(JsonObject root)
    {
        foreach (string field in SharedFields) if (root[field] is { } node) yield return node;
        foreach (string series in new[] { "stock", "xiu" })
            foreach (var node in root[series]!["bars"]!.AsArray()) yield return node!;
    }
    private static string ReadHash(JsonNode node)
    {
        if (node is not JsonObject value || value.Count != 1 || value["contentHash"] is not JsonValue hashValue ||
            !hashValue.TryGetValue<string>(out var hash) || hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigitUpper(c)))
            throw new InvalidDataException("Invalid shared input reference.");
        return hash;
    }
    private static void ValidateMarker(JsonObject root)
    {
        if (root[Marker] is not JsonObject marker || marker.Count != 2 || marker["version"]?.GetValue<int>() != 1 ||
            marker["fullHash"] is not JsonValue hash || !hash.TryGetValue<string>(out var value) || value.Length != 64)
            throw new InvalidDataException("Unsupported shared input representation.");
    }
}
