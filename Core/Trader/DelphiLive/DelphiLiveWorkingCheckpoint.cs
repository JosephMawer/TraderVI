#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveWorkingItem(string Collection, int Ordinal, int ArchiveOrdinal, string ContentHash);
public sealed record DelphiLiveWorkingCheckpoint(int Version, string SnapshotRoot, DelphiLiveWorkingTotals Totals,
    ImmutableArray<DelphiLiveWorkingItem> Items, ImmutableDictionary<string, int> Counts, ImmutableDictionary<string, string> Roots)
{
    public string? CandidateStateHash { get; init; }
    public sealed record Prepared(DelphiLiveWorkingCheckpoint Checkpoint, DelphiLivePortfolioSnapshot Snapshot,
        DelphiLiveSnapshotPackage Archive, ImmutableArray<DelphiLiveSnapshotItem> ChangedItems,
        ImmutableDictionary<string, string> NewContents, ImmutableArray<DelphiLiveSnapshotItem> NewIdentities);

    public static Prepared Qualify(DelphiLivePortfolioSnapshot full, DelphiLiveSnapshotPackage package)
    {
        if (full.WorkingTotals is not null) throw new InvalidOperationException("Qualification requires the complete verified archive.");
        DelphiLiveLedgerIntegrity.ValidateTransition(full, full with { Revision = checked(full.Revision + 1) });
        var nodes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var counts = DelphiLiveSnapshotCodec.ReadCounts(package.SnapshotJson).ToImmutableDictionary();
        var roots = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (string name in DelphiLiveSnapshotCodec.Collections)
        {
            var tree = DelphiLiveArchiveTree.BuildCollection(name, package.Items.Where(i => i.Collection == name).OrderBy(i => i.Ordinal).Select(i => i.ContentHash).ToArray());
            roots[name] = tree.Root;
            foreach (var pair in tree.Nodes) nodes[pair.Key] = pair.Value;
        }
        var selection = DelphiLiveWorkingState.Select(full);
        string candidateJson = DelphiLiveSnapshotCodec.CandidateStateReference(package.SnapshotJson) is string originalCandidateHash
            ? package.Contents[originalCandidateHash]
            : DelphiLiveSnapshotCodec.CanonicalJson(JsonSerializer.Serialize(full.CandidateStates, DelphiLiveLedgerJson.Options));
        string candidateHash = DelphiLiveSnapshotCodec.Hash(candidateJson);
        nodes[candidateHash] = candidateJson;
        var compact = DelphiLiveSnapshotCodec.Pack(selection.Snapshot with { CandidateStates = ImmutableDictionary<string, DelphiLivePortfolioCandidateState>.Empty });
        var items = SelectItems(package.Items, selection.RetainedIndices);
        var checkpoint = new DelphiLiveWorkingCheckpoint(2, compact.SnapshotJson, selection.Snapshot.WorkingTotals!, items,
            counts, roots.ToImmutable()) { CandidateStateHash = candidateHash };
        var proofContents = package.Contents.ToDictionary(p => p.Key, p => p.Value);
        proofContents[candidateHash] = candidateJson;
        _ = checkpoint.Restore(package.SnapshotJson, proofContents);
        return new(checkpoint, selection.Snapshot, package, [], nodes.ToImmutable(), package.Items.Where(i => i.Collection != "sessionOpeningPositions").ToImmutableArray());
    }

    public DelphiLivePortfolioSnapshot Restore(string archiveRoot, IReadOnlyDictionary<string, string> contents)
    {
        if (Version is not (1 or 2) || Counts.Count != 6 || Roots.Count != 6 || Totals is null || Totals.ArchivedEstimatedFillCount < 0 ||
            Version == 2 && (CandidateStateHash is not { Length: 64 } || !CandidateStateHash.All(char.IsAsciiHexDigitUpper)) ||
            Version == 1 && CandidateStateHash is not null)
            throw new InvalidDataException("Unsupported current paper checkpoint.");
        if (Header(SnapshotRoot, CandidateStateHash) != Header(archiveRoot)) throw new InvalidDataException("Current paper header differs from its archive revision.");
        var fullCounts = DelphiLiveSnapshotCodec.ReadCounts(archiveRoot);
        var fullRoots = DelphiLiveSnapshotCodec.IsIncremental(archiveRoot) ? DelphiLiveSnapshotCodec.ReadRoots(archiveRoot) : null;
        foreach (string name in DelphiLiveSnapshotCodec.Collections)
        {
            DelphiLiveArchiveTree.RequireRoot(Counts[name], Roots[name]);
            if (Counts[name] != fullCounts[name] || fullRoots is not null && Roots[name] != fullRoots[name])
                throw new InvalidDataException("Current paper archive commitment is stale.");
            var group = Items.Where(i => i.Collection == name).ToArray();
            if (group.Any(i => i.ArchiveOrdinal < 0 || i.ArchiveOrdinal >= Counts[name]) || group.Select(i => i.ArchiveOrdinal).Distinct().Count() != group.Length)
                throw new InvalidDataException("Current paper item ordinals are invalid.");
        }
        var snapshot = DelphiLiveSnapshotCodec.Unpack(SnapshotRoot,
            Items.Select(i => new DelphiLiveSnapshotItem(i.Collection, i.Ordinal, i.ContentHash)).ToArray(), contents) with { WorkingTotals = Totals };
        if (CandidateStateHash is string hash)
        {
            if (!contents.TryGetValue(hash, out string? candidateJson) || DelphiLiveSnapshotCodec.Hash(candidateJson) != hash)
                throw new InvalidDataException("Current paper candidate state is missing or damaged.");
            snapshot = snapshot with { CandidateStates = JsonSerializer.Deserialize<ImmutableDictionary<string, DelphiLivePortfolioCandidateState>>(
                candidateJson, DelphiLiveLedgerJson.Options) ?? throw new InvalidDataException("Missing current candidate dictionary.") };
        }
        if (snapshot.Cash != snapshot.StartingCapital + Totals.ArchivedCashNet + DelphiLiveWorkingState.CashNet(snapshot.Fills))
            throw new InvalidDataException("Current paper cash does not reconcile to its checkpoint.");
        return snapshot;
    }

    public async Task<Prepared> AdvanceAsync(DelphiLivePortfolioSnapshot prior, DelphiLivePortfolioSnapshot next,
        IReadOnlyDictionary<string, string> priorContents, Func<string, CancellationToken, Task<string?>> read, CancellationToken ct)
    {
        DelphiLiveLedgerIntegrity.ValidateTransition(prior, next);
        if (prior.WorkingTotals != Totals) throw new InvalidDataException("Paper working totals differ from their verified source.");
        var package = DelphiLiveSnapshotCodec.Pack(next);
        var counts = Counts.ToBuilder(); var roots = Roots.ToBuilder();
        var contents = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var pair in package.Contents)
        {
            if (priorContents.TryGetValue(pair.Key, out var old))
            { if (old != pair.Value) throw new InvalidDataException("An existing paper record changed under the same checksum."); }
            else contents[pair.Key] = pair.Value;
        }
        var changed = ImmutableArray.CreateBuilder<DelphiLiveSnapshotItem>();
        var identities = ImmutableArray.CreateBuilder<DelphiLiveSnapshotItem>();
        var mapped = new List<DelphiLiveSnapshotItem>();
        foreach (string name in DelphiLiveSnapshotCodec.Collections)
        {
            var before = Items.Where(i => i.Collection == name).OrderBy(i => i.Ordinal).ToArray();
            var after = package.Items.Where(i => i.Collection == name).OrderBy(i => i.Ordinal).ToArray();
            DelphiLiveArchiveTree.Result tree;
            if (name == "sessionOpeningPositions")
            {
                tree = DelphiLiveArchiveTree.Build(after.Select(i => i.ContentHash).ToArray());
                foreach (var item in after)
                {
                    mapped.Add(item);
                    if (item.Ordinal >= before.Length || item.ContentHash != before[item.Ordinal].ContentHash) changed.Add(item);
                }
            }
            else
            {
                if (after.Length < before.Length) throw new InvalidDataException("Working history was removed before validating its append.");
                var edits = new List<DelphiLiveArchiveTree.Change>();
                int count = Counts[name];
                foreach (var item in after)
                {
                    var previous = item.Ordinal < before.Length ? before[item.Ordinal] : null;
                    if (previous is not null && RecordId(name, priorContents[previous.ContentHash]) != RecordId(name, package.Contents[item.ContentHash]))
                        throw new InvalidDataException("Current paper records cannot change their stable archive order or identity.");
                    int ordinal = previous?.ArchiveOrdinal ?? count++;
                    var archiveItem = item with { Ordinal = ordinal }; mapped.Add(archiveItem);
                    if (previous?.ContentHash == item.ContentHash) continue;
                    changed.Add(archiveItem);
                    if (previous is null) identities.Add(archiveItem);
                    edits.Add(new(ordinal, previous?.ContentHash, item.ContentHash));
                }
                if (DelphiLiveArchiveTree.UsesAppendChain(name))
                {
                    int size = Counts[name]; string chain = Roots[name];
                    foreach (var edit in edits)
                    {
                        if (edit.Expected is not null || edit.Ordinal != size) throw new InvalidDataException("Completed paper records are append-only.");
                        chain = DelphiLiveArchiveTree.AppendChain(chain, size++, edit.ContentHash);
                    }
                    tree = new(size, chain, ImmutableDictionary<string, string>.Empty);
                }
                else tree = await DelphiLiveArchiveTree.ApplyAsync(Counts[name], Roots[name], edits, read, ct);
            }
            counts[name] = tree.Count; roots[name] = tree.Root;
            foreach (var pair in tree.Nodes) contents[pair.Key] = pair.Value;
        }
        string candidateJson = DelphiLiveSnapshotCodec.CanonicalJson(JsonNode.Parse(package.SnapshotJson)!["candidateStates"]!.ToJsonString());
        string candidateHash = DelphiLiveSnapshotCodec.Hash(candidateJson);
        if (!priorContents.TryGetValue(candidateHash, out string? previousCandidates)) contents[candidateHash] = candidateJson;
        else if (previousCandidates != candidateJson) throw new InvalidDataException("Candidate state changed under the same checksum.");
        string root = DelphiLiveSnapshotCodec.CreateIncrementalRoot(package.SnapshotJson, counts, roots, candidateHash);
        var selection = DelphiLiveWorkingState.Select(next);
        var compact = DelphiLiveSnapshotCodec.Pack(selection.Snapshot with { CandidateStates = ImmutableDictionary<string, DelphiLivePortfolioCandidateState>.Empty });
        var checkpoint = new DelphiLiveWorkingCheckpoint(2, compact.SnapshotJson, selection.Snapshot.WorkingTotals!,
            SelectItems(mapped, selection.RetainedIndices), counts.ToImmutable(), roots.ToImmutable()) { CandidateStateHash = candidateHash };
        return new(checkpoint, selection.Snapshot, new(root, DelphiLiveSnapshotCodec.Hash(root), contents, changed.ToImmutable()),
            changed.ToImmutable(), contents.ToImmutable(), identities.ToImmutable());
    }

    private static ImmutableArray<DelphiLiveWorkingItem> SelectItems(IReadOnlyList<DelphiLiveSnapshotItem> items,
        IReadOnlyDictionary<string, ImmutableArray<int>> retained)
    {
        var result = ImmutableArray.CreateBuilder<DelphiLiveWorkingItem>();
        foreach (string name in DelphiLiveSnapshotCodec.Collections)
        {
            // Input order is the pre-pruning working order; stored archive ordinals need not be contiguous.
            var group = items.Where(i => i.Collection == name).ToArray();
            for (int ordinal = 0; ordinal < retained[name].Length; ordinal++)
            {
                var item = group[retained[name][ordinal]];
                result.Add(new(name, ordinal, item.Ordinal, item.ContentHash));
            }
        }
        return result.ToImmutable();
    }
    private static string Header(string json, string? candidateHash = null)
    {
        var root = JsonNode.Parse(json)!.AsObject(); root.Remove(DelphiLiveSnapshotCodec.Marker);
        candidateHash ??= DelphiLiveSnapshotCodec.CandidateStateReference(json);
        if (candidateHash is null)
        {
            var candidates = root["candidateStates"]?.Deserialize<ImmutableDictionary<string, DelphiLivePortfolioCandidateState>>(DelphiLiveLedgerJson.Options)
                ?? ImmutableDictionary<string, DelphiLivePortfolioCandidateState>.Empty;
            candidateHash = DelphiLiveSnapshotCodec.Hash(DelphiLiveSnapshotCodec.CanonicalJson(JsonSerializer.Serialize(candidates, DelphiLiveLedgerJson.Options)));
        }
        root["candidateStates"] = new JsonObject();
        foreach (string name in DelphiLiveSnapshotCodec.Collections) root[name] = new JsonArray();
        // Older roots may omit subsequently added optional fields. Compare their decoded meaning;
        // the enclosing checkpoint still binds the exact original root bytes separately.
        root = JsonSerializer.SerializeToNode(root.Deserialize<DelphiLivePortfolioSnapshot>(DelphiLiveLedgerJson.Options), DelphiLiveLedgerJson.Options)!.AsObject();
        root["candidateStates"] = new JsonObject { ["$contentHash"] = candidateHash };
        return DelphiLiveSnapshotCodec.Fingerprint(root.ToJsonString());
    }

    public static Guid RecordId(string collection, string json)
    {
        var item = JsonNode.Parse(json)!;
        Guid id = collection switch
        {
            "positions" => item["positionId"]!.GetValue<Guid>(),
            "actions" => item["intent"]!["actionId"]!.GetValue<Guid>(),
            "fills" => item["fillId"]!.GetValue<Guid>(),
            "quotes" => item["quoteId"]!.GetValue<Guid>(),
            "marks" => item["markId"]!.GetValue<Guid>(),
            _ => throw new InvalidDataException("Unsupported paper archive record identity.")
        };
        if (id == Guid.Empty) throw new InvalidDataException("Empty paper archive identity.");
        return id;
    }
}
