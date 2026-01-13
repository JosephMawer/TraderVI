using Core.Db;
using Core.ML.Engine;
using Core.Trader;
using Core.Trader.Signals.Structure;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Runtime;

public static class DelphiBootstrap
{
    public static async Task<TradeDecisionEngine> BuildTradeDecisionEngineFromRegistry()
    {
        var repo = new ModelRegistryRepository();
        var models = await repo.GetEnabledModels();

        var signalModels = new List<IStockSignalModel>();

        foreach (var m in models)
        {
            var signal = m.TaskType switch
            {
                TaskTypes.Trend30 => new Trend30ContextSignalModel(m.ZipPath) as IStockSignalModel,
                TaskTypes.Trend10 => new Trend10ContextSignalModel(m.ZipPath),
                _ => null
            };

            if (signal != null)
                signalModels.Add(signal);
        }

        return new TradeDecisionEngine(signalModels);
    }
}