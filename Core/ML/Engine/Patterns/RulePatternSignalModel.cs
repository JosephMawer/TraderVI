// NOTE: namespace predates A2 rule-based pattern refactor.
// Patterns are no longer ML models — see docs/design-rules.md ("Rule-Based Pattern Signals").
// A future cleanup may move these types to Core.Indicators.Patterns or Core.Trader.Patterns.

using Core.Trader;
using System.Collections.Generic;
using System.Linq;

namespace Core.ML.Engine.Patterns;

/// <summary>
/// Rule-based pattern presence signal. Wraps an <see cref="IPatternDetector"/> so it
/// participates in the <see cref="TradeDecisionEngine"/> signal pipeline without going
/// through ML.NET.
///
/// Score is binary (1.0 when the pattern is present on the most recent window, 0.0 otherwise).
/// The trade-direction hint is derived from <see cref="SignalSemantics"/>.
/// </summary>
public class RulePatternSignalModel : IStockSignalModel
{
    private readonly PatternDefinition _pattern;

    public string Name => _pattern.TaskType;

    public RulePatternSignalModel(PatternDefinition pattern)
    {
        _pattern = pattern;
    }

    public SignalResult Evaluate(IReadOnlyList<DailyBar> history)
    {
        int lookback = _pattern.Lookback;

        if (history.Count < lookback)
        {
            return new SignalResult(
                Name,
                Score: 0,
                Hint: TradeDirection.Hold,
                Notes: $"Insufficient history (need {lookback} bars, got {history.Count})");
        }

        var windowBars = history
            .Skip(history.Count - lookback)
            .Take(lookback)
            .ToList();

        bool present = _pattern.Detector.Detect(windowBars);
        double score = present ? 1.0 : 0.0;

        var hint = GetHint(present);

        return new SignalResult(
            Name,
            Score: score,
            Hint: hint,
            Notes: $"Pattern={_pattern.Detector.PatternName}, Present={present}, Semantics={_pattern.Semantics} (rule-based)");
    }

    private TradeDirection GetHint(bool present)
    {
        if (!present)
            return TradeDirection.Hold;

        return _pattern.Semantics switch
        {
            SignalSemantics.BullishWhenTrue => TradeDirection.Buy,
            SignalSemantics.BearishWhenTrue => TradeDirection.Sell,
            SignalSemantics.BullishBearishSymmetric => TradeDirection.Buy, // symmetric detectors flip via a sibling bearish detector
            _ => TradeDirection.Hold
        };
    }
}