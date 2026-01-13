using Microsoft.ML.Data;

namespace Core.ML.Engine.Training.Classifiers
{
    public class PatternWindow
    {
        [VectorType(30)]
        public float[] WindowPrices { get; set; } = [];

        public bool HasHeadAndShoulders { get; set; }
    }
}