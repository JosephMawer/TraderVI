using Core.ML.Engine.Training.Classifiers;
using Microsoft.ML;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.ML.Engine.Prediction
{
    public static class Regression
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
