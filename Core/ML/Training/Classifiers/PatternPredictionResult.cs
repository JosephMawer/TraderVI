using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.ML.Classifiers
{

    //public class PatternWindow
    //{
    //    //[VectorType(lookback)] // lookback is a constant like 30
    //    public float[] WindowPrices { get; set; } = Array.Empty<float>();

    //    // Label: 1 = head-and-shoulders present, 0 = not present
    //    public bool HasHeadAndShoulders { get; set; }
    //}


  
    public class PatternWindow
    {
        //[VectorType(lookback)] // lookback is a constant like 30
        public float[] WindowPrices { get; set; } = Array.Empty<float>();

        // 30-day window of normalized prices
        [VectorType(30)]   //Change 30 to whatever lookback you want.
        public float[] PriceNorm { get; set; } = Array.Empty<float>();

        // Optional: 30-day window of normalized volume
        [VectorType(30)]   //Change 30 to whatever lookback you want.
        public float[] VolumeNorm { get; set; } = Array.Empty<float>();

        // Label: 1 = head-and-shoulders present in this window (according to you)
        public bool HasHeadAndShoulders { get; set; }
    }

    public class PatternPredictionResult
    {
        [ColumnName("PredictedLabel")]
        public bool PredictedLabel { get; set; }

        public float Probability { get; set; }
        public float Score { get; set; }
    }
}
