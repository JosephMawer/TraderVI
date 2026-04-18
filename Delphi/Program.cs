using Core.Db;
using Core.Indicators;
using Core.Indicators.Granville;
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

// ═══════════════════════════════════════════════════════════════════
// CONFIGURATION (aggressive single-position rotation)
// ═══════════════════════════════════════════════════════════════════
decimal availableCapital = 700.00m;
int minBarsRequired = 55;              // Increased for enhanced features
decimal reserveCashPercent = 0m;//0.02m;
double minExpectedReturn = 0.00;
int maxSymbolsToScan = 500;
int topPicksToSave = 25;
bool saveToDB = false;

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
engine.RankingMode = RankingMode.Probability;

engine.Sizer = new PositionSizer(availableCapital)
{
    Strategy = AllocationStrategy.SinglePositionAllIn,
    ReserveCashPercent = reserveCashPercent,
    MinPositionSize = 25m,
    MinExpectedReturn = minExpectedReturn,
    MinConfidence = config.MinCompositeScore,
    RequireBothSignals = false
};

Console.WriteLine($"[DelphiBootstrap] Ranking mode:    {engine.RankingMode}");

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

var sectorIndexRepo = new SectorIndexRepository();
var stockSectorRepo = new StockSectorRepository();

// Load once — reused by both Granville and RS sections
var stockSectorMappings = await stockSectorRepo.GetAllAsync();

if (adLine.Count >= 2)
{
    // Load the cyclical basket sector snapshots required by Disparity.
    // We pull a small recent window so 1-day and 5-day comparisons have enough history.
    var granvilleSectorSnapshots = await sectorIndexRepo.GetRecentAsync(TsxSectorSymbols.CyclicalBasket, days: 10);

    var granvilleContext = new GranvilleMarketContext
    {
        Today = adLine[^1],
        Yesterday = adLine[^2],
        RecentHistory = adLine,
        SectorSnapshots = granvilleSectorSnapshots,
        StockSectorMappings = stockSectorMappings
    };

    var granville = new GranvilleComposite();
    granvilleForecast = granville.Evaluate(granvilleContext);

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

    if (granvilleSectorSnapshots.Count == 0)
    {
        Console.WriteLine("  ⚠️  No sector index snapshots loaded — Disparity will degrade to neutral/no-data.");
    }

    if (stockSectorMappings.Count == 0)
    {
        Console.WriteLine("  ⚠️  No stock-sector mappings loaded — future sector-aware Granville groups will be unavailable.");
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
        var evalDate = DateTime.Today;
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

var symbols = constituents
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

// Minimum price: must be able to afford at least 10 shares from deployable capital
decimal deployableCapital = availableCapital * (1 - reserveCashPercent);
decimal maxPriceForMinLot = deployableCapital / 10m;

Console.WriteLine($"Affordability filter: max price ${maxPriceForMinLot:N2} (must afford >= 10 shares from ${deployableCapital:N2} deployable)\n");

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

Console.WriteLine($"Loaded: {loaded} symbols | Skipped: {skipped} (insufficient history), {skippedPrice} (price > ${maxPriceForMinLot:N2})");
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

foreach (var (symbol, bars) in allBars)
{
    var stockCloses = bars.Select(b => (double)b.Close).ToList();

    // Determine this stock's sector index
    string? sectorSymbol = stockSectorMap.TryGetValue(symbol, out var sec) ? sec : null;

    List<double>? sectorCloses = null;
    if (sectorSymbol != null)
        sectorClosesBySector.TryGetValue(sectorSymbol, out sectorCloses);

    // Compute RS — stock vs market always works; stock vs sector only if we have sector data.
    // For missing sector data, use XIU as a fallback (RS_StockVsSector ≈ 0).
    var rs = Core.RelativeStrength.RelativeStrengthCalculator.Compute(
        stockCloses: stockCloses,
        sectorCloses: sectorCloses ?? xiuCloses,
        marketCloses: xiuCloses,
        symbol: symbol,
        date: DateOnly.FromDateTime(DateTime.Today),
        sectorIndexSymbol: sectorSymbol ?? "XIU");

    rsScores[symbol] = rs;
}

// Inject RS scores into engine before evaluation
engine.RsCompositeScores = rsScores
    .Where(kvp => kvp.Value.CompositeScore.HasValue)
    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.CompositeScore!.Value, StringComparer.OrdinalIgnoreCase);

Console.WriteLine($"Relative Strength: computed for {rsScores.Count} symbols ({sectorClosesBySector.Count} sectors loaded)");
int withSector = rsScores.Count(kvp => stockSectorMap.ContainsKey(kvp.Key));
Console.WriteLine($"  With sector data: {withSector} | Fallback to XIU: {rsScores.Count - withSector}\n");

// ═══════════════════════════════════════════════════════════════════
// EVALUATE + RANK (SINGLE PASS) + PICK BEST + SIZE IT
// ═══════════════════════════════════════════════════════════════════
var top = engine.EvaluateAndRank(allBars, topN: topPicksToSave);
var (bestPick, size) = engine.EvaluateBestPickAllIn(top, availableCapital);

Console.WriteLine(new string('═', 80));
Console.WriteLine("BEST PICK (SINGLE-POSITION MODE) - DIRECTION EDGE RANKING");
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
    $"  {"VolExp10",8} {"RelStr10",8}" +
    $"  {"MaCross Trnd30 Trnd10",21}" +
    $"  {"",14}");
