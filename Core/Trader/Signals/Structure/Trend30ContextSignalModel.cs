using Core.ML;
using Core.ML.Engine.Prediction;
using Core.ML.Engine.Training.Classifiers;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.Signals.Structure;

public class Trend30ContextSignalModel : IStockSignalModel
{
    public string Name => "Trend30Context";

    private readonly string _modelZipPath;

    public Trend30ContextSignalModel(string modelZipPath)
    {
        _modelZipPath = modelZipPath;
    }

    public SignalResult Evaluate(IReadOnlyList<DailyBar> history)
    {
        const int lookback = 30;
        if (history.Count < lookback)
            return new SignalResult(Name, 0, TradeDirection.Hold, "Insufficient history (need 30 bars)");

        var windowBars = history.Skip(history.Count - lookback).Take(lookback).ToList();

        float firstClose = (float)windowBars[0].Close;
        if (firstClose == 0) firstClose = 1f;

        float avgVol = (float)windowBars.Average(b => (double)b.Volume);
        if (avgVol == 0) avgVol = 1f;

        var input = new TrendWindow30
        {
            PriceNorm = windowBars.Select(b => (float)b.Close / firstClose).ToArray(),
            VolumeNorm = windowBars.Select(b => (float)b.Volume / avgVol).ToArray()
        };

        var pred = TrendPrediction.PredictTrend30(input, _modelZipPath);

        var hint =
            pred.Probability > 0.60f ? TradeDirection.Buy :
            pred.Probability < 0.40f ? TradeDirection.Sell :
            TradeDirection.Hold;

        return new SignalResult(
            Name,
            pred.Probability,
            hint,
            $"P(TrendUp)={pred.Probability:0.###}, Score={pred.Score:0.###}");
    }
}