# Copilot Instructions (TraderVI)

## System Goal

TraderVI is a daily-stock trading system for **short-term aggressive momentum rotation** on the TSX, with strict risk controls.

### Core Philosophy
- **Ensemble confidence**: Use multiple diverse signals (not redundant models) to increase conviction.
- **Aggressive rotation**: Continuously seek the best opportunity; don't hold losers.
- **Capital preservation first**: Stop-loss rules override all model predictions.
- **Iterative improvement**: Track experiments, version strategies, measure everything.

## Architecture

### Programs

| Program | Purpose | Frequency |
|---------|---------|-----------|
| **Hermes** | Import OHLCV data from TMX | Daily (after market close) |
| **Hercules** | Train/retrain ML models | Weekly or on-demand |
| **Delphi** | Evaluate universe, rank picks, output recommendations | Daily (pre-market) |
| **Sentinel** (planned) | Intraday monitoring, stop-loss execution, rotation triggers | Continuous (market hours) |
| **TraderVI** (later) | Automated order execution via Wealthsimple API | Event-driven |

### Data Flow