Console.WriteLine(
    $"{"#",-3}  {"Symbol",-8}  {"Action",-6}  {"Price",7} {"Shrs",5} {"Vol20d",8}" +
    $"  {"Comp",6}" +
    $"  {"Brk%",6} {"BrkRaw",7}" +
    $"  {"P(Up)",6} {"P(Dn)",6} {"Edge",6}" +
    $"  {"Vol%",6} {"RS%",6}" +
    $"  {"MA",3} {"T30",4} {"T10",4}" +
    $"  {"Gate",18}");
Console.WriteLine(new string('─', 138));

int rank = 1;
foreach (var p in top)
{
    double breakout = GetBreakoutProb(p);
    double pUp      = GetUpProb(p);
    double pDown    = GetDownProb(p);
    double edge     = pUp - pDown;
    double volExp   = GetProbContains(p, "VolExpansion");
    double relStr   = GetProbContains(p, "RelStrength");

    string edgeStr = edge >= 0 ? $"+{edge:P0}" : $"{edge:P0}";

    decimal lastPrice = allBars.TryGetValue(p.Symbol, out var bars) ? (decimal)bars[^1].Close : 0m;
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
        $"  {volExp,6:P0} {relStr,6:P0}" +
        $"  {maCross,3} {trend30,4} {trend10,4}" +
        $"  {gateStatus,-18}");
    rank++;
}

// ═══════════════════════════════════════════════════════════════════
// SAVE DAILY PICKS TO DATABASE
// ═══════════════════════════════════════════════════════════════════
if (saveToDB && top.Count > 0)
{
    var pickDate = DateTime.Today;
    var pickRepo = new DailyPickRepository();

    await pickRepo.DeletePicksByDate(pickDate);

    Console.WriteLine($"\nSaving {top.Count} picks to database...");

    int savedRank = 1;
    foreach (var p in top)
    {
        double breakout = GetBreakoutProb(p);
        double pUp = GetUpProb(p);
        double pDown = GetDownProb(p);
        double volExp = GetProbContains(p, "VolExpansion");
        double relStrength = GetProbContains(p, "RelStrength");

        await pickRepo.InsertPick(
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
            notes: savedRank == 1 ? $"Top pick. P↓={pDown:P0}, Edge={pUp - pDown:P0}" : null);

        savedRank++;
    }

    Console.WriteLine($"✓ Saved {top.Count} picks to [dbo].[DailyPick] for {pickDate:yyyy-MM-dd}");
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
foreach (var s in bestPick.Signals)
{
    Console.WriteLine($"  [{s.Hint,-5}] {s.Name,-25} Score={s.Score:0.###} {s.Notes}");
}