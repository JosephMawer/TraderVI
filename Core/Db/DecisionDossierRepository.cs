using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Oracle;
using Microsoft.Data.SqlClient;

namespace Core.Db;

/// <summary>
/// Persists <see cref="DecisionDossier"/> snapshots to <c>[dbo].[DecisionDossier]</c>.
/// One row per pick per day. The JSON payload is the authoritative input for
/// the downstream LLM layer (Rule R9 — the dossier is the audit unit).
/// </summary>
public sealed class DecisionDossierRepository : SQLBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DecisionDossierRepository()
        : base("[dbo].[DecisionDossier]",
               "[DossierId],[PickDate],[PickId],[Symbol],[Rank],[SchemaVersion],[DossierJson],[CreatedUtc]")
    { }

    /// <summary>
    /// Inserts a single dossier row, returning its generated id.
    /// </summary>
    public async Task<Guid> InsertAsync(DecisionDossier dossier)
    {
        var dossierId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(dossier, JsonOptions);

        const string sql = @"
INSERT INTO [dbo].[DecisionDossier]
([DossierId],[PickDate],[PickId],[Symbol],[Rank],[SchemaVersion],[DossierJson])
VALUES
(@DossierId,@PickDate,@PickId,@Symbol,@Rank,@SchemaVersion,@DossierJson);";

        await Insert(sql,
        [
            new SqlParameter("@DossierId", SqlDbType.UniqueIdentifier) { Value = dossierId },
            new SqlParameter("@PickDate", SqlDbType.Date) { Value = dossier.PickDate.Date },
            new SqlParameter("@PickId", SqlDbType.UniqueIdentifier) { Value = dossier.PickId },
            new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = dossier.Symbol },
            new SqlParameter("@Rank", SqlDbType.Int) { Value = dossier.Rank },
            new SqlParameter("@SchemaVersion", SqlDbType.Int) { Value = dossier.SchemaVersion },
            new SqlParameter("@DossierJson", SqlDbType.NVarChar, -1) { Value = json }
        ]);

        return dossierId;
    }

    /// <summary>
    /// Removes all dossiers for a given date. Used to keep daily runs idempotent
    /// (mirrors <c>DailyPickRepository.DeletePicksByDate</c>).
    /// </summary>
    public async Task DeleteByDateAsync(DateTime pickDate)
    {
        const string sql = "DELETE FROM [dbo].[DecisionDossier] WHERE [PickDate] = @PickDate";
        await Delete(sql,
            [new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate.Date }]);
    }

    /// <summary>
    /// Reads back a dossier and deserializes its JSON payload. Returns null if not found.
    /// </summary>
    public async Task<DecisionDossier?> GetByPickIdAsync(Guid pickId)
    {
        const string sql = "SELECT TOP 1 [DossierJson] FROM [dbo].[DecisionDossier] WHERE [PickId] = @PickId";
        var rows = await ExecuteReaderAsync(sql,
            [new SqlParameter("@PickId", SqlDbType.UniqueIdentifier) { Value = pickId }],
            r => r.GetString(0));

        if (rows.Count == 0) return null;
        return JsonSerializer.Deserialize<DecisionDossier>(rows[0], JsonOptions);
    }

    /// <summary>
    /// Reads all dossiers for a given date (ordered by rank).
    /// </summary>
    public async Task<List<DecisionDossier>> GetByDateAsync(DateTime pickDate)
    {
        const string sql = @"
SELECT [DossierJson]
FROM [dbo].[DecisionDossier]
WHERE [PickDate] = @PickDate
ORDER BY [Rank]";

        var rows = await ExecuteReaderAsync(sql,
            [new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate.Date }],
            r => r.GetString(0));

        var result = new List<DecisionDossier>(rows.Count);
        foreach (var json in rows)
        {
            var d = JsonSerializer.Deserialize<DecisionDossier>(json, JsonOptions);
            if (d is not null) result.Add(d);
        }
        return result;
    }
}
