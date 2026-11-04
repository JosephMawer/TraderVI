#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

/// <summary>Replays one changed revision from a verified working boundary, never its lifetime book.</summary>
internal static class DelphiLiveIncrementalVerification
{
    internal static async Task<DelphiLiveWorkingCheckpoint.Prepared> VerifyAsync(
        DelphiLiveWorkingCheckpoint prior, string priorRoot, string nextRoot,
        IReadOnlyList<DelphiLiveSnapshotItem> changes, IReadOnlyDictionary<string, string> contents,
        Func<string, CancellationToken, Task<string?>> read, CancellationToken ct,
        Func<DelphiLivePortfolioSnapshot, DelphiLivePortfolioSnapshot, Task>? validate = null)
    {
        var before = prior.Restore(priorRoot, contents);
        var node = JsonNode.Parse(nextRoot)!.AsObject();
        var counts = DelphiLiveSnapshotCodec.ReadCounts(nextRoot);
        _ = DelphiLiveSnapshotCodec.ReadRoots(nextRoot);
        if (changes.Any(i => !DelphiLiveSnapshotCodec.Collections.Contains(i.Collection) || i.Ordinal < 0) ||
            changes.Select(i => (i.Collection, i.Ordinal)).Distinct().Count() != changes.Count)
            throw new InvalidDataException("Invalid or duplicate Paper revision changes.");
        string Content(string hash)
        {
            if (!contents.TryGetValue(hash, out var value) || DelphiLiveSnapshotCodec.Hash(value) != hash)
                throw new InvalidDataException("A referenced Paper record is missing or damaged.");
            return value;
        }
        foreach (string name in DelphiLiveSnapshotCodec.Collections)
        {
            var items = prior.Items.Where(i => i.Collection == name).OrderBy(i => i.Ordinal)
                .Select(i => new DelphiLiveSnapshotItem(name, i.ArchiveOrdinal, i.ContentHash)).ToList();
            foreach (var change in changes.Where(i => i.Collection == name).OrderBy(i => i.Ordinal))
            {
                int at = items.FindIndex(i => i.Ordinal == change.Ordinal);
                if (at >= 0) items[at] = change;
                else
                {
                    if (name != "sessionOpeningPositions" && change.Ordinal < prior.Counts[name])
                        throw new InvalidDataException("An archived record outside the working boundary changed; targeted investigation is required.");
                    items.Add(change);
                }
            }
            if (name == "sessionOpeningPositions") items.RemoveAll(i => i.Ordinal >= counts[name]);
            var array = new JsonArray();
            foreach (var item in items)
            {
                var record = JsonNode.Parse(Content(item.ContentHash))!.AsObject();
                foreach (string field in DelphiLiveSnapshotCodec.DossierFields)
                    if (record[field] is JsonObject link)
                        record[field] = DelphiLiveLedgerJson.Deserialize<string>(Content(link["contentHash"]!.GetValue<string>()));
                array.Add(record);
            }
            node[name] = array;
        }
        string candidate = DelphiLiveSnapshotCodec.CandidateStateReference(nextRoot)
            ?? throw new InvalidDataException("New Paper history requires a candidate-state reference.");
        node["candidateStates"] = JsonNode.Parse(Content(candidate));
        node.Remove(DelphiLiveSnapshotCodec.Marker);
        var after = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(node.ToJsonString()) with { WorkingTotals = prior.Totals };
        if (after.PortfolioId != before.PortfolioId || after.Revision != before.Revision + 1)
            throw new InvalidDataException("Paper verification requires the next original revision.");
        if (validate != null) await validate(before, after);
        var prepared = validate == null ? await prior.AdvanceAsync(before, after, contents, read, ct)
            : await prior.AdvanceVerifiedAsync(before, after, contents, read, ct);
        if (DelphiLiveSnapshotCodec.CanonicalJson(prepared.Archive.SnapshotJson) != DelphiLiveSnapshotCodec.CanonicalJson(nextRoot))
            throw new InvalidDataException("The Paper revision differs from its verified incremental transition.");
        return prepared;
    }
}
