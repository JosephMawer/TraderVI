namespace Core.ML.Engine.Profit;

public record ProfitTrainingResult(
    bool Success,
    int SymbolsUsed,
    int TrainWindows,
    int TestWindows,
    double PrimaryMetric,
    double SecondaryMetric);