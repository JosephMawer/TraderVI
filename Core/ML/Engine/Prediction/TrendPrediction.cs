using Core.ML.Engine.Training.Classifiers;
using Microsoft.ML;

namespace Core.ML.Engine.Prediction
{
    public static class TrendPrediction
    {
        private static readonly MLContext mlContext = new MLContext();

        public static PatternPredictionResult PredictTrend30(TrendWindow30 window, string modelZipPath)
        {
            var model = mlContext.Model.Load(modelZipPath, out _);
            var engine = mlContext.Model.CreatePredictionEngine<TrendWindow30, PatternPredictionResult>(model);
            return engine.Predict(window);
        }

        public static PatternPredictionResult PredictTrend10(TrendWindow10 window, string modelZipPath)
        {
            var model = mlContext.Model.Load(modelZipPath, out _);
            var engine = mlContext.Model.CreatePredictionEngine<TrendWindow10, PatternPredictionResult>(model);
            return engine.Predict(window);
        }
    }
}