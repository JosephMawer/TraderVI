using System;
using System.Collections.Generic;
namespace Core.ML.Classifiers
{

    public enum ClassificationPattern
    {
        HeadAndShoulders
    }

    public record ClassificationTrainerOptions(List<DailyBar> DailyBars, Dictionary<DateTime, bool> Labels, int LookBackWindow);


    // todo:
    // If you want, next I can expand the classifier features to include volume + indicators(e.g., volume spike on the breakdown),
    // not just raw prices, so the model’s idea of “head-and-shoulders” is closer to how a trader would see it.

    public static class ClassificationTrainerFactory
    {
        /// <summary>
        /// Kinda like a factory pattern for training different classifications
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="options"></param>
        public static PatternPredictionResult Train(ClassificationPattern pattern, ClassificationTrainerOptions options)
        {
            return pattern switch
            {
                ClassificationPattern.HeadAndShoulders => HeadAndShoulders.TrainClassifier(options.DailyBars,
                                                                                            options.Labels,
                                                                                            options.LookBackWindow),
                _ => throw new Exception("No matching pattern found to train"),
            };
        }
    }
}
