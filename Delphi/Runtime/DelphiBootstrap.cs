using Core.Db;
using Core.Trader;
using Delphi.Signals.Structure;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Delphi.Runtime
{
    public static class DelphiBootstrap
    {
        public static async Task<TradeDecisionEngine> BuildTradeDecisionEngineFromRegistry()
        {
            var repo = new ModelRegistryRepository();
            var models = await repo.GetEnabledModels();

            var signalModels = new List<IStockSignalModel>();

            foreach (var m in models)
            {
                if (string.Equals(m.TaskType, "Trend30", StringComparison.OrdinalIgnoreCase))
                {
                    signalModels.Add(new Trend30ContextSignalModel(m.ZipPath));
                }
            }

            return new TradeDecisionEngine(signalModels);
        }
    }
}