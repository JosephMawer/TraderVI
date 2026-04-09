# Copilot Instructions (TraderVI)

## System Goal

TraderVI is a daily-stock trading system for **short-term aggressive momentum rotation** on the TSX, with strict risk controls.

### Core Philosophy
- **Ensemble confidence**: Use multiple diverse signals (not redundant models) to increase conviction.
- **Aggressive rotation**: Continuously seek the best opportunity; don't hold losers.
- **Capital preservation first**: Stop-loss rules override all model predictions.
- **Iterative improvement**: Track experiments, version strategies, measure everything.

## Strategy Logic
- Keep **BreakoutEnhanced** as a setup filter.
- Determine direction using **BinaryUp10** and **BinaryDown10** with **DirectionEdge = P(up) - P(down)**.
- Optionally add **DirUp5** as a smoother "base drift" signal: **Edge = 0.3×P(dir) + 0.5×P(up) - 0.5×P(down)**.
- Apply down-probability veto for longs.
- Implement a market regime filter requiring benchmark (XIU) uptrend/positive return.
- Rank candidates by **DirectionEdge** then composite.
- Maintain the **Advance-Decline Line** as a rule-based market breadth/regime indicator, not a direct ML feature, unless explicitly revisited.

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

## Decision Thresholds
- Document decision thresholds as initial defaults intended to be tuned based on Hercules training outputs (AUC, optimal threshold, decile lift), rather than fixed constants.
