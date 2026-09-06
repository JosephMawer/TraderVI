using Core.Db;
using Core.Indicators;
using Core.Calibration;
using Core.DataQuality;
using Core.Indicators.Granville;
using Core.Config;
using Core.ML;
using Core.RelativeStrength;
using Core.Runtime;
using Core.Trader;
using Core.Trader.Gates;
using Core.TMX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Console = Core.Runtime.DelphiWorkflowLog;

#nullable enable

namespace Core.Runtime;

/// <summary>
/// Host-neutral Delphi orchestration shared by the Delphi CLI and TraderVI WPF.
/// The workflow remains a consequential database writer when SaveToDatabase is true.
/// </summary>
public sealed class DelphiWorkflow
{
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    public async Task<DelphiWorkflowRunResult> RunAsync(
        DelphiWorkflowOptions? options = null,
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DelphiWorkflowOptions();
        options.Validate();
        await RunGate.WaitAsync(cancellationToken);
        try
        {
            using IDisposable logScope = DelphiWorkflowLog.Use(output ?? TextWriter.Null);
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine("=== The Oracle Of Delphi ===\n");

            DateTime runStartedUtc = DateTime.UtcNow;
            CalibrationRunPurpose calibrationPurpose = options.Purpose;

            // The recommendation date is the run date and remains the persistence key for
            // DailyPick, DecisionDossier, and GranvilleIndicatorLog. The market-data date is
            // reported separately once the canonical XIU session has been loaded.
            DateTime recommendationDate = (options.RecommendationDate ?? DateTime.Today).Date;

            // ═══════════════════════════════════════════════════════════════════
            // CONFIGURATION (aggressive single-position rotation)
            // ═══════════════════════════════════════════════════════════════════
            decimal availableCapital = options.AvailableCapital;
            int minBarsRequired = 55;              // Increased for enhanced features
            decimal reserveCashPercent = 0m;//0.02m;
            double minExpectedReturn = 0.00;
            int maxSymbolsToScan = options.MaxSymbolsToScan;
            int topPicksToSave = options.TopPicksToSave;
            bool saveToDB = options.SaveToDatabase;
            bool saveOperationalState = saveToDB && calibrationPurpose == CalibrationRunPurpose.OfficialPaper;

            Console.WriteLine($"Available Capital: ${availableCapital:N2}");
            Console.WriteLine($"Reserve Cash:      {reserveCashPercent:P0}");
            Console.WriteLine($"Save to DB:        {saveToDB}");
            Console.WriteLine($"Calibration run:   {calibrationPurpose}");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // LOAD ACTIVE STRATEGY VERSION → DERIVE RUNTIME CONFIG
            // ═══════════════════════════════════════════════════════════════════
            var strategyRepo = new StrategyVersionRepository();
            var activeStrategy = await strategyRepo.GetActiveVersion();

            if (saveOperationalState &&
                (activeStrategy is null || !activeStrategy.HasOfficialEvidenceIdentity))
            {
                throw new InvalidOperationException(
                    "A persisted OfficialPaper run requires exactly one active strategy with " +
                    "InitialCodeCommit and DecisionRef. Apply the separately authorized ADR-0042 " +
                    "identity migration before publishing another official cohort.");
            }

            Guid? strategyVersionId = activeStrategy?.VersionId;
            StrategyConfig config = activeStrategy?.ToConfig() ?? StrategyConfig.Default;

            if (activeStrategy != null)
            {
                Console.WriteLine($"Strategy Version:  {activeStrategy.VersionName}");
                Console.WriteLine($"Description:       {activeStrategy.Description}");
                Console.WriteLine($"Decision / code:   {activeStrategy.DecisionRef ?? "unidentified"} / " +
                                  $"{activeStrategy.InitialCodeCommit ?? "unidentified"}");
                Console.WriteLine("Strategy gate thresholds:");
                Console.WriteLine($"  MinComposite:    {config.MinCompositeScore:P0}");
                Console.WriteLine($"  MinUpProb:       {config.MinUpProb:P0}");
                Console.WriteLine($"  MinBreakout:     {config.MinBreakoutProb:P0}");
                Console.WriteLine($"  MaxDownProb:     {config.MaxDownProb:P0}");
                Console.WriteLine($"  MinDirEdge:      {config.MinDirectionEdge:P0}");
                Console.WriteLine($"  BreadthVeto:     {config.BreadthVetoThreshold:0.00;-0.00}");
                Console.WriteLine($"  StopLoss:        {config.StopLossPercent:P0}");
                Console.WriteLine($"  MaxPositions:    {config.MaxPositions}");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("⚠️  No active strategy version found. Using defaults.\n");
            }

            // ═══════════════════════════════════════════════════════════════════
            // BOOTSTRAP ENGINE (loads enabled models from registry + strategy config)
            // ═══════════════════════════════════════════════════════════════════
            var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry(
                config,
                output);
            var continuationLens = LensCatalog.Continuation(config);
            var breakoutLens = LensCatalog.Breakout(config);

            engine.Sizer = new PositionSizer(availableCapital)
            {
                Strategy = AllocationStrategy.SinglePositionAllIn,
                ReserveCashPercent = reserveCashPercent,
                MinPositionSize = 25m,
                MinExpectedReturn = minExpectedReturn,
                MinConfidence = config.MinCompositeScore,
                RequireBothSignals = false
            };

            Console.WriteLine("[DelphiBootstrap] Ranking lenses:");
            Console.WriteLine("  Continuation (executed): RScomp + OBV tilt; DirectionEdge and composite tiebreakers");
            Console.WriteLine("  Breakout (journaled):    DirectionEdge + RScomp + OBV tilt; DirectionEdge and composite tiebreakers");

            // ═══════════════════════════════════════════════════════════════════
            // COMPUTE MARKET REGIME FROM XIU + SPY BENCHMARKS
            // ═══════════════════════════════════════════════════════════════════
            var quoteRepo = new QuoteRepository();
            var xiuBars = await quoteRepo.GetDailyBarsAsync("XIU");
            var spyBars = await quoteRepo.GetDailyBarsAsync("SPY");

            if (xiuBars.Count == 0)
            {
                const string status = "Cannot evaluate: XIU has no daily price history, so Delphi cannot establish the canonical TSX session.";
                Console.WriteLine(status);
                return DelphiWorkflowRunResult.Failed(
                    options,
                    runStartedUtc,
                    recommendationDate,
                    status);
            }

            DateTime marketDataAsOf = xiuBars[^1].Date.Date;
            var benchmarkSessions = xiuBars
                .Select(bar => bar.Date.Date)
                .Where(date => date <= marketDataAsOf)
                .Distinct()
                .OrderBy(date => date)
                .ToArray();
            RelativeStrengthPricePoint[] xiuCloses = xiuBars
                .Select(bar => new RelativeStrengthPricePoint(
                    DateOnly.FromDateTime(bar.Date),
                    bar.Close))
                .ToArray();
            int rsBarsRequired = RelativeStrengthCalculator.RequiredCanonicalSessionCount();
            DateOnly rsTargetSession = DateOnly.FromDateTime(marketDataAsOf);
            HashSet<DateOnly> requiredXiuSessions = xiuCloses
                .Select(point => point.Date)
                .Where(date => date <= rsTargetSession)
                .Distinct()
                .OrderBy(date => date)
                .TakeLast(rsBarsRequired)
                .ToHashSet();

            // Validate the shared RS calendar before any optional operational write. A failed
            // canonical input must not leave Granville logs or later projections partially replaced.
            IGrouping<DateOnly, RelativeStrengthPricePoint>? duplicateXiuSession = xiuCloses
                .Where(point => requiredXiuSessions.Contains(point.Date))
                .GroupBy(point => point.Date)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateXiuSession is not null)
            {
                string status = $"XIU contains duplicate daily rows for canonical session {duplicateXiuSession.Key:yyyy-MM-dd}; relative strength was not evaluated.";
                Console.WriteLine(status);
                return DelphiWorkflowRunResult.Failed(
                    options,
                    runStartedUtc,
                    recommendationDate,
                    status,
                    marketDataAsOf);
            }

            RelativeStrengthPricePoint[] invalidXiuCloses = xiuCloses
                .Where(point => requiredXiuSessions.Contains(point.Date))
                .OrderBy(point => point.Date)
                .Where(point => !double.IsFinite(point.Close) || point.Close <= 0)
                .ToArray();
            if (invalidXiuCloses.Length > 0)
            {
                string invalidSessions = string.Join(", ", invalidXiuCloses.Select(point => point.Date.ToString("yyyy-MM-dd")));
                string status = $"XIU has invalid closes in the canonical relative-strength window ({invalidSessions}); relative strength was not evaluated.";
                Console.WriteLine(status);
                return DelphiWorkflowRunResult.Failed(
                    options,
                    runStartedUtc,
                    recommendationDate,
                    status,
                    marketDataAsOf);
            }

            MarketRegime? regime = null;
            if (xiuBars.Count >= 200)
            {
                regime = TradeDecisionEngine.ComputeRegime(xiuBars, spyBars.Count >= 200 ? spyBars : null);

                Console.WriteLine("Market Regime:");
                Console.WriteLine($"  XIU Uptrend (MA50>MA200): {(regime.IsBenchmarkUptrend ? "✓ Yes" : "✗ No")}");
                Console.WriteLine($"  XIU 20d Return:           {regime.BenchmarkReturn20d:P2} {(regime.IsBenchmark20dPositive ? "✓" : "✗")}");
                Console.WriteLine($"  XIU Volatility:           {(regime.IsVolatilityNormal ? "Normal" : "⚠️ Elevated")}");
                Console.WriteLine($"  SPY Uptrend (MA50>MA200): {(regime.IsSpyUptrend ? "✓ Yes" : "✗ No")}");
                Console.WriteLine($"  SPY 20d Positive:         {(regime.IsSpy20dPositive ? "✓ Yes" : "✗ No")}");
                Console.WriteLine($"  Any Benchmark Uptrend:    {(regime.IsAnyBenchmarkUptrend ? "✓ Yes" : "✗ No")}");
                Console.WriteLine();

                if (regime.IsBothBearish)
                {
                    Console.WriteLine("⚠️  BEARISH REGIME: Both XIU and SPY are bearish. Long trades will be filtered out.\n");
                }
                else if (!regime.IsBenchmarkUptrend && !regime.IsBenchmark20dPositive)
                {
                    Console.WriteLine("⚠️  XIU BEARISH but SPY positive — proceeding with caution.\n");
                }
            }
            else
            {
                Console.WriteLine("⚠️  Insufficient XIU data for regime calculation.\n");
            }

            // Inject regime into engine (thresholds already set via StrategyConfig)
            engine.CurrentRegime = regime;


            // ═══════════════════════════════════════════════════════════════════
            // LOAD A/D LINE BREADTH — WIRE IN BEFORE EVALUATION
            // ═══════════════════════════════════════════════════════════════════
            var adRepo = new AdvanceDeclineRepository();
            var adLine = await adRepo.GetRecentAsync(200);

            Console.WriteLine("Evaluation Dates:");
            Console.WriteLine($"  Recommendation date: {recommendationDate:yyyy-MM-dd} (run date; database PickDate/EvalDate)");
            Console.WriteLine($"  Market data as of:    {marketDataAsOf:yyyy-MM-dd} (latest completed TSX session)");
            Console.WriteLine();

            double breadthScore = AdvanceDeclineCalculator.BreadthScore(adLine);
            bool bearishDivergence = AdvanceDeclineCalculator.HasBearishDivergence(adLine);

            // Inject breadth into engine BEFORE evaluation
            engine.BreadthScore = breadthScore;

            Console.WriteLine($"A/D Line Breadth Score: {breadthScore:+0.00;-0.00}");
            Console.WriteLine($"  Slope (20d):         {AdvanceDeclineCalculator.Slope(adLine):+0.0;-0.0}");
            Console.WriteLine($"  Above SMA(50):       {(AdvanceDeclineCalculator.IsAboveSma(adLine) ? "✓" : "✗")}");
            Console.WriteLine($"  Bearish Divergence:  {(bearishDivergence ? "⚠️ YES" : "No")}");

            if (breadthScore <= engine.BreadthVetoThreshold)
            {
                Console.WriteLine($"  ⚠️  BREADTH VETO ACTIVE (score {breadthScore:+0.00} ≤ {engine.BreadthVetoThreshold:+0.00})");
            }

            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // GRANVILLE'S 56 DAY-TO-DAY INDICATORS
            // ═══════════════════════════════════════════════════════════════════
            GranvilleDailyForecast? granvilleForecast = null;
            WeightingSnapshot? weightingSnapshot = null;
            MarketTapeContext? marketTape = null;
            var leadershipCalculator = new LeadershipCalculator();
            int leadershipHistoryDays = 0;
            int leadershipActiveBreadthDays = 0;
            int leadershipActiveBreadthRequired = leadershipCalculator.RequiredActiveBreadthDays;
            var usIndexBars = new Dictionary<string, IReadOnlyList<Core.Indicators.Granville.UsIndexBar>>(StringComparer.OrdinalIgnoreCase);

            var sectorIndexRepo = new SectorIndexRepository();
            var stockSectorRepo = new StockSectorRepository();

            // Load once — reused by both Granville and RS sections
            var stockSectorMappings = await stockSectorRepo.GetAllAsync();

            if (adLine.Count >= 2)
            {
                // Load the cyclical basket sector snapshots required by Disparity.
                // We pull a small recent window so 1-day and 5-day comparisons have enough history.
                var granvilleSectorSnapshots = await sectorIndexRepo.GetRecentAsync(TsxSectorSymbols.CyclicalBasket, days: 10);

                // Load leadership history for Leadership indicators (#7–#10).
                // Need ~50 days for EMA-10 smoothing + 20-day large-cap RS lookback.
                var leadershipRepo = new LeadershipRepository();
                var leadershipHistory = await leadershipRepo.GetRecentAsync(50);
                leadershipHistoryDays = leadershipHistory.Count;
                leadershipActiveBreadthDays = leadershipCalculator.CountTrailingActiveBreadthDays(
                    leadershipHistory,
                    benchmarkSessions);

                // Load the 15 most active stocks by volume for today (Features indicators #11–#14).
                var mostActiveRepo = new MostActiveStocksRepository();
                var mostActiveStocks = await mostActiveRepo.GetTopByVolumeAsync(adLine[^1].Date, count: 15);

                // Load XIU constituent closes for Weighting indicator (#15/#16 — see ADR-0003).
                // Only the two most recent sessions per symbol are needed. Pull a small recent
                // window to absorb stale bars / weekends and reduce per-symbol payload.
                DateTime evalDateForWeighting = adLine[^1].Date;
                DateTime weightingFrom = evalDateForWeighting.AddDays(-10);
                var xiuConstituentBars = new List<XiuConstituentBar>(Xiu60Constituents.Symbols.Count);
                foreach (var symbol in Xiu60Constituents.Symbols)
                {
                    var bars = await quoteRepo.GetDailyBarsAsync(symbol, weightingFrom);
                    if (bars.Count < 2) continue;
                    var todayBar = bars[^1];
                    var prevBar = bars[^2];
                    xiuConstituentBars.Add(new XiuConstituentBar(
                        Symbol: symbol,
                        TodayClose: todayBar.Close,
                        YesterdayClose: prevBar.Close));
                }

                // Load US confirming-index bars for Genuity indicators (#17–#20 — see ADR-0004).
                // Need at least ~6 trading bars for the 5-day trend check (#20); pull a small
                // calendar window to absorb US-vs-CA holiday offsets and weekends.
                DateTime usIndexFrom = adLine[^1].Date.AddDays(-30);
                var usIndexBarsRepo = new UsIndexBarsRepository();
                foreach (var usSymbol in Core.TMX.UsIndexSymbols.AllSymbols)
                {
                    var bars = await usIndexBarsRepo.GetBarsAsync(usSymbol, usIndexFrom);
                    if (bars.Count > 0) usIndexBars[usSymbol] = bars;
                }

                // Load XIU bars for the Market Tape (Light Volume indicators #25–#28 — see ADR-0006).
                // Reuses the full XIU history already loaded above for regime computation;
                // MarketTapeCalculator only inspects the trailing 21 sessions.
                marketTape = MarketTapeCalculator.Build(xiuBars);

                var granvilleContext = new GranvilleMarketContext
                {
                    Today = adLine[^1],
                    Yesterday = adLine[^2],
                    RecentHistory = adLine,
                    SectorSnapshots = granvilleSectorSnapshots,
                    StockSectorMappings = stockSectorMappings,
                    LeadershipHistory = leadershipHistory.Count >= 2 ? leadershipHistory : null,
                    LeadershipExpectedSessions = benchmarkSessions,
                    MostActiveStocks = mostActiveStocks.Count >= 8 ? mostActiveStocks : null,
                    XiuConstituentBars = xiuConstituentBars,
                    UsIndexBars = usIndexBars.Count > 0 ? usIndexBars : null,
                    MarketTape = marketTape
                };

                var granville = new GranvilleComposite();
                granvilleForecast = granville.Evaluate(granvilleContext);

                // Compute Weighting snapshot separately for typed surfacing in the report builder.
                double xiuRetForWeighting = 0.0;
                if (adLine[^1].XiuClose is float todayXiu && adLine[^2].XiuClose is float yestXiu && yestXiu > 0f)
                {
                    xiuRetForWeighting = (todayXiu / (double)yestXiu) - 1.0;
                }
                weightingSnapshot = WeightingCalculator.Compute(xiuConstituentBars, xiuRetForWeighting);

                // Inject into engine BEFORE symbol evaluation
                engine.GranvilleForecast = granvilleForecast;

                // ── Display Granville original scoring ──
                Console.WriteLine("Granville Day-to-Day Indicators:");
                Console.WriteLine($"  Date:               {adLine[^1].Date:yyyy-MM-dd}");
                Console.WriteLine($"  Advancers:          {adLine[^1].Advancers}");
                Console.WriteLine($"  Decliners:          {adLine[^1].Decliners}");
                Console.WriteLine($"  Daily Plurality:    {adLine[^1].DailyPlurality:+0;-0}");
                Console.WriteLine($"  XIU Close:          {adLine[^1].XiuClose:F2} (prev: {adLine[^2].XiuClose:F2})");
                Console.WriteLine($"  Sector snapshots:   {granvilleSectorSnapshots.Count}");
                Console.WriteLine($"  Stock-sector maps:  {stockSectorMappings.Count}");
                Console.WriteLine($"  Leadership history: {leadershipHistoryDays} days");
                Console.WriteLine($"  Leadership movers:  {leadershipActiveBreadthDays}/{leadershipActiveBreadthRequired} contiguous observations");
                Console.WriteLine($"  Most active stocks: {mostActiveStocks.Count}");
                Console.WriteLine($"  US index symbols:   {usIndexBars.Count} (Genuity inputs)");
                if (marketTape is { XiuVolumeRatio20: decimal vr, XiuReturn1d: decimal r1 })
                {
                    Console.WriteLine($"  Market tape:        XIU ret={r1:+0.00%;-0.00%}, vol ratio={vr:F2} ({(vr < 0.85m ? "light" : "normal")})");
                }
                else
                {
                    Console.WriteLine("  Market tape:        unavailable (need ≥ 21 XIU sessions)");
                }

                if (mostActiveStocks.Count < 8)
                {
                    Console.WriteLine("  ⚠️  Insufficient most-active data (< 8 stocks) — Features indicators #11–#14 will degrade to neutral.");
                }

                if (granvilleSectorSnapshots.Count == 0)
                {
                    Console.WriteLine("  ⚠️  No sector index snapshots loaded — Disparity will degrade to neutral/no-data.");
                }

                if (stockSectorMappings.Count == 0)
                {
                    Console.WriteLine("  ⚠️  No stock-sector mappings loaded — future sector-aware Granville groups will be unavailable.");
                }

                if (leadershipActiveBreadthDays < leadershipActiveBreadthRequired)
                {
                    Console.WriteLine("  ⚠️  Leadership movers unavailable — insufficient contiguous observations; mover-dependent Leadership/Light Volume evidence will be neutral/no-data.");
                }

                if (marketTape is null || marketTape.XiuVolumeRatio20 is null || marketTape.XiuReturn1d is null)
                {
                    Console.WriteLine("  ⚠️  Market tape unavailable — Light Volume indicators (#25–#28) will degrade to neutral.");
                }

                Console.WriteLine();

                foreach (var result in granvilleForecast.Results)
                {
                    string icon = result.Signal switch
                    {
                        IndicatorSignal.Bullish => "📈",
                        IndicatorSignal.StrongBullish => "🚀",
                        IndicatorSignal.Bearish => "📉",
                        IndicatorSignal.StrongBearish => "🔻",
                        _ => "➖"
                    };
                    Console.WriteLine($"  {icon} [{result.IndicatorNumber:D2}] {result.Name}");
                    Console.WriteLine($"       Points: {result.GranvillePoints:+0;-0}  Signal: {result.Signal}");
                    Console.WriteLine($"       {result.Description}");
                }

                Console.WriteLine();
                Console.WriteLine($"  Granville Summary:");
                Console.WriteLine($"    Bullish signals:      {granvilleForecast.BullishCount}");
                Console.WriteLine($"    Bearish signals:      {granvilleForecast.BearishCount}");
                Console.WriteLine($"    Net Points:           {granvilleForecast.NetPoints:+0;-0}");
                Console.WriteLine($"    Composite Adjustment: {granvilleForecast.CompositeAdjustment:+0.000;-0.000}");
                Console.WriteLine();

            }
            else
            {
                Console.WriteLine("⚠️  Insufficient A/D line data for Granville indicators (need >= 2 entries).\n");
            }

            // ═══════════════════════════════════════════════════════════════════
            // LOAD ALL SYMBOLS FROM DATABASE
            // ═══════════════════════════════════════════════════════════════════
            var db = new SymbolsRepository();
            var constituents = await db.GetEquitiesAsync();

            // ── Defense-in-depth: leveraged/inverse ETP keyword guard (ADR-0009) ──
            // Primary exclusion is the IsLeveragedOrInverseEtp flag in dbo.Symbols
            // (GetEquitiesAsync already filters those out). This is a safety net for
            // future imports that arrive un-flagged: catch obvious leveraged/inverse
            // product names by their ShortName before they reach the ranking universe.
            static bool IsLeveragedOrInverseByName(string? shortName)
            {
                return SecurityNameHeuristics.LooksLeveragedOrInverse(shortName);
            }

            int skippedLeveraged = 0;
            var leveragedExclusions = new List<string>();
            var stockOnly = new List<SymbolInfo>(constituents.Count);
            foreach (var c in constituents)
            {
                if (IsLeveragedOrInverseByName(c.ShortName))
                {
                    skippedLeveraged++;
                    leveragedExclusions.Add($"{c.Symbol} ({c.ShortName})");
                    continue;
                }
                stockOnly.Add(c);
            }

            if (skippedLeveraged > 0)
            {
                Console.WriteLine($"Defense-in-depth: excluded {skippedLeveraged} un-flagged leveraged/inverse ETP(s) by ShortName (ADR-0009)");
                foreach (var s in leveragedExclusions.Take(10))
                    Console.WriteLine($"  ! {s}");
            }

            var symbols = stockOnly
                .Select(c => c.Symbol)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxSymbolsToScan)
                .ToList();

