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

        // Only allow models that exist in the current registries.
        var allowedPatternTaskTypes = PatternRegistry.All
            .Select(p => p.TaskType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedProfitTaskTypes = ProfitModelRegistry.All
            .Select(p => p.TaskType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var patternModels = new List<IStockSignalModel>();
        var profitModels = new List<UnifiedProfitSignalModel>();

        var loadedPatterns = new List<string>();
        var loadedProfit = new List<string>();
        var skipped = new List<string>();

        var loadedTaskTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var modelInfo in enabledModels)
        {
            bool isAllowedPattern = allowedPatternTaskTypes.Contains(modelInfo.TaskType);
            bool isAllowedProfit = allowedProfitTaskTypes.Contains(modelInfo.TaskType);

            if (!isAllowedPattern && !isAllowedProfit)
            {
                skipped.Add($"{modelInfo.TaskType} (enabled in DB, but disabled in code registry)");
                continue;
            }

            if (!File.Exists(modelInfo.ZipPath))
            {
                skipped.Add($"{modelInfo.TaskType} (file not found)");
                continue;
            }

            if (!loadedTaskTypes.Add(modelInfo.TaskType))
            {
                skipped.Add($"{modelInfo.TaskType} (duplicate enabled row)");
                continue;
            }

            if (isAllowedPattern)
            {
                var patternModel = UnifiedPatternSignalModel.FromRegistryInfo(modelInfo);
                if (patternModel != null)
                {
                    patternModels.Add(patternModel);
                    loadedPatterns.Add(modelInfo.TaskType);
                    continue;
                }

                skipped.Add($"{modelInfo.TaskType} (allowed pattern, but failed to load)");
                continue;
            }

            if (isAllowedProfit)
            {
                var profitModel = UnifiedProfitSignalModel.FromRegistryInfo(modelInfo);
                if (profitModel != null)
                {
                    profitModels.Add(profitModel);
                    loadedProfit.Add(modelInfo.TaskType);
                    continue;
                }

                skipped.Add($"{modelInfo.TaskType} (allowed profit, but failed to load)");
                continue;
            }
        }

        if (loadedPatterns.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Pattern models: {string.Join(", ", loadedPatterns)}");

        if (loadedProfit.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Profit models: {string.Join(", ", loadedProfit)}");

        if (skipped.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Skipped: {string.Join(", ", skipped)}");

        return new TradeDecisionEngine(patternModels, profitModels, config);
    }
}