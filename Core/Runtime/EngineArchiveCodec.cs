#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Core.Runtime;

public sealed record EngineArchivePackage(string Manifest, IReadOnlyDictionary<string, string> Contents);

/// <summary>Lossless content-addressed records; repeated nested state is stored once, never joined to latest rows.</summary>
public static class EngineArchiveCodec
{
    public const string Marker = "$engineArchive";
    private const string Reference = "$engineRef";
    public const int MaximumExpandedBytes = 8 * 1024 * 1024;
    public static bool IsPacked(string json) => JsonNode.Parse(json) is JsonObject root && root.ContainsKey(Marker);

    public static EngineArchivePackage Pack(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumExpandedBytes)
            throw new InvalidDataException("Engine state exceeds its supported expanded size.");
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Engine state must be an object.");
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        JsonNode? Compact(JsonNode? node, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Engine state nesting exceeds its limit.");
            if (node is JsonObject o)
            {
                if (o.ContainsKey(Marker) || o.ContainsKey(Reference)) throw new InvalidDataException("Engine archive markers are reserved.");
                foreach (var key in o.Select(p => p.Key).ToArray()) o[key] = Compact(o[key]?.DeepClone(), depth + 1);
            }
            else if (node is JsonArray a)
                for (int i = 0; i < a.Count; i++) a[i] = Compact(a[i]?.DeepClone(), depth + 1);
            if (node is null || depth == 0) return node;
            string value = node.ToJsonString();
            if (value.Length < 256) return node;
            string hash = DelphiLiveSnapshotCodec.Hash(value);
            if (contents.TryGetValue(hash, out var existing) && existing != value)
                throw new InvalidDataException("Engine archive fingerprint conflict.");
            contents[hash] = value;
            return new JsonObject { [Reference] = hash };
        }
        root = (JsonObject)Compact(root, 0)!;
        root[Marker] = new JsonObject { ["version"] = 1, ["fullHash"] = DelphiLiveSnapshotCodec.Fingerprint(json) };
        return new(root.ToJsonString(), contents);
    }

    public static IReadOnlyList<string> References(string json)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        void Scan(JsonNode? node)
        {
            if (node is JsonObject o)
            {
                if (o.ContainsKey(Reference)) { found.Add(ReadReference(o)); return; }
                foreach (var pair in o) Scan(pair.Value);
            }
            else if (node is JsonArray a) foreach (var value in a) Scan(value);
        }
        Scan(JsonNode.Parse(json)); return found.ToArray();
    }

    public static string Restore(string json, IReadOnlyDictionary<string, string> contents)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Engine archive root is not an object.");
        if (!root.TryGetPropertyValue(Marker, out var marker)) return json;
        if (marker is not JsonObject m || m.Count != 2 || m["version"]?.GetValue<int>() != 1 ||
            m["fullHash"] is not JsonValue hash || !hash.TryGetValue<string>(out var fullHash) || !ValidHash(fullHash))
            throw new InvalidDataException("Unsupported engine archive representation.");
        root.Remove(Marker);
        long expandedBytes = 0;
        JsonNode? Expand(JsonNode? node, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Engine archive is cyclic or exceeds its nesting limit.");
            if (node is JsonObject o)
            {
                if (o.ContainsKey(Reference))
                {
                    string key = ReadReference(o);
                    if (!contents.TryGetValue(key, out var value) || DelphiLiveSnapshotCodec.Hash(value) != key)
                        throw new InvalidDataException("Engine archive record is missing or its checksum failed.");
                    // Count each expansion, including repeated references, before constructing the expanded state.
                    expandedBytes += System.Text.Encoding.UTF8.GetByteCount(value);
                    if (expandedBytes > MaximumExpandedBytes * 2L) throw new InvalidDataException("Engine archive expansion exceeds its limit.");
                    return Expand(JsonNode.Parse(value), depth + 1);
                }
                if (o.ContainsKey(Marker)) throw new InvalidDataException("Nested engine archive marker.");
                foreach (var key in o.Select(p => p.Key).ToArray()) o[key] = Expand(o[key]?.DeepClone(), depth + 1);
            }
            else if (node is JsonArray a)
                for (int i = 0; i < a.Count; i++) a[i] = Expand(a[i]?.DeepClone(), depth + 1);
            return node;
        }
        string restored = Expand(root, 0)!.ToJsonString();
        if (System.Text.Encoding.UTF8.GetByteCount(restored) > MaximumExpandedBytes || DelphiLiveSnapshotCodec.Fingerprint(restored) != fullHash)
            throw new InvalidDataException("Engine archive does not match the original complete state.");
        return restored;
    }

    private static string ReadReference(JsonObject node)
    {
        if (node.Count != 1 || node[Reference] is not JsonValue value || !value.TryGetValue<string>(out var hash) || !ValidHash(hash))
            throw new InvalidDataException("Invalid engine archive reference.");
        return hash;
    }
    private static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigitUpper);
}
