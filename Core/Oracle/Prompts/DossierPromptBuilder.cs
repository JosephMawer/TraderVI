using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core.Oracle.Prompts;

/// <summary>
/// Builds prompts from <see cref="DecisionDossier"/> rows. Two flavors:
/// per-pick critique and a market-wide summary across all today's picks.
///
/// Rules baked into the prompts:
/// - R2: instructs the model to **cite dossier fields by name** and forbids
///       computing or fabricating numbers not present in the dossier.
/// - R8: declares <see cref="MinSupportedSchemaVersion"/> so a dossier from a
///       future schema can be rejected loudly rather than silently misread.
/// - R9: the dossier JSON is the only source of facts the model is shown.
/// </summary>
public static class DossierPromptBuilder
{
    /// <summary>
    /// Minimum dossier <c>SchemaVersion</c> these prompt templates understand.
    /// Bump in lockstep with prompt updates that depend on new fields.
    /// </summary>
    public const int MinSupportedSchemaVersion = 1;

    /// <summary>System prompt shared by per-pick and market summaries.</summary>
    public static string SystemPrompt => SystemPromptValue;

    private const string SystemPromptValue =
        "You are Oracle, the narration and critique layer for the TraderVI trading system.\n" +
        "You read structured DecisionDossier JSON produced by a deterministic pipeline.\n" +
        "\n" +
        "STRICT RULES (binding):\n" +
        "1. NEVER produce a number that is not present in the dossier. Do not compute, average, or derive figures.\n" +
        "   If you mention a number, cite the dossier field name in parentheses, e.g. (Decision.CompositeScore).\n" +
        "2. NEVER invent symbols, sectors, indicator names, or events that are not in the dossier.\n" +
        "3. NEVER recommend changing the decision. You are a critic and narrator, not a decision-maker.\n" +
        "4. If a field is null or missing, say so plainly — do not guess.\n" +
        "5. Be terse. Prefer short paragraphs and bullets over flowery prose.\n" +
        "6. Output plain text. No markdown headers, no JSON.\n";

    public static LlmPrompt BuildPerPick(DecisionDossier dossier, JsonSerializerOptions? jsonOpts = null)
    {
        EnsureSchemaSupported(dossier);

        var json = JsonSerializer.Serialize(dossier, jsonOpts ?? DefaultJsonOpts);

        var sb = new StringBuilder();
        sb.AppendLine($"Pick date:   {dossier.PickDate:yyyy-MM-dd}");
        sb.AppendLine($"Symbol:      {dossier.Symbol}");
        sb.AppendLine($"Rank:        {dossier.Rank}");
        sb.AppendLine($"Direction:   {dossier.Decision.Direction}");
        sb.AppendLine($"Schema:      v{dossier.SchemaVersion}");
        sb.AppendLine();
        sb.AppendLine("Tasks:");
        sb.AppendLine("- In 3-5 short bullets, summarize WHY this pick was selected, citing dossier fields by name.");
        sb.AppendLine("- In 2-3 bullets, list the strongest dissenting signals or risks visible in the dossier.");
        sb.AppendLine("- In one sentence, name the gate(s) that came closest to blocking this pick.");
        sb.AppendLine();
        sb.AppendLine("Dossier JSON (the only source of facts):");
        sb.AppendLine(json);

        return new LlmPrompt(SystemPrompt, sb.ToString());
    }

    public static LlmPrompt BuildMarketSummary(
        DateTime pickDate,
        IReadOnlyList<DecisionDossier> dossiers,
        JsonSerializerOptions? jsonOpts = null)
    {
        foreach (var d in dossiers) EnsureSchemaSupported(d);

        // Compact, structured payload — not full per-pick JSON, to stay within budget.
        var compact = dossiers.Select(d => new
        {
            d.Symbol,
            d.Rank,
            Direction = d.Decision.Direction,
            d.Decision.CompositeScore,
            d.Decision.DirectionEdge,
            d.Decision.DirectionProbability,
            d.Decision.DownProbability,
            GranvilleAdj = d.Granville?.CompositeAdjustment,
            RsComposite = d.RelativeStrength?.CompositeScore
        }).ToList();

        var json = JsonSerializer.Serialize(compact, jsonOpts ?? DefaultJsonOpts);

        var market = dossiers.FirstOrDefault()?.Market;
        var marketJson = market is null
            ? "null"
            : JsonSerializer.Serialize(market, jsonOpts ?? DefaultJsonOpts);

        var sb = new StringBuilder();
        sb.AppendLine($"Pick date:   {pickDate:yyyy-MM-dd}");
        sb.AppendLine($"Pick count:  {dossiers.Count}");
        sb.AppendLine();
        sb.AppendLine("Tasks:");
        sb.AppendLine("- In 4-6 bullets, summarize today's market posture using the MarketContext fields (cite by name).");
        sb.AppendLine("- In 2-3 bullets, characterize the top picks as a *group* — themes, sector tilts, edge profile.");
        sb.AppendLine("- In one sentence, flag the single biggest portfolio-level risk visible in the data.");
        sb.AppendLine();
        sb.AppendLine("Market context JSON:");
        sb.AppendLine(marketJson);
        sb.AppendLine();
        sb.AppendLine("Picks (compact) JSON:");
        sb.AppendLine(json);

        return new LlmPrompt(SystemPrompt, sb.ToString());
    }

    /// <summary>SHA-256 of system+user for fast equality lookups (Rule R3).</summary>
    public static string ComputePromptHash(LlmPrompt prompt)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(
            Encoding.UTF8.GetBytes(prompt.SystemPrompt + "\n---\n" + prompt.UserPrompt));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void EnsureSchemaSupported(DecisionDossier d)
    {
        if (d.SchemaVersion < MinSupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Dossier schema v{d.SchemaVersion} is older than the prompt template's " +
                $"minimum supported version v{MinSupportedSchemaVersion} (Rule R8). " +
                $"Symbol={d.Symbol}, Date={d.PickDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.");
        }
    }

    private static readonly JsonSerializerOptions DefaultJsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Bundled (system, user) pair ready to feed to <c>ILlmClient</c>.</summary>
public sealed record LlmPrompt(string SystemPrompt, string UserPrompt);