            Console.WriteLine($"Scanning symbols: {symbols.Count:N0} (equities only)\n");

            var allBars = new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase);

            int loaded = 0;
            int skipped = 0;
            int skippedStaleHistory = 0;
            int skippedPrice = 0;
            int skippedLowPrice = 0;
            int skippedLowVolume = 0;
            var staleHistoryExclusions = new List<HistoryFreshnessExclusion>();

            // Minimum price: must be able to afford at least 10 shares from deployable capital
            decimal deployableCapital = availableCapital * (1 - reserveCashPercent);
            decimal maxPriceForMinLot = deployableCapital / 10m;

            // ── Liquidity floor (ADR-0007) ──
            // Initial defaults — tunable. Goal: avoid signal/order-execution rot on sub-dollar
            // or thinly-traded names where ML probabilities and RS are trained out-of-distribution
            // and where any market order will move the tape against us.
            decimal minPriceFloor = 1.00m;
            long minVolume20d = 50_000;

            Console.WriteLine($"Affordability filter: max price ${maxPriceForMinLot:N2} (must afford >= 10 shares from ${deployableCapital:N2} deployable)");
            Console.WriteLine($"Liquidity floor:      min price ${minPriceFloor:N2}, min 20d avg volume {minVolume20d:N0} shares (ADR-0007)\n");
            Console.WriteLine($"History freshness:    latest symbol bar must match XIU session {marketDataAsOf:yyyy-MM-dd} (ADR-0019)\n");

            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bars = await quoteRepo.GetDailyBarsAsync(symbol);

                if (bars.Count < minBarsRequired)
                {
                    skipped++;
                    continue;
                }

                var freshness = HistoryFreshnessEligibility.Evaluate(
                    bars[^1].Date,
                    marketDataAsOf,
                    benchmarkSessions);
                if (!freshness.IsEligible)
                {
                    skippedStaleHistory++;
                    staleHistoryExclusions.Add(new HistoryFreshnessExclusion(
                        symbol,
                        freshness.LatestBarDate!.Value,
                        freshness.SessionsBehind,
                        freshness.Reason));
                    continue;
                }

                // Filter out stocks we can't afford at least 10 shares of
                var lastClose = (decimal)bars[^1].Close;
                if (lastClose > maxPriceForMinLot)
                {
                    skippedPrice++;
                    continue;
                }

                // Liquidity floor — price (penny-stock cutoff)
                if (lastClose < minPriceFloor)
                {
                    skippedLowPrice++;
                    continue;
                }

                // Liquidity floor — 20d average volume (thin-tape cutoff)
                var avgVolume20d = bars.TakeLast(20).Average(b => (double)b.Volume);
                if (avgVolume20d < minVolume20d)
                {
                    skippedLowVolume++;
                    continue;
                }

                allBars[symbol] = bars;
                loaded++;
            }

            // Sort by average 20-day volume descending — prefer highly liquid stocks
            allBars = allBars
                .OrderByDescending(kvp =>
                {
                    var vol = kvp.Value.TakeLast(20).Average(b => (double)b.Volume);
                    return vol;
                })
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            Console.WriteLine($"Loaded: {loaded} symbols | Skipped: {skipped} (insufficient history), {skippedStaleHistory} (stale history), {skippedPrice} (price > ${maxPriceForMinLot:N2}), {skippedLowPrice} (price < ${minPriceFloor:N2}), {skippedLowVolume} (20d vol < {minVolume20d:N0}), {skippedLeveraged} (lev/inv ETP)");
            Console.WriteLine($"Sorted by: avg 20-day volume (most liquid first)\n");

            if (allBars.Count == 0)
            {
                const string status = "No symbols with sufficient data to evaluate.";
                Console.WriteLine(status);
                return DelphiWorkflowRunResult.Failed(
                    options,
                    runStartedUtc,
                    recommendationDate,
                    status,
                    marketDataAsOf);
            }

            // ═══════════════════════════════════════════════════════════════════
            // COMPUTE LIVE RELATIVE STRENGTH (per-stock, before ranking)
            // ═══════════════════════════════════════════════════════════════════
            var stockSectorMap = stockSectorMappings
                .ToDictionary(m => m.Symbol, m => m.SectorIndexSymbol, StringComparer.OrdinalIgnoreCase);

            // XIU dated closes and the canonical depth were validated before any optional write.

            // Sector index closes keyed by sector symbol — retain headroom beyond the current
            // 61-session requirement for the 60-session return and 10-session Z-score.
            var rsSectorSnapshots = await sectorIndexRepo.GetRecentAsync(TsxSectorSymbols.AllSymbols, days: 80);

            var sectorClosesBySector = rsSectorSnapshots
                .GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<RelativeStrengthPricePoint>)(
                        g.OrderBy(snapshot => snapshot.Date)
                            .Select(snapshot => new RelativeStrengthPricePoint(
                                DateOnly.FromDateTime(snapshot.Date),
                                (double)snapshot.Price))
                            .ToArray()),
                    StringComparer.OrdinalIgnoreCase);

            var rsScores = new Dictionary<string, Core.RelativeStrength.RelativeStrengthRow>(StringComparer.OrdinalIgnoreCase);
            var rsFallbackSymbols = new List<string>();

            // RS coverage counters — surface degenerate/missing RS instead of silently emitting null.
            int rsFallbackToXiu = 0;
            int rsCompositeNull = 0;
            int rsFullCoverageCount = 0;
            int rsAlignmentGapCount = 0;
            var rsAlignmentGapSymbols = new List<string>();

            int rsMinSectorBars = sectorClosesBySector.Count > 0
                ? sectorClosesBySector.Values.Min(v => v.Count)
                : 0;
            int rsMaxSectorBars = sectorClosesBySector.Count > 0
                ? sectorClosesBySector.Values.Max(v => v.Count)
                : 0;

            foreach (var (symbol, bars) in allBars)
            {
                RelativeStrengthPricePoint[] stockCloses = bars
                    .Select(bar => new RelativeStrengthPricePoint(
                        DateOnly.FromDateTime(bar.Date),
                        bar.Close))
                    .ToArray();

                // Determine this stock's sector index
                string? sectorSymbol = stockSectorMap.TryGetValue(symbol, out string? mappedSector) &&
                    !string.IsNullOrWhiteSpace(mappedSector)
                        ? mappedSector.Trim()
                        : null;

                IReadOnlyList<RelativeStrengthPricePoint>? sectorCloses = null;
                if (sectorSymbol != null)
                    sectorClosesBySector.TryGetValue(sectorSymbol, out sectorCloses);

                bool usedFallback = sectorCloses == null;
                if (usedFallback)
                {
                    rsFallbackToXiu++;
                    rsFallbackSymbols.Add(symbol);
                }

                // Stock-vs-market does not depend on sector availability; any missing exact stock/XIU
                // endpoint still remains null under the calculator's coverage contract.
                // For an unmapped/missing sector series, use XIU as the explicit fallback:
                // StockVsSector equals StockVsMarket and SectorVsMarket is zero.
                RelativeStrengthCalculationResult rsCalculation = RelativeStrengthCalculator.Compute(
                    stockCloses: stockCloses,
                    sectorCloses: sectorCloses ?? xiuCloses,
                    marketCloses: xiuCloses,
                    symbol: symbol,
                    date: DateOnly.FromDateTime(marketDataAsOf),
                    sectorIndexSymbol: sectorSymbol ?? "XIU");
                RelativeStrengthRow rs = rsCalculation.Features;

                if (rsCalculation.Coverage.HasFullCoverage)
                    rsFullCoverageCount++;

                if (rsCalculation.Coverage.HasAlignmentGap)
                {
                    rsAlignmentGapCount++;
                    rsAlignmentGapSymbols.Add(symbol);
                }

                if (!rs.CompositeScore.HasValue) rsCompositeNull++;

                rsScores[symbol] = rs;
            }

            // Inject RS scores into engine before evaluation
            engine.RsCompositeScores = rsScores
                .Where(kvp => kvp.Value.CompositeScore.HasValue)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.CompositeScore!.Value, StringComparer.OrdinalIgnoreCase);

            Console.WriteLine($"Relative Strength: computed for {rsScores.Count} symbols ({sectorClosesBySector.Count} sectors loaded)");
            int withSector = rsScores.Count - rsFallbackToXiu;
            Console.WriteLine($"  With sector data: {withSector} | Fallback to XIU: {rsFallbackToXiu}");
            if (rsFallbackSymbols.Count > 0)
                Console.WriteLine($"  Fallback symbols:      {string.Join(", ", rsFallbackSymbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))}");
            Console.WriteLine($"  Raw sector rows min/max: {rsMinSectorBars} / {rsMaxSectorBars}");
            Console.WriteLine($"  Full canonical coverage: {rsFullCoverageCount} / {rsScores.Count} ({rsBarsRequired} XIU sessions required)");
            Console.WriteLine($"  Date-alignment gaps:   {rsAlignmentGapCount}");
            if (rsAlignmentGapSymbols.Count > 0)
                Console.WriteLine($"  Gap symbols:           {string.Join(", ", rsAlignmentGapSymbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))}");
            Console.WriteLine($"  Composite null:        {rsCompositeNull} (missing exact 10-session endpoints or insufficient history)");
            if (rsFullCoverageCount < rsScores.Count)
                Console.WriteLine("  ⚠ Some symbols lack the full canonical stock/sector session window; shorter features compute only from exact available endpoints.");
            if (rsAlignmentGapCount > 0)
                Console.WriteLine("  ⚠ RS inputs contain missing or duplicate exact canonical XIU sessions; metrics requiring those sessions remain null instead of shifting dates.");
            Console.WriteLine();

            // ═══════════════════════════════════════════════════════════════════
            // EVALUATE + RANK (SINGLE PASS) + PICK BEST + SIZE IT
            // ═══════════════════════════════════════════════════════════════════
            // ── On-Balance Volume (OBV) field trend — soft per-symbol confirmation signal ──
            // Granville's OBV is a running cumulative volume tally. Its absolute value is
            // anchor-relative (meaningless alone) — what matters is the *field trend*: the
            // zigzag of UP/DOWN breakouts. We classify each loaded symbol's field trend and
            // turn it into a small additive ranking tilt (NOT a gate):
            //   • Rising  → +ObvSignalWeight   (volume confirms a long)
            //   • Falling → −ObvSignalWeight   (volume contradicts)
            //   • Doubtful / Indeterminate → 0 (no opinion)
            // The tilt is injected into the engine and folded into each lens's ranking key
            // alongside RS. Series are maintained by Hermes (UpdateObvAsync) and seeded by
            // the Sandbox `obv-backfill` probe. See ADR (OBV soft signal).
            var obvRepo = new SymbolObvRepository();
            var obvResults = new Dictionary<string, ObvFieldTrendResult>(StringComparer.OrdinalIgnoreCase);
            var obvTilts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            // Pull only the window the classifier needs (retention window already bounds the table).
            DateTime obvSeriesStart = recommendationDate.AddMonths(-Core.Constants.ObvRetentionMonths);

            int obvRising = 0, obvFalling = 0, obvDoubtful = 0, obvIndeterminate = 0;

            foreach (var (symbol, _) in allBars)
            {
                var series = await obvRepo.GetSeriesFromDateAsync(symbol, obvSeriesStart);
                var result = ObvFieldTrendCalculator.Classify(series, config.ObvBreakoutWindow);
                obvResults[symbol] = result;

                double tilt = result.Trend switch
                {
                    ObvFieldTrend.Rising => config.ObvSignalWeight,
                    ObvFieldTrend.Falling => -config.ObvSignalWeight,
                    _ => 0.0
                };
                if (tilt != 0) obvTilts[symbol] = tilt;

                switch (result.Trend)
                {
                    case ObvFieldTrend.Rising: obvRising++; break;
                    case ObvFieldTrend.Falling: obvFalling++; break;
                    case ObvFieldTrend.Doubtful: obvDoubtful++; break;
                    default: obvIndeterminate++; break;
                }
            }

            // Inject OBV tilts into the engine BEFORE evaluation (mirrors RS injection).
            engine.ObvTilts = obvTilts;

            Console.WriteLine($"On-Balance Volume: classified {obvResults.Count} symbols (window {config.ObvBreakoutWindow}, tilt ±{config.ObvSignalWeight:0.##})");
            Console.WriteLine($"  Field trend: {obvRising} rising, {obvFalling} falling, {obvDoubtful} doubtful, {obvIndeterminate} indeterminate");
            if (obvResults.Count > 0 && obvRising + obvFalling == 0)
                Console.WriteLine("  ⚠ No rising/falling field trends — OBV table may be empty or too short. Run: dotnet run --project Sandbox -- obv-backfill");
            Console.WriteLine();

            // LOAD MARKET CLIMAX (CLX) -- standalone volume-breadth regime signal (diagnostic-only).
            // CLX is Granville's market-wide net OBV-breakout tally across the XIU-60 leaders, a
            // sibling to the A/D Line. It is produced by Hermes (UpdateMarketClimaxAsync) and seeded by
            // the Sandbox `climax-backfill` probe. v1 is DIAGNOSTIC-ONLY: we read recent CLX, classify
            // its confirmation/divergence vs XIU, print it, and surface it in the report -- no gate or
            // ranking change. Phases 2 (composite adjustment) and 3 (regime gate) are deferred.
            var climaxRepo = new MarketClimaxRepository();
            var climaxRecent = await climaxRepo.GetRecentAsync(60);
            var climaxRegime = MarketClimaxCalculator.ClassifyRegime(
                climaxRecent, config.ClimaxDivergenceWindow, config.ClimaxDivergenceThreshold);

            if (climaxRecent.Count > 0)
            {
                var clxLatest = climaxRecent[^1];
                Console.WriteLine(
                    $"Market Climax (CLX): {clxLatest.Clx:+0;-0;0} on {clxLatest.Date:yyyy-MM-dd} " +
                    $"({clxLatest.UpBreakouts} up / {clxLatest.DownBreakouts} down, covered {clxLatest.Covered}/{clxLatest.BasketSize})");
                Console.WriteLine($"  Regime ({config.ClimaxDivergenceWindow}d): {climaxRegime.Description}");
            }
            else
            {
                Console.WriteLine("Market Climax (CLX): [no data] -- run: dotnet run --project Sandbox -- climax-backfill");
            }
            Console.WriteLine();

            // Two independent lenses (ADR-0013): each is a (thesis -> gate stack -> ranking key)
            // triple. The Continuations lens (RS-primary, trend-confirmation gate) DRIVES the
            // executed recommendation (B1). The Breakouts lens (edge+RS, breakout setup gate)
            // is computed for supplemental awareness and JOURNALED only (B3) -- never executed.
            var lensEvaluations = engine.EvaluateAndRank(
                [continuationLens, breakoutLens],
                allBars,
                topN: allBars.Count);
            var continuationEvaluations = lensEvaluations[RankingLens.Continuation];
            var breakoutEvaluations = lensEvaluations[RankingLens.Breakout];
            var top = continuationEvaluations.Take(topPicksToSave).ToList();
            var breakoutTop = breakoutEvaluations.Take(topPicksToSave).ToList();
            var (bestPick, size) = engine.EvaluateBestPickAllIn(top, availableCapital);

            Console.WriteLine(new string('═', 80));
            Console.WriteLine("BEST PICK (SINGLE-POSITION MODE) - CONTINUATIONS LENS (RS-PRIMARY)");
            Console.WriteLine(new string('═', 80));

            Console.WriteLine("\nTop Ranked Candidates:");

            // ═══════════════════════════════════════════════════════════════════
            // HELPER FUNCTIONS
            // ═══════════════════════════════════════════════════════════════════
            static double GetProb(RankedPick pick, string nameEquals) =>
                pick.Signals
                    .FirstOrDefault(s => string.Equals(s.Name, nameEquals, StringComparison.OrdinalIgnoreCase))
                    ?.Score ?? 0;

            static double GetProbContains(RankedPick pick, string nameContains) =>
                pick.Signals
                    .FirstOrDefault(s => s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                    ?.Score ?? 0;

            static double GetBreakoutProb(RankedPick pick) => GetProb(pick, "BreakoutEnhanced");
            static double GetUpProb(RankedPick pick) => GetProb(pick, "BinaryUp10");
            static double GetDownProb(RankedPick pick) => GetProb(pick, "BinaryDown10");

            // ═══════════════════════════════════════════════════════════════════
            // DISPLAY RANKED CANDIDATES
            // ═══════════════════════════════════════════════════════════════════

            // Helper: get pattern signal hint (Buy = Y, anything else = N)
            static string PatternFlag(RankedPick pick, string name) =>
                pick.Signals.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?.Hint == TradeDirection.Buy ? "Y" : "N";

            // Row 1: model/group labels
            // Row 2: column names aligned to data
            Console.WriteLine();
            Console.WriteLine(
                $"{"",3}  {"",8}  {"",6}  {"",7} {"",10}" +
                $"  {"",6}" +
                $"  {"BreakoutEnhanced",12}" +
                $"  {"BinaryUp10  BinaryDown10",23}" +
                $"  {"VolExp10",8} {"RS",9}" +
                $"  {"MaCross Trnd30 Trnd10",21}" +
                $"  {"",18}");
            Console.WriteLine(
                $"{"#",-3}  {"Symbol",-8}  {"Action",-6}  {"Price",7} {"Shrs",5} {"Vol20d",8}" +
                $"  {"Comp",6}" +
                $"  {"Brk%",6} {"BrkRaw",7}" +
                $"  {"P(Up)",6} {"P(Dn)",6} {"Edge",6}" +
                $"  {"Vol%",6} {"RScomp",10} {"CompZ",8} {"RS10d",9}" +
                $"  {"MA",3} {"T30",4} {"T10",4}" +
                $"  {"Gate",18}");
            Console.WriteLine(new string('─', 140));

            int rank = 1;
            foreach (var p in top)
            {
                double breakout = GetBreakoutProb(p);
                double pUp = GetUpProb(p);
                double pDown = GetDownProb(p);
                double edge = pUp - pDown;
                double volExp = GetProbContains(p, "VolExpansion");

                rsScores.TryGetValue(p.Symbol, out var rsRow);
                // Note: raw 10d return differences are small (most stocks ±0.5% vs sector/market on a 10d window),
                // so we display 4 decimals to avoid silent rounding to 0.000. Nulls (insufficient history)
                // are rendered as "null" rather than coerced to 0 — see ADR follow-up on Z-score composite.
                string rsCompStr = rsRow?.CompositeScore is double rsC ? rsC.ToString("+0.0000;-0.0000;0.0000") : "null";
                string rsCompZStr = rsRow?.CompositeScoreZ is double rsCz ? rsCz.ToString("+0.00;-0.00;0.00") : "null";
                string rs10dStr = rsRow?.RS_StockVsMarket_10d is double rs10 ? rs10.ToString("+0.000;-0.000;0.000") : "null";

                string edgeStr = edge >= 0 ? $"+{edge:P0}" : $"{edge:P0}";

                decimal lastPrice = allBars.TryGetValue(p.Symbol, out var priceBars) ? (decimal)priceBars[^1].Close : 0m;
                long avgVolume = allBars.TryGetValue(p.Symbol, out var volBars)
                    ? (long)volBars.TakeLast(20).Average(b => (double)b.Volume)
                    : 0;
                int affordableShares = lastPrice > 0 ? (int)(deployableCapital / lastPrice) : 0;

                // Pattern model results
                string maCross = PatternFlag(p, "MaCrossover");
                string trend30 = PatternFlag(p, "Trend30");
                string trend10 = PatternFlag(p, "Trend10");

                // Gate result — show first blocking gate name
                string gateStatus = "Pass (all gates)";
                if (p.GateTrace != null)
                {
                    var blocked = p.GateTrace.FirstOrDefault(g => !g.Passed);
                    if (blocked.Reason != null)
                        gateStatus = $"Fail: {blocked.GateName}";
                }

                Console.WriteLine(
                    $"{rank,-3}  {p.Symbol,-8}  {p.Direction,-6}  {lastPrice,7:C2} {affordableShares,5} {avgVolume,8:N0}" +
                    $"  {p.CompositeScore,6:P0}" +
                    $"  {breakout,6:P0} {breakout,7:P1}" +
                    $"  {pUp,6:P0} {pDown,6:P0} {edgeStr,6}" +
                    $"  {volExp,6:P0} {rsCompStr,10} {rsCompZStr,8} {rs10dStr,9}" +
                    $"  {maCross,3} {trend30,4} {trend10,4}" +
                    $"  {gateStatus,-18}");
                rank++;
            }

            // ═══════════════════════════════════════════════════════════════════════════════
            // SUPPLEMENTAL LENS: BREAKOUTS (journaled, not executed — ADR-0013)
            // ═══════════════════════════════════════════════════════════════════════════════
            // Shown for situational awareness and as a continuity baseline against the
            // Continuations lens. The executed recommendation always comes from Continuations.
            Console.WriteLine();
            Console.WriteLine(new string('─', 80));
            Console.WriteLine("SUPPLEMENTAL: BREAKOUTS LENS (Edge+RScomp ranking — journaled, NOT executed)");
            Console.WriteLine(new string('─', 80));
            Console.WriteLine(
                $"{"#",-3}  {"Symbol",-8}  {"Action",-6}  {"P(Up)",6} {"P(Dn)",6} {"Edge",6}" +
                $"  {"Brk%",6}  {"RScomp",10}  {"Gate",-18}");
            Console.WriteLine(new string('─', 80));

            int brRank = 1;
            foreach (var p in breakoutTop)
            {
                double pUp = GetUpProb(p);
                double pDown = GetDownProb(p);
                double edge = pUp - pDown;
                double breakout = GetBreakoutProb(p);
                string edgeStr = edge >= 0 ? $"+{edge:P0}" : $"{edge:P0}";

                rsScores.TryGetValue(p.Symbol, out var rsRowB);
                string rsCompStr = rsRowB?.CompositeScore is double rsCb ? rsCb.ToString("+0.0000;-0.0000;0.0000") : "null";

                string gateStatus = "Pass (all gates)";
                if (p.GateTrace != null)
                {
                    var blocked = p.GateTrace.FirstOrDefault(g => !g.Passed);
                    if (blocked.Reason != null)
                        gateStatus = $"Fail: {blocked.GateName}";
                }

                Console.WriteLine(
                    $"{brRank,-3}  {p.Symbol,-8}  {p.Direction,-6}  {pUp,6:P0} {pDown,6:P0} {edgeStr,6}" +
                    $"  {breakout,6:P0}  {rsCompStr,10}  {gateStatus,-18}");
                brRank++;
            }
            Console.WriteLine();

            // Build one typed presentation snapshot from the same evaluated facts used by
            // Delphi's structured reports. Hosts render this data without parsing console text.
            var reportSectorSnapshots = await sectorIndexRepo.GetRecentAsync(TsxSectorSymbols.AllSymbols, days: 3);
            DateTime? reportSectorDate = reportSectorSnapshots
                .Where(snapshot => snapshot.Date.Date <= marketDataAsOf)
                .Select(snapshot => (DateTime?)snapshot.Date.Date)
                .Max();
            var todaySectorSnapshots = reportSectorDate.HasValue
                ? reportSectorSnapshots.Where(snapshot => snapshot.Date.Date == reportSectorDate.Value).ToList()
                : [];

            var report = new DelphiReportBuilder
            {
                RecommendationDate = recommendationDate,
                MarketDataAsOf = marketDataAsOf,
                Regime = regime,
                AdLine = adLine,
                BreadthScore = breadthScore,
                BearishDivergence = bearishDivergence,
                Granville = granvilleForecast,
                LeadershipHistoryDays = leadershipHistoryDays,
                LeadershipActiveBreadthDays = leadershipActiveBreadthDays,
                LeadershipActiveBreadthRequired = leadershipActiveBreadthRequired,
                Weighting = weightingSnapshot,
                MarketTape = marketTape,
                SectorSnapshots = todaySectorSnapshots,
                UsIndexBars = usIndexBars,
                TopPicks = top,
                BestPick = bestPick,
                Size = size,
                RsScores = rsScores,
                AllBars = allBars,
                ObvResults = obvResults,
                ObvSignalWeight = config.ObvSignalWeight,
                MarketClimax = climaxRecent,
                ClimaxRegime = climaxRegime,
                ClimaxDivergenceWindow = config.ClimaxDivergenceWindow,
                ClimaxDivergenceThreshold = config.ClimaxDivergenceThreshold,
                DiscoveredSymbols = symbols.Count,
                LoadedSymbols = loaded,
                SkippedHistory = skipped,
                SkippedStaleHistory = skippedStaleHistory,
                StaleHistoryExclusions = staleHistoryExclusions,
                SkippedPrice = skippedPrice,
                SkippedLowPrice = skippedLowPrice,
                SkippedLowVolume = skippedLowVolume,
                SkippedLeveragedEtp = skippedLeveraged,
                MinPriceFloor = minPriceFloor,
                MinVolume20d = minVolume20d,
                DeployableCapital = deployableCapital,
                RsFallbackToXiuCount = rsFallbackToXiu,
                RsFallbackSymbols = rsFallbackSymbols,
                RsFullCoverageCount = rsFullCoverageCount,
                RsAlignmentGapCount = rsAlignmentGapCount,
                RsAlignmentGapSymbols = rsAlignmentGapSymbols,
                RsCompositeNullCount = rsCompositeNull,
                RsMinSectorBars = rsMinSectorBars,
                RsMaxSectorBars = rsMaxSectorBars,
                RsBarsRequired = rsBarsRequired,
                StrategyVersionName = activeStrategy?.VersionName ?? "Default",
                StrategyDescription = activeStrategy?.Description ?? "Built-in strategy defaults",
                StrategyInitialCodeCommit = activeStrategy?.InitialCodeCommit ?? string.Empty,
                StrategyDecisionRef = activeStrategy?.DecisionRef ?? string.Empty,
                StrategyConfig = config,
                PatternModels = Core.ML.Engine.Patterns.PatternRegistry.All
                    .Select(model => model.TaskType)
                    .ToArray(),
                ProfitModels = Core.ML.Engine.Profit.ProfitModelRegistry.All
                    .Select(model => $"{model.TaskType} · {model.Role} · weight {model.CompositeWeight:+0.00;-0.00;0.00}")
                    .ToArray()
            };

            string diagnosticReport = report.BuildDiagnostic();
            string summaryReport = report.BuildSummary();
            DelphiPresentationSnapshot presentationSnapshot = report.BuildPresentationSnapshot(
                summaryReport,
                diagnosticReport);

            // ═══════════════════════════════════════════════════════════════════
            // APPEND IMMUTABLE CALIBRATION EVIDENCE (ADR-0020)
            // ═══════════════════════════════════════════════════════════════════
            if (saveToDB)
            {
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var code = CalibrationProvenance.ResolveCode();
                var modelProvenance = await CalibrationProvenance.ResolveLoadedModelsAsync();
                int expectedModels = Core.ML.Engine.Profit.ProfitModelRegistry.All.Count;
                CalibrationRunAuditDecision audit = CalibrationRunAuditPolicy.Evaluate(
                    code,
                    modelProvenance.Count,
                    expectedModels);

                var runId = Guid.NewGuid();
                var candidateIds = allBars.Keys.ToDictionary(s => s, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
                var continuationBySymbol = continuationEvaluations.ToDictionary(p => p.Symbol, StringComparer.OrdinalIgnoreCase);
                var continuationPublished = top.Select(p => p.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var breakoutPublished = breakoutTop.Select(p => p.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var candidates = new List<CalibrationCandidateEvidence>(allBars.Count);
                foreach (var (symbol, bars) in allBars)
                {
                    var p = continuationBySymbol[symbol];
                    var lastBar = bars[^1];
                    rsScores.TryGetValue(symbol, out var rs);
                    obvResults.TryGetValue(symbol, out var obv);
                    var payload = new
                    {
                        schemaVersion = CalibrationSchemaVersions.CandidateSnapshot,
                        signals = p.Signals,
                        relativeStrength = rs,
                        obv,
                        observationBar = lastBar
                    };

                    candidates.Add(new CalibrationCandidateEvidence(
                        candidateIds[symbol], runId, symbol, marketDataAsOf,
                        lastBar.Open, lastBar.High, lastBar.Low, lastBar.Close, lastBar.Volume,
                        GetUpProb(p), GetDownProb(p), GetBreakoutProb(p), GetProbContains(p, "VolExpansion"),
                        p.DirectionEdge, p.CompositeScore, rs?.CompositeScore, rs?.CompositeScoreZ,
                        obv?.Trend.ToString(), obvTilts.GetValueOrDefault(symbol, 0),
                        JsonSerializer.Serialize(payload, jsonOptions)));
                }

                CalibrationLensEvidence BuildLensEvidence(
                    RankedPick pick, LensDefinition lens, int rank, bool published)
                {
                    double rsValue = rsScores.TryGetValue(pick.Symbol, out var rs) ? rs.CompositeScore ?? 0 : 0;
                    double obvValue = obvTilts.GetValueOrDefault(pick.Symbol, 0);
                    var trace = pick.GateTrace ?? [];
                    string? firstFailed = trace.FirstOrDefault(g => !g.Passed).GateName;
                    return new CalibrationLensEvidence(
                        Guid.NewGuid(), candidateIds[pick.Symbol], lens.Label, pick.Direction.ToString(),
                        pick.Direction == TradeDirection.Buy, rank, lens.PrimaryKey(pick, rsValue, obvValue),
                        published, firstFailed,
                        JsonSerializer.Serialize(new LensTracePayload(CalibrationSchemaVersions.LensTrace, trace), jsonOptions));
                }

                var lensEvidence = new List<CalibrationLensEvidence>(allBars.Count * 2);
                for (int i = 0; i < continuationEvaluations.Count; i++)
                {
                    var p = continuationEvaluations[i];
                    lensEvidence.Add(BuildLensEvidence(p, continuationLens, i + 1, continuationPublished.Contains(p.Symbol)));
                }
                for (int i = 0; i < breakoutEvaluations.Count; i++)
                {
                    var p = breakoutEvaluations[i];
                    lensEvidence.Add(BuildLensEvidence(p, breakoutLens, i + 1, breakoutPublished.Contains(p.Symbol)));
                }

                var runContext = new
                {
                    schemaVersion = CalibrationSchemaVersions.Feature,
                    regime,
                    breadthScore,
                    bearishDivergence,
                    granvilleForecast,
                    marketClimax = climaxRecent,
                    climaxRegime,
                    availableCapital,
                    reserveCashPercent,
                    minPriceFloor,
                    minVolume20d,
                    maxPriceForMinLot,
                    presentation = presentationSnapshot
                };
                var run = new CalibrationRunEvidence(
                    runId, calibrationPurpose, recommendationDate, marketDataAsOf, runStartedUtc,
                    strategyVersionId, JsonSerializer.Serialize(config, jsonOptions),
                    JsonSerializer.Serialize(modelProvenance, jsonOptions), JsonSerializer.Serialize(runContext, jsonOptions),
                    code, audit.State, audit.Message,
                    symbols.Count, allBars.Count, skipped, skippedStaleHistory, skippedPrice,
                    skippedLowPrice, skippedLowVolume, skippedLeveraged);

                await new CalibrationEvidenceRepository().AppendAsync(new CalibrationEvidenceBatch(run, candidates, lensEvidence));
                Console.WriteLine($"✓ Appended immutable calibration run {runId} ({candidates.Count} candidates, {lensEvidence.Count} lens evaluations, audit={audit.State})");
            }

            // ═══════════════════════════════════════════════════════════════════
            // REFRESH OPERATIONAL DAILY PICKS
            // ═══════════════════════════════════════════════════════════════════
            if (saveOperationalState)
            {
                var pickDate = recommendationDate;
                var operationalPicks = new List<DelphiOperationalPick>(top.Count + breakoutTop.Count);

                Console.WriteLine($"\nPublishing {top.Count + breakoutTop.Count} operational picks atomically...");

                var strategyRef = activeStrategy is not null
                    ? new Core.Oracle.StrategyVersionRef(activeStrategy.VersionId, activeStrategy.VersionName)
                    : null;

                int savedRank = 1;
                int dossiersSaved = 0;
                foreach (var p in top)
                {
                    double breakout = GetBreakoutProb(p);
                    double pUp = GetUpProb(p);
                    double pDown = GetDownProb(p);
                    double volExp = GetProbContains(p, "VolExpansion");
                    double relStrength = GetProbContains(p, "RelStrength");

                    var pickId = Guid.NewGuid();

                    // ── Phase 1 of the Oracle layer: emit a DecisionDossier per pick.
                    // The dossier is the audit unit fed to the downstream LLM layer
                    // (see Docs/oracle-rules.md and Docs/oracle-phases.md). It MUST
                    // remain strictly downstream of TradeDecisionEngine — no value
                    // computed here flows back into scoring.
                    decimal lastPriceForDossier = allBars.TryGetValue(p.Symbol, out var priceBarsDossier)
                        ? (decimal)priceBarsDossier[^1].Close
                        : 0m;

                    rsScores.TryGetValue(p.Symbol, out var rsRowForDossier);

                    Core.Oracle.SizingSnapshot? sizingForDossier = null;
                    if (savedRank == 1 && size != null)
                    {
                        int shares = lastPriceForDossier > 0
                            ? (int)(size.SuggestedSize / lastPriceForDossier)
                            : 0;
                        sizingForDossier = new Core.Oracle.SizingSnapshot(
                            SuggestedSize: size.SuggestedSize,
                            AllocationPercent: (double)size.AllocationPercent,
                            Shares: shares,
                            Reason: size.Reason);
                    }

                    var dossier = Core.Oracle.DecisionDossierBuilder.Build(
                        pickDate: pickDate,
                        pickId: pickId,
                        rank: savedRank,
                        pick: p,
                        lastPrice: lastPriceForDossier,
                        regime: regime,
                        breadthScore: breadthScore,
                        breadthVetoThreshold: config.BreadthVetoThreshold,
                        granville: granvilleForecast,
                        rs: rsRowForDossier,
                        sizing: sizingForDossier,
                        strategy: strategyRef);

                    operationalPicks.Add(new DelphiOperationalPick(
                        pickId,
                        pickDate,
                        p.Symbol,
                        savedRank,
                        p.Direction.ToString(),
                        p.CompositeScore,
                        breakout,
                        pUp,
                        volExp,
                        relStrength > 0 ? relStrength : null,
                        p.ExpectedReturn,
                        savedRank == 1 && size != null ? size.SuggestedSize : null,
                        savedRank == 1 && size != null ? (double)size.AllocationPercent : null,
                        strategyVersionId,
                        savedRank == 1 ? $"Top pick. P↓={pDown:P0}, Edge={pUp - pDown:P0}" : null,
                        "Continuation",
                        dossier));
                    dossiersSaved++;

                    savedRank++;
                }

                // ── Journal the Breakouts lens (B3): picks only, no dossiers/sizing.
                // These are never executed; they exist so the two theses' outcomes can be
                // compared later via the [Lens] discriminator (ADR-0013).
                int breakoutRank = 1;
                foreach (var p in breakoutTop)
                {
                    double brBreakout = GetBreakoutProb(p);
                    double brUp = GetUpProb(p);
                    double brVolExp = GetProbContains(p, "VolExpansion");
                    double brRelStrength = GetProbContains(p, "RelStrength");

                    operationalPicks.Add(new DelphiOperationalPick(
                        Guid.NewGuid(),
                        pickDate,
                        p.Symbol,
                        breakoutRank,
                        p.Direction.ToString(),
                        p.CompositeScore,
                        brBreakout,
                        brUp,
                        brVolExp,
                        brRelStrength > 0 ? brRelStrength : null,
                        p.ExpectedReturn,
                        SuggestedSize: null,
                        AllocationPercent: null,
                        StrategyVersionId: strategyVersionId,
                        Notes: breakoutRank == 1 ? "Breakouts lens top pick (journaled, not executed)" : null,
                        Lens: "Breakout",
                        Dossier: null));
                    breakoutRank++;
                }

                DelphiOperationalPublicationResult publication =
                    await new DelphiOperationalPublicationRepository().ReplaceAsync(
                        pickDate,
                        operationalPicks,
                        granvilleForecast,
                        cancellationToken);
                Console.WriteLine(
                    $"✓ Published {top.Count} Continuation and {breakoutTop.Count} Breakout picks, " +
                    $"{dossiersSaved} dossiers, and {publication.GranvilleLogCount} Granville logs " +
                    $"for {pickDate:yyyy-MM-dd} in one transaction.");
                if (publication.PickCount == 0)
                    Console.WriteLine("✓ Cleared stale same-date operational recommendations for the successful zero-result run.");
            }

            // ═══════════════════════════════════════════════════════════════════
            // DISPLAY BEST PICK DETAILS
            // ═══════════════════════════════════════════════════════════════════
            if (bestPick == null || size == null || size.SuggestedSize <= 0)
            {
                var reason = size?.Reason ?? "Unknown (size is null)";
                Console.WriteLine($"\nNo qualifying trade found. Reason: {reason}");
                Console.WriteLine(diagnosticReport);
                Console.WriteLine(summaryReport);
                return new DelphiWorkflowRunResult(
                    true,
                    $"Evaluation completed without a qualifying trade: {reason}",
                    calibrationPurpose,
                    recommendationDate,
                    marketDataAsOf,
                    runStartedUtc,
                    DateTime.UtcNow,
                    top.Count,
                    breakoutTop.Count,
                    diagnosticReport,
                    summaryReport,
                    presentationSnapshot);
            }

            double bestBreakout = GetBreakoutProb(bestPick);
            double bestUp = GetUpProb(bestPick);
            double bestDown = GetDownProb(bestPick);
            double bestEdge = bestUp - bestDown;
            double bestVolExp = GetProbContains(bestPick, "VolExpansion");
            double bestRelStrength = GetProbContains(bestPick, "RelStrength");

            // Best pick RS
            double bestRsComposite = rsScores.TryGetValue(bestPick.Symbol, out var bestRsRow) && bestRsRow.CompositeScore.HasValue
                ? bestRsRow.CompositeScore.Value
                : 0;

            Console.WriteLine($"\n{"═",-80}");
            Console.WriteLine("RECOMMENDATION");
            Console.WriteLine($"{"═",-80}");
            Console.WriteLine($"Symbol:          {bestPick.Symbol}");
            Console.WriteLine($"Direction:       {bestPick.Direction}");
            Console.WriteLine($"Composite Score: {bestPick.CompositeScore:P1}");

            // Show Granville influence on composite
            if (granvilleForecast is not null)
            {
                Console.WriteLine($"  (includes Granville adj: {granvilleForecast.CompositeAdjustment:+0.000;-0.000})");
            }

            Console.WriteLine();
            Console.WriteLine($"Setup Signal:");
            Console.WriteLine($"  Breakout Prob: {bestBreakout:P1}");
            Console.WriteLine();
            Console.WriteLine($"Direction Signals:");
            Console.WriteLine($"  P(Up +4%):     {bestUp:P1}");
            Console.WriteLine($"  P(Down -4%):   {bestDown:P1}");
            Console.WriteLine($"  Direction Edge:{bestEdge:+0.0%;-0.0%} {(bestEdge > 0 ? "✓ Bullish" : "✗ Bearish")}");
            Console.WriteLine();
            Console.WriteLine($"Confirmation Signals:");
            Console.WriteLine($"  Vol Expansion: {bestVolExp:P1}");
            if (bestRelStrength > 0)
                Console.WriteLine($"  Rel Strength:  {bestRelStrength:P1}");
            Console.WriteLine($"  RS Composite:  {bestRsComposite:+0.000;-0.000}");
            Console.WriteLine();
            Console.WriteLine($"Position:");
            Console.WriteLine($"  Allocate:      {size.SuggestedSize:C2} ({size.AllocationPercent:P1})");
            Console.WriteLine($"  Reason:        {size.Reason}");

            Console.WriteLine("\nGate Pipeline (best pick):");
            if (bestPick.GateTrace != null)
            {
                foreach (var g in bestPick.GateTrace)
                {
                    string icon = g.Passed ? "✓" : "✗";
                    string reason = g.Reason ?? "Passed";
                    Console.WriteLine($"  {icon} {g.GateName,-18} {reason}");
                }


            }

            Console.WriteLine("\nAll Signals (best pick):");
            Console.WriteLine("  Signal hints use ModelRegistry thresholds shown below; trade eligibility uses the strategy gate thresholds printed at startup.");
            foreach (var s in bestPick.Signals)
            {
                Console.WriteLine($"  [{s.Hint,-5}] {s.Name,-25} Score={s.Score:0.###} {s.Notes}");
            }

            Console.WriteLine(diagnosticReport);
            Console.WriteLine(summaryReport);

            return new DelphiWorkflowRunResult(
                true,
                "Evaluation and persistence completed.",
                calibrationPurpose,
                recommendationDate,
                marketDataAsOf,
                runStartedUtc,
                DateTime.UtcNow,
                top.Count,
                breakoutTop.Count,
                diagnosticReport,
                summaryReport,
                presentationSnapshot);
        }
        finally
        {
            RunGate.Release();
        }
    }
}
