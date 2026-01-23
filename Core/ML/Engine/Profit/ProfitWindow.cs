using Microsoft.ML.Data;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Unified ML input window for profit prediction models.
/// </summary>
public class ProfitWindow
{
    /// <summary>
    /// Feature vector built by IFeatureBuilder.
    /// </summary>
    [VectorType]
    public float[] Features { get; set; } = [];

    /// <summary>
    /// Forward return (for regression training).
    /// </summary>
    public float ForwardReturn { get; set; }

    /// <summary>
    /// 3-way label encoded as: -1=Sell, 0=Hold, 1=Buy (for classification training).
    /// </summary>
    public uint ThreeWayLabel { get; set; }
}

/// <summary>
/// Regression prediction output.
/// </summary>
public class RegressionPrediction
{
    public float Score { get; set; } // predicted return
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