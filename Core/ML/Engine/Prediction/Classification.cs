using Core.ML.Engine.Training.Classifiers;
using Microsoft.ML;

namespace Core.ML.Engine.Prediction
{
    /// <summary>
    /// Note: PredictionEngine is fine for low-volume / single-threaded prediction.
    /// For high-throughput / ASP.NET, you’d use PredictionEnginePool, but concept is the same.
    /// </summary>
    public static class Classification
    {
        private static readonly MLContext mlContext = new MLContext();

        private static PredictionEngine<PatternWindow, PatternPredictionResult> Engine; 

        private static PredictionEngine<PatternWindow, PatternPredictionResult> CreateEngine(string file)
        {
            var model = mlContext.Model.Load(file, out DataViewSchema schema);
            return mlContext.Model.CreatePredictionEngine<PatternWindow, PatternPredictionResult>(model);
        }

        public static PatternPredictionResult Predict(PatternWindow window, ClassificationPattern pattern)
        {
            var zipFile = GetFileFromClassificationModel(pattern);
            Engine = CreateEngine(zipFile);
            return Engine.Predict(window);
        }


        private static string GetFileFromClassificationModel(ClassificationPattern pattern)
        {
            return pattern switch
            {
                ClassificationPattern.HeadAndShoulders => "hs_classifier.zip",
                _ => "",
            };
        }

    }
}
