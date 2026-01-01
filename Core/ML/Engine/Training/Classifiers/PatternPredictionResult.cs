using Microsoft.ML.Data;

namespace Core.ML.Engine.Training.Classifiers
{

    public class PatternPredictionResult
    {
        [ColumnName("PredictedLabel")]
        public bool PredictedLabel { get; set; }

        public float Probability { get; set; }
        public float Score { get; set; }
    }
}
