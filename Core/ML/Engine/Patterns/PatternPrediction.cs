namespace Core.ML.Engine.Patterns;

/// <summary>
/// ML.NET prediction output for pattern classification.
/// </summary>
public class PatternPrediction
{
    public bool PredictedLabel { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}