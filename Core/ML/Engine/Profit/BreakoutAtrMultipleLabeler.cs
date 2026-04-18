using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Profit;

/// <summary>
/// Event label: within the next HorizonBars, does price move upward by (ATR * multiple)
/// from the entry close (end of window)?
/// ATR is computed as classic True Range average over AtrPeriod using window bars.
/// </summary>
public sealed class BreakoutAtrMultipleLabeler : ILabeler
{
    public string Name { get; }
    public int HorizonBars { get; }

    private readonly int _atrPeriod;
    private readonly float _atrMultiple;

    public BreakoutAtrMultipleLabeler(
        int horizonBars = 10,
        int atrPeriod = 14,
        float atrMultiple = 1.5f)
    {
        HorizonBars = horizonBars;
        _atrPeriod = atrPeriod;
        _atrMultiple = atrMultiple;

        Name = $"BreakoutAtr_{horizonBars}d_atr{atrPeriod}_x{atrMultiple:0.##}";
    }

    public LabelResult ComputeLabel(IReadOnlyList<DailyBar> windowBars, IReadOnlyList<DailyBar> futureBars)
    {
        if (futureBars.Count < HorizonBars || windowBars.Count < 2)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        float entry = windowBars[^1].Close;
        if (entry <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        double atr = CalculateAtr(windowBars, _atrPeriod);
        if (atr <= 0)
            return new LabelResult(0, ThreeWayLabel.Hold, IsValid: false);

        double trigger = entry + (atr * _atrMultiple);

        bool hit = futureBars
            .Take(HorizonBars)
            .Any(b => b.High >= trigger);

        return new LabelResult(
            ForwardReturn: 0,
            ThreeWayClass: hit ? ThreeWayLabel.Buy : ThreeWayLabel.Hold,
            IsValid: true);
    }

    private static double CalculateAtr(IReadOnlyList<DailyBar> bars, int period)
    {
        int n = bars.Count;
        if (n < 2)
            return 0;

        int p = System.Math.Min(period, n - 1);
        int start = n - p;

        double sumTr = 0;
        for (int i = start; i < n; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];

            double prevClose = prev.Close <= 0 ? 1.0 : prev.Close;
            double high = cur.High;
            double low = cur.Low;

            double tr = System.Math.Max(high - low, System.Math.Max(System.Math.Abs(high - prevClose), System.Math.Abs(low - prevClose)));
            sumTr += tr;
        }

        return sumTr / p;
    }
}