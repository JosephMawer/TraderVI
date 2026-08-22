using Core.Db;
using Core.Indicators;
using Core.Indicators.Granville;
using Core.Config;
using Core.ML;
using Core.Runtime;
using Core.Trader;
using Core.Trader.Gates;
using Core.TMX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine("=== The Oracle Of Delphi ===\n");

// The recommendation date is the run date and remains the persistence key for
// DailyPick, DecisionDossier, and GranvilleIndicatorLog. The market-data date is
// reported separately once the latest completed A/D session has been loaded.
DateTime recommendationDate = DateTime.Today;

// ═══════════════════════════════════════════════════════════════════
// CONFIGURATION (aggressive single-position rotation)
// ═══════════════════════════════════════════════════════════════════
decimal availableCapital = 700.00m;
int minBarsRequired = 55;              // Increased for enhanced features
decimal reserveCashPercent = 0m;//0.02m;
double minExpectedReturn = 0.00;
int maxSymbolsToScan = 500;
int topPicksToSave = 25;
bool saveToDB = true;

Console.WriteLine($"Available Capital: ${availableCapital:N2}");
Console.WriteLine($"Reserve Cash:      {reserveCashPercent:P0}");
Console.WriteLine($"Save to DB:        {saveToDB}");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
// LOAD ACTIVE STRATEGY VERSION → DERIVE RUNTIME CONFIG
// ═══════════════════════════════════════════════════════════════════
var strategyRepo = new StrategyVersionRepository();
var activeStrategy = await strategyRepo.GetActiveVersion();

Guid? strategyVersionId = activeStrategy?.VersionId;
StrategyConfig config = activeStrategy?.ToConfig() ?? StrategyConfig.Default;

