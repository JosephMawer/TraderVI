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

        var loadedTaskTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var modelInfo in enabledModels)
        {
            bool isAllowedPattern = allowedPatternTaskTypes.Contains(modelInfo.TaskType);
            bool isAllowedProfit = allowedProfitTaskTypes.Contains(modelInfo.TaskType);

            // Silently skip DB rows for models disabled in the code registry
            if (!isAllowedPattern && !isAllowedProfit)
                continue;

            if (!File.Exists(modelInfo.ZipPath))
            {
                // File missing is actionable — warn but don't crash
                Console.WriteLine($"[DelphiBootstrap] ⚠️  Model file not found, skipping: {modelInfo.TaskType}");
                continue;
            }

            if (!loadedTaskTypes.Add(modelInfo.TaskType))
                continue;

            if (isAllowedPattern)
            {
                var patternModel = UnifiedPatternSignalModel.FromRegistryInfo(modelInfo);
                if (patternModel != null)
                {
                    patternModels.Add(patternModel);
                    loadedPatterns.Add(modelInfo.TaskType);
                }
                continue;
            }

            if (isAllowedProfit)
            {
                var profitModel = UnifiedProfitSignalModel.FromRegistryInfo(modelInfo);
                if (profitModel != null)
                {
                    profitModels.Add(profitModel);
                    loadedProfit.Add(modelInfo.TaskType);
                }
                continue;
            }
        }

        Console.WriteLine($"[DelphiBootstrap] Pattern models: {string.Join(", ", loadedPatterns)}");
        Console.WriteLine($"[DelphiBootstrap] Profit models:  {string.Join(", ", loadedProfit)}");

        return new TradeDecisionEngine(patternModels, profitModels, config);
    }
}