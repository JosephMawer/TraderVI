#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public static class EngineEvidenceStorage
{
    public static string Serialize(IReadOnlyDictionary<string, string> contents) =>
        JsonSerializer.Serialize(contents.Select(c => new { ContentHash = c.Key, ContentJson = c.Value }));

    internal const string InsertSql = """
DECLARE @Fragments TABLE(ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,ContentJson nvarchar(max) NOT NULL);
INSERT @Fragments SELECT ContentHash,ContentJson FROM OPENJSON(@Contents)
 WITH(ContentHash char(64) '$.ContentHash',ContentJson nvarchar(max) '$.ContentJson');
IF EXISTS(SELECT 1 FROM @Fragments n JOIN dbo.EngineEvidenceContent c WITH(UPDLOCK,HOLDLOCK) ON c.ContentHash=n.ContentHash
 WHERE CONVERT(varbinary(max),c.ContentJson)<>CONVERT(varbinary(max),n.ContentJson))
 THROW 54401,'Shared evidence fingerprint conflict.',1;
INSERT dbo.EngineEvidenceContent(ContentHash,ContentJson)
 SELECT n.ContentHash,n.ContentJson FROM @Fragments n
 WHERE NOT EXISTS(SELECT 1 FROM dbo.EngineEvidenceContent c WITH(UPDLOCK,HOLDLOCK) WHERE c.ContentHash=n.ContentHash);
""";
    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync(SqlConnection connection,
        SqlTransaction? transaction, IEnumerable<string> manifests, CancellationToken ct = default)
    {
        var hashes = manifests.SelectMany(DelphiLiveInputCodec.References).Distinct(StringComparer.Ordinal).ToArray();
        if (hashes.Length == 0) return new Dictionary<string, string>();
        var rows = await connection.QueryAsync<Content>(new CommandDefinition("""
SELECT c.ContentHash,c.ContentJson FROM dbo.EngineEvidenceContent c
JOIN OPENJSON(@Hashes) h ON c.ContentHash=CONVERT(char(64),h.value) COLLATE Latin1_General_100_BIN2;
""", new { Hashes = JsonSerializer.Serialize(hashes) }, transaction, cancellationToken: ct));
        return rows.ToDictionary(c => c.ContentHash, c => c.ContentJson, StringComparer.Ordinal);
    }

    private sealed record Content(string ContentHash, string ContentJson);
}
