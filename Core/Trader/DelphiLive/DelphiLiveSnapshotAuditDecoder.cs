#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core.Trader.DelphiLive;

public static partial class DelphiLiveSnapshotCodec
{
    /// <summary>One fresh immutable archive pass. Never shared with operational reads or another audit.</summary>
    internal sealed class AuditDecoder(long maximumBytes = 384L * 1024 * 1024)
    {
        private sealed record Content(string Json, JsonNode Node);
        private sealed record Item(JsonObject Expanded, object Value);
        private readonly Dictionary<string, Content> contents = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Collection, string Hash), Item> decoded = new();
        private readonly Dictionary<string, string> reports = new(StringComparer.Ordinal);
        private readonly CanonicalStrings strings = new(32 * 1024 * 1024);
        private long retained;
        internal int ContentCount => contents.Count;
        internal int DecodedCount => decoded.Count;
        internal bool Contains(string hash) => contents.ContainsKey(hash);

        internal void Add(string hash, string json)
        {
            if (contents.TryGetValue(hash, out var prior))
            {
                if (prior.Json != json) throw new InvalidDataException("Immutable audit content changed within a pass.");
                return;
            }
            if (Hash(json) != hash) throw new InvalidDataException("Audit content checksum failed.");
            // Includes parsed/typed records, strings and dictionary overhead. Fail closed instead of
            // silently evicting evidence and repeatedly reopening the same history or exhausting memory.
            long allowance = checked(12L * json.Length + 4096);
            if (retained + allowance > maximumBytes) throw new InvalidDataException("Paper audit memory bound exceeded; split the audit reader before resuming.");
            var node = JsonNode.Parse(json) ?? throw new InvalidDataException("Empty audit content.");
            contents.Add(hash, new(json, node)); retained += allowance;
        }

        internal IEnumerable<string> ReportReferences(string hash)
        {
            var item = Require(hash).AsObject();
            foreach (string field in DossierFields)
            {
                if (item[field] is null) continue;
                if (item[field] is not JsonObject link || link.Count != 1 || link["contentHash"] is not JsonValue value ||
                    !value.TryGetValue<string>(out var reference)) throw new InvalidDataException("Invalid audit report reference.");
                yield return reference;
            }
        }

        internal DelphiLivePortfolioSnapshot Read(string json, IReadOnlyList<DelphiLiveSnapshotItem> items)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            if (!root.ContainsKey(Marker))
            {
                if (items.Count != 0) throw new InvalidDataException("Legacy snapshot has compact membership.");
                return Read<DelphiLivePortfolioSnapshot>(root);
            }
            var metadata = root[Marker]!.AsObject();
            int version = metadata["version"]?.GetValue<int>() ?? 0;
            if ((version != 2 || metadata.Count != 3) && (version != 3 || metadata.Count != 4))
                throw new InvalidDataException("Unsupported audit snapshot manifest.");
            var counts = ReadCounts(json);
            var roots = version == 3 ? ReadRoots(json) : null;
            var selected = new Dictionary<string, Item[]>();
            foreach (string name in Collections)
            {
                if (root[name] is not JsonArray empty || empty.Count != 0) throw new InvalidDataException("Invalid compact collection.");
                var references = items.Where(i => i.Collection == name).OrderBy(i => i.Ordinal).ToArray();
                if (references.Length != counts[name] || references.Where((r, n) => r.Ordinal != n).Any())
                    throw new InvalidDataException("Audit membership is missing, duplicated or out of order.");
                if (roots is not null && DelphiLiveArchiveTree.BuildCollection(name, references.Select(r => r.ContentHash).ToArray()).Root != roots[name])
                    throw new InvalidDataException("Audit collection differs from its original archive root.");
                selected.Add(name, references.Select(r => Decode(name, r.ContentHash)).ToArray());
            }
            if (items.Count != counts.Values.Sum()) throw new InvalidDataException("Unexpected audit membership.");
            string expected = metadata["fullHash"]!.GetValue<string>();
            root.Remove(Marker);
            if (version == 2 && Fingerprint(writer =>
            {
                writer.WriteStartObject();
                foreach (var field in root.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(field.Key);
                    if (selected.TryGetValue(field.Key, out var collection))
                    {
                        writer.WriteStartArray();
                        foreach (var item in collection) WriteCanonical(writer, item.Expanded, strings);
                        writer.WriteEndArray();
                    }
                    else WriteCanonical(writer, field.Value, strings);
                }
                writer.WriteEndObject();
            }) != expected) throw new InvalidDataException("Audit snapshot fingerprint differs from the original.");
            if (version == 3 && CandidateStateReference(json) is string candidateHash)
                root["candidateStates"] = Require(candidateHash).DeepClone();
            return Read<DelphiLivePortfolioSnapshot>(root) with
            {
                SessionOpeningPositions = Values<DelphiLiveLedgerPosition>("sessionOpeningPositions"),
                Positions = Values<DelphiLiveLedgerPosition>("positions"), Actions = Values<DelphiLiveLedgerAction>("actions"),
                Fills = Values<DelphiLiveLedgerFill>("fills"), Quotes = Values<DelphiLiveLedgerQuote>("quotes"), Marks = Values<DelphiLiveLedgerMark>("marks")
            };
            ImmutableArray<T> Values<T>(string name) => selected[name].Select(i => (T)i.Value).ToImmutableArray();
        }

        private JsonNode Require(string hash) => contents.TryGetValue(hash, out var content) ? content.Node
            : throw new InvalidDataException("Audit content is missing.");

        private Item Decode(string collection, string hash)
        {
            if (decoded.TryGetValue((collection, hash), out var item)) return item;
            var expanded = Require(hash).DeepClone().AsObject();
            var small = expanded.DeepClone().AsObject();
            foreach (string field in DossierFields)
            {
                if (expanded[field] is null) continue;
                // Validate the exact reference before reusing its immutable report string.
                _ = ReportReferences(hash).ToArray();
                var link = expanded[field]!.AsObject()["contentHash"]!.GetValue<string>();
                if (!reports.TryGetValue(link, out var report))
                {
                    if (Require(link) is not JsonValue reportValue || !reportValue.TryGetValue<string>(out report))
                        throw new InvalidDataException("Audit report is not text.");
                    reports.Add(link, report);
                }
                expanded[field] = JsonValue.Create(report); small[field] = null;
            }
            string? Report(string name) => expanded[name]?.GetValue<string>();
            object value = collection switch
            {
                "positions" or "sessionOpeningPositions" => Read<DelphiLiveLedgerPosition>(small) with { OriginalEntryDossierJson = Report("originalEntryDossierJson")! },
                "actions" => Read<DelphiLiveLedgerAction>(small) with { DossierJson = Report("dossierJson")! },
                "quotes" => Read<DelphiLiveLedgerQuote>(small) with { DossierJson = Report("dossierJson")! },
                "fills" => Read<DelphiLiveLedgerFill>(small), "marks" => Read<DelphiLiveLedgerMark>(small),
                _ => throw new InvalidDataException("Unknown audit collection.")
            };
            item = new(expanded, value); decoded.Add((collection, hash), item); return item;
        }
    }
}
