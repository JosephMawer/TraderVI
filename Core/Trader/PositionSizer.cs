using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

/// <summary>
/// Calculates position sizes based on expected return, confidence, and risk parameters.
/// </summary>
public class PositionSizer
{
    /// <summary>
    /// Total available capital for trading.
    /// </summary>
    public decimal AvailableCapital { get; set; }

    /// <summary>
    /// Maximum percentage of capital to allocate to a single position (default 20%).
    /// </summary>
    public decimal MaxPositionPercent { get; set; } = 0.20m;

    /// <summary>
    /// Minimum position size in dollars (below this, don't trade).
    /// </summary>
    public decimal MinPositionSize { get; set; } = 50m;

    /// <summary>
    /// Minimum expected return threshold to consider a trade (default 1%).
    /// </summary>
    public double MinExpectedReturn { get; set; } = 0.01;

    /// <summary>
    /// Minimum confidence threshold to consider a trade (default 50%).
    /// </summary>
    public double MinConfidence { get; set; } = 0.50;

    /// <summary>
    /// Scaling factor for Kelly-inspired sizing (default 0.5 = half-Kelly for safety).
    /// </summary>
    public double KellyFraction { get; set; } = 0.5;

    public PositionSizer(decimal availableCapital)
    {
        AvailableCapital = availableCapital;
    }

    /// <summary>
    /// Calculates suggested position size for a single trade.
    /// </summary>
    public PositionSizeResult Calculate(
        TradeDirection direction,
        double expectedReturn,
        double confidence)
    {
        // Only size Buy positions (can extend to Sell for shorting later)
        if (direction != TradeDirection.Buy)
        {
            return new PositionSizeResult(
                SuggestedSize: 0,
                AllocationPercent: 0,
                Reason: "Not a Buy signal");
        }

        // Check minimum thresholds
        if (expectedReturn < MinExpectedReturn)
        {
            return new PositionSizeResult(
                SuggestedSize: 0,
                AllocationPercent: 0,
                Reason: $"Expected return {expectedReturn:P2} below minimum {MinExpectedReturn:P2}");
        }

        if (confidence < MinConfidence)
        {
            return new PositionSizeResult(
                SuggestedSize: 0,
                AllocationPercent: 0,
                Reason: $"Confidence {confidence:P1} below minimum {MinConfidence:P1}");
        }

        // ─────────────────────────────────────────────────────────────
        // Kelly-inspired position sizing:
        // Base allocation = expectedReturn * confidence * kellyFraction
        // This naturally scales with both conviction and edge size
        // ─────────────────────────────────────────────────────────────

        double baseAllocation = expectedReturn * confidence * KellyFraction;

        // Clamp to max position percent
        double allocationPercent = System.Math.Min(baseAllocation, (double)MaxPositionPercent);

        // Calculate dollar amount
        decimal suggestedSize = AvailableCapital * (decimal)allocationPercent;

        // Apply minimum position size
        if (suggestedSize < MinPositionSize)
        {
            return new PositionSizeResult(
                SuggestedSize: 0,
                AllocationPercent: 0,
                Reason: $"Position size ${suggestedSize:F2} below minimum ${MinPositionSize:F2}");
        }

        // Round to nearest dollar
        suggestedSize = System.Math.Round(suggestedSize, 2);

        return new PositionSizeResult(
            SuggestedSize: suggestedSize,
            AllocationPercent: allocationPercent,
            Reason: $"Kelly-based sizing (f={KellyFraction}, er={expectedReturn:P2}, conf={confidence:P1})");
    }

    /// <summary>
    /// Sizes multiple picks, ensuring total allocation doesn't exceed available capital.
    /// </summary>
    public List<SizedPick> SizePortfolio(IReadOnlyList<RankedPick> picks, int maxPositions = 5)
    {
        var sized = new List<SizedPick>();
        decimal remainingCapital = AvailableCapital;

        foreach (var pick in picks.Take(maxPositions))
        {
            if (remainingCapital <= MinPositionSize)
                break;

            // Temporarily set available capital to remaining
            var originalCapital = AvailableCapital;
            AvailableCapital = remainingCapital;

            var sizeResult = Calculate(pick.Direction, pick.ExpectedReturn, pick.Confidence);

            AvailableCapital = originalCapital;

            if (sizeResult.SuggestedSize > 0)
            {
                sized.Add(new SizedPick(
                    Symbol: pick.Symbol,
                    Direction: pick.Direction,
                    ExpectedReturn: pick.ExpectedReturn,
                    Confidence: pick.Confidence,
                    PositionSize: sizeResult.SuggestedSize,
                    AllocationPercent: sizeResult.AllocationPercent,
                    Signals: pick.Signals));

                remainingCapital -= sizeResult.SuggestedSize;
            }
        }

        return sized;
    }
}

public record PositionSizeResult(
    decimal SuggestedSize,
    double AllocationPercent,
    string Reason);

public record SizedPick(
    string Symbol,
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    decimal PositionSize,
    double AllocationPercent,
    IReadOnlyList<SignalResult> Signals);