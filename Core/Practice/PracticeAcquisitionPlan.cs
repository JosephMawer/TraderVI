#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Core.Practice;

public sealed record PracticeIntradayRequest(DateOnly Session, string Symbol, int IntervalMinutes);

/// <summary>Acquire only possible Shadow candidates/holdings, without reducing Delphi's daily universe.
/// A selected stock remains in the acquisition plan through the last session, even after a missing day.</summary>
public static class PracticeAcquisitionPlan
{
    public static IReadOnlyList<PracticeIntradayRequest> Create(PracticeDataset dataset, IReadOnlyList<PracticeDailyResult> daily)
    {
        if (!daily.Select(x => x.Session).SequenceEqual(dataset.Sessions) ||
            daily.Any(x => x.MarketDataAsOf.HasValue && DateOnly.FromDateTime(x.MarketDataAsOf.Value) >= x.Session))
            throw new InvalidDataException("Acquisition requires one bounded daily result for every dataset session, in order.");
        var possibleHoldings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requests = new List<PracticeIntradayRequest>();
        foreach (var result in daily)
        {
            if (result.Published)
                possibleHoldings.UnionWith(result.Picks.Where(p => p.IsEligible && p.Rank is >= 1 and <= 5 &&
                    p.Lens is "Continuation" or "Breakout").Select(p => p.Symbol));
            foreach (var symbol in possibleHoldings.Order(StringComparer.OrdinalIgnoreCase))
                foreach (int interval in new[] { 5, 15 }) requests.Add(new(result.Session, symbol, interval));
        }
        return requests;
    }
}
