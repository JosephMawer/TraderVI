using System;
using Microsoft.ML.Data;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Unified ML input window for profit prediction models.
/// </summary>
public class ProfitWindow
{
    [VectorType]
    public float[] Features { get; set; } = [];

    public float ForwardReturn { get; set; }

    /// <summary>
    /// 3-way label encoded as: Sell=0, Hold=1, Buy=2 (for classification training).
    /// </summary>
    public uint ThreeWayLabel { get; set; }

    /// <summary>
    /// Binary label for event-based models (e.g., breakout, volatility expansion).
    /// </summary>
    public bool IsEvent { get; set; }

    /// <summary>
    /// Calendar date of the last bar in the feature window. Used by the trainer
    /// to perform a global time-based train/test split (never referenced inside the pipeline).
    /// </summary>
    public DateTime Date { get; set; }
}

/// <summary>
/// Regression prediction output.
/// </summary>
public class RegressionPrediction
{
    public float Score { get; set; }
}

/// <summary>
/// 3-way classification prediction output.
/// </summary>
public class ThreeWayPrediction
{
    public uint PredictedLabel { get; set; }

    [VectorType(3)]
    public float[] Score { get; set; } = [];
}

/// <summary>
/// Binary classification prediction output.
/// </summary>
public class BinaryPrediction
{
    public bool PredictedLabel { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}