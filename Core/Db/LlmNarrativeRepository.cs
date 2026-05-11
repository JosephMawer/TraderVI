using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Core.Db;

/// <summary>
/// Persists Oracle LLM narratives to <c>[dbo].[LlmNarrative]</c>.
/// Per Rule R3 every call is fully reproducible — we store the exact prompt
/// text, prompt hash, model, temperature, token usage, cost, and latency.
/// </summary>
public sealed class LlmNarrativeRepository : SQLBase
{
    public const string ScopePerPick = "PerPick";
    public const string ScopeMarket  = "Market";

    public LlmNarrativeRepository()
        : base("[dbo].[LlmNarrative]",
               "[NarrativeId],[PickDate],[DossierId],[Scope],[Symbol],[Provider],[Model]," +
               "[Temperature],[PromptHash],[SystemPrompt],[UserPrompt],[ResponseText]," +
               "[InputTokens],[OutputTokens],[CostUsd],[LatencyMs],[SchemaVersion],[CreatedUtc]")
    { }

    public async Task<Guid> InsertAsync(
        DateTime pickDate,
        Guid? dossierId,
        string scope,
        string? symbol,
        string provider,
        string model,
        double temperature,
        string promptHash,
        string systemPrompt,
        string userPrompt,
        string responseText,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        int latencyMs,
        int schemaVersion)
    {
        var id = Guid.NewGuid();

        const string sql = @"
INSERT INTO [dbo].[LlmNarrative]
([NarrativeId],[PickDate],[DossierId],[Scope],[Symbol],[Provider],[Model],
 [Temperature],[PromptHash],[SystemPrompt],[UserPrompt],[ResponseText],
 [InputTokens],[OutputTokens],[CostUsd],[LatencyMs],[SchemaVersion])
VALUES
(@NarrativeId,@PickDate,@DossierId,@Scope,@Symbol,@Provider,@Model,
 @Temperature,@PromptHash,@SystemPrompt,@UserPrompt,@ResponseText,
 @InputTokens,@OutputTokens,@CostUsd,@LatencyMs,@SchemaVersion);";

        await Insert(sql,
        [
            new SqlParameter("@NarrativeId", SqlDbType.UniqueIdentifier) { Value = id },
            new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate.Date },
            new SqlParameter("@DossierId", SqlDbType.UniqueIdentifier) { Value = (object?)dossierId ?? DBNull.Value },
            new SqlParameter("@Scope", SqlDbType.NVarChar, 16) { Value = scope },
            new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = (object?)symbol ?? DBNull.Value },
            new SqlParameter("@Provider", SqlDbType.NVarChar, 32) { Value = provider },
            new SqlParameter("@Model", SqlDbType.NVarChar, 64) { Value = model },
            new SqlParameter("@Temperature", SqlDbType.Float) { Value = temperature },
            new SqlParameter("@PromptHash", SqlDbType.Char, 64) { Value = promptHash },
            new SqlParameter("@SystemPrompt", SqlDbType.NVarChar, -1) { Value = systemPrompt },
            new SqlParameter("@UserPrompt", SqlDbType.NVarChar, -1) { Value = userPrompt },
            new SqlParameter("@ResponseText", SqlDbType.NVarChar, -1) { Value = responseText },
            new SqlParameter("@InputTokens", SqlDbType.Int) { Value = inputTokens },
            new SqlParameter("@OutputTokens", SqlDbType.Int) { Value = outputTokens },
            new SqlParameter("@CostUsd", SqlDbType.Decimal) { Value = costUsd },
            new SqlParameter("@LatencyMs", SqlDbType.Int) { Value = latencyMs },
            new SqlParameter("@SchemaVersion", SqlDbType.Int) { Value = schemaVersion }
        ]);

        return id;
    }

    /// <summary>Idempotent re-runs: remove a date's narratives before re-inserting.</summary>
    public async Task DeleteByDateAsync(DateTime pickDate)
    {
        const string sql = "DELETE FROM [dbo].[LlmNarrative] WHERE [PickDate] = @PickDate";
        await Delete(sql,
            [new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate.Date }]);
    }

    public async Task<List<LlmNarrativeRow>> GetByDateAsync(DateTime pickDate)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [PickDate] = @PickDate ORDER BY [Scope], [Symbol]";
        return await ExecuteReaderAsync(query,
            [new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate.Date }],
            Map);
    }

    private static LlmNarrativeRow Map(SqlDataReader r) => new()
    {
        NarrativeId  = r.GetGuid(0),
        PickDate     = r.GetDateTime(1),
        DossierId    = r.IsDBNull(2) ? null : r.GetGuid(2),
        Scope        = r.GetString(3),
        Symbol       = r.IsDBNull(4) ? null : r.GetString(4),
        Provider     = r.GetString(5),
        Model        = r.GetString(6),
        Temperature  = r.GetDouble(7),
        PromptHash   = r.GetString(8),
        SystemPrompt = r.GetString(9),
        UserPrompt   = r.GetString(10),
        ResponseText = r.GetString(11),
        InputTokens  = r.GetInt32(12),
        OutputTokens = r.GetInt32(13),
        CostUsd      = r.GetDecimal(14),
        LatencyMs    = r.GetInt32(15),
        SchemaVersion = r.GetInt32(16),
        CreatedUtc   = r.GetDateTime(17)
    };
}

public sealed record LlmNarrativeRow
{
    public Guid NarrativeId { get; init; }
    public DateTime PickDate { get; init; }
    public Guid? DossierId { get; init; }
    public required string Scope { get; init; }
    public string? Symbol { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public double Temperature { get; init; }
    public required string PromptHash { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public required string ResponseText { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal CostUsd { get; init; }
    public int LatencyMs { get; init; }
    public int SchemaVersion { get; init; }
    public DateTime CreatedUtc { get; init; }
}
