#nullable enable

using Core.Calibration;
using Core.Db;
using Core.Indicators;
using Core.Indicators.Granville;
using Core.ML;
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader;
using Core.Trader.Gates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>
/// Loads the immutable presentation snapshot for a published recommendation date.
/// Runs created before ADR-0035 are reconstructed only from date-aligned saved evidence.
/// </summary>
public sealed class DelphiPersistedPresentationReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<DelphiPresentationSnapshot?> LoadAsync(
        DateTime recommendationDate,
        DateTime publishedUtc,
        IReadOnlyList<DailyPickInfo> continuation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidenceRepository = new CalibrationEvidenceRepository();
        CalibrationRunInfo? run = await evidenceRepository.GetLatestRunAsync(
            recommendationDate,
            CalibrationRunPurpose.OfficialPaper,
            publishedUtc);
        if (run is null)
            return null;

        DelphiPresentationSnapshot? captured = TryReadCaptured(run.RunContextJson);
        if (captured is not null &&
            captured.RecommendationDate.Date == run.RecommendationDate.Date &&
            captured.MarketDataAsOf.Date == run.MarketDataAsOf.Date)
            return captured;

        return await ReconstructLegacyAsync(run, continuation, evidenceRepository, cancellationToken);
    }

    internal static DelphiPresentationSnapshot? TryReadCaptured(string runContextJson)
    {
        if (string.IsNullOrWhiteSpace(runContextJson))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(runContextJson);
            if (!document.RootElement.TryGetProperty("presentation", out JsonElement element))
                return null;
            DelphiPresentationSnapshot? snapshot = element.Deserialize<DelphiPresentationSnapshot>(JsonOptions);
            return snapshot?.SchemaVersion == DelphiPresentationSchema.CurrentVersion
                ? snapshot
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<DelphiPresentationSnapshot> ReconstructLegacyAsync(
        CalibrationRunInfo run,
        IReadOnlyList<DailyPickInfo> continuation,
        CalibrationEvidenceRepository evidenceRepository,
        CancellationToken cancellationToken)
    {
        LegacyRunContext context = DeserializeOrDefault<LegacyRunContext>(run.RunContextJson) ?? new();
        StrategyConfig config = DeserializeOrDefault<StrategyConfig>(run.StrategyConfigJson) ?? StrategyConfig.Default;
        IReadOnlyList<ModelArtifactProvenance> models =
            DeserializeOrDefault<List<ModelArtifactProvenance>>(run.ModelSnapshotJson) ?? [];

        StrategyVersionInfo? strategyVersion = run.StrategyVersionId.HasValue
            ? await new StrategyVersionRepository().GetVersionById(run.StrategyVersionId.Value)
            : null;

        DailyPickInfo? topPick = continuation.OrderBy(pick => pick.Rank).FirstOrDefault();
        CalibrationCandidateRunInfo? candidate = topPick is null
            ? null
            : await evidenceRepository.GetCandidateAsync(run.RunId, topPick.Symbol);
        IReadOnlyList<CalibrationObvStateCount> obvCounts =
            await evidenceRepository.GetObvStateCountsAsync(run.RunId);

        cancellationToken.ThrowIfCancellationRequested();
        List<ADLineEntry> adLine = (await new AdvanceDeclineRepository().GetRecentAsync(200))
            .Where(entry => entry.Date.Date <= run.MarketDataAsOf.Date)
            .OrderBy(entry => entry.Date)
            .ToList();
        ADLineEntry? latestAd = adLine.LastOrDefault();
        DelphiBreadthPresentation? breadth = latestAd is null
            ? null
            : new DelphiBreadthPresentation(
                latestAd.Date,
                latestAd.Advancers,
                latestAd.Decliners,
                latestAd.Unchanged,
                latestAd.DailyPlurality,
                latestAd.CumulativeDifferential,
                context.BreadthScore,
                AdvanceDeclineCalculator.Slope(adLine),
                AdvanceDeclineCalculator.IsAboveSma(adLine),
                context.BearishDivergence);

        IReadOnlyList<SectorIndexSnapshot> sectorHistory =
            await new SectorIndexRepository().GetRecentAsync(TsxSectorSymbols.AllSymbols, days: 10);
        IReadOnlyList<DelphiSectorPresentation> sectors = sectorHistory
            .Where(sector => sector.Date.Date == run.MarketDataAsOf.Date)
            .OrderByDescending(sector => sector.PercentChange)
            .Select(sector => new DelphiSectorPresentation(
                sector.Symbol,
                sector.SectorName,
                sector.Price,
                sector.PriceChange,
                sector.PercentChange,
                sector.Date))
            .ToArray();

        var usRepository = new UsIndexBarsRepository();
        List<DelphiUsIndexPresentation> usIndices = [];
        foreach (string symbol in UsIndexSymbols.AllSymbols)
        {
            IReadOnlyList<UsIndexBar> bars = (await usRepository.GetBarsAsync(
                    symbol,
                    run.MarketDataAsOf.AddDays(-30)))
                .Where(bar => bar.Date.Date <= run.MarketDataAsOf.Date)
                .OrderBy(bar => bar.Date)
                .ToArray();
            if (bars.Count == 0)
                continue;
            UsIndexBar last = bars[^1];
            double return1d = bars.Count >= 2 && bars[^2].Close > 0
                ? last.Close / bars[^2].Close - 1.0
                : 0;
            double return5d = bars.Count >= 6 && bars[^6].Close > 0
                ? last.Close / bars[^6].Close - 1.0
                : 0;
            usIndices.Add(new DelphiUsIndexPresentation(
                symbol,
                last.Date,
                last.Close,
                return1d,
                return5d,
                bars.Count));
        }

        List<DailyBar> xiuBars = (await new QuoteRepository().GetDailyBarsAsync(
                "XIU",
                run.MarketDataAsOf.AddDays(-60)))
            .Where(bar => bar.Date.Date <= run.MarketDataAsOf.Date)
            .OrderBy(bar => bar.Date)
            .ToList();
        MarketTapeContext? tape = MarketTapeCalculator.Build(xiuBars);
        DelphiMarketTapePresentation? marketTape = tape is null
            ? null
            : new DelphiMarketTapePresentation(
                tape.Date,
                tape.XiuClose,
                tape.XiuPrevClose,
                tape.XiuReturn1d,
                tape.XiuVolume,
                tape.XiuVolumeSma20Prior,
                tape.XiuVolumeRatio20,
                tape.XiuVolumeRatio20 is decimal ratio && ratio < 0.85m);

        DelphiGranvillePresentation? granville = context.GranvilleForecast is null
            ? await LoadGranvilleLogAsync(run.RecommendationDate)
            : MapGranville(context.GranvilleForecast);

        IReadOnlyList<SignalResult> signals = ReadSignals(candidate?.SnapshotJson);
        IReadOnlyList<GateTraceEntry> gates = ReadGates(candidate?.GateTraceJson);
        DelphiRecommendationPresentation recommendation = BuildLegacyRecommendation(topPick, candidate);

        DelphiRegimePresentation? regime = context.Regime is null
            ? null
            : new DelphiRegimePresentation(
                context.Regime.IsBothBearish ? "Bearish" : context.Regime.IsAnyBenchmarkUptrend ? "Bullish" : "Mixed",
                context.Regime.IsBenchmarkUptrend,
                context.Regime.IsBenchmark20dPositive,
                context.Regime.BenchmarkReturn20d,
                context.Regime.IsVolatilityNormal,
                context.Regime.IsSpyUptrend,
                context.Regime.IsSpy20dPositive,
                context.Regime.IsAnyBenchmarkUptrend,
                context.Regime.IsBothBearish);

        MarketClimaxEntry? latestClimax = context.MarketClimax?
            .Where(item => item.Date.Date <= run.MarketDataAsOf.Date)
            .OrderBy(item => item.Date)
            .LastOrDefault();
        DelphiClimaxPresentation? climax = latestClimax is null
            ? null
            : new DelphiClimaxPresentation(
                latestClimax.Date,
                latestClimax.Clx,
                latestClimax.UpBreakouts,
                latestClimax.DownBreakouts,
                latestClimax.Covered,
                latestClimax.BasketSize,
                latestClimax.FreshUp,
                latestClimax.FreshDown,
                latestClimax.XiuClose,
                context.ClimaxRegime?.Regime.ToString() ?? "Unavailable",
                context.ClimaxRegime?.Description ?? "The legacy run did not preserve a CLX verdict.",
                context.ClimaxRegime?.ClxChange,
                context.ClimaxRegime?.XiuChangePct);

        int CountObv(string state) => obvCounts
            .FirstOrDefault(item => string.Equals(item.State, state, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

        string unavailableReport =
            "This official run predates ADR-0035, so its exact structured report text was not persisted.\n" +
            "The other views reconstruct only date-aligned facts preserved in the calibration ledger and local historical tables.";

        return new DelphiPresentationSnapshot(
            DelphiPresentationSchema.CurrentVersion,
            true,
            $"Reconstructed from official run {run.RunId}; unavailable fields were not replaced with current values.",
            run.RecommendationDate,
            run.MarketDataAsOf,
            recommendation,
            regime,
            breadth,
            sectors,
            usIndices,
            marketTape,
            granville,
            null,
            new DelphiUniversePresentation(
                run.SymbolsDiscovered,
                run.SymbolsModelEvaluated,
                run.SkippedHistory,
                run.SkippedStaleHistory,
                run.SkippedUnaffordable,
                run.SkippedLowPrice,
                run.SkippedLowVolume,
                run.SkippedLeveragedEtp,
                context.MinPriceFloor,
                context.MinVolume20d,
                []),
            new DelphiRelativeStrengthPresentation(
                run.SymbolsModelEvaluated,
                null,
                null,
                null,
                null,
                null,
                []),
            new DelphiObvPresentation(
                null,
                config.ObvSignalWeight,
                CountObv("Rising"),
                CountObv("Falling"),
                CountObv("Doubtful"),
                CountObv("Indeterminate") + CountObv("Unavailable"),
                []),
            climax,
            new DelphiStrategyPresentation(
                strategyVersion?.VersionName ?? "Unknown saved strategy",
                strategyVersion?.Description ?? "Strategy metadata unavailable",
                config.MinCompositeScore,
                config.MinUpProb,
                config.MinBreakoutProb,
                config.MaxDownProb,
                config.MinDirectionEdge,
                config.BreadthVetoThreshold,
                config.StopLossPercent,
                config.MaxPositions,
                [],
                models.Select(model => model.TaskType).ToArray()),
            signals.Select(signal => new DelphiSignalPresentation(
                signal.Name,
                signal.Score,
                signal.Hint?.ToString() ?? "—",
                signal.Notes ?? "")).ToArray(),
            gates.Select(gate => new DelphiGatePresentation(
                gate.GateName,
                gate.Passed,
                gate.Reason ?? "Passed")).ToArray(),
            unavailableReport,
            unavailableReport);
    }

    private static DelphiRecommendationPresentation BuildLegacyRecommendation(
        DailyPickInfo? pick,
        CalibrationCandidateRunInfo? candidate)
    {
        if (pick is null || candidate is null)
        {
            return new DelphiRecommendationPresentation(
                false, "—", "NO TRADE", 0, 0, 0, 0, 0, 0, 0, 0,
                "No persisted qualifying recommendation", "Unavailable");
        }

        string obv = candidate.ObvState switch
        {
            "Rising" => "Confirms",
            "Falling" => "Contradicts",
            "Doubtful" => "Neutral",
            _ => "No read"
        };
        return new DelphiRecommendationPresentation(
            true,
            pick.Symbol,
            pick.Direction,
            candidate.CompositeScore,
            candidate.UpProbability ?? 0,
            candidate.DownProbability ?? 0,
            candidate.DirectionEdge,
            candidate.BreakoutProbability ?? 0,
            candidate.VolExpansionProbability ?? 0,
            pick.SuggestedSize ?? 0,
            pick.AllocationPercent ?? 0,
            pick.Notes ?? "Persisted top Continuation pick",
            obv);
    }

    private static async Task<DelphiGranvillePresentation?> LoadGranvilleLogAsync(DateTime recommendationDate)
    {
        List<GranvilleIndicatorLogEntry> rows =
            await new GranvilleIndicatorLogRepository().GetByDateAsync(recommendationDate);
        if (rows.Count == 0)
            return null;
        return new DelphiGranvillePresentation(
            rows.Count(row => row.Signal is "Bullish" or "StrongBullish"),
            rows.Count(row => row.Signal is "Bearish" or "StrongBearish"),
            rows[0].NetPoints,
            rows[0].CompositeAdjustment,
            rows.Select(row => new DelphiGranvilleIndicatorPresentation(
                row.IndicatorNumber,
                row.Category,
                row.Name,
                row.Signal,
                row.GranvillePoints,
                row.Description)).ToArray());
    }

    private static DelphiGranvillePresentation MapGranville(GranvilleDailyForecast forecast) =>
        new(
            forecast.BullishCount,
            forecast.BearishCount,
            forecast.NetPoints,
            forecast.CompositeAdjustment,
            forecast.Results.Select(result => new DelphiGranvilleIndicatorPresentation(
                result.IndicatorNumber,
                result.Category.ToString(),
                result.Name,
                result.Signal.ToString(),
                result.GranvillePoints,
                result.Description)).ToArray());

    private static IReadOnlyList<SignalResult> ReadSignals(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("signals", out JsonElement element)
                ? element.Deserialize<List<SignalResult>>(JsonOptions) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<GateTraceEntry> ReadGates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("gates", out JsonElement element)
                ? element.Deserialize<List<GateTraceEntry>>(JsonOptions) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static T? DeserializeOrDefault<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class LegacyRunContext
    {
        public MarketRegime? Regime { get; init; }
        public double BreadthScore { get; init; }
        public bool BearishDivergence { get; init; }
        public GranvilleDailyForecast? GranvilleForecast { get; init; }
        public IReadOnlyList<MarketClimaxEntry>? MarketClimax { get; init; }
        public ClimaxRegimeResult? ClimaxRegime { get; init; }
        public decimal MinPriceFloor { get; init; }
        public long MinVolume20d { get; init; }
    }
}
