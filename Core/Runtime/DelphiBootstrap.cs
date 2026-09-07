using Core.Db;
using Core.ML.Engine.Patterns;
using Core.ML.Engine.Profit;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

#nullable enable

namespace Core.Runtime;

public static class DelphiBootstrap
{
    public static async Task<TradeDecisionEngine> BuildTradeDecisionEngineFromRegistry(
        StrategyConfig? config = null,
        TextWriter? output = null,
        ProfitFeatureInputs? featureInputs = null,
        DateTime? predictionSession = null,
        ReviewedProfitInputBinding? binding = null,
        StoredProfitModelSet? storedModelSet = null)
    {
        void Log(string message) => (output ?? Console.Out).WriteLine(message);
        var repo = new ModelRegistryRepository();
        if ((featureInputs is null) != (binding is null))
            throw new InvalidOperationException("Corrected model inputs require an explicit reviewed binding.");
        var enabledModels = storedModelSet is not null ? storedModelSet.Models.Select(model => model.Registry).ToList() : binding is null
            ? await repo.GetEnabledModels()
            : await repo.GetModelsById(binding.Models.Select(model => model.ModelId).ToArray());
        binding?.ValidateRows(enabledModels);
        var artifactBinding = storedModelSet?.ToBinding() ?? binding;

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
                if (artifactBinding is not null) throw new InvalidOperationException($"Reviewed model artifact missing for {modelInfo.TaskType}.");
                Log($"[DelphiBootstrap] ⚠️  Model file not found, skipping: {modelInfo.TaskType}");
                continue;
            }

            if (!loadedTaskTypes.Add(modelInfo.TaskType))
                continue;

            var profitModel = UnifiedProfitSignalModel.FromRegistryInfo(modelInfo, featureInputs, predictionSession, artifactBinding);
            if (profitModel != null)
            {
                profitModels.Add(profitModel);
                loadedProfit.Add($"{modelInfo.TaskType} [registry signal Buy >= {modelInfo.ThresholdBuy:P0}]");
            }
        }

        Log($"[DelphiBootstrap] Pattern signals (rule-based): {string.Join(", ", loadedPatterns)}");
        Log($"[DelphiBootstrap] Profit models (ML):          {string.Join(", ", loadedProfit)}");

        return new TradeDecisionEngine(patternModels, profitModels, config);
    }
}
