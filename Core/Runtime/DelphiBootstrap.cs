using Core.Db;
using Core.ML.Engine.Patterns;
using Core.ML.Engine.Profit;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Runtime;

public static class DelphiBootstrap
{
    public static async Task<TradeDecisionEngine> BuildTradeDecisionEngineFromRegistry(
        StrategyConfig? config = null)
    {
        var repo = new ModelRegistryRepository();
        var enabledModels = await repo.GetEnabledModels();

        var allowedProfitTaskTypes = ProfitModelRegistry.All
            .Select(p => p.TaskType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A2 refactor: pattern task types are rule-based and no longer read from ModelRegistry.
        // If a DB row for a pattern task type still exists (legacy), it is silently ignored here.
        var patternTaskTypesInCode = PatternRegistry.All
            .Select(p => p.TaskType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── Pattern models: built directly from the code registry (no DB, no ML.NET) ─────
        var patternModels = PatternRegistry.All
            .Select(p => (IStockSignalModel)new RulePatternSignalModel(p))
            .ToList();

        var loadedPatterns = PatternRegistry.All.Select(p => p.TaskType).ToList();

        // ── Profit models: still registry-driven ──────────────────────────────────────────
        var profitModels = new List<UnifiedProfitSignalModel>();
        var loadedProfit = new List<string>();
        var loadedTaskTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var modelInfo in enabledModels)
        {
            // Silently skip any legacy pattern rows still marked IsEnabled in the DB.
            if (patternTaskTypesInCode.Contains(modelInfo.TaskType))
                continue;

            if (!allowedProfitTaskTypes.Contains(modelInfo.TaskType))
                continue; // disabled in code registry

            if (!File.Exists(modelInfo.ZipPath))
            {
                Console.WriteLine($"[DelphiBootstrap] ⚠️  Model file not found, skipping: {modelInfo.TaskType}");
                continue;
            }

            if (!loadedTaskTypes.Add(modelInfo.TaskType))
                continue;

            var profitModel = UnifiedProfitSignalModel.FromRegistryInfo(modelInfo);
            if (profitModel != null)
            {
                profitModels.Add(profitModel);
                loadedProfit.Add(modelInfo.TaskType);
            }
        }

        Console.WriteLine($"[DelphiBootstrap] Pattern signals (rule-based): {string.Join(", ", loadedPatterns)}");
        Console.WriteLine($"[DelphiBootstrap] Profit models (ML):          {string.Join(", ", loadedProfit)}");

        return new TradeDecisionEngine(patternModels, profitModels, config);
    }
}