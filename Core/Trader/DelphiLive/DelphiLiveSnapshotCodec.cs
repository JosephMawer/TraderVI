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
        return Fingerprint(writer => WriteCanonical(writer, document.RootElement));
    }

    private static string Fingerprint(Action<Utf8JsonWriter> write)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
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
        IReadOnlyDictionary<string, string> contents)
    {
        var root = RestoreRoot(json, items, contents);
        // Legacy JSON keeps the original deserializer's optional-property handling.
        if (!IsCompact(json)) return Read<DelphiLivePortfolioSnapshot>(root);

        // The verified tree shares each decoded report. Deserialize small records without
        // their reports, then attach those same immutable strings; never expand all reports
        // into one giant JSON string or allocate a fresh report for every historical quote.
        var arrays = Collections.ToDictionary(name => name, name => root[name]!.AsArray());
        foreach (string name in Collections) root[name] = new JsonArray();
        return Read<DelphiLivePortfolioSnapshot>(root) with
        {
            SessionOpeningPositions = ReadArray(arrays["sessionOpeningPositions"], ReadPosition),
            Positions = ReadArray(arrays["positions"], ReadPosition),
            Actions = ReadArray(arrays["actions"], node => ReadWithReport<DelphiLiveLedgerAction>(node,
                "dossierJson", (item, report) => item with { DossierJson = report })),
            Fills = ReadArray(arrays["fills"], Read<DelphiLiveLedgerFill>),
            Quotes = ReadArray(arrays["quotes"], node => ReadWithReport<DelphiLiveLedgerQuote>(node,
                "dossierJson", (item, report) => item with { DossierJson = report })),
            Marks = ReadArray(arrays["marks"], Read<DelphiLiveLedgerMark>)
        };
    }

    private static T Read<T>(JsonNode node) => node.Deserialize<T>(DelphiLiveLedgerJson.Options)
        ?? throw new InvalidOperationException("Stored Delphi Live ledger JSON is empty.");

    private static ImmutableArray<T> ReadArray<T>(JsonArray array, Func<JsonNode, T> read) =>
        array.Select(node => read(node!)).ToImmutableArray();

    private static DelphiLiveLedgerPosition ReadPosition(JsonNode node) =>
        ReadWithReport<DelphiLiveLedgerPosition>(node, "originalEntryDossierJson",
            (item, report) => item with { OriginalEntryDossierJson = report });

    private static T ReadWithReport<T>(JsonNode node, string field, Func<T, string, T> attach)
    {
        var item = node.AsObject();
        string? report = item[field]?.GetValue<string>();
        if (item.ContainsKey(field)) item[field] = null;
        return attach(Read<T>(item), report!);
    }

    public static void Validate(string json, IReadOnlyList<DelphiLiveSnapshotItem> items,
        IReadOnlyDictionary<string, string> contents) => _ = RestoreRoot(json, items, contents);

    public static string RestoreJson(string json, IReadOnlyList<DelphiLiveSnapshotItem> items,
        IReadOnlyDictionary<string, string> contents)
    {
        var root = RestoreRoot(json, items, contents);
        return IsCompact(json) ? root.ToJsonString(DelphiLiveLedgerJson.Options) : json;
    }

    private static JsonObject RestoreRoot(string json, IReadOnlyList<DelphiLiveSnapshotItem> items,
        IReadOnlyDictionary<string, string> contents)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        if (!root.ContainsKey(Marker))
        {
            if (items.Count != 0) throw new InvalidOperationException("Legacy snapshots cannot reference compact items.");
            return root;
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
        if (!string.Equals(Fingerprint(writer => WriteCanonical(writer, root)), expectedHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Reconstructed portfolio snapshot fingerprint does not match.");
        return root;
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
        // Utf8JsonWriter buffers until flushed, even when its destination is a stream.
        // Bound the hash buffer to roughly one scalar value instead of the full history.
        if (writer.BytesPending >= 16 * 1024) writer.Flush();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            writer.WriteStartObject();
            foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Key); WriteCanonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (node is JsonArray array)
        {
            writer.WriteStartArray();
            foreach (var child in array) WriteCanonical(writer, child);
            writer.WriteEndArray();
        }
        else if (node is null) writer.WriteNullValue();
        else node.WriteTo(writer, DelphiLiveLedgerJson.Options);
        if (writer.BytesPending >= 16 * 1024) writer.Flush();
    }
}
