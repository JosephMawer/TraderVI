using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Core.Db;
using Core.Oracle;
using Core.Oracle.Llm;
using Core.Oracle.Prompts;

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
///
/// Environment:
///   ORACLE_LLM_PROVIDER   mock (default) | openai | dotllm
///   ORACLE_LLM_MODEL      provider-specific model id
///   OPENAI_API_KEY        required when provider=openai
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var opts = ParseArgs(args);

            Console.WriteLine($"[Oracle] PickDate={opts.PickDate:yyyy-MM-dd}  " +
                              $"Mode={opts.Mode}  DryRun={opts.DryRun}");

            var llm = LlmClientFactory.FromEnvironment();
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

            if (!opts.DryRun)
            {
                await narrativeRepo.DeleteByDateAsync(opts.PickDate);
            }

            int totalIn = 0, totalOut = 0;
            decimal totalCost = 0m;

            // Per-pick critiques.
            if (opts.Mode != Mode.MarketOnly)
            {
                foreach (var d in dossiers.OrderBy(x => x.Rank))
                {
                    var prompt = DossierPromptBuilder.BuildPerPick(d);
                    var resp = await CallAndPersist(
                        llm, narrativeRepo,
                        pickDate: opts.PickDate,
                        dossierId: null, // we don't have DossierId here; PickId identifies row
                        scope: LlmNarrativeRepository.ScopePerPick,
                        symbol: d.Symbol,
                        prompt: prompt,
                        schemaVersion: d.SchemaVersion,
                        dryRun: opts.DryRun);

                    totalIn += resp.Usage.InputTokens;
                    totalOut += resp.Usage.OutputTokens;
                    totalCost += resp.Usage.CostUsd;

                    Console.WriteLine($"[Oracle] PerPick  {d.Symbol,-6} rank={d.Rank,-2}  " +
                                      $"in={resp.Usage.InputTokens,5}  out={resp.Usage.OutputTokens,5}  " +
                                      $"cost=${resp.Usage.CostUsd:0.0000}  " +
                                      $"{resp.Latency.TotalMilliseconds:0} ms");
                }
            }

            // Market-wide summary.
            if (opts.Mode != Mode.PerPickOnly)
            {
                var prompt = DossierPromptBuilder.BuildMarketSummary(opts.PickDate, dossiers);
                var resp = await CallAndPersist(
                    llm, narrativeRepo,
                    pickDate: opts.PickDate,
                    dossierId: null,
                    scope: LlmNarrativeRepository.ScopeMarket,
                    symbol: null,
                    prompt: prompt,
                    schemaVersion: dossiers.Max(x => x.SchemaVersion),
                    dryRun: opts.DryRun);

                totalIn += resp.Usage.InputTokens;
                totalOut += resp.Usage.OutputTokens;
                totalCost += resp.Usage.CostUsd;

                Console.WriteLine($"[Oracle] Market   {dossiers.Count} picks    " +
                                  $"in={resp.Usage.InputTokens,5}  out={resp.Usage.OutputTokens,5}  " +
                                  $"cost=${resp.Usage.CostUsd:0.0000}  " +
                                  $"{resp.Latency.TotalMilliseconds:0} ms");
            }

            Console.WriteLine($"[Oracle] Totals: in={totalIn}  out={totalOut}  cost=${totalCost:0.0000}");
            if (opts.DryRun)
                Console.WriteLine("[Oracle] DRY RUN — no rows written to [dbo].[LlmNarrative].");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Oracle] FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static async Task<LlmResponse> CallAndPersist(
        ILlmClient llm,
        LlmNarrativeRepository repo,
        DateTime pickDate,
        Guid? dossierId,
        string scope,
        string? symbol,
        LlmPrompt prompt,
        int schemaVersion,
        bool dryRun)
    {
        var req = new LlmRequest(prompt.SystemPrompt, prompt.UserPrompt, Temperature: 0.2);
        var resp = await llm.CompleteAsync(req);

        if (!dryRun)
        {
            await repo.InsertAsync(
                pickDate: pickDate,
                dossierId: dossierId,
                scope: scope,
                symbol: symbol,
                provider: resp.Provider,
                model: resp.Model,
                temperature: req.Temperature,
                promptHash: DossierPromptBuilder.ComputePromptHash(prompt),
                systemPrompt: prompt.SystemPrompt,
                userPrompt: prompt.UserPrompt,
                responseText: resp.Text,
                inputTokens: resp.Usage.InputTokens,
                outputTokens: resp.Usage.OutputTokens,
                costUsd: resp.Usage.CostUsd,
                latencyMs: (int)resp.Latency.TotalMilliseconds,
                schemaVersion: schemaVersion);
        }

        return resp;
    }

    private enum Mode { Both, PerPickOnly, MarketOnly }

    private sealed record CliOptions(DateTime PickDate, Mode Mode, bool DryRun);

    private static CliOptions ParseArgs(string[] args)
    {
        DateTime date = DateTime.Today;
        var mode = Mode.Both;
        bool dryRun = false;

        foreach (var a in args)
        {
            if (a.Equals("--per-pick-only", StringComparison.OrdinalIgnoreCase)) mode = Mode.PerPickOnly;
            else if (a.Equals("--market-only", StringComparison.OrdinalIgnoreCase)) mode = Mode.MarketOnly;
            else if (a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)) dryRun = true;
            else if (DateTime.TryParseExact(a, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                            DateTimeStyles.AssumeLocal, out var parsed))
            {
                date = parsed.Date;
            }
            else
            {
                throw new ArgumentException($"Unrecognized argument: '{a}'. " +
                    "Expected yyyy-MM-dd date or --per-pick-only / --market-only / --dry-run.");
            }
        }

        return new CliOptions(date, mode, dryRun);
    }
}

