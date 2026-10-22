#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core.Trader.Promotion;

/// <summary>Audit-only proof of the original hash across process-dependent immutable dictionary ordering.</summary>
internal sealed class BrokerHistoricalStateHash
{
    private readonly BrokerManagedSession original;
    private readonly HashSet<string> representations;
    private readonly string originalJson;
    private BrokerHistoricalStateHash(BrokerManagedSession original, string originalJson, string orderedJson)
    { this.original = original; this.originalJson = originalJson; representations = [Hash(originalJson), Hash(orderedJson)]; }

    internal static bool Required(BrokerManagedSession prior, BrokerManagedSession next) =>
        prior.Continuation?.Fingerprint != next.Continuation?.Fingerprint || prior.October22?.Fingerprint != next.October22?.Fingerprint ||
        BrokerAccountImport.Hash(prior.SourceSwitch) != BrokerAccountImport.Hash(next.SourceSwitch) ||
        BrokerAccountImport.Hash(prior.ImplementationReview) != BrokerAccountImport.Hash(next.ImplementationReview);

    internal static BrokerHistoricalStateHash Create(BrokerManagedSession prior, string priorJson,
        BrokerManagedSession next, string nextJson)
    {
        using var before = JsonDocument.Parse(priorJson);
        using var after = JsonDocument.Parse(nextJson);
        if (prior.BindingId != next.BindingId || next.PreviousRevisionId != prior.RevisionId || next.Revision != prior.Revision + 1 ||
            !JsonElement.DeepEquals(before.RootElement, JsonSerializer.SerializeToElement(prior)) ||
            !JsonElement.DeepEquals(after.RootElement, JsonSerializer.SerializeToElement(next)))
            throw new InvalidDataException("Historical hash evidence differs from its exact adjacent broker records.");
        return new(prior, priorJson, RestoreMemoryRepresentation(priorJson, nextJson));
    }

    internal static bool Matches(BrokerManagedSession prior, string expected, BrokerHistoricalStateHash? evidence) =>
        BrokerAccountImport.Hash(prior) == expected || evidence is not null && ReferenceEquals(prior, evidence.original) &&
        evidence.representations.Contains(expected);

    // A source switch clears signal dictionaries. A bounded later immutable broker record can
    // retain their old property order. It supplies names/order only, never financial/signal values.
    internal void AddOrderWitness(string witnessJson) => representations.Add(Hash(RestoreDictionaryOrder(originalJson, witnessJson)));

    internal static string RestoreDictionaryOrder(string priorJson, string witnessJson)
    {
        using var before = JsonDocument.Parse(priorJson);
        using var witness = JsonDocument.Parse(witnessJson);
        var memory = before.RootElement.GetProperty("Memory");
        var orders = new List<string[]>();
        Gather(witness.RootElement.GetProperty("Memory"));
        string represented = Reorder(memory);
        using var check = JsonDocument.Parse(represented);
        if (!JsonElement.DeepEquals(memory, check.RootElement)) throw new InvalidDataException("Historical dictionary values changed during representation recovery.");
        string raw = memory.GetRawText();
        int start = priorJson.IndexOf(raw, StringComparison.Ordinal);
        if (start < 0 || start != priorJson.LastIndexOf(raw, StringComparison.Ordinal)) return priorJson;
        return priorJson[..start] + represented + priorJson[(start + raw.Length)..];

        void Gather(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                var names = node.EnumerateObject().Select(p => p.Name).ToArray();
                if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) throw new InvalidDataException("Ambiguous historical property order.");
                orders.Add(names);
                foreach (var p in node.EnumerateObject()) Gather(p.Value);
            }
            else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Gather(item);
        }
        string Reorder(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                var keys = node.EnumerateObject().Select(p => p.Name).ToArray();
                var names = orders.Where(order => keys.All(k => order.Contains(k, StringComparer.Ordinal)))
                    .OrderBy(order => order.Length).FirstOrDefault()?.Where(k => keys.Contains(k, StringComparer.Ordinal)) ?? keys;
                return "{" + string.Join(",", names.Select(k => JsonSerializer.Serialize(k) + ":" + Reorder(node.GetProperty(k)))) + "}";
            }
            if (node.ValueKind == JsonValueKind.Array) return "[" + string.Join(",", node.EnumerateArray().Select(Reorder)) + "]";
            return node.GetRawText();
        }
    }

    internal static string RestoreMemoryRepresentation(string priorJson, string nextJson)
    {
        using var before = JsonDocument.Parse(priorJson);
        using var after = JsonDocument.Parse(nextJson);
        var memory = before.RootElement.GetProperty("Memory");
        var following = after.RootElement.GetProperty("Memory");
        string represented = Represent(memory, following), memoryJson = memory.GetRawText();
        using var check = JsonDocument.Parse(represented);
        if (!JsonElement.DeepEquals(memory, check.RootElement)) throw new InvalidDataException("Historical memory values changed during representation recovery.");
        int start = priorJson.IndexOf(memoryJson, StringComparison.Ordinal);
        if (start < 0 || start != priorJson.LastIndexOf(memoryJson, StringComparison.Ordinal)) return priorJson;
        return priorJson[..start] + represented + priorJson[(start + memoryJson.Length)..];
    }
    private static string Represent(JsonElement original, JsonElement following)
    {
        // The adjacent immutable receipt supplies an original serialization, not new values. This
        // handles dictionary ordering and JSON escaping/decimal scale without guessing hash inputs.
        if (JsonElement.DeepEquals(original, following)) return following.GetRawText();
        if (original.ValueKind == JsonValueKind.Object && following.ValueKind == JsonValueKind.Object)
        {
            var names = following.EnumerateObject().Select(p => p.Name).ToArray();
            var oldNames = original.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() == names.Length && oldNames.Distinct(StringComparer.Ordinal).Count() == oldNames.Length &&
                names.Order(StringComparer.Ordinal).SequenceEqual(oldNames.Order(StringComparer.Ordinal)))
                return "{" + string.Join(",", names.Select(name => JsonSerializer.Serialize(name) + ":" + Represent(original.GetProperty(name), following.GetProperty(name)))) + "}";
        }
        if (original.ValueKind == JsonValueKind.Array && following.ValueKind == JsonValueKind.Array && original.GetArrayLength() == following.GetArrayLength())
            return "[" + string.Join(",", Enumerable.Range(0, original.GetArrayLength()).Select(n => Represent(original[n], following[n]))) + "]";
        return original.GetRawText();
    }
    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
