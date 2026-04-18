# Models (Pattern vs Profit)

This repo trains two distinct families of models aligned to the trading strategy.

Glossary: `docs/glossary.md`

## Pattern Models

### What they predict
Pattern models answer:

> “Does the latest lookback window look like Pattern X?”

Examples:
- `Trend10`, `Trend30`
- `MaCrossover`
- `RsiOversold`, `RsiOverbought`

### How they are labeled
Pattern labels are derived from **rule-based detectors**:
- `IPatternDetector.Detect(windowBars)` produces a boolean label for each window.

### How they are trained / used
- Registry: `PatternDefinition` and `PatternRegistry`
- Trainer: `UnifiedPatternTrainer`
- Runtime: `UnifiedPatternSignalModel`

### Semantics (important)
Pattern presence is not always symmetric. Example:
- `RsiOversold` being false should typically mean **Hold**, not Sell.

This is handled by:
- `SignalSemantics` on `PatternDefinition`
- mapping in `UnifiedPatternSignalModel`

Pattern models are typically used for:
- explanation/context (“why is this stock a candidate?”)
- confirmation filters
- features that may later be fed into profit/outcome models

## Profit Models (Forward Outcome Models)

Profit models answer forward-looking questions:

> “What is likely to happen after the lookback window?”

Model types:

1) Direction models (3-way)
- Predict Buy/Hold/Sell + confidence (primary decision input)

2) Event probability models (binary)
- Predict probability of discrete events within the horizon:
  - breakout vs prior-high
  - breakout vs ATR multiple
  - volatility expansion

3) Expected return regression (ranking hint)
- Still supported, but treated as a “ranking hint” rather than the single source of truth.

### Labeling
Profit labels are generated from future bars using `ILabeler`.
Unlike pattern detectors (which label the current window), labelers use:
- `windowBars` (inputs/features)
- `futureBars` (outcomes/targets)

### Outlier handling (recommended)
Returns can include extreme tail events (splits, bad data, spikes).
For stability:
- regression labels may be clipped (e.g., ±25%)
- samples with absurd forward returns may be filtered out (e.g., > ±50% is skipped)

## Training output metrics
How to read Hercules training output (RMSE/R², Spearman, confusion matrix, event precision/recall):
- `docs/training-metrics.md`