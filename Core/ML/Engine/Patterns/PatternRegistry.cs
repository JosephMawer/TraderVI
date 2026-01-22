using Core.ML.Engine.Patterns.Detectors;
using Core.ML.Engine.Patterns.Features;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns;

/// <summary>
/// Central registry of all trainable patterns.
/// Add new patterns here to include them in training and runtime prediction.
/// </summary>
public static class PatternRegistry
{
    public static IReadOnlyList<PatternDefinition> All { get; } =
    [
        // ═══════════════════════════════════════════════════════════
        // Trend Patterns
        // ═══════════════════════════════════════════════════════════
        new PatternDefinition(
            TaskType: "Trend10",
            Lookback: 10,
            Detector: new TrendUpDetector(10),
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Category: "Trend"),

        new PatternDefinition(
            TaskType: "Trend30",
            Lookback: 30,
            Detector: new TrendUpDetector(30),
            FeatureBuilder: new PriceVolumeFeatureBuilder(),
            Category: "Trend"),

        // ═══════════════════════════════════════════════════════════
        // Reversal Patterns (add these later)
        // ═══════════════════════════════════════════════════════════
        // new PatternDefinition("HeadAndShoulders", 30, new HeadAndShouldersDetector(), new OhlcFeatureBuilder(), "Reversal"),
        // new PatternDefinition("DoubleTop", 20, new DoubleTopDetector(), new OhlcFeatureBuilder(), "Reversal"),

        // ═══════════════════════════════════════════════════════════
        // Continuation Patterns (add these later)
        // ═══════════════════════════════════════════════════════════
        // new PatternDefinition("Flag", 15, new FlagDetector(), new PriceVolumeFeatureBuilder(), "Continuation"),
        // new PatternDefinition("Triangle", 20, new TriangleDetector(), new PriceVolumeFeatureBuilder(), "Continuation"),
    ];

    public static PatternDefinition? GetByTaskType(string taskType)
        => All.FirstOrDefault(p => p.TaskType == taskType);

    public static IEnumerable<PatternDefinition> GetByCategory(string category)
        => All.Where(p => p.Category == category);
}