using Core.ML;
using Core.ML.Engine.Profit;
using Core.Indicators;
using Core.Trader.Gates;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

public enum TradeDirection
{
    Buy,
    Sell,
    Hold
}

public record SignalResult(
    string Name,
    double Score,
    TradeDirection? Hint,
    string? Notes = null
);

public interface IStockSignalModel
{
    string Name { get; }
    SignalResult Evaluate(IReadOnlyList<DailyBar> history);
}

/// <summary>
/// Market regime information for filtering trades.
/// Includes both TSX (XIU) and S&amp;P 500 (SPY) benchmarks.
/// </summary>
public record MarketRegime(
    bool IsBenchmarkUptrend,        // XIU MA50 > MA200
    bool IsBenchmark20dPositive,    // XIU 20-day return > 0
    bool IsVolatilityNormal,        // Not in a vol spike
    double BenchmarkReturn20d,
    double BenchmarkMA50,
    double BenchmarkMA200,
    // S&P 500 cross-market confirmation
    bool IsSpyUptrend = true,       // SPY MA50 > MA200
    bool IsSpy20dPositive = true    // SPY 20-day return > 0
)
{
    public bool IsAnyBenchmarkUptrend => IsBenchmarkUptrend || IsSpyUptrend;
    public bool IsAny20dPositive => IsBenchmark20dPositive || IsSpy20dPositive;
    public bool IsBothBearish =>
        !IsBenchmarkUptrend && !IsBenchmark20dPositive &&
        !IsSpyUptrend && !IsSpy20dPositive;
}

/// <summary>
/// A scored signal tagged with the model's semantic role.
/// </summary>
public record RoleTaggedSignal(SignalRole Role, string Name, double Score, float CompositeWeight);

public class TradeDecisionEngine
{
    private readonly IReadOnlyList<IStockSignalModel> _patternModels;
    private readonly IReadOnlyList<UnifiedProfitSignalModel> _profitModels;
    private readonly TradePipeline _pipeline;

    /// <summary>
    /// The active strategy configuration driving all gate thresholds.
    /// </summary>
    public StrategyConfig Config { get; }

    public PositionSizer? Sizer { get; set; }
    public RankingMode RankingMode { get; set; } = RankingMode.Probability;

    public MarketRegime? CurrentRegime { get; set; }
    public bool RequireBenchmarkUptrend => Config.RequireBenchmarkUptrend;
    public double? BreadthScore { get; set; }
    public double BreadthVetoThreshold => Config.BreadthVetoThreshold;

    public TradeDecisionEngine(IEnumerable<IStockSignalModel> patternModels)
        : this(patternModels, Enumerable.Empty<UnifiedProfitSignalModel>())
    {
    }

    public TradeDecisionEngine(
        IEnumerable<IStockSignalModel> patternModels,
        IEnumerable<UnifiedProfitSignalModel> profitModels,
        StrategyConfig? config = null)
    {
        _patternModels = patternModels.ToList();
        _profitModels = profitModels.ToList();
        Config = config ?? StrategyConfig.Default;
        _pipeline = TradePipeline.FromConfig(Config);
    }

    public (RankedPick? Pick, PositionSizeResult? Size) EvaluateBestPickAllIn(
        List<RankedPick> ranked,
        decimal availableCapital)
    {
        if (ranked.Count == 0)
            return (null, null);

        var best = ranked.FirstOrDefault(p => p.Direction == TradeDirection.Buy) ?? ranked[0];

        var sizer = Sizer ?? new PositionSizer(availableCapital);
        sizer.AvailableCapital = availableCapital;

        var size = sizer.SizeSingleBestPick(best);

        return (best, size);
    }

