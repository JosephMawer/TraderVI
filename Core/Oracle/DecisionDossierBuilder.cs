using System;
using System.Collections.Generic;
using System.Linq;
using Core.Indicators.Granville;
using Core.RelativeStrength;
using Core.Trader;

namespace Core.Oracle;

/// <summary>
/// Pure-function builder that assembles a <see cref="DecisionDossier"/> from a
/// <see cref="RankedPick"/> and the market-level context computed for the run.
///
/// Design notes:
/// - Stateless / no side effects: same inputs always produce the same dossier.
/// - Lives downstream of <c>TradeDecisionEngine</c>. The dossier never flows
///   back into scoring (Rule R1).
/// - All numeric facts come from already-computed inputs; this builder does
///   not derive new numbers (Rule R2).
/// </summary>
public static class DecisionDossierBuilder
{
    public static DecisionDossier Build(
        DateTime pickDate,
        Guid pickId,
        int rank,
        RankedPick pick,
        decimal lastPrice,
        MarketRegime? regime,
        double? breadthScore,
        double? breadthVetoThreshold,
        GranvilleDailyForecast? granville,
        RelativeStrengthRow? rs,
        SizingSnapshot? sizing = null,
        StrategyVersionRef? strategy = null)
    {
        var market = new MarketContext(
            IsBenchmarkUptrend: regime?.IsBenchmarkUptrend,
            IsBenchmark20dPositive: regime?.IsBenchmark20dPositive,
            IsVolatilityNormal: regime?.IsVolatilityNormal,
            BenchmarkReturn20d: regime?.BenchmarkReturn20d,
            BenchmarkMA50: regime?.BenchmarkMA50,
            BenchmarkMA200: regime?.BenchmarkMA200,
            IsSpyUptrend: regime?.IsSpyUptrend,
            IsSpy20dPositive: regime?.IsSpy20dPositive,
            BreadthScore: breadthScore,
            BreadthVetoThreshold: breadthVetoThreshold);

        var ml = BuildMlBreakdown(pick);

        GranvilleBreakdown? gran = null;
        if (granville is not null)
        {
            gran = new GranvilleBreakdown(
                NetPoints: granville.NetPoints,
                CompositeAdjustment: granville.CompositeAdjustment,
                Indicators: granville.Results
                    .Select(r => new GranvilleIndicatorRecord(
                        IndicatorNumber: r.IndicatorNumber,
                        Category: r.Category.ToString(),
                        Name: r.Name,
                        Signal: r.Signal.ToString(),
                        GranvillePoints: r.GranvillePoints,
                        Description: r.Description))
                    .ToList());
        }

        RelativeStrengthBreakdown? rsBreakdown = null;
        if (rs is not null)
        {
            rsBreakdown = new RelativeStrengthBreakdown(
                CompositeScore: rs.CompositeScore,
                Return5d: rs.RS_StockVsMarket_5d,
                Return10d: rs.RS_StockVsMarket_10d,
                Return20d: rs.RS_StockVsMarket_20d,
                Return60d: rs.RS_StockVsMarket_60d,
                Z5d: rs.RS_Z_StockVsMarket,
                Z10d: rs.RS_Z_StockVsSector,
                Z20d: rs.RS_Z_SectorVsMarket,
                Z60d: null,
                SectorSymbol: rs.SectorIndexSymbol);
        }

        var gates = pick.GateTrace is null
            ? (IReadOnlyList<GateTraceRecord>)Array.Empty<GateTraceRecord>()
            : pick.GateTrace.Select(GateTraceRecord.From).ToList();

        var decision = new DecisionSummary(
            Direction: pick.Direction.ToString(),
            CompositeScore: pick.CompositeScore,
            Confidence: pick.Confidence,
            DirectionProbability: pick.DirectionProbability,
            DownProbability: pick.DownProbability,
            DirectionEdge: pick.DirectionEdge,
            ExpectedReturn: pick.ExpectedReturn,
            LastPrice: lastPrice);

        return new DecisionDossier(
            SchemaVersion: DecisionDossier.CurrentSchemaVersion,
            PickDate: pickDate.Date,
            PickId: pickId,
            Symbol: pick.Symbol,
            Rank: rank,
            Decision: decision,
            Market: market,
            MlSignals: ml,
            Granville: gran,
            RelativeStrength: rsBreakdown,
            Sizing: sizing,
            Gates: gates,
            Strategy: strategy);
    }

    private static MlSignalBreakdown BuildMlBreakdown(RankedPick pick)
    {
        double Find(string name) => pick.Signals
            .FirstOrDefault(s => s.Name?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            ?.Score ?? 0;

        var contributions = pick.Signals
            .Select(s => new SignalContribution(
                Name: s.Name,
                Score: s.Score,
                Hint: s.Hint?.ToString(),
                Notes: s.Notes))
            .ToList();

        return new MlSignalBreakdown(
            BreakoutProb: Find("Breakout"),
            UpProb: pick.DirectionProbability,
            DownProb: pick.DownProbability,
            DirectionEdge: pick.DirectionEdge,
            VolExpansionProb: Find("VolExpansion"),
            RelStrengthProb: Find("RelStrength"),
            Signals: contributions);
    }
}
