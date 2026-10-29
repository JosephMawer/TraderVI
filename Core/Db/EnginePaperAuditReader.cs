#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Fresh, bounded membership/content pass for one immutable portfolio and fixed boundary.</summary>
internal sealed class EnginePaperAuditReader(Guid owner, long through) : IDisposable
{
    private const int MaximumChanges = 1_000_000;
    private readonly DelphiLiveSnapshotCodec.AuditDecoder decoder = new();
    private Change[]? changes;
    private int changeIndex;
    private long last = -1;
    private readonly Dictionary<(string Collection, int Ordinal), string> membership = new();
    private readonly Queue<Row> roots = new();
    public void Dispose() => decoder.Dispose();

    internal async Task<(Row Row, DelphiLivePortfolioSnapshot State)> ReadAsync(SqlConnection c, long revision, CancellationToken ct)
    {
        if (revision < 0 || revision > through || last >= 0 && revision != last + 1)
            throw new InvalidOperationException("Audit reader requires adjacent revisions within its original boundary.");
        if (changes is null)
        {
            // Read the compact change index once. Do not run an as-of-history window query for
            // every revision. SQL statements close before a batch yields, releasing read locks.
            changes = (await c.QueryAsync<Change>(new CommandDefinition("""
SELECT TOP(1000001) Revision,CollectionName,Ordinal,ContentHash FROM dbo.DelphiLiveSnapshotItem
WHERE PortfolioId=@Owner AND Revision<=@Through ORDER BY Revision,CollectionName,Ordinal;
""", new { Owner = owner, Through = through }, cancellationToken: ct))).ToArray();
            if (changes.Length > MaximumChanges) throw new InvalidDataException("Paper audit membership exceeds its bounded reader capacity.");
        }
        if (roots.Count == 0)
            foreach (var row in await c.QueryAsync<Row>(new CommandDefinition("""
SELECT TOP(32) PortfolioId,Revision,SnapshotJson,SettingsAssignmentId FROM dbo.DelphiLivePortfolioRevision
WHERE PortfolioId=@Owner AND Revision>=@Revision AND Revision<=@Through ORDER BY Revision;
""", new { Owner = owner, Revision = revision, Through = through }, cancellationToken: ct))) roots.Enqueue(row);
        if (roots.Count == 0 || roots.Peek().Revision != revision) throw new InvalidDataException("An original paper revision is missing.");
        var root = roots.Dequeue();
        while (changeIndex < changes.Length && changes[changeIndex].Revision <= revision)
        {
            var delta = changes[changeIndex++];
            if (!DelphiLiveSnapshotCodec.Collections.Contains(delta.CollectionName) || delta.Ordinal < 0)
                throw new InvalidDataException("Invalid historical membership change.");
            membership[(delta.CollectionName, delta.Ordinal)] = delta.ContentHash;
        }
        var items = new List<DelphiLiveSnapshotItem>();
        if (DelphiLiveSnapshotCodec.IsCompact(root.SnapshotJson))
        {
            var counts = DelphiLiveSnapshotCodec.ReadCounts(root.SnapshotJson);
            foreach (var collection in counts)
                for (int n = 0; n < collection.Value; n++)
                {
                    if (!membership.TryGetValue((collection.Key, n), out var hash)) throw new InvalidDataException("An original paper item is missing.");
                    items.Add(new(collection.Key, n, hash));
                }
            string[] missing = items.Select(i => i.ContentHash).Distinct().Where(h => !decoder.Contains(h)).ToArray();
            await LoadAsync(c, missing, ct);
            var reports = missing.SelectMany(decoder.ReportReferences).ToList();
            if (DelphiLiveSnapshotCodec.CandidateStateReference(root.SnapshotJson) is string candidate) reports.Add(candidate);
            await LoadAsync(c, reports, ct);
        }
        var state = decoder.Read(root.SnapshotJson, items);
        if (root.PortfolioId != owner || state.PortfolioId != owner || state.Revision != revision)
            throw new InvalidDataException("Historical paper revision identity failed.");
        last = revision;
        return (root, state);
    }

    private async Task LoadAsync(SqlConnection c, IEnumerable<string> hashes, CancellationToken ct)
    {
        foreach (var page in hashes.Distinct().Where(h => !decoder.Contains(h)).Chunk(512))
        {
            var rows = await c.QueryAsync<Content>(new CommandDefinition("""
SELECT c.ContentHash,c.ContentJson FROM dbo.DelphiLiveSnapshotContent c
JOIN OPENJSON(@Hashes) h ON c.ContentHash=CONVERT(char(64),h.value) COLLATE Latin1_General_100_BIN2;
""", new { Hashes = JsonSerializer.Serialize(page) }, cancellationToken: ct));
            foreach (var row in rows) decoder.Add(row.ContentHash, row.ContentJson);
            if (page.Any(h => !decoder.Contains(h))) throw new InvalidDataException("Missing immutable paper audit content.");
        }
    }
    private sealed record Change(long Revision, string CollectionName, int Ordinal, string ContentHash);
    private sealed record Content(string ContentHash, string ContentJson);
    internal sealed record Row(Guid PortfolioId, long Revision, string SnapshotJson, Guid? SettingsAssignmentId);
}
