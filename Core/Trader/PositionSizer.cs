using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

public enum AllocationStrategy
{
    Portfolio,
    SinglePositionAllIn
}

/// <summary>
/// Calculates position sizes based on expected return and confidence.
/// Supports both diversified (portfolio) and aggressive single-position rotation.
/// </summary>
public class PositionSizer
{
    public decimal AvailableCapital { get; set; }

    public AllocationStrategy Strategy { get; set; } = AllocationStrategy.SinglePositionAllIn;

    /// <summary>
    /// Keep a small cash buffer (e.g., fees/slippage). For all-in, set 0.
    /// </summary>
    public decimal ReserveCashPercent { get; set; } = 0.00m;

    /// <summary>
    /// Minimum dollars to allocate; if below => no trade.
    /// </summary>
    public decimal MinPositionSize { get; set; } = 25m;

    /// <summary>
    /// Minimum expected return to consider (e.g., 0.01 = 1%).
    /// Note: expected return is now treated as a "ranking hint", so this gate
    /// can optionally be satisfied by strong event probabilities (see below).
    /// </summary>
    public double MinExpectedReturn { get; set; } = 0.01;

    /// <summary>
    /// Minimum confidence to consider (0..1).
    /// </summary>
    public double MinConfidence { get; set; } = 0.55;

    /// <summary>
    /// Optional extra gate: require both high expected return and confidence.
    /// </summary>
    public bool RequireBothSignals { get; set; } = true;

    /// <summary>
    /// Allow a strong breakout probability ("hint") to qualify a trade even when
    /// expected return regression is conservative/under-calibrated.
    /// </summary>
    public double MinBreakoutPriorHighProb { get; set; } = 0.60;

    public PositionSizer(decimal availableCapital)
    {
        AvailableCapital = availableCapital;
    }

    public PositionSizeResult SizeSingleBestPick(RankedPick pick)
    {
        if (pick.Direction != TradeDirection.Buy)
        {
            return new PositionSizeResult(
                SuggestedSize: 0,
                AllocationPercent: 0,
                Reason: "Top pick is not a Buy");
        }

        double breakoutPriorHighProb = pick.Signals
            .FirstOrDefault(s => string.Equals(s.Name, "BreakoutPriorHigh10", StringComparison.OrdinalIgnoreCase))
            ?.Score ?? 0;

        if (RequireBothSignals)
        {
            // Confidence remains a hard gate.
            if (pick.Confidence < MinConfidence)
            {
                return new PositionSizeResult(
                    SuggestedSize: 0,
                    AllocationPercent: 0,
                    Reason: $"Confidence {pick.Confidence:P1} below minimum {MinConfidence:P1}");
            }

            // Expected return is treated as a ranking hint; allow a strong breakout probability to qualify the trade.
            bool passesReturnOrHint =
                pick.ExpectedReturn >= MinExpectedReturn ||
                breakoutPriorHighProb >= MinBreakoutPriorHighProb;

            if (!passesReturnOrHint)
            {
                return new PositionSizeResult(
                    SuggestedSize: 0,
                    AllocationPercent: 0,
                    Reason: $"Expected return {pick.ExpectedReturn:P2} < {MinExpectedReturn:P2} and BreakoutPriorHigh10 {breakoutPriorHighProb:P1} < {MinBreakoutPriorHighProb:P1}");
            }
        }

        var deployable = AvailableCapital * (1 - ReserveCashPercent);

        if (deployable < MinPositionSize)
        {
            return new PositionSizeResult(
                SuggestedSize: 0,
                AllocationPercent: 0,
                Reason: $"Deployable capital ${deployable:N2} below minimum ${MinPositionSize:N2}");
        }

        return new PositionSizeResult(
            SuggestedSize: System.Math.Round(deployable, 2),
            AllocationPercent: (double)(deployable / AvailableCapital),
            Reason: $"Single-position all-in (reserve={ReserveCashPercent:P0})");
    }

    public List<SizedPick> SizePortfolio(IReadOnlyList<RankedPick> picks, int maxPositions = 5)
    {
        // Keep your original diversified behavior if you still want it later.
        var sized = new List<SizedPick>();
        decimal remainingCapital = AvailableCapital;

        foreach (var pick in picks.Take(maxPositions))
        {
            if (remainingCapital <= MinPositionSize)
                break;

            if (pick.Direction != TradeDirection.Buy)
                continue;

            if (pick.ExpectedReturn < MinExpectedReturn)
                continue;

            if (pick.Confidence < MinConfidence)
                continue;

            // Simple proportional sizing for portfolio mode (can be replaced later)
            var allocationPercent = System.Math.Min(0.20, pick.ExpectedReturn * pick.Confidence);
            var dollars = remainingCapital * (decimal)allocationPercent;

            if (dollars < MinPositionSize)
                continue;

            dollars = System.Math.Round(dollars, 2);
            remainingCapital -= dollars;

            sized.Add(new SizedPick(
                Symbol: pick.Symbol,
                Direction: pick.Direction,
                ExpectedReturn: pick.ExpectedReturn,
                Confidence: pick.Confidence,
                PositionSize: dollars,
                AllocationPercent: allocationPercent,
                Signals: pick.Signals));
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