    public TradeDecisionResult Evaluate(IReadOnlyList<DailyBar> history)
    {
        var patternSignals = _patternModels
            .Select(m => m.Evaluate(history))
            .ToList();

        // Evaluate profit models and tag each result with its role
        var taggedSignals = _profitModels
            .Select(m => new RoleTaggedSignal(m.Role, m.Name, m.Evaluate(history).Score, m.CompositeWeight))
            .ToList();

        // Also collect the raw SignalResult list for output
        var profitSignals = _profitModels
            .Select(m => m.Evaluate(history))
            .ToList();

        var (composite, breakoutProb, upProb, downProb, directionEdge) =
            ComputeCompositeFromRoles(taggedSignals);

        // Build gate context
        var patternHints = patternSignals
            .Where(s => s.Hint.HasValue && s.Hint != TradeDirection.Hold)
            .Select(s => s.Hint!.Value)
            .ToList();

        // Get confirmation/momentum scores for gate context
        double volExpansionProb = taggedSignals
            .FirstOrDefault(s => s.Role == SignalRole.Confirmation)?.Score ?? 0;

        var gateContext = new GateContext
        {
            Regime = CurrentRegime,
            BreadthScore = BreadthScore,
            BreadthVetoThreshold = Config.BreadthVetoThreshold,
            RequireBenchmarkUptrend = Config.RequireBenchmarkUptrend,
            BreakoutProb = breakoutProb,
            UpProb = upProb,
            DownProb = downProb,
            DirectionEdge = directionEdge,
            CompositeScore = composite,
            VolExpansionProb = volExpansionProb,
            PatternBuys = patternHints.Count(h => h == TradeDirection.Buy),
            PatternCount = patternSignals.Count
        };

        var direction = _pipeline.Evaluate(gateContext);

        var allSignals = patternSignals.Concat(profitSignals).ToList();

        return new TradeDecisionResult(
            Direction: direction,
            ExpectedReturn: 0,
            Confidence: composite,
            CompositeScore: composite,
            DirectionProbability: upProb,
            DownProbability: downProb,
            DirectionEdge: directionEdge,
            PositionSize: null,
            Signals: allSignals,
            GateTrace: gateContext.Trace);
    }

    /// <summary>
    /// Computes composite score using model roles and weights — no string matching.
    /// Each model's CompositeWeight comes from ProfitModelDefinition.
    /// Veto models have negative weights (penalty).
    /// </summary>
    private static (double Composite, double BreakoutProb, double UpProb, double DownProb, double DirectionEdge)
        ComputeCompositeFromRoles(IReadOnlyList<RoleTaggedSignal> signals)
    {
        if (signals.Count == 0)
            return (0, 0, 0, 0, 0);

        // Extract key probabilities by role
        double breakoutProb = signals
            .Where(s => s.Role == SignalRole.Setup)
            .Select(s => s.Score)
            .DefaultIfEmpty(0)
            .Max();

        double upProb = signals
            .Where(s => s.Role == SignalRole.DirectionUp)
            .Select(s => s.Score)
            .DefaultIfEmpty(0)
            .Max();

        double downProb = signals
            .Where(s => s.Role == SignalRole.Veto)
            .Select(s => s.Score)
            .DefaultIfEmpty(0)
            .Max();

        double directionEdge = upProb - downProb;

        // Composite: sum of (score × weight) for each model, plus ensemble residual.
        // Weights are defined in the registry — positive for bullish signals, negative for veto.
        double weightedSum = 0;
        double totalPositiveWeight = 0;

        foreach (var signal in signals)
        {
            weightedSum += signal.Score * signal.CompositeWeight;

            if (signal.CompositeWeight > 0)
                totalPositiveWeight += signal.CompositeWeight;
        }

        // Ensemble residual: allocate any remaining weight to average of non-veto signals
        double usedPositiveWeight = signals
            .Where(s => s.CompositeWeight > 0)
            .Sum(s => s.CompositeWeight);

        double residualWeight = System.Math.Max(0, 1.0 - usedPositiveWeight - System.Math.Abs(signals
            .Where(s => s.CompositeWeight < 0)
            .Sum(s => s.CompositeWeight)));

        if (residualWeight > 0.01)
        {
            double ensembleAvg = signals
                .Where(s => s.Role != SignalRole.Veto)
                .Select(s => s.Score)
                .DefaultIfEmpty(0)
                .Average();

            weightedSum += ensembleAvg * residualWeight;
        }

        double composite = System.Math.Max(weightedSum, 0);

        return (composite, breakoutProb, upProb, downProb, directionEdge);
    }

    public List<RankedPick> EvaluateAndRank(
        Dictionary<string, IReadOnlyList<DailyBar>> symbolBars,
        int topN = 10)
    {
        var picks = new List<RankedPick>();

        foreach (var (symbol, history) in symbolBars)
        {
            var result = Evaluate(history);

            picks.Add(new RankedPick(
                Symbol: symbol,
                Direction: result.Direction,
                ExpectedReturn: result.ExpectedReturn,
                Confidence: result.Confidence,
                CompositeScore: result.CompositeScore,
                DirectionProbability: result.DirectionProbability,
                DownProbability: result.DownProbability,
                DirectionEdge: result.DirectionEdge,
                Signals: result.Signals,
                GateTrace: result.GateTrace));
        }

        return RankingMode switch
        {
            RankingMode.Probability => picks
                .OrderByDescending(p => p.Direction == TradeDirection.Buy ? 1 : 0)
                .ThenByDescending(p => p.DirectionEdge)
                .ThenByDescending(p => p.CompositeScore)
                .Take(topN)
                .ToList(),

            RankingMode.ExpectedReturn => picks
                .OrderByDescending(p => p.Direction == TradeDirection.Buy ? 2 : p.Direction == TradeDirection.Hold ? 1 : 0)
                .ThenByDescending(p => p.ExpectedReturn)
                .ThenByDescending(p => p.Confidence)
                .Take(topN)
                .ToList(),

            _ => picks.Take(topN).ToList()
        };
    }

