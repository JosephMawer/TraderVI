#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveSnapshotItem(string Collection, int Ordinal, string ContentHash);
public sealed record DelphiLiveSnapshotPackage(string SnapshotJson, string FullHash,
    IReadOnlyDictionary<string, string> Contents, IReadOnlyList<DelphiLiveSnapshotItem> Items);

/// <summary>Lossless storage representation. Domain snapshots and trading rules are unchanged.</summary>
public static class DelphiLiveSnapshotCodec
{
    public const string Marker = "$storage";
    public static ImmutableArray<string> Collections { get; } =
        ["sessionOpeningPositions", "positions", "actions", "fills", "quotes", "marks"];
    internal static ImmutableArray<string> DossierFields { get; } = ["dossierJson", "originalEntryDossierJson"];

    public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    public static string Fingerprint(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement);
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
    public static bool IsCompact(string json) => JsonNode.Parse(json)?.AsObject().ContainsKey(Marker) == true;

    public static DelphiLiveSnapshotPackage Pack(DelphiLivePortfolioSnapshot snapshot) => PackJson(DelphiLiveLedgerJson.Serialize(snapshot));

    public static DelphiLiveSnapshotPackage PackJson(string full)
    {
        var root = JsonNode.Parse(full)!.AsObject();
        if (root.ContainsKey(Marker)) throw new InvalidOperationException("Cannot compact an already compact snapshot.");
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        var reportHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = new List<DelphiLiveSnapshotItem>();
        var counts = new JsonObject();
        foreach (string name in Collections)
        {
            var array = root[name]!.AsArray();
            counts[name] = array.Count;
            for (int ordinal = 0; ordinal < array.Count; ordinal++)
            {
                var item = array[ordinal]!.DeepClone().AsObject();
                foreach (string field in DossierFields)
                {
                    if (item[field] is not JsonValue value || !value.TryGetValue<string>(out string? dossier)) continue;
                    // Store the exact string, including whitespace/escaping within a saved report.
                    if (!reportHashes.TryGetValue(dossier, out string? dossierHash))
                    {
                        string dossierJson = JsonSerializer.Serialize(dossier, DelphiLiveLedgerJson.Options);
                        dossierHash = Add(contents, dossierJson);
                        reportHashes.Add(dossier, dossierHash);
                    }
                    item[field] = new JsonObject { ["contentHash"] = dossierHash };
                }
                string itemHash = Add(contents, item.ToJsonString(DelphiLiveLedgerJson.Options));
                items.Add(new(name, ordinal, itemHash));
            }
            root[name] = new JsonArray();
        }
        string fullHash = Fingerprint(full);
        root[Marker] = new JsonObject { ["version"] = 2, ["fullHash"] = fullHash, ["counts"] = counts };
        return new(root.ToJsonString(DelphiLiveLedgerJson.Options), fullHash, contents, items);
    }

    public static DelphiLivePortfolioSnapshot Unpack(string json, IReadOnlyList<DelphiLiveSnapshotItem> items,
        IReadOnlyDictionary<string, string> contents) =>
        DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(RestoreJson(json, items, contents));

    public static string RestoreJson(string json, IReadOnlyList<DelphiLiveSnapshotItem> items,
        IReadOnlyDictionary<string, string> contents)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        if (!root.ContainsKey(Marker))
        {
            if (items.Count != 0) throw new InvalidOperationException("Legacy snapshots cannot reference compact items.");
            return json;
        }
        var metadata = root[Marker]!.AsObject();
        if (metadata["version"]?.GetValue<int>() != 2 || metadata.Count != 3)
            throw new InvalidOperationException("Unsupported Delphi Live snapshot storage version.");
        var counts = metadata["counts"]!.AsObject();
        if (counts.Count != Collections.Length || counts.Any(x => !Collections.Contains(x.Key)))
            throw new InvalidOperationException("Snapshot collection manifest is invalid.");
        int expected = 0;
        var reports = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in Collections)
        {
            int count = counts[name]!.GetValue<int>();
            if (count < 0 || count > 2_000_000 || root[name] is not JsonArray empty || empty.Count != 0)
                throw new InvalidOperationException("Snapshot collection count or representation is invalid.");
            expected = checked(expected + count);
            var selected = items.Where(x => x.Collection == name).OrderBy(x => x.Ordinal).ToArray();
            if (selected.Length != count || selected.Where((x, i) => x.Ordinal != i).Any())
                throw new InvalidOperationException("Snapshot items are missing, duplicated or out of order.");
            var array = new JsonArray();
            foreach (var reference in selected)
            {
                var item = JsonNode.Parse(RequireContent(contents, reference.ContentHash))!.AsObject();
                foreach (string field in DossierFields)
                {
                    if (item[field] is null) continue;
                    if (item[field] is not JsonObject link || link.Count != 1 || link["contentHash"] is null)
                        throw new InvalidOperationException("Snapshot report reference is invalid.");
                    string reportHash = link["contentHash"]!.GetValue<string>();
                    if (!reports.TryGetValue(reportHash, out string? report))
                    {
                        var saved = JsonNode.Parse(RequireContent(contents, reportHash));
                        if (saved is not JsonValue value || !value.TryGetValue<string>(out report))
                            throw new InvalidOperationException("Snapshot report content is not text.");
                        reports.Add(reportHash, report);
                    }
                    item[field] = JsonValue.Create(report);
                }
                array.Add(item);
            }
            root[name] = array;
        }
        if (expected != items.Count) throw new InvalidOperationException("Snapshot contains unexpected collection items.");
        string expectedHash = metadata["fullHash"]!.GetValue<string>();
        root.Remove(Marker);
        string restored = root.ToJsonString(DelphiLiveLedgerJson.Options);
        if (!string.Equals(Fingerprint(restored), expectedHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Reconstructed portfolio snapshot fingerprint does not match.");
        return restored;
    }

    public static IReadOnlyDictionary<string, int> ReadCounts(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var metadata = root[Marker]!.AsObject();
        if (metadata["version"]?.GetValue<int>() != 2) throw new InvalidOperationException("Unsupported snapshot storage version.");
        return Collections.ToDictionary(x => x, x => metadata["counts"]![x]!.GetValue<int>());
    }

    private static string Add(Dictionary<string, string> contents, string json)
    {
        string hash = Hash(json);
        if (contents.TryGetValue(hash, out string? prior) && prior != json)
            throw new InvalidOperationException("Snapshot content hash collision.");
        contents[hash] = json;
        return hash;
    }
    private static string RequireContent(IReadOnlyDictionary<string, string> contents, string hash)
    {
        if (!contents.TryGetValue(hash, out string? json) || Hash(json) != hash)
            throw new InvalidOperationException("Snapshot content is missing or its hash is invalid.");
        return json;
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var child in element.EnumerateArray()) WriteCanonical(writer, child); writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }
}
