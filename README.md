# TraderVI

TraderVI is a TSX-focused daily-stock trading system that combines market data collection, ML model training, and runtime inference to recommend trades.

- **Universe:** TSX constituents (from local DB)
- **Mode (current):** advisory-only (prints recommendations; manual execution)
- **Mode (future):** automated execution via Wealthsimple API (`Core\WSTrade`)
- **Core strategy:** aggressive single-position rotation based primarily on predicted profit (“expected return”) plus confirmation signals

## System Overview

High-level pipeline:

1. **Hermes** (`Hermes`) downloads historical daily OHLCV bars and stores them in SQL.
2. **Hercules** (`ML.Train`) trains ML.NET models and registers them in `[dbo].[ModelRegistry]`.
3. **Delphi** (`Delphi`) loads enabled models from the registry and evaluates the TSX universe to produce:
   - best pick
   - expected return + confidence
   - recommended position sizing

## Pattern Models vs Profit Models (Key Concepts)

- **Pattern models** (Pattern taxonomy):
  - Predict whether a technical pattern is present in the latest window (trend, RSI states, MA crossover, etc.)
  - Useful for interpretability and confirmation

- **Profit models** (Forward outcome prediction):
  - Regression predicts **expected forward return** over a horizon (e.g., 5d/10d)
  - 3-way classifier predicts **Buy/Hold/Sell** as confirmation
  - These outputs drive ranking and trade selection for the rotation strategy

More details: see `docs/models.md`.

## Quickstart (End-to-End)

See `docs/running.md` for full details.

Typical flow:
1. Run Hermes to backfill/update data
2. Run Hercules to train and register models
3. Run Delphi to get today’s best pick

## Documentation

- `docs/running.md` — How to run Hermes/Hercules/Delphi
- `docs/architecture.md` — Component architecture and data/model flow
- `docs/models.md` — Pattern vs profit models, horizons/thresholds, semantics
- `docs/strategy.md` — Single-position rotation, rotation threshold, risk rules

## Engineering Rules (Copilot)
Engineering/iteration constraints live in:
- `.github/copilot-instructions.md`