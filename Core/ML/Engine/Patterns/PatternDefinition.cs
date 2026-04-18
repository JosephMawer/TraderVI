namespace Core.ML.Engine.Patterns;

/// <summary>
/// Defines a trainable pattern by combining:
/// - A detector (for labeling during training)
/// - A feature builder (for ML input)
/// - Configuration (lookback, task type)
/// </summary>
public record PatternDefinition(
    string TaskType,
    int Lookback,
    IPatternDetector Detector,
    IFeatureBuilder FeatureBuilder,
    string Category = "Uncategorized",
    SignalSemantics Semantics = SignalSemantics.BullishWhenTrue);