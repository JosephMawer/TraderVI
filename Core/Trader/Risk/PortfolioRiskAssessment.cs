#nullable enable
using System.Collections.Immutable;

namespace Core.Trader.Risk;

public enum PortfolioRiskProfile { EngineDaily, AiContinuousRounds }

/// <summary>
/// Common output for the future shared risk boundary, not permission to submit an order.
/// Execution must still check account, cash, exposure, position stops and current authority.
/// The multiplier caps combined per-stock allocation, including holdings and pending buys.
/// </summary>
public sealed record PortfolioRiskAssessment(
    PortfolioRiskProfile Profile,
    decimal NewBuyAllowanceMultiplier,
    bool RequiresManagedLiquidation,
    ImmutableArray<string> NewBuyBlocks);
