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
///
/// Prompt-tightening (v2):
/// - Nulls / default-zero fields are stripped from the JSON view so the model
///   does not interpret defaults as facts (e.g. "ExpectedReturn=0").
/// - <see cref="DecisionSummary.Confidence"/> is hidden when it equals
///   <c>CompositeScore</c> (it would otherwise read as a duplicate metric).
/// - A <see cref="MarketSharedContext"/> may be supplied so per-pick prompts
///   can skip warnings that apply to every pick today.
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
        "4. If a field is null, missing, or omitted from the JSON, say nothing about it — do not guess.\n" +
        "   Treat the absence of a field as 'not known', NOT as 'zero'.\n" +
        "5. Be terse. Prefer short paragraphs and bullets over flowery prose.\n" +
        "6. Output plain text. No markdown headers, no JSON.\n" +
        "7. Focus on what makes THIS pick distinct. If a signal is listed under SHARED_TODAY in the user message,\n" +
        "   it applies to most/all picks today — do NOT restate it unless this pick is materially stronger or\n" +
        "   weaker than the shared baseline on that dimension.\n";

    /// <summary>
    /// Per-pick critique. <paramref name="shared"/> lets the model skip redundant
    /// warnings that hit every pick today (e.g. a market-wide Granville disparity).
    /// </summary>
    public static LlmPrompt BuildPerPick(
        DecisionDossier dossier,
        MarketSharedContext? shared = null,
        JsonSerializerOptions? jsonOpts = null)
    {
        EnsureSchemaSupported(dossier);

        var view = ProjectPerPickView(dossier);
        var json = JsonSerializer.Serialize(view, jsonOpts ?? DefaultJsonOpts);

        var sb = new StringBuilder();
        sb.AppendLine($"Pick date:   {dossier.PickDate:yyyy-MM-dd}");
        sb.AppendLine($"Symbol:      {dossier.Symbol}");
        sb.AppendLine($"Rank:        {dossier.Rank}");
        sb.AppendLine($"Direction:   {dossier.Decision.Direction}");
        sb.AppendLine($"Schema:      v{dossier.SchemaVersion}");
        sb.AppendLine();

        if (shared is { SharedGranvilleWarnings.Count: > 0 } sg)
        {
            sb.AppendLine("SHARED_TODAY (applies to most/all picks — do not restate unless this pick is an outlier):");
            foreach (var w in sg.SharedGranvilleWarnings)
                sb.AppendLine($"- Granville: {w}");
            sb.AppendLine();
        }

        sb.AppendLine("Tasks:");
        sb.AppendLine("- In 3-5 short bullets, summarize WHY this pick was selected, citing dossier fields by name.");
        sb.AppendLine("- In 2-3 bullets, list the strongest dissenting signals or risks visible in the dossier.");
        sb.AppendLine("- In one sentence, name the gate(s) that came closest to blocking this pick.");
        sb.AppendLine();
        sb.AppendLine("Dossier JSON (the only source of facts; null/zero-default fields are intentionally omitted):");
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

        var shared = ComputeSharedContext(dossiers);
        var sharedJson = JsonSerializer.Serialize(shared, jsonOpts ?? DefaultJsonOpts);

        var sb = new StringBuilder();
        sb.AppendLine($"Pick date:   {pickDate:yyyy-MM-dd}");
        sb.AppendLine($"Pick count:  {dossiers.Count}");
        sb.AppendLine();
        sb.AppendLine("Tasks:");
        sb.AppendLine("- In 4-6 bullets, summarize today's market posture using the MarketContext fields (cite by name).");
        sb.AppendLine("- In 2-3 bullets, characterize the top picks as a *group* — themes, sector tilts, edge profile.");
        sb.AppendLine("- If any signal in SHARED_TODAY hits a strong majority of picks, explicitly call out the");
        sb.AppendLine("  TENSION between that signal and the market posture (e.g. uptrend benchmark vs. broad");
        sb.AppendLine("  near-term-decline warnings). One sentence is enough.");
        sb.AppendLine("- In one sentence, flag the single biggest portfolio-level risk visible in the data.");
        sb.AppendLine();
        sb.AppendLine("SHARED_TODAY (signals that fired on most/all picks):");
        sb.AppendLine(sharedJson);
        sb.AppendLine();
        sb.AppendLine("Market context JSON:");
        sb.AppendLine(marketJson);
        sb.AppendLine();
        sb.AppendLine("Picks (compact) JSON:");
        sb.AppendLine(json);

        return new LlmPrompt(SystemPrompt, sb.ToString());
    }

    /// <summary>
    /// Pre-computes signals that fire on a strong majority (&gt;= 70%) of today's
    /// picks, so per-pick prompts can suppress them.
    /// </summary>
    public static MarketSharedContext ComputeSharedContext(IReadOnlyList<DecisionDossier> dossiers)
    {
        if (dossiers.Count == 0)
            return new MarketSharedContext(Array.Empty<string>(), 0, 0);

        const double threshold = 0.7;

        var perPickWarnings = dossiers.Select(d =>
            (d.Granville?.Indicators ?? Array.Empty<GranvilleIndicatorRecord>())
            .Where(i => i.GranvillePoints < 0)
            .Select(i => i.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToList();

        var allWarningNames = perPickWarnings
            .SelectMany(set => set)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var shared = allWarningNames
            .Where(name => perPickWarnings.Count(set => set.Contains(name)) >= dossiers.Count * threshold)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MarketSharedContext(shared, dossiers.Count, threshold);
    }

    /// <summary>
    /// Curated per-pick payload: strips nulls, drops <c>Confidence</c> when it
    /// duplicates <c>CompositeScore</c>, and elides default-zero fields that
    /// would otherwise be read as meaningful (e.g. <c>ExpectedReturn=0</c>).
    /// </summary>
    private static object ProjectPerPickView(DecisionDossier d)
    {
        var dec = d.Decision;

        // Build Decision view, omitting noisy/duplicate fields.
        var decision = new Dictionary<string, object?>
        {
            ["Direction"] = dec.Direction,
            ["CompositeScore"] = dec.CompositeScore,
            ["DirectionProbability"] = dec.DirectionProbability,
            ["DownProbability"] = dec.DownProbability,
            ["DirectionEdge"] = dec.DirectionEdge,
            ["LastPrice"] = dec.LastPrice
        };
        // Only include Confidence if it's distinct from CompositeScore.
        if (System.Math.Abs(dec.Confidence - dec.CompositeScore) > 1e-9)
            decision["Confidence"] = dec.Confidence;
        // Only include ExpectedReturn if non-zero (zero ≈ unset).
        if (System.Math.Abs(dec.ExpectedReturn) > 1e-9)
            decision["ExpectedReturn"] = dec.ExpectedReturn;

        return new
        {
            d.Symbol,
            d.Rank,
            Decision = decision,
            Market = d.Market,
            MlSignals = d.MlSignals,
            Granville = d.Granville,
            RelativeStrength = d.RelativeStrength,
            Sizing = d.Sizing,
            Gates = d.Gates,
            Strategy = d.Strategy
        };
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

/// <summary>
/// Cross-pick context — signals that fired on a strong majority of today's
/// picks. Per-pick prompts use this to suppress repetitive callouts.
/// </summary>
/// <param name="SharedGranvilleWarnings">Granville indicator names with negative points present in ≥ Threshold of picks.</param>
/// <param name="PickCount">Total picks evaluated.</param>
/// <param name="Threshold">Fraction (0–1) used to qualify as "shared".</param>
public sealed record MarketSharedContext(
    IReadOnlyList<string> SharedGranvilleWarnings,
    int PickCount,
    double Threshold);

