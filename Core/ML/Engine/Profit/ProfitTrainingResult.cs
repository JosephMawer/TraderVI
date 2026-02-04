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
    double? F1AtOptimal = null);