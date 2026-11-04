#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

// All holding consumers have this reader. Including broker portfolios in a lane is a separate
// choice from verifying the identity of a tracked Real holding that the lane already observes.
internal sealed class HoldingMarketSymbolResolver(
    Func<CancellationToken, Task<IReadOnlyList<EngineRealObservation>>> readReal,
    Func<EngineRealObservation, CancellationToken, Task<IReadOnlyList<TrackedHoldingMarketSymbol>>> readManaged)
{
    internal HoldingMarketSymbolResolver() : this(new EngineRealObservationStore().ReadAsync,
        new BrokerManagedSessionRepository().ReadTrackedMarketSymbolsAsync) { }

    internal async Task<IReadOnlyList<DelphiLiveObservedHolding>> ResolveAsync(
        IReadOnlyList<DelphiLiveObservedHolding> holdings, IEnumerable<string> symbols,
        IReadOnlyList<HoldingSymbolAlias> aliases, IReadOnlyList<EngineRealObservation>? real = null,
        CancellationToken ct = default)
    {
        var canonical = symbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managed = new List<TrackedHoldingMarketSymbol>();
        if (holdings.Any(h => h.Owner == "Real" && !canonical.Contains(h.Symbol.Trim()) &&
            !aliases.Any(a => a.Source == "Broker" && string.Equals(a.Alias, h.Symbol.Trim(), StringComparison.OrdinalIgnoreCase))))
            foreach (var observation in real ?? await readReal(ct))
                managed.AddRange(await readManaged(observation, ct));
        return DelphiLiveHoldingSymbols.Resolve(holdings, canonical, aliases, managed);
    }
}
