# Models (Pattern vs Profit)

This repo trains two distinct families of models, aligned to the trading strategy.

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
Pattern presence is not always symmetric.

Example:
- `RsiOversold` being false should typically mean **Hold**, not Sell.

This is handled by:
- `SignalSemantics` on `PatternDefinition`
- mapping in `UnifiedPatternSignalModel`

Pattern models usually serve as:
- explanation/context (“why is this stock a candidate?”)
- confirmation filters
- potential future features for profit models

## Profit Models

### What they predict
Profit models answer:

> “What is likely to happen after the lookback window?”

They are forward-outcome models and are the primary drivers for ranking and rotation decisions.

### Labeling: horizons and thresholds
Profit labels use:
- `HorizonBars` (e.g., 5 or 10)
- threshold bands to generate 3-way labels (Buy/Hold/Sell)

Implementation:
- `ILabeler` (e.g., `ForwardReturnLabeler`)

### Two profit model kinds
1) **Regression**
- Task examples: `ExpectedReturn5`, `ExpectedReturn10`
- Output: predicted forward return (ExpectedReturn)
- Used for: ranking, rotation comparisons, (later) sizing

2) **3-way classification**
- Task examples: `Direction5`, `Direction10`
- Output: Buy/Hold/Sell + confidence (probability-like)
- Used for: confirmation and reducing false positives

### Training / runtime
- Registry: `ProfitModelDefinition` and `ProfitModelRegistry`
- Trainer: `UnifiedProfitTrainer`
- Runtime: `UnifiedProfitSignalModel`

### Outlier handling (recommended)
Forward returns can include extreme tail events (splits, crashes, spikes).
For stabilization, regression labels can optionally be clipped (winsorized), e.g. ±25%, while leaving 3-way thresholding intact.

## Why profit models drive selection (strategy-aligned)
Given the system’s current strategy (aggressive single-position rotation), the decision engine needs:
- a comparable score across the universe (**ExpectedReturn**) for ranking and rotation thresholds
- a confirmation score (**Confidence**) to avoid churn and low-quality trades

Pattern models support interpretability and confirmation but do not replace outcome prediction.