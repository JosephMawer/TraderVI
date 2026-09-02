#nullable enable

using System;
using System.Collections.Generic;

namespace Core.Runtime;

public static class DelphiPresentationSchema
{
    public const int CurrentVersion = 1;
}

public sealed record DelphiPresentationSnapshot(
    int SchemaVersion,
    bool IsReconstructed,
    string SourceNote,
    DateTime RecommendationDate,
    DateTime MarketDataAsOf,
    DelphiRecommendationPresentation Recommendation,
    DelphiRegimePresentation? Regime,
    DelphiBreadthPresentation? Breadth,
    IReadOnlyList<DelphiSectorPresentation> Sectors,
    IReadOnlyList<DelphiUsIndexPresentation> UsIndices,
    DelphiMarketTapePresentation? MarketTape,
    DelphiGranvillePresentation? Granville,
    DelphiWeightingPresentation? Weighting,
    DelphiUniversePresentation Universe,
    DelphiRelativeStrengthPresentation RelativeStrength,
    DelphiObvPresentation Obv,
    DelphiClimaxPresentation? Climax,
    DelphiStrategyPresentation Strategy,
    IReadOnlyList<DelphiSignalPresentation> BestPickSignals,
    IReadOnlyList<DelphiGatePresentation> BestPickGates,
    string SummaryReport,
    string DiagnosticReport);

public sealed record DelphiRecommendationPresentation(
    bool HasTrade,
    string Symbol,
    string Direction,
    double CompositeScore,
    double UpProbability,
    double DownProbability,
    double DirectionEdge,
    double BreakoutProbability,
    double VolumeExpansionProbability,
    decimal SuggestedSize,
    double AllocationPercent,
    string Reason,
    string ObvConfirmation);

public sealed record DelphiRegimePresentation(
    string Label,
    bool XiuUptrend,
    bool Xiu20dPositive,
    double XiuReturn20d,
    bool VolatilityNormal,
    bool SpyUptrend,
    bool Spy20dPositive,
    bool AnyBenchmarkUptrend,
    bool BothBearish);

public sealed record DelphiBreadthPresentation(
    DateTime Date,
    int Advancers,
    int Decliners,
    int Unchanged,
    int DailyPlurality,
    int CumulativeDifferential,
    double BreadthScore,
    double Slope20d,
    bool AboveSma50,
    bool BearishDivergence);

public sealed record DelphiSectorPresentation(
    string Symbol,
    string SectorName,
    decimal Price,
    decimal PriceChange,
    decimal PercentChange,
    DateTime Date);

public sealed record DelphiUsIndexPresentation(
    string Symbol,
    DateTime Date,
    double Close,
    double Return1d,
    double Return5d,
    int BarCount);

public sealed record DelphiMarketTapePresentation(
    DateTime Date,
    decimal? XiuClose,
    decimal? XiuPreviousClose,
    decimal? XiuReturn1d,
    long? XiuVolume,
    decimal? XiuVolumeSma20Prior,
    decimal? XiuVolumeRatio20,
    bool IsLightVolume);

public sealed record DelphiGranvillePresentation(
    int BullishCount,
    int BearishCount,
    int NetPoints,
    double CompositeAdjustment,
    IReadOnlyList<DelphiGranvilleIndicatorPresentation> Indicators);

public sealed record DelphiGranvilleIndicatorPresentation(
    int IndicatorNumber,
    string Category,
    string Name,
    string Signal,
    int Points,
    string Description);

public sealed record DelphiWeightingPresentation(
    int ConstituentsObserved,
    int ConstituentsRequired,
    double XiuReturn,
    double ScoreB,
    double ScoreC,
    bool Triggered,
    bool Degraded,
    IReadOnlyList<string> TopContributors);

public sealed record DelphiUniversePresentation(
    int Discovered,
    int Loaded,
    int SkippedHistory,
    int SkippedStaleHistory,
    int SkippedPriceCeiling,
    int SkippedPriceFloor,
    int SkippedLowVolume,
    int SkippedLeveragedEtp,
    decimal MinimumPrice,
    long MinimumVolume20d,
    IReadOnlyList<string> StaleSymbols);

public sealed record DelphiRelativeStrengthPresentation(
    int Computed,
    int? SectorBarsMinimum,
    int? SectorBarsMaximum,
    int? BarsRequired,
    int? FallbackToXiu,
    int? CompositeNull,
    IReadOnlyList<string> FallbackSymbols,
    int? AlignmentGapCount = null,
    IReadOnlyList<string>? AlignmentGapSymbols = null,
    int? FullCoverageCount = null);

public sealed record DelphiObvPresentation(
    int? BreakoutWindow,
    double? SignalWeight,
    int? Rising,
    int? Falling,
    int? Doubtful,
    int? Indeterminate,
    IReadOnlyList<DelphiObvSymbolPresentation> PublishedSymbols);

public sealed record DelphiObvSymbolPresentation(
    string Symbol,
    string Trend,
    string Designation,
    DateTime AsOf,
    int PivotCount,
    double Tilt);

public sealed record DelphiClimaxPresentation(
    DateTime Date,
    int Clx,
    int UpBreakouts,
    int DownBreakouts,
    int Covered,
    int BasketSize,
    int FreshUp,
    int FreshDown,
    float? XiuClose,
    string Regime,
    string Description,
    int? ClxChange,
    double? XiuChangePercent);

public sealed record DelphiStrategyPresentation(
    string VersionName,
    string Description,
    double MinimumComposite,
    double MinimumUpProbability,
    double MinimumBreakoutProbability,
    double MaximumDownProbability,
    double MinimumDirectionEdge,
    double BreadthVetoThreshold,
    double StopLossPercent,
    int MaximumPositions,
    IReadOnlyList<string> PatternSignals,
    IReadOnlyList<string> ProfitModels);

public sealed record DelphiSignalPresentation(
    string Name,
    double Score,
    string Hint,
    string Notes);

public sealed record DelphiGatePresentation(
    string Name,
    bool Passed,
    string Reason);
