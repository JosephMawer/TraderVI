#nullable enable
using Core.Runtime;
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

public static class EngineArchiveStorage
{
    public static async Task<IReadOnlyDictionary<string, string>> ReadContentsAsync(SqlConnection connection,
        SqlTransaction? transaction, string manifest, CancellationToken ct = default)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!EngineArchiveCodec.IsPacked(manifest)) return contents;
        var pending = EngineArchiveCodec.References(manifest);
        long bytes = 0;
        for (int depth = 0; pending.Count > 0; depth++)
        {
            if (depth > 64 || contents.Count + pending.Count > 100_000) throw new InvalidDataException("Engine archive exceeds its reference limits.");
            var rows = (await connection.QueryAsync<Content>(new CommandDefinition("""
SELECT c.ContentHash,c.ContentJson FROM dbo.EngineEvidenceContent c
JOIN OPENJSON(@Hashes) h ON c.ContentHash=CONVERT(char(64),h.value) COLLATE Latin1_General_100_BIN2;
""", new { Hashes = JsonSerializer.Serialize(pending) }, transaction, cancellationToken: ct))).ToArray();
            if (rows.Length != pending.Count) throw new InvalidDataException("Engine archive record is missing.");
            foreach (var row in rows)
            {
                if (DelphiLiveSnapshotCodec.Hash(row.ContentJson) != row.ContentHash) throw new InvalidDataException("Engine archive checksum failed.");
                bytes += System.Text.Encoding.UTF8.GetByteCount(row.ContentJson);
                if (bytes > EngineArchiveCodec.MaximumExpandedBytes * 2L) throw new InvalidDataException("Engine archive exceeds its storage limit.");
                contents.Add(row.ContentHash, row.ContentJson);
            }
            pending = rows.SelectMany(r => EngineArchiveCodec.References(r.ContentJson)).Where(h => !contents.ContainsKey(h)).Distinct().ToArray();
        }
        return contents;
    }
    public static async Task<string> RestoreAsync(SqlConnection connection, SqlTransaction? transaction,
        string manifest, CancellationToken ct = default) => EngineArchiveCodec.Restore(manifest,
            await ReadContentsAsync(connection, transaction, manifest, ct).ConfigureAwait(false));
    private sealed record Content(string ContentHash, string ContentJson);
}
