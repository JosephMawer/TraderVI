using Core.ML.Engine.Patterns.Detectors;
using Core.ML.Engine.Patterns.Features;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns;

public static class PatternRegistry
{
    public static IReadOnlyList<PatternDefinition> All { get; } =
    [
        new PatternDefinition(
            TaskType: "Trend10",
            Lookback: 10,
            Detector: new TrendUpDetector(10),
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Category: "Trend",
            Semantics: SignalSemantics.BullishWhenTrue),

        new PatternDefinition(
            TaskType: "Trend30",
            Lookback: 30,
            Detector: new TrendUpDetector(30),
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Category: "Trend",
            Semantics: SignalSemantics.BullishWhenTrue),

        new PatternDefinition(
            TaskType: "MaCrossover",
            Lookback: 35,
            Detector: new MaCrossoverDetector(shortPeriod: 10, longPeriod: 30),
            FeatureBuilder: new PriceWithMaFeatureBuilder(shortMaPeriod: 10, longMaPeriod: 30),
            Category: "Trend",
            Semantics: SignalSemantics.BullishWhenTrue),

        new PatternDefinition(
            TaskType: "RsiOversold",
            Lookback: 20,
            Detector: new RsiOversoldDetector(lookback: 20, rsiPeriod: 14, oversoldThreshold: 30),
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Category: "Momentum",
            Semantics: SignalSemantics.BullishWhenTrue),

        new PatternDefinition(
            TaskType: "RsiOverbought",
            Lookback: 20,
            Detector: new RsiOverboughtDetector(lookback: 20, rsiPeriod: 14, overboughtThreshold: 70),
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Category: "Momentum",
            Semantics: SignalSemantics.BearishWhenTrue),
    ];

    public static PatternDefinition? GetByTaskType(string taskType)
        => All.FirstOrDefault(p => p.TaskType == taskType);

    public static IEnumerable<PatternDefinition> GetByCategory(string category)
        => All.Where(p => p.Category == category);
}