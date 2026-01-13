using Microsoft.ML.Data;

namespace Core.ML.Engine.Training.Classifiers;

/// <summary>
/// Base interface for trend pattern windows of any lookback size.
/// </summary>
public interface ITrendPatternWindow
{
    float[] PriceNorm { get; set; }
    float[] VolumeNorm { get; set; }
    bool IsTrendUp { get; set; }
}

/// <summary>10-day trend window for short-term momentum classification.</summary>
public class TrendWindow10 : ITrendPatternWindow
{
    [VectorType(10)]
    public float[] PriceNorm { get; set; } = new float[10];

    [VectorType(10)]
    public float[] VolumeNorm { get; set; } = new float[10];

    public bool IsTrendUp { get; set; }
}

/// <summary>30-day trend window for medium-term trend classification.</summary>
public class TrendWindow30 : ITrendPatternWindow
{
    [VectorType(30)]
    public float[] PriceNorm { get; set; } = new float[30];

    [VectorType(30)]
    public float[] VolumeNorm { get; set; } = new float[30];

    public bool IsTrendUp { get; set; }
}