    public List<SizedPick> EvaluateRankAndSize(
        Dictionary<string, IReadOnlyList<DailyBar>> symbolBars,
        decimal availableCapital,
        int maxPositions = 5)
    {
        var rankedPicks = EvaluateAndRank(symbolBars, topN: maxPositions * 2);

        var sizer = Sizer ?? new PositionSizer(availableCapital);
        sizer.AvailableCapital = availableCapital;

        return sizer.SizePortfolio(rankedPicks, maxPositions);
    }

    /// <summary>
    /// Computes market regime from benchmark bars.
    /// Pass both XIU and SPY bars for cross-market confirmation.
    /// </summary>
    public static MarketRegime ComputeRegime(
        IReadOnlyList<DailyBar> xiuBars,
        IReadOnlyList<DailyBar>? spyBars = null)
    {
        var (xiuUptrend, xiu20dPos, xiuVolNormal, xiuReturn, xiuMa50, xiuMa200) =
            ComputeSingleBenchmark(xiuBars);

        bool spyUptrend = true;
        bool spy20dPos = true;

        if (spyBars != null && spyBars.Count >= 200)
        {
            var (up, pos, _, _, _, _) = ComputeSingleBenchmark(spyBars);
            spyUptrend = up;
            spy20dPos = pos;
        }

        return new MarketRegime(
            IsBenchmarkUptrend: xiuUptrend,
            IsBenchmark20dPositive: xiu20dPos,
            IsVolatilityNormal: xiuVolNormal,
            BenchmarkReturn20d: xiuReturn,
            BenchmarkMA50: xiuMa50,
            BenchmarkMA200: xiuMa200,
            IsSpyUptrend: spyUptrend,
            IsSpy20dPositive: spy20dPos);
    }

    private static (bool Uptrend, bool Return20dPositive, bool VolNormal, double Return20d, double MA50, double MA200)
        ComputeSingleBenchmark(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count < 200)
            return (true, true, true, 0, 0, 0);

        double ma50 = bars.TakeLast(50).Average(b => (double)b.Close);
        double ma200 = bars.TakeLast(200).Average(b => (double)b.Close);

        double price20Ago = bars[^21].Close;
        double currentPrice = bars[^1].Close;
        double return20d = (currentPrice - price20Ago) / price20Ago;

        double atr14 = CalculateAtrPercent(bars.TakeLast(15).ToList());
        double atr60 = CalculateAtrPercent(bars.TakeLast(61).ToList());
        bool isVolNormal = atr14 < atr60 * 1.5;

        return (ma50 > ma200, return20d > 0, isVolNormal, return20d, ma50, ma200);
    }

    private static double CalculateAtrPercent(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count < 2) return 0;

        double sum = 0;
        for (int i = 1; i < bars.Count; i++)
        {
            var cur = bars[i];
            var prev = bars[i - 1];
            double prevClose = prev.Close == 0 ? 1 : prev.Close;
            double tr = System.Math.Max(cur.High - cur.Low,
                        System.Math.Max(System.Math.Abs(cur.High - prevClose),
                                 System.Math.Abs(cur.Low - prevClose)));
            sum += tr / prevClose;
        }

        return sum / (bars.Count - 1);
    }
}

public enum RankingMode
{
    Probability,
    ExpectedReturn
}

public record TradeDecisionResult(
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    double CompositeScore,
    double DirectionProbability,
    double DownProbability,
    double DirectionEdge,
    PositionSizeResult? PositionSize,
    IReadOnlyList<SignalResult> Signals,
    IReadOnlyList<GateTraceEntry>? GateTrace = null
);

public record RankedPick(
    string Symbol,
    TradeDirection Direction,
    double ExpectedReturn,
    double Confidence,
    double CompositeScore,
    double DirectionProbability,
    double DownProbability,
    double DirectionEdge,
    IReadOnlyList<SignalResult> Signals,
    IReadOnlyList<GateTraceEntry>? GateTrace = null
);