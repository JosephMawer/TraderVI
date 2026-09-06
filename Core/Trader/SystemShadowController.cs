#nullable enable

using Core.Calibration;
using Core.Db;
using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader;

/// <summary>
/// WPF-hostable Shadow V1 coordinator. Every provider receipt is persisted
/// before it can create a decision. Signals create durable pending orders;
/// only a later five-minute bar can supply the simulated fill.
/// </summary>
public sealed class SystemShadowController
{
    private static readonly TimeSpan RegularOpen = new(9, 30, 0);
    private static readonly TimeSpan EarliestNormalDecision = new(9, 50, 0);
    private static readonly TimeSpan RegularClose = new(16, 0, 0);
    private readonly SemaphoreSlim pollGate = new(1, 1);
    private readonly DateTime hostStartedUtc = DateTime.UtcNow;

    public async Task<SystemShadowPollResult> PollOnceAsync(
        CancellationToken cancellationToken = default)
    {
        await pollGate.WaitAsync(cancellationToken);
        try
        {
            DateTime startedUtc = DateTime.UtcNow;
            Guid pollCycleId = Guid.NewGuid();
            var warnings = new List<string>();
            var repository = new SystemShadowRepository();
            if (!await repository.HasSchemaAsync(cancellationToken))
                throw new InvalidOperationException($"Shadow V1 requires migration {SystemShadowRepository.MigrationFileName}.");

            IReadOnlyList<SystemShadowRuntimePortfolio> portfolios =
                await repository.GetRunnablePortfoliosAsync(cancellationToken);
            if (portfolios.Count == 0)
            {
                return new(pollCycleId, startedUtc, DateTime.UtcNow, 0, 0, 0, 0,
                    new[] { "Shadow is off; no active generation." });
            }

            DateTime localNow = PaperTradingMonitor.ToToronto(startedUtc);
            if (localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                localNow.TimeOfDay < RegularOpen ||
                localNow.TimeOfDay > new TimeSpan(16, 5, 0))
            {
                return new(pollCycleId, startedUtc, DateTime.UtcNow, 0, 0, 0, 0,
                    new[] { "Outside the regular TSX Shadow window; no evidence was requested." });
            }

            DateTime tradingDate = localNow.Date;
            await repository.ExpirePendingBuysBeforeDateAsync(tradingDate, cancellationToken);
            DateTime cutoffUtc = PaperTradingMonitor.ToUtc(tradingDate.Add(RegularOpen));
            var calibration = new CalibrationEvidenceRepository();
            SystemShadowDelphiRun? run = await calibration.GetLatestValidOfficialRunAsync(
                tradingDate,
                cutoffUtc,
                cancellationToken);

            var sessions = new Dictionary<Guid, SystemShadowRuntimeSession>();
            var candidatesByPortfolio = new Dictionary<Guid, IReadOnlyList<SystemShadowRuntimeCandidate>>();
            foreach (SystemShadowRuntimePortfolio portfolio in portfolios)
            {
                IReadOnlyList<SystemShadowDelphiCandidate> frozen = run is null
                    ? Array.Empty<SystemShadowDelphiCandidate>()
                    : await calibration.GetPublishedCandidatesAsync(
                        run.RunId,
                        portfolio.Lens,
                        portfolio.MaximumPositions,
                        cancellationToken);
                DateTime openingUtc = cutoffUtc;
                DateTime activationBaseline = portfolio.ActivatedUtc > openingUtc &&
                                                  PaperTradingMonitor.ToToronto(portfolio.ActivatedUtc).Date == tradingDate
                    ? portfolio.ActivatedUtc
                    : openingUtc;
                SystemShadowRuntimeSession session = await repository.EnsureSessionAsync(
                    portfolio,
                    tradingDate,
                    run,
                    frozen,
                    activationBaseline,
                    cancellationToken);
                sessions[portfolio.PortfolioId] = session;
                candidatesByPortfolio[portfolio.PortfolioId] =
                    await repository.GetSessionCandidatesAsync(session.SessionId, cancellationToken);
            }

            var positionsByPortfolio = new Dictionary<Guid, IReadOnlyList<SystemShadowPositionInfo>>();
            var pendingByPortfolio = new Dictionary<Guid, IReadOnlyList<SystemShadowPendingOrder>>();
            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SystemShadowRuntimePortfolio portfolio in portfolios)
            {
                IReadOnlyList<SystemShadowPositionInfo> positions =
                    await repository.GetPositionsAsync(portfolio.PortfolioId, cancellationToken);
                IReadOnlyList<SystemShadowPendingOrder> pending =
                    await repository.GetPendingOrdersAsync(portfolio.PortfolioId, cancellationToken);
                positionsByPortfolio[portfolio.PortfolioId] = positions;
                pendingByPortfolio[portfolio.PortfolioId] = pending;
                foreach (SystemShadowRuntimeCandidate candidate in candidatesByPortfolio[portfolio.PortfolioId])
                    symbols.Add(candidate.Symbol);
                foreach (SystemShadowPositionInfo position in positions.Where(x => x.Status == "Open"))
                    symbols.Add(position.Symbol);
                foreach (SystemShadowPendingOrder order in pending)
                    symbols.Add(order.Symbol);
            }

            DateTime requestStartUtc = cutoffUtc;
            CodeProvenance code = CalibrationProvenance.ResolveCode();
            var context = new IntradayPollContext(
                pollCycleId,
                IntradayPollPurpose.PaperMonitor,
                IntradayEvidenceVersions.Collector,
                SystemShadowVersions.Policy,
                code);
            var fiveMinute = new Dictionary<string, TmxIntradayBatch>(StringComparer.OrdinalIgnoreCase);
            var fifteenMinute = new Dictionary<string, TmxIntradayBatch>(StringComparer.OrdinalIgnoreCase);
            var evidenceRepository = new IntradayEvidenceRepository();
            using var tmx = new TmxClient();
            foreach (string symbol in symbols.OrderBy(x => x))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TmxIntradayBatch? five = await CollectAsync(
                    tmx, evidenceRepository, context, symbol, 5, requestStartUtc, warnings, cancellationToken);
                if (five is not null)
                    fiveMinute[symbol] = five;

                bool needsFifteen = positionsByPortfolio.Values
                    .SelectMany(x => x)
                    .Any(x => x.Status == "Open" && string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                if (needsFifteen)
                {
                    TmxIntradayBatch? fifteen = await CollectAsync(
                        tmx, evidenceRepository, context, symbol, 15, requestStartUtc, warnings, cancellationToken);
                    if (fifteen is not null)
                        fifteenMinute[symbol] = fifteen;
                }
            }

            int fills = 0;
            foreach (SystemShadowRuntimePortfolio portfolio in portfolios)
            {
                foreach (SystemShadowPendingOrder order in pendingByPortfolio[portfolio.PortfolioId])
                {
                    if (!fiveMinute.TryGetValue(order.Symbol, out TmxIntradayBatch? batch))
                        continue;
                    List<OhlcvBar> completed = Completed(batch);
                    OhlcvBar? fillBar;
                    if (order.Side == "Buy")
                    {
                        SystemShadowPendingBuyAction action = SystemShadowPolicy.EvaluatePendingBuyFill(
                            order.EarliestFillUtc,
                            hostStartedUtc,
                            completed.LastOrDefault()?.TimestampUtc);
                        if (action == SystemShadowPendingBuyAction.Requalify)
                        {
                            await repository.CancelPendingOrderAsync(
                                order,
                                "StaleBuyRequiresRequalification",
                                batch.ReceivedUtc,
                                cancellationToken);
                            continue;
                        }
                        if (action == SystemShadowPendingBuyAction.Wait)
                            continue;
                        fillBar = completed.FirstOrDefault(bar => bar.TimestampUtc == order.EarliestFillUtc);
                    }
                    else
                    {
                        DateTime observableBoundary = SystemShadowPolicy.EarliestObservableFillBoundary(
                            order.SignalReceivedUtc,
                            hostStartedUtc);
                        fillBar = completed.FirstOrDefault(bar =>
                            bar.TimestampUtc >= order.EarliestFillUtc &&
                            bar.TimestampUtc >= observableBoundary);
                    }
                    if (fillBar is null)
                        continue;
                    int reentry = order.OrderKind == "Reentry" ? 1 : 0;
                    if (await repository.FillPendingOrderAsync(
                            order,
                            fillBar.Open,
                            fillBar.TimestampUtc,
                            tradingDate,
                            reentry,
                            cancellationToken))
                        fills++;
                }
                positionsByPortfolio[portfolio.PortfolioId] =
                    await repository.GetPositionsAsync(portfolio.PortfolioId, cancellationToken);
            }

            int signals = 0;
            int blocked = 0;
            foreach (SystemShadowRuntimePortfolio portfolio in portfolios)
            {
                SystemShadowRuntimeSession session = sessions[portfolio.PortfolioId];
                List<SystemShadowPositionInfo> openPositions = positionsByPortfolio[portfolio.PortfolioId]
                    .Where(x => x.Status == "Open")
                    .ToList();
                bool portfolioEvidenceHealthy = true;

                foreach (SystemShadowPositionInfo position in openPositions)
                {
                    if (!fiveMinute.TryGetValue(position.Symbol, out TmxIntradayBatch? fiveBatch))
                    {
                        portfolioEvidenceHealthy = false;
                        blocked++;
                        continue;
                    }
                    OhlcvBar? latestFive = Completed(fiveBatch).LastOrDefault();
                    if (latestFive is null ||
                        fiveBatch.ReceivedUtc - latestFive.TimestampUtc.AddMinutes(5) > TimeSpan.FromMinutes(45))
                    {
                        portfolioEvidenceHealthy = false;
                        blocked++;
                        continue;
                    }

                    var trailing = new SystemShadowTrailingState(
                        position.AverageCost,
                        position.HighestFifteenClose ?? position.AverageCost,
                        position.ProfitProtectionArmed,
                        position.TrailingStopPrice,
                        position.LastFifteenMinuteBarUtc);
                    SystemShadowExitReason exitReason = SystemShadowPolicy.EvaluateFiveMinuteRisk(
                        position.AverageCost,
                        latestFive.Low,
                        position.TrailingStopPrice);
                    if (fifteenMinute.TryGetValue(position.Symbol, out TmxIntradayBatch? fifteenBatch))
                    {
                        OhlcvBar? latestFifteen = Completed(fifteenBatch).LastOrDefault();
                        if (latestFifteen is not null)
                        {
                            SystemShadowTrailingDecision trailingDecision =
                                SystemShadowPolicy.EvaluateFifteenMinuteClose(
                                    trailing,
                                    latestFifteen.TimestampUtc,
                                    latestFifteen.Low,
                                    latestFifteen.Close);
                            trailing = trailingDecision.State;
                            if (exitReason == SystemShadowExitReason.None)
                                exitReason = trailingDecision.ExitReason;
                        }
                    }
                    await repository.UpdatePositionEvidenceAsync(
                        position.PositionId,
                        latestFive.Close,
                        latestFive.TimestampUtc,
                        trailing,
                        cancellationToken);

                    if (exitReason != SystemShadowExitReason.None)
                    {
                        bool created = await repository.TryCreateOrderAsync(
                            portfolio.PortfolioId,
                            session.SessionId,
                            position.PositionId,
                            null,
                            position.Symbol,
                            "Sell",
                            "RiskExit",
                            fiveBatch.ReceivedUtc,
                            null,
                            exitReason.ToString(),
                            cancellationToken);
                        if (created) signals++;
                        continue;
                    }

                    int sessionOrdinal = await repository.GetPositionSessionOrdinalAsync(
                        portfolio.PortfolioId,
                        position.EntryTradingDate,
                        tradingDate,
                        cancellationToken);
                    if (localNow.TimeOfDay >= RegularClose &&
                        SystemShadowPolicy.ShouldExitAtSessionTwoClose(
                            latestFive.Close,
                            position.AverageCost,
                            sessionOrdinal))
                    {
                        bool created = await repository.TryCreateOrderAsync(
                            portfolio.PortfolioId,
                            session.SessionId,
                            position.PositionId,
                            null,
                            position.Symbol,
                            "Sell",
                            "SessionTwoExit",
                            fiveBatch.ReceivedUtc,
                            null,
                            "SessionTwoUnprofitable",
                            cancellationToken);
                        if (created) signals++;
                    }
                }

                positionsByPortfolio[portfolio.PortfolioId] =
                    await repository.GetPositionsAsync(portfolio.PortfolioId, cancellationToken);

                IReadOnlyList<SystemShadowPortfolioOverview> overviewRows =
                    await repository.GetPortfolioOverviewsAsync(portfolio.GenerationId, cancellationToken);
                SystemShadowPortfolioOverview overview = overviewRows.Single(x => x.PortfolioId == portfolio.PortfolioId);
                SystemShadowGuardDecision guards = SystemShadowPolicy.EvaluateGuards(
                    overview.NetAssetValue,
                    session.OpeningValue,
                    portfolio.HighestClosingValue);
                await repository.SetRiskGuardAsync(
                    portfolio.PortfolioId,
                    session.SessionId,
                    guards.DailyBuyingPaused,
                    guards.CapitalReviewRequired,
                    cancellationToken);

                bool buyingAllowed = portfolio.Status == "Active" &&
                                     portfolioEvidenceHealthy &&
                                     !session.DailyLossGuardActive &&
                                     !guards.DailyBuyingPaused &&
                                     !guards.CapitalReviewRequired &&
                                     session.CalibrationRunId.HasValue &&
                                     localNow.TimeOfDay >= EarliestNormalDecision &&
                                     localNow.TimeOfDay < RegularClose;
                if (buyingAllowed)
                {
                    signals += await EvaluateNewRiskAsync(
                        repository,
                        portfolio,
                        session,
                        candidatesByPortfolio[portfolio.PortfolioId],
                        positionsByPortfolio[portfolio.PortfolioId],
                        fiveMinute,
                        overview.NetAssetValue,
                        cancellationToken);
                }

                if (localNow.TimeOfDay >= RegularClose)
                {
                    await repository.ExpirePendingBuysAndMarkNoEntryAsync(
                        session.SessionId,
                        DateTime.UtcNow,
                        cancellationToken);
                    IReadOnlyList<SystemShadowPortfolioOverview> closingRows =
                        await repository.GetPortfolioOverviewsAsync(portfolio.GenerationId, cancellationToken);
                    decimal closingValue = closingRows.Single(x => x.PortfolioId == portfolio.PortfolioId).NetAssetValue;
                    await repository.CompleteSessionAsync(
                        session.SessionId,
                        closingValue,
                        DateTime.UtcNow,
                        cancellationToken);
                }
            }

            return new(
                pollCycleId,
                startedUtc,
                DateTime.UtcNow,
                symbols.Count,
                fills,
                signals,
                blocked,
                warnings.AsReadOnly());
        }
        finally
        {
            pollGate.Release();
        }
    }

    private static async Task<int> EvaluateNewRiskAsync(
        SystemShadowRepository repository,
        SystemShadowRuntimePortfolio portfolio,
        SystemShadowRuntimeSession session,
        IReadOnlyList<SystemShadowRuntimeCandidate> candidates,
        IReadOnlyList<SystemShadowPositionInfo> allPositions,
        IReadOnlyDictionary<string, TmxIntradayBatch> fiveMinute,
        decimal netAssetValue,
        CancellationToken cancellationToken)
    {
        List<SystemShadowPositionInfo> open = allPositions.Where(x => x.Status == "Open").ToList();
        var decisions = new Dictionary<string, SystemShadowEntryDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (SystemShadowRuntimeCandidate candidate in candidates)
        {
            SystemShadowEntryDecision decision = EvaluateCandidate(candidate, session, fiveMinute);
            decisions[candidate.Symbol] = decision;
            bool held = open.Any(x => Same(x.Symbol, candidate.Symbol));
            bool exitedThisSession = allPositions.Any(x =>
                x.Status == "Closed" &&
                x.EntryTradingDate.Date == session.TradingDate.Date &&
                Same(x.Symbol, candidate.Symbol));
            await repository.UpdateCandidateDecisionAsync(
                candidate.CandidateTrackingId,
                held ? "Held" : exitedThisSession ? "Exited" : decision.IsEligible ? "Qualified" : "Blocked",
                decision.Reason.ToString(),
                DateTime.UtcNow,
                cancellationToken);
        }

        int createdSignals = 0;

        // At the first Session-2 checkpoint, rotate only the weakest losing,
        // non-moving incumbent and only when an unheld current contender qualifies.
        bool firstCheckpoint = candidates.Any(x => x.LastEvaluatedUtc is null);
        if (firstCheckpoint && open.Count >= portfolio.MaximumPositions)
        {
            SystemShadowRuntimeCandidate? contender = candidates
                .Where(x => decisions[x.Symbol].IsEligible && open.All(p => !Same(p.Symbol, x.Symbol)))
                .OrderBy(x => x.Rank)
                .FirstOrDefault();
            if (contender is not null)
            {
                foreach (SystemShadowPositionInfo incumbent in open.OrderBy(x => x.LastPrice / x.AverageCost))
                {
                    int ordinal = await repository.GetPositionSessionOrdinalAsync(
                        portfolio.PortfolioId,
                        incumbent.EntryTradingDate,
                        session.TradingDate,
                        cancellationToken);
                    SystemShadowEntryDecision momentum = decisions.TryGetValue(incumbent.Symbol, out SystemShadowEntryDecision? value)
                        ? value
                        : new(false, SystemShadowEntryReason.MissingEvidence);
                    if (!SystemShadowPolicy.ShouldReplaceAtSessionTwoOpening(
                            incumbent.LastPrice,
                            incumbent.AverageCost,
                            momentum,
                            true,
                            ordinal))
                        continue;
                    if (fiveMinute.TryGetValue(incumbent.Symbol, out TmxIntradayBatch? incumbentBatch) &&
                        await repository.TryCreateOrderAsync(
                            portfolio.PortfolioId,
                            session.SessionId,
                            incumbent.PositionId,
                            contender.CandidateTrackingId,
                            incumbent.Symbol,
                            "Sell",
                            "RotationExit",
                            incumbentBatch.ReceivedUtc,
                            null,
                            "SessionTwoRotation",
                            cancellationToken))
                        createdSignals++;
                    break;
                }
            }
        }

        // A renewed next-session Delphi pick can complete the remaining 25%
        // only while the incumbent is profitable and still rising.
        foreach (SystemShadowPositionInfo position in open.Where(x => x.AddOnCount == 0 && x.EntryTradingDate.Date < session.TradingDate.Date))
        {
            SystemShadowRuntimeCandidate? candidate = candidates.FirstOrDefault(x => Same(x.Symbol, position.Symbol));
            if (candidate is null || !decisions[candidate.Symbol].IsEligible ||
                !fiveMinute.TryGetValue(position.Symbol, out TmxIntradayBatch? batch))
                continue;
            OhlcvBar? latest = Completed(batch).LastOrDefault();
            if (latest is null || SystemShadowPolicy.AdjustedSellPrice(latest.Close) <= position.AverageCost)
                continue;
            decimal budget = System.Math.Min(
                SystemShadowPolicy.AddOnBudget(position.FullPositionTarget),
                portfolio.CashBalance);
            if (budget > 0m && await repository.TryCreateOrderAsync(
                    portfolio.PortfolioId,
                    session.SessionId,
                    position.PositionId,
                    candidate.CandidateTrackingId,
                    position.Symbol,
                    "Buy",
                    "AddOn",
                    batch.ReceivedUtc,
                    budget,
                    "DelphiReaffirmed",
                    cancellationToken))
                createdSignals++;
        }

        int pendingBuys = (await repository.GetPendingOrdersAsync(portfolio.PortfolioId, cancellationToken))
            .Count(x => x.Side == "Buy" && x.OrderKind != "AddOn");
        int vacancies = portfolio.MaximumPositions - open.Count - pendingBuys;
        if (vacancies <= 0)
            return createdSignals;

        foreach (SystemShadowRuntimeCandidate candidate in candidates.OrderBy(x => x.Rank))
        {
            if (vacancies <= 0 || !decisions[candidate.Symbol].IsEligible || open.Any(x => Same(x.Symbol, candidate.Symbol)))
                continue;

            List<SystemShadowPositionInfo> entriesToday = allPositions
                .Where(x => Same(x.Symbol, candidate.Symbol) && x.EntryTradingDate.Date == session.TradingDate.Date)
                .OrderByDescending(x => x.EntryUtc)
                .ToList();
            bool priceBased = entriesToday.FirstOrDefault()?.ExitReasonCode is
                "HardLoss" or "TrailingProfit" or "SessionTwoUnprofitable" or "SessionTwoRotation";
            if (!SystemShadowPolicy.CanEnterAgainToday(entriesToday.Count, priceBased))
                continue;

            if (!fiveMinute.TryGetValue(candidate.Symbol, out TmxIntradayBatch? batch))
                continue;
            decimal target = SystemShadowPolicy.PositionTarget(netAssetValue, portfolio.MaximumPositions);
            decimal budget = SystemShadowPolicy.InitialBudget(target);
            string kind = entriesToday.Count == 0 ? "Initial" : "Reentry";
            if (await repository.TryCreateOrderAsync(
                    portfolio.PortfolioId,
                    session.SessionId,
                    null,
                    candidate.CandidateTrackingId,
                    candidate.Symbol,
                    "Buy",
                    kind,
                    batch.ReceivedUtc,
                    budget,
                    entriesToday.Count == 0 ? "Qualified" : "PriceBasedRequalification",
                    cancellationToken))
            {
                createdSignals++;
                vacancies--;
            }
        }
        return createdSignals;
    }

    private static SystemShadowEntryDecision EvaluateCandidate(
        SystemShadowRuntimeCandidate candidate,
        SystemShadowRuntimeSession session,
        IReadOnlyDictionary<string, TmxIntradayBatch> fiveMinute)
    {
        if (!fiveMinute.TryGetValue(candidate.Symbol, out TmxIntradayBatch? batch))
            return new(false, SystemShadowEntryReason.MissingEvidence);
        List<OhlcvBar> completed = Completed(batch);
        if (completed.Count < 2)
            return new(false, SystemShadowEntryReason.MissingEvidence);
        OhlcvBar prior = completed[^2];
        OhlcvBar latest = completed[^1];
        DateTime latestEndUtc = latest.TimestampUtc.AddMinutes(5);
        if (session.ActivationBaselineUtc.HasValue && latestEndUtc <= session.ActivationBaselineUtc.Value)
            return new(false, SystemShadowEntryReason.MissingEvidence);
        TimeSpan age = batch.ReceivedUtc - latestEndUtc;
        return SystemShadowPolicy.EvaluateEntry(new(
            candidate.PreviousSessionClose,
            prior.Close,
            latest.Close,
            latestEndUtc,
            batch.ReceivedUtc,
            IsComplete: true,
            IsLate: age > TimeSpan.FromMinutes(45),
            IsConflicting: false));
    }

    private static List<OhlcvBar> Completed(TmxIntradayBatch batch) =>
        batch.Bars
            .Where(x => x.TimestampUtc.AddMinutes(batch.IntervalMinutes) <= batch.ReceivedUtc)
            .OrderBy(x => x.TimestampUtc)
            .ToList();

    private static async Task<TmxIntradayBatch?> CollectAsync(
        TmxClient tmx,
        IntradayEvidenceRepository repository,
        IntradayPollContext context,
        string symbol,
        int intervalMinutes,
        DateTime requestStartUtc,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        DateTime requestEndUtc = DateTime.UtcNow;
        try
        {
            TmxIntradayBatch batch = await tmx.GetIntradayTimeSeriesBatchAsync(
                symbol,
                intervalMinutes,
                requestStartUtc,
                requestEndUtc,
                cancellationToken);
            IntradayEvidenceAppendResult append = await repository.AppendCompletedBatchAsync(
                context,
                batch,
                cancellationToken);
            if (append.AuditState == IntradayPollAuditState.Invalid)
            {
                warnings.Add($"{symbol} {intervalMinutes}m evidence invalid: {append.AuditCode}");
                return null;
            }
            return batch;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"{symbol} {intervalMinutes}m unavailable: {ex.GetType().Name}");
            try
            {
                await repository.AppendFailedObservationAsync(
                    context,
                    symbol,
                    intervalMinutes,
                    requestStartUtc,
                    requestEndUtc,
                    DateTime.UtcNow,
                    1,
                    1,
                    $"Shadow{intervalMinutes}FetchOrPersistFailed",
                    cancellationToken);
            }
            catch
            {
                // The source error remains the bounded result when even its
                // secondary durable audit record cannot be written.
            }
            return null;
        }
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
