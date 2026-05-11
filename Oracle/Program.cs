using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Core.Db;
using Core.Oracle;
using Core.Oracle.Llm;
using Core.Oracle.Prompts;
using Microsoft.Extensions.Configuration;

namespace Oracle;

/// <summary>
/// Phase 2 entry point. Reads persisted <see cref="DecisionDossier"/> rows for a
/// given pick date, builds prompts, calls the configured LLM provider, and
/// persists the resulting narratives to <c>[dbo].[LlmNarrative]</c>.
///
/// Strictly downstream of the trading engine (Rule R1).
///
/// Usage:
///   Oracle.exe                       (uses today's date)
///   Oracle.exe 2025-11-12
///   Oracle.exe 2025-11-12 --per-pick-only
///   Oracle.exe 2025-11-12 --market-only
///   Oracle.exe 2025-11-12 --dry-run
///   Oracle.exe 2025-11-12 --print            print narratives to console
///   Oracle.exe 2025-11-12 --markdown         write Oracle/output/&lt;date&gt;.md
///   Oracle.exe 2025-11-12 --force            ignore cache and re-call the API
///
/// By default Oracle is incremental: a narrative is reused from
/// <c>[dbo].[LlmNarrative]</c> when its stored <c>PromptHash</c> matches the
/// current prompt. Re-run Delphi (which rewrites the dossier) and Oracle will
/// detect the new hash and regenerate only the affected narratives.
///
/// Configuration (user-secrets preferred; env vars also supported):
///   Oracle:Llm:Provider        ORACLE_LLM_PROVIDER     mock | openai | dotllm
///   Oracle:Llm:Model           ORACLE_LLM_MODEL        provider-specific model id
///   Oracle:OpenAi:ApiKey       OPENAI_API_KEY          required when provider=openai
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var opts = ParseArgs(args);

            Console.WriteLine($"[Oracle] PickDate={opts.PickDate:yyyy-MM-dd}  " +
                              $"Mode={opts.Mode}  DryRun={opts.DryRun}  Force={opts.Force}");

            var cfg = BuildConfiguration();
            var llm = LlmClientFactory.FromConfiguration(cfg);
            Console.WriteLine($"[Oracle] LLM provider={llm.Provider}  model={llm.Model}");

            var dossierRepo = new DecisionDossierRepository();
            var narrativeRepo = new LlmNarrativeRepository();

            var dossiers = await dossierRepo.GetByDateAsync(opts.PickDate);
            if (dossiers.Count == 0)
            {
                Console.WriteLine($"[Oracle] No dossiers found for {opts.PickDate:yyyy-MM-dd}. Nothing to do.");
                return 0;
            }
            Console.WriteLine($"[Oracle] Loaded {dossiers.Count} dossier(s).");

            int totalIn = 0, totalOut = 0, cached = 0, called = 0;
            decimal totalCost = 0m;

            var emitted = new List<EmittedNarrative>();

            // Precompute cross-pick context once so per-pick prompts can suppress
            // signals that fire on a strong majority of today's picks.
            var sharedContext = DossierPromptBuilder.ComputeSharedContext(dossiers);
            if (sharedContext.SharedGranvilleWarnings.Count > 0)
            {
                Console.WriteLine($"[Oracle] Shared warnings ({sharedContext.SharedGranvilleWarnings.Count}): " +
                                  string.Join(", ", sharedContext.SharedGranvilleWarnings));
            }
            if (sharedContext.SharedGranvilleConfirmations.Count > 0)
            {
                Console.WriteLine($"[Oracle] Shared Granville confirmations ({sharedContext.SharedGranvilleConfirmations.Count}): " +
                                  string.Join(", ", sharedContext.SharedGranvilleConfirmations));
            }
            if (sharedContext.SharedMlConfirmations.Count > 0)
            {
                Console.WriteLine($"[Oracle] Shared ML/rule confirmations ({sharedContext.SharedMlConfirmations.Count}): " +
                                  string.Join(", ", sharedContext.SharedMlConfirmations));
            }

            // Per-pick critiques.
            if (opts.Mode != Mode.MarketOnly)
            {
                foreach (var d in dossiers.OrderBy(x => x.Rank))
                {
                    var prompt = DossierPromptBuilder.BuildPerPick(d, sharedContext);
                    var result = await ResolveAsync(
                        llm, narrativeRepo,
                        pickDate: opts.PickDate,
                        scope: LlmNarrativeRepository.ScopePerPick,
                        symbol: d.Symbol,
                        prompt: prompt,
                        schemaVersion: d.SchemaVersion,
                        opts: opts);

                    totalIn += result.InputTokens;
                    totalOut += result.OutputTokens;
                    totalCost += result.CostUsd;
                    if (result.FromCache) cached++; else called++;

                    emitted.Add(new EmittedNarrative(
                        LlmNarrativeRepository.ScopePerPick, d.Symbol, d.Rank, result.ResponseText));

                    Console.WriteLine($"[Oracle] PerPick  {d.Symbol,-6} rank={d.Rank,-2}  " +
                                      $"in={result.InputTokens,5}  out={result.OutputTokens,5}  " +
                                      $"cost=${result.CostUsd:0.0000}  " +
                                      $"{result.LatencyMs,5} ms  " +
                                      (result.FromCache ? "[cache]" : "[api]  "));
                }
            }

            // Market-wide summary.
            if (opts.Mode != Mode.PerPickOnly)
            {
                var prompt = DossierPromptBuilder.BuildMarketSummary(opts.PickDate, dossiers);
                var result = await ResolveAsync(
                    llm, narrativeRepo,
                    pickDate: opts.PickDate,
                    scope: LlmNarrativeRepository.ScopeMarket,
                    symbol: null,
                    prompt: prompt,
                    schemaVersion: dossiers.Max(x => x.SchemaVersion),
                    opts: opts);

                totalIn += result.InputTokens;
                totalOut += result.OutputTokens;
                totalCost += result.CostUsd;
                if (result.FromCache) cached++; else called++;

                emitted.Add(new EmittedNarrative(
                    LlmNarrativeRepository.ScopeMarket, null, 0, result.ResponseText));

                Console.WriteLine($"[Oracle] Market   {dossiers.Count} picks    " +
                                  $"in={result.InputTokens,5}  out={result.OutputTokens,5}  " +
                                  $"cost=${result.CostUsd:0.0000}  " +
                                  $"{result.LatencyMs,5} ms  " +
                                  (result.FromCache ? "[cache]" : "[api]  "));
            }

            Console.WriteLine($"[Oracle] Totals: in={totalIn}  out={totalOut}  " +
                              $"cost=${totalCost:0.0000}  api={called}  cache={cached}");
            if (opts.DryRun)
                Console.WriteLine("[Oracle] DRY RUN — no rows written to [dbo].[LlmNarrative].");

            if (opts.Print)
                PrintNarratives(emitted);

            if (opts.Markdown)
            {
                var path = WriteMarkdown(opts.PickDate, emitted);
                Console.WriteLine($"[Oracle] Markdown written: {path}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Oracle] FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    /// <summary>
    /// Returns a narrative either from cache (if a row exists with a matching prompt hash)
    /// or by calling the LLM and persisting the result. Honors --force and --dry-run.
    /// </summary>
    private static async Task<NarrativeResult> ResolveAsync(
        ILlmClient llm,
        LlmNarrativeRepository repo,
        DateTime pickDate,
        string scope,
        string? symbol,
        LlmPrompt prompt,
        int schemaVersion,
        CliOptions opts)
    {
        var promptHash = DossierPromptBuilder.ComputePromptHash(prompt);

        if (!opts.Force)
        {
            var existing = await repo.GetOneAsync(pickDate, scope, symbol);
            if (existing is not null && string.Equals(existing.PromptHash, promptHash, StringComparison.OrdinalIgnoreCase))
            {
                return new NarrativeResult(
                    ResponseText: existing.ResponseText,
                    InputTokens: 0,
                    OutputTokens: 0,
                    CostUsd: 0m,
                    LatencyMs: 0,
                    FromCache: true);
            }
        }

        var req = new LlmRequest(prompt.SystemPrompt, prompt.UserPrompt, Temperature: 0.2);
        var resp = await llm.CompleteAsync(req);

        if (!opts.DryRun)
        {
            // Replace any stale row for this (date, scope, symbol) before inserting.
            await repo.DeleteOneAsync(pickDate, scope, symbol);

            await repo.InsertAsync(
                pickDate: pickDate,
                dossierId: null,
                scope: scope,
                symbol: symbol,
                provider: resp.Provider,
                model: resp.Model,
                temperature: req.Temperature,
                promptHash: promptHash,
                systemPrompt: prompt.SystemPrompt,
                userPrompt: prompt.UserPrompt,
                responseText: resp.Text,
                inputTokens: resp.Usage.InputTokens,
                outputTokens: resp.Usage.OutputTokens,
                costUsd: resp.Usage.CostUsd,
                latencyMs: (int)resp.Latency.TotalMilliseconds,
                schemaVersion: schemaVersion);
        }

        return new NarrativeResult(
            ResponseText: resp.Text,
            InputTokens: resp.Usage.InputTokens,
            OutputTokens: resp.Usage.OutputTokens,
            CostUsd: resp.Usage.CostUsd,
            LatencyMs: (int)resp.Latency.TotalMilliseconds,
            FromCache: false);
    }

    private static void PrintNarratives(IReadOnlyList<EmittedNarrative> items)
    {
        Console.WriteLine();
        Console.WriteLine("══════════════════════════ Narratives ══════════════════════════");
        foreach (var n in items.OrderBy(x => x.Scope).ThenBy(x => x.Rank).ThenBy(x => x.Symbol))
        {
            var header = n.Scope == LlmNarrativeRepository.ScopeMarket
                ? "── Market summary ──"
                : $"── {n.Symbol} (rank {n.Rank}) ──";
            Console.WriteLine();
            Console.WriteLine(header);
            Console.WriteLine(n.ResponseText.Trim());
        }
        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════════");
    }

    private static string WriteMarkdown(DateTime pickDate, IReadOnlyList<EmittedNarrative> items)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "output");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{pickDate:yyyy-MM-dd}.md");

        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine($"# Oracle briefing — {pickDate:yyyy-MM-dd}");
        writer.WriteLine();

        var market = items.FirstOrDefault(x => x.Scope == LlmNarrativeRepository.ScopeMarket);
        if (market is not null)
        {
            writer.WriteLine("## Market summary");
            writer.WriteLine();
            writer.WriteLine(market.ResponseText.Trim());
            writer.WriteLine();
        }

        var perPick = items
            .Where(x => x.Scope == LlmNarrativeRepository.ScopePerPick)
            .OrderBy(x => x.Rank);

        foreach (var n in perPick)
        {
            writer.WriteLine($"## {n.Symbol} — rank {n.Rank}");
            writer.WriteLine();
            writer.WriteLine(n.ResponseText.Trim());
            writer.WriteLine();
        }

        return path;
    }

    private enum Mode { Both, PerPickOnly, MarketOnly }

    private sealed record CliOptions(
        DateTime PickDate,
        Mode Mode,
        bool DryRun,
        bool Print,
        bool Markdown,
        bool Force);

    private sealed record EmittedNarrative(string Scope, string? Symbol, int Rank, string ResponseText);

    private sealed record NarrativeResult(
        string ResponseText,
        int InputTokens,
        int OutputTokens,
        decimal CostUsd,
        int LatencyMs,
        bool FromCache);

    private static CliOptions ParseArgs(string[] args)
    {
        DateTime date = DateTime.Today;
        var mode = Mode.Both;
        bool dryRun = false, print = false, markdown = false, force = false;

        foreach (var a in args)
        {
            if (a.Equals("--per-pick-only", StringComparison.OrdinalIgnoreCase)) mode = Mode.PerPickOnly;
            else if (a.Equals("--market-only", StringComparison.OrdinalIgnoreCase)) mode = Mode.MarketOnly;
            else if (a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)) dryRun = true;
            else if (a.Equals("--print", StringComparison.OrdinalIgnoreCase)) print = true;
            else if (a.Equals("--markdown", StringComparison.OrdinalIgnoreCase) ||
                     a.Equals("--md", StringComparison.OrdinalIgnoreCase)) markdown = true;
            else if (a.Equals("--force", StringComparison.OrdinalIgnoreCase)) force = true;
            else if (DateTime.TryParseExact(a, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                            DateTimeStyles.AssumeLocal, out var parsed))
            {
                date = parsed.Date;
            }
            else
            {
                throw new ArgumentException($"Unrecognized argument: '{a}'. " +
                    "Expected yyyy-MM-dd date or --per-pick-only / --market-only / " +
                    "--dry-run / --print / --markdown / --force.");
            }
        }

        return new CliOptions(date, mode, dryRun, print, markdown, force);
    }

    /// <summary>
    /// Builds the configuration root used to resolve LLM settings.
    /// Order (later overrides earlier): user-secrets (dev only) → environment variables.
    /// Keep secrets (API keys) in user-secrets so they never land in source control.
    /// </summary>
    private static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .AddEnvironmentVariables();
        return builder.Build();
    }
}

