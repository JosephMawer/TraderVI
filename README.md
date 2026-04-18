# TraderVI

TraderVI is a TSX-focused daily-stock trading system that combines market data collection, ML model training, and runtime inference to recommend trades.

- **Universe:** TSX constituents (from local DB)
- **Mode (current):** advisory-only (prints recommendations; manual execution)
- **Mode (future):** automated execution via Wealthsimple API (`Core\WSTrade`)
- **Core strategy:** aggressive single-position rotation using multiple ML “hints” (direction, breakout probability, volatility regime), with regression used as a ranking hint

## System Overview

High-level pipeline:

1. **Hermes** (`Hermes`) downloads historical daily OHLCV bars and stores them in SQL.
2. **Hercules** (`ML.Train`) trains ML.NET models and registers them in `[dbo].[ModelRegistry]`.
3. **Delphi** (`Delphi`) loads enabled models from the registry and evaluates the TSX universe to produce:
   - best pick
   - direction/confidence + event probabilities (breakout/volatility)
   - ranking hints (including regression)
   - recommended position sizing

## Pattern Models vs Profit Models (Key Concepts)

- **Pattern models** (Pattern taxonomy):
  - Predict whether a technical pattern is present in the latest window (trend, RSI states, MA crossover, etc.)
  - Useful for interpretability and confirmation

- **Profit models** (Forward outcome prediction):
  - Direction/classification models predict **Buy/Hold/Sell** as a primary trading signal + confidence
  - Event models predict things like:
    - breakout probability
    - volatility expansion probability
  - Regression (expected return) remains supported but is treated as a **ranking hint**, not the sole selection signal

More details: see `docs/models.md`.

## Documentation

- `docs/running.md` — How to run Hermes/Hercules/Delphi
- `docs/architecture.md` — Component architecture and data/model flow
- `docs/models.md` — Pattern vs profit models, horizons/thresholds, semantics
- `docs/training-metrics.md` — How to interpret Hercules training output
- `docs/strategy.md` — Single-position rotation, rotation threshold, risk rules
- `docs/glossary.md` — Terms used throughout the system

## Engineering Rules (Copilot)

Engineering/iteration constraints live in:
- `.github/copilot-instructions.md`