namespace Core.ML.Engine.Profit;

public record ProfitTrainingResult(
    bool Success,
    int SymbolsUsed,
    int TrainWindows,
    int TestWindows,
    double PrimaryMetric,
    double SecondaryMetric,
    double? OptimalThreshold = null,
    double? PrecisionAtOptimal = null,
    double? RecallAtOptimal = null,
    double? F1AtOptimal = null)
{
    public System.DateTime? TrainingWindowFrom { get; init; }
    public System.DateTime? TrainingWindowTo { get; init; }
    public System.DateTime? TestWindowFrom { get; init; }
    public System.DateTime? TestWindowTo { get; init; }
    public System.Collections.Generic.IReadOnlyDictionary<string, int> InputExclusions { get; init; }
        = new System.Collections.Generic.Dictionary<string, int>();
}
