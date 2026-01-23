using Core.Db;
using Core.Db;
using Core.ML.Engine.Patterns;
using Core.ML.Engine.Profit;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>
/// Bootstraps the TradeDecisionEngine by loading all enabled models
/// from the registry and creating signal models dynamically.
/// </summary>
public static class DelphiBootstrap
{
    public static async Task<TradeDecisionEngine> BuildTradeDecisionEngineFromRegistry()
    {
        var repo = new ModelRegistryRepository();
        var enabledModels = await repo.GetEnabledModels();

        var patternModels = new List<IStockSignalModel>();
        var profitModels = new List<UnifiedProfitSignalModel>();

        var loadedPatterns = new List<string>();
        var loadedProfit = new List<string>();
        var skipped = new List<string>();

        foreach (var modelInfo in enabledModels)
        {
            if (!File.Exists(modelInfo.ZipPath))
            {
                skipped.Add($"{modelInfo.TaskType} (file not found)");
                continue;
            }

            // Try pattern model first
            var patternModel = UnifiedPatternSignalModel.FromRegistryInfo(modelInfo);
            if (patternModel != null)
            {
                patternModels.Add(patternModel);
                loadedPatterns.Add(modelInfo.TaskType);
                continue;
            }

            // Try profit model
            var profitModel = UnifiedProfitSignalModel.FromRegistryInfo(modelInfo);
            if (profitModel != null)
            {
                profitModels.Add(profitModel);
                loadedProfit.Add(modelInfo.TaskType);
                continue;
            }

            skipped.Add($"{modelInfo.TaskType} (not in any registry)");
        }

        if (loadedPatterns.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Pattern models: {string.Join(", ", loadedPatterns)}");

        if (loadedProfit.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Profit models: {string.Join(", ", loadedProfit)}");

        if (skipped.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Skipped: {string.Join(", ", skipped)}");

        return new TradeDecisionEngine(patternModels, profitModels);
    }

    /// <summary>
    /// Loads only specific pattern types (useful for testing or selective evaluation).
    /// </summary>
    public static async Task<TradeDecisionEngine> BuildTradeDecisionEngineForPatterns(params string[] taskTypes)
    {
        var repo = new ModelRegistryRepository();
        var enabledModels = await repo.GetEnabledModels();

        var targetTypes = new HashSet<string>(taskTypes, StringComparer.OrdinalIgnoreCase);
        var signalModels = new List<IStockSignalModel>();

        var loadedPatterns = new List<string>();
        var loadedProfit = new List<string>();
        var skipped = new List<string>();

        foreach (var modelInfo in enabledModels)
        {
            if (!File.Exists(modelInfo.ZipPath))
            {
                skipped.Add($"{modelInfo.TaskType} (file not found)");
                continue;
            }

            // Try pattern model first
            var patternModel = UnifiedPatternSignalModel.FromRegistryInfo(modelInfo);
            if (patternModel != null)
            {
                patternModels.Add(patternModel);
                loadedPatterns.Add(modelInfo.TaskType);
                continue;
            }

            // Try profit model
            var profitModel = UnifiedProfitSignalModel.FromRegistryInfo(modelInfo);
            if (profitModel != null)
            {
                profitModels.Add(profitModel);
                loadedProfit.Add(modelInfo.TaskType);
                continue;
            }

            skipped.Add($"{modelInfo.TaskType} (not in any registry)");
        }

        if (loadedPatterns.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Pattern models: {string.Join(", ", loadedPatterns)}");

        if (loadedProfit.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Profit models: {string.Join(", ", loadedProfit)}");

        if (skipped.Count > 0)
            Console.WriteLine($"[DelphiBootstrap] Skipped: {string.Join(", ", skipped)}");

        return new TradeDecisionEngine(patternModels, profitModels);
    }
}