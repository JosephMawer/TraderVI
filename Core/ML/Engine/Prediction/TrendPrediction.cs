using Core.ML.Engine.Training.Classifiers;
using Microsoft.ML;

namespace Core.ML.Engine.Prediction
{
    public static class TrendPrediction
    {
        private static readonly MLContext mlContext = new MLContext();

        public static PatternPredictionResult PredictTrend30(PatternWindow window, string modelZipPath)
        {
            var model = mlContext.Model.Load(modelZipPath, out _);
            var engine = mlContext.Model.CreatePredictionEngine<PatternWindow, PatternPredictionResult>(model);
            return engine.Predict(window);
        }
    }
}