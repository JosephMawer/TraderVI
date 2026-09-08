#nullable enable
using Core.Db;
using Core.ML.Engine.Profit;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Explicit SQL acquisition for preparation only. Offline Delphi never calls this service.</summary>
public static class DelphiHistoricalPreparation
{
    public static async Task<(StrategyVersionInfo Strategy, StoredProfitModelSet Models)> ReadSelectionAsync(CancellationToken ct)
    {
        await using var selection = await DelphiStrategySelectionLease.AcquireForRunAsync(ct);
        var strategy = await new StrategyVersionRepository().GetActiveVersion() ?? throw new InvalidDataException("No active daily strategy.");
        var models = await new StrategyModelBindingRepository().GetAsync(strategy.VersionId)
            ?? throw new InvalidDataException("No preserved model set for the selected strategy.");
        models.Validate(strategy.VersionId, strategy.DecisionRef);
        return (strategy, models);
    }
}