if (activeStrategy != null)
{
    Console.WriteLine($"Strategy Version:  {activeStrategy.VersionName}");
    Console.WriteLine($"Description:       {activeStrategy.Description}");
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
var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry(config);
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
DateTime marketDataAsOf = adLine.Count > 0
    ? adLine[^1].Date.Date
    : xiuBars.Count > 0
        ? xiuBars[^1].Date.Date
        : recommendationDate;

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
    Console.WriteLine($"  Leadership history: {leadershipHistory.Count} days");
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

    if (leadershipHistory.Count < 12)
    {
        Console.WriteLine("  ⚠️  Insufficient leadership history (< 12 days) — Leadership indicators will degrade to neutral.");
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

    // ── Log to database ──
    if (saveToDB)
    {
        var granvilleLog = new GranvilleIndicatorLogRepository();
        var evalDate = recommendationDate;
        await granvilleLog.DeleteByDateAsync(evalDate);
        await granvilleLog.LogForecastAsync(evalDate, granvilleForecast);
        Console.WriteLine($"  ✓ Granville indicators logged to [dbo].[GranvilleIndicatorLog] for {evalDate:yyyy-MM-dd}");
        Console.WriteLine();
    }
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
    if (string.IsNullOrWhiteSpace(shortName)) return false;
    string n = shortName;
    string[] markers =
    [
        "2x", "3x", "-2x", "-3x", "(2X)", "(3X)",
        "BetaPro", "BtaPro", "MegaLong", "MegaShort",
        "SavvyLong", "SavvyShort", "SavvyLg", "SavvyLng", "SavvyShrt",
        "LFG Daily", "Inverse", "Invrs", "Leveraged",
        "DlyBl", "DlyBr", "DailyInvrs"
    ];
    foreach (var m in markers)
    {
        if (n.Contains(m, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
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
int skippedPrice = 0;
int skippedLowPrice = 0;
int skippedLowVolume = 0;

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

foreach (var symbol in symbols)
{
    var bars = await quoteRepo.GetDailyBarsAsync(symbol);

    if (bars.Count < minBarsRequired)
    {
        skipped++;
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

Console.WriteLine($"Loaded: {loaded} symbols | Skipped: {skipped} (insufficient history), {skippedPrice} (price > ${maxPriceForMinLot:N2}), {skippedLowPrice} (price < ${minPriceFloor:N2}), {skippedLowVolume} (20d vol < {minVolume20d:N0}), {skippedLeveraged} (lev/inv ETP)");
Console.WriteLine($"Sorted by: avg 20-day volume (most liquid first)\n");

if (allBars.Count == 0)
{
    Console.WriteLine("No symbols with sufficient data to evaluate.");
    return;
}

// ═══════════════════════════════════════════════════════════════════
// COMPUTE LIVE RELATIVE STRENGTH (per-stock, before ranking)
// ═══════════════════════════════════════════════════════════════════
var stockSectorMap = stockSectorMappings
    .ToDictionary(m => m.Symbol, m => m.SectorIndexSymbol, StringComparer.OrdinalIgnoreCase);

// XIU closes (already loaded above for regime)
var xiuCloses = xiuBars.Select(b => (double)b.Close).ToList();

// Sector index closes keyed by sector symbol — wider window for RS horizons (60d + 20d Z)
var rsSectorSnapshots = await sectorIndexRepo.GetRecentAsync(TsxSectorSymbols.AllSymbols, days: 80);

var sectorClosesBySector = rsSectorSnapshots
    .GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(
        g => g.Key,
        g => g.OrderBy(s => s.Date).Select(s => (double)s.Price).ToList(),
        StringComparer.OrdinalIgnoreCase);

var rsScores = new Dictionary<string, Core.RelativeStrength.RelativeStrengthRow>(StringComparer.OrdinalIgnoreCase);
var rsFallbackSymbols = new List<string>();

// RS coverage counters — surface degenerate/missing RS instead of silently emitting null.
int rsFallbackToXiu = 0;
int rsCompositeNull = 0;
int rsBarsRequired = 80; // max horizon (60d) + Z window (20d) in RelativeStrengthCalculator

int rsMinSectorBars = sectorClosesBySector.Count > 0
    ? sectorClosesBySector.Values.Min(v => v.Count)
    : 0;
int rsMaxSectorBars = sectorClosesBySector.Count > 0
    ? sectorClosesBySector.Values.Max(v => v.Count)
    : 0;

foreach (var (symbol, bars) in allBars)
{
    var stockCloses = bars.Select(b => (double)b.Close).ToList();

    // Determine this stock's sector index
    string? sectorSymbol = stockSectorMap.TryGetValue(symbol, out var sec) ? sec : null;

    List<double>? sectorCloses = null;
    if (sectorSymbol != null)
        sectorClosesBySector.TryGetValue(sectorSymbol, out sectorCloses);

    bool usedFallback = sectorCloses == null;
    if (usedFallback)
    {
        rsFallbackToXiu++;
        rsFallbackSymbols.Add(symbol);
    }

    // Compute RS — stock vs market always works; stock vs sector only if we have sector data.
    // For missing sector data, use XIU as a fallback (RS_StockVsSector ≈ 0).
    var rs = Core.RelativeStrength.RelativeStrengthCalculator.Compute(
        stockCloses: stockCloses,
        sectorCloses: sectorCloses ?? xiuCloses,
        marketCloses: xiuCloses,
        symbol: symbol,
        date: DateOnly.FromDateTime(marketDataAsOf),
        sectorIndexSymbol: sectorSymbol ?? "XIU");

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
Console.WriteLine($"  Sector bars (min/max): {rsMinSectorBars} / {rsMaxSectorBars} (required >= {rsBarsRequired} for full composite)");
Console.WriteLine($"  Composite null:        {rsCompositeNull} (insufficient bars for 10d/60d horizons or 20d Z window)");
if (rsMinSectorBars > 0 && rsMinSectorBars < rsBarsRequired)
{
    Console.WriteLine($"  ⚠ Sector index history too short ({rsMinSectorBars} < {rsBarsRequired} bars).");
    Console.WriteLine($"    RS composite will be null for sector-mapped symbols — top-pick RScomp/CompZ/RS10d columns will display 'null'.");
    Console.WriteLine($"    Action: backfill TraderDB.dbo.SectorIndices to >= {rsBarsRequired} trading days of history.");
}
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
var top = engine.EvaluateAndRank(continuationLens, allBars, topN: topPicksToSave);
var breakoutTop = engine.EvaluateAndRank(breakoutLens, allBars, topN: topPicksToSave);
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
// ═══════════════════════════════════════════════════════════════════
// SAVE DAILY PICKS TO DATABASE
// ═══════════════════════════════════════════════════════════════════
if (saveToDB && top.Count > 0)
{
    var pickDate = recommendationDate;
    var pickRepo = new DailyPickRepository();
    var dossierRepo = new DecisionDossierRepository();
    var narrativeRepo = new Core.Db.LlmNarrativeRepository();

    // Delete child→parent to respect FKs:
    //   LlmNarrative → DecisionDossier → DailyPick
    await narrativeRepo.DeleteByDateAsync(pickDate);
    await dossierRepo.DeleteByDateAsync(pickDate);
    await pickRepo.DeletePicksByDate(pickDate);

    Console.WriteLine($"\nSaving {top.Count} picks to database...");

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

        var pickId = await pickRepo.InsertPick(
            pickDate: pickDate,
            symbol: p.Symbol,
            rank: savedRank,
            direction: p.Direction.ToString(),
            compositeScore: p.CompositeScore,
            breakoutProb: breakout,
            directionProb: pUp,
            volExpansionProb: volExp,
            relStrengthProb: relStrength > 0 ? relStrength : null,
            expectedReturn: p.ExpectedReturn,
            suggestedSize: savedRank == 1 && size != null ? size.SuggestedSize : null,
            allocationPercent: savedRank == 1 && size != null ? (double)size.AllocationPercent : null,
            strategyVersionId: strategyVersionId,
            notes: savedRank == 1 ? $"Top pick. P↓={pDown:P0}, Edge={pUp - pDown:P0}" : null,
            lens: "Continuation");

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

        await dossierRepo.InsertAsync(dossier);
        dossiersSaved++;

        savedRank++;
    }

    Console.WriteLine($"✓ Saved {top.Count} Continuation picks to [dbo].[DailyPick] for {pickDate:yyyy-MM-dd}");
    Console.WriteLine($"✓ Saved {dossiersSaved} dossiers to [dbo].[DecisionDossier]");

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

        await pickRepo.InsertPick(
            pickDate: pickDate,
            symbol: p.Symbol,
            rank: breakoutRank,
            direction: p.Direction.ToString(),
            compositeScore: p.CompositeScore,
            breakoutProb: brBreakout,
            directionProb: brUp,
            volExpansionProb: brVolExp,
            relStrengthProb: brRelStrength > 0 ? brRelStrength : null,
            expectedReturn: p.ExpectedReturn,
            strategyVersionId: strategyVersionId,
            notes: breakoutRank == 1 ? "Breakouts lens top pick (journaled, not executed)" : null,
            lens: "Breakout");
        breakoutRank++;
    }

    Console.WriteLine($"✓ Journaled {breakoutTop.Count} Breakout picks to [dbo].[DailyPick] (lens='Breakout', not executed)");
}

// ═══════════════════════════════════════════════════════════════════
// DISPLAY BEST PICK DETAILS
// ═══════════════════════════════════════════════════════════════════
if (bestPick == null || size == null || size.SuggestedSize <= 0)
{
    var reason = size?.Reason ?? "Unknown (size is null)";
    Console.WriteLine($"\nNo qualifying trade found. Reason: {reason}");
    return;
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

// ═══════════════════════════════════════════════════════════════════
// STRUCTURED REPORTS (Diagnostic + Summary)
// ═══════════════════════════════════════════════════════════════════

// Load latest sector snapshots for report
var reportSectorSnapshots = await sectorIndexRepo.GetRecentAsync(TsxSectorSymbols.AllSymbols, days: 1);
var latestSectorDate = reportSectorSnapshots.Count > 0 ? reportSectorSnapshots.Max(s => s.Date) : (DateTime?)null;
var todaySectorSnapshots = latestSectorDate.HasValue
    ? reportSectorSnapshots.Where(s => s.Date.Date == latestSectorDate.Value.Date).ToList()
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
    LoadedSymbols = loaded,
    SkippedHistory = skipped,
    SkippedPrice = skippedPrice,
    SkippedLowPrice = skippedLowPrice,
    SkippedLowVolume = skippedLowVolume,
    SkippedLeveragedEtp = skippedLeveraged,
    MinPriceFloor = minPriceFloor,
    MinVolume20d = minVolume20d,
    DeployableCapital = deployableCapital,
    RsFallbackToXiuCount = rsFallbackToXiu,
    RsFallbackSymbols = rsFallbackSymbols,
    RsCompositeNullCount = rsCompositeNull,
    RsMinSectorBars = rsMinSectorBars,
    RsMaxSectorBars = rsMaxSectorBars,
    RsBarsRequired = rsBarsRequired
};

Console.WriteLine(report.BuildDiagnostic());
Console.WriteLine(report.BuildSummary());
