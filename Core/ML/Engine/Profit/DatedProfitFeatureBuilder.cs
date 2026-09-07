#nullable enable
using Core.ML.Engine.Patterns;
using Core.ML.Engine.Patterns.Features;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

public sealed record ProfitFeatureInputExclusion(string Symbol, string Reason, DateTime Session);

public sealed class ProfitFeatureInputException(string reason, bool sharedBenchmark, DateTime session)
    : InvalidOperationException($"{reason}: required feature input at {session:yyyy-MM-dd} is unavailable or invalid.")
{
    public string Reason { get; } = reason;
    public bool SharedBenchmark { get; } = sharedBenchmark;
    public DateTime Session { get; } = session.Date;
}

/// <summary>
/// A private copy of the dated XIU input and its canonical session sequence.
/// It may serve many training anchors or a single prediction anchor without
/// mutating the globally registered feature builders.
/// </summary>
public sealed class ProfitFeatureInputs
{
    public const string Contract = "ProfitInputs.XiuDatedV1";
    public const string LegacyContract = "ProfitInputs.LegacyUnverified";
    private readonly Dictionary<DateTime, DailyBar[]> market;
    private readonly DateTime[] sessions;
    private readonly HashSet<DateTime> duplicateSessions;

    public ProfitFeatureInputs(IReadOnlyList<DailyBar> marketBars, IReadOnlyList<DateTime> canonicalSessions)
    {
        ArgumentNullException.ThrowIfNull(marketBars);
        ArgumentNullException.ThrowIfNull(canonicalSessions);
        market = marketBars.Select(Copy).GroupBy(bar => bar.Date).ToDictionary(group => group.Key, group => group.ToArray());
        var groups = canonicalSessions.Select(date => date.Date).GroupBy(date => date).ToArray();
        duplicateSessions = groups.Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        sessions = groups.Select(group => group.Key).OrderBy(date => date).ToArray();
    }

    internal DailyBar[] MarketWindow(DateTime asOf, int lookback)
    {
        int end = Array.BinarySearch(sessions, asOf.Date);
        if (end < lookback - 1)
            throw new ProfitFeatureInputException("XiuFeatureHistoryMissing", true, asOf);
        var dates = sessions.Skip(end - lookback + 1).Take(lookback).ToArray();
        if (dates.Any(duplicateSessions.Contains))
            throw new ProfitFeatureInputException("XiuFeatureSessionDuplicate", true, asOf);
        var result = new List<DailyBar>(lookback);
        foreach (var date in dates)
        {
            var matches = market.GetValueOrDefault(date) ?? [];
            if (matches.Length != 1)
                throw new ProfitFeatureInputException(matches.Length == 0 ? "XiuFeatureBarMissing" : "XiuFeatureBarDuplicate", true, date);
            if (!float.IsFinite(matches[0].Close) || matches[0].Close <= 0)
                throw new ProfitFeatureInputException("XiuFeatureCloseInvalid", true, date);
            result.Add(Copy(matches[0]));
        }
        return result.ToArray();
    }

    public void ValidateBenchmark(DateTime asOf, int requiredSessions) => MarketWindow(asOf, requiredSessions);

    public ProfitModelDefinition Bind(ProfitModelDefinition model, DateTime? predictionSession = null) =>
        model with { FeatureBuilder = new DatedProfitFeatureBuilder(model.FeatureBuilder, model.Lookback, this, predictionSession) };

    internal static DailyBar Copy(DailyBar bar) => new()
    {
        Date = bar.Date.Date, Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close, Volume = bar.Volume
    };
}

/// <summary>One calculation path for training and prediction; preserves the active feature order and formulas.</summary>
public sealed class DatedProfitFeatureBuilder : IFeatureBuilder
{
    private readonly IFeatureBuilder original;
    private readonly ProfitFeatureInputs inputs;
    private readonly int lookback;
    private readonly DateTime? predictionSession;
    public string Name => original.Name + ".XiuDatedV1";

    internal DatedProfitFeatureBuilder(IFeatureBuilder original, int lookback, ProfitFeatureInputs inputs, DateTime? predictionSession)
    {
        // Supporting a new feature family requires an explicit contract, never
        // falling back to a builder whose market dependencies are unknown.
        if (original is not (EnhancedFeatureBuilder or AtrVolatilityBreakoutFeatureBuilder))
            throw new ArgumentException("The dated V1 contract supports only the current active profit feature families.", nameof(original));
        if (lookback < 21) throw new ArgumentOutOfRangeException(nameof(lookback));
        this.original = original;
        this.lookback = lookback;
        this.inputs = inputs;
        this.predictionSession = predictionSession?.Date;
    }

    public int FeatureCount(int windowSize) => original.FeatureCount(windowSize);

    internal static HashSet<DateTime> DuplicateSessions(IReadOnlyList<DailyBar> history) =>
        history.GroupBy(bar => bar.Date.Date).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();

    public float[] Build(IReadOnlyList<DailyBar> windowBars) => Build(windowBars, DuplicateSessions(windowBars));

    internal float[] Build(IReadOnlyList<DailyBar> windowBars, ISet<DateTime> duplicateStockSessions)
    {
        DateTime asOf = predictionSession ?? (windowBars.Count == 0 ? DateTime.MinValue : windowBars[^1].Date.Date);
        // Shared faults are distinguished from symbol exclusions by the caller.
        var market = inputs.MarketWindow(asOf, lookback);
        if (windowBars.Count != lookback)
            throw new ProfitFeatureInputException("StockFeatureHistoryMissing", false, asOf);
        var stock = windowBars.Select(ProfitFeatureInputs.Copy).ToArray();
        for (int i = 0; i < stock.Length; i++)
        {
            if (stock[i].Date != market[i].Date)
                throw new ProfitFeatureInputException("StockFeatureSessionMismatch", false, market[i].Date);
            var bar = stock[i];
            if (duplicateStockSessions.Contains(bar.Date))
                throw new ProfitFeatureInputException("StockFeatureBarDuplicate", false, bar.Date);
            if (!float.IsFinite(bar.Open) || !float.IsFinite(bar.High) || !float.IsFinite(bar.Low) ||
                !float.IsFinite(bar.Close) || bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0 ||
                bar.Low > System.Math.Min(bar.Open, bar.Close) || bar.High < System.Math.Max(bar.Open, bar.Close) || bar.Volume < 0)
                throw new ProfitFeatureInputException("StockFeatureBarInvalid", false, bar.Date);
        }
        // A fresh builder gets only the exact aligned window. No training or
        // inference instance can contaminate another instance's market input.
        IFeatureBuilder builder = original is EnhancedFeatureBuilder
            ? new EnhancedFeatureBuilder { MarketBars = market }
            : new AtrVolatilityBreakoutFeatureBuilder();
        float[] features = builder.Build(stock);
        if (features.Length != FeatureCount(lookback) || features.Any(value => !float.IsFinite(value)))
            throw new ProfitFeatureInputException("StockFeatureVectorInvalid", false, asOf);
        return features;
    }
}
