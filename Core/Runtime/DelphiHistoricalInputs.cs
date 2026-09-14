#nullable enable
using Core.Db;
using Core.Indicators;
using Core.Indicators.Granville;
using Core.Indicators.Models;
using Core.ML;
using Core.ML.Engine.Profit;
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Runtime;

/// <summary>Saved observations only. Every market-data read is bounded before recommendation day.
/// Empty collections mean unavailable; this source never falls back to a repository or provider.</summary>
public sealed record DelphiHistoricalInputs
{
    public required StrategyVersionInfo Strategy { get; init; }
    public required StoredProfitModelSet Models { get; init; }
    public Dictionary<string, List<DailyBar>> Daily { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ADLineEntry> Breadth { get; init; } = [];
    public List<LeadershipSnapshot> Leadership { get; init; } = [];
    public List<SectorIndexSnapshot> Sectors { get; init; } = [];
    public Dictionary<string, List<UsIndexBar>> UsIndices { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<OBV>> Obv { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MarketClimaxEntry> Climax { get; init; } = [];
    public List<DatedDelphiMembership> Membership { get; init; } = [];
    // Explicit exploratory assumption, never disguised as an observed historical snapshot.
    // Only membership/classification changes: prices and indicator series remain bounded before each day.
    public DatedDelphiMembership? ExploratoryCurrentUniverse { get; init; }
    public string UniverseTimingNote => ExploratoryCurrentUniverse is { } current
        ? $"Exploratory current stock list and classifications, captured {current.AsOf:yyyy-MM-dd} UTC; historical membership unverified."
        : "Dated historical membership only; absent snapshots remain unavailable.";

    public List<DailyBar> Bars(string symbol, DateTime before, DateTime? from = null) =>
        Daily.GetValueOrDefault(symbol, []).Where(x => x.Date.Date < before.Date && (!from.HasValue || x.Date >= from))
            .OrderBy(x => x.Date).ToList();

    public DatedDelphiMembership Members(DateTime before)
    {
        if (ExploratoryCurrentUniverse is not null) return ExploratoryCurrentUniverse;
        var snapshot = Membership.Where(x => x.AsOf.Date < before.Date).OrderBy(x => x.AsOf).LastOrDefault()
            ?? new(before.AddDays(-1), [], [], []);
        return snapshot with { Sectors = snapshot.Sectors.Where(x => x.LastUpdated.Date < before.Date).ToArray() };
    }

    public IReadOnlyList<SectorIndexSnapshot> SectorHistory(IEnumerable<string> symbols, int days, DateTime before)
    {
        var bounded = Sectors.Where(x => x.Date.Date < before.Date).ToArray();
        if (bounded.Length == 0) return [];
        var from = bounded.Max(x => x.Date).AddDays(-days * 2);
        return bounded.Where(x => x.Date >= from && symbols.Contains(x.Symbol, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x.Date).ThenBy(x => x.Symbol).ToArray();
    }
}

public sealed record DatedDelphiMembership(DateTime AsOf, IReadOnlyList<SymbolInfo> Equities,
    IReadOnlyList<StockSectorMapping> Sectors, IReadOnlyList<string> XiuConstituents);

public sealed record DelphiEvaluatedPick(string Lens, string Symbol, int Rank, bool IsEligible, decimal PreviousClose);
