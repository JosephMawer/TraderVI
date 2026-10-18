#nullable enable
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Practice;

public sealed record PracticeLedgerEvent(DateTime RecordedUtc, string Kind, Guid PortfolioId, object Details);
public sealed record PracticeFill(Guid OrderId, Guid PortfolioId, string Symbol, string Side, int Shares,
    decimal RawPrice, decimal AdjustedPrice, DateTime FillUtc, DateTime ObservedUtc, string Reason);

/// <summary>Isolated ledger implementation for the shared Shadow controller. It has no SQL identity
/// or operational publication path. The runner checkpoints state at session boundaries.</summary>
public sealed class PracticeShadowStore : ISystemShadowStore
{
    private readonly Func<DateTime> now;
    private readonly Func<Guid> newId;
    public List<SystemShadowRuntimePortfolio> Portfolios { get; } = [];
    public List<SystemShadowRuntimeSession> Sessions { get; } = [];
    public List<SystemShadowRuntimeCandidate> Candidates { get; } = [];
    public List<SystemShadowPositionInfo> Positions { get; } = [];
    public List<SystemShadowPendingOrder> PendingOrders { get; } = [];
    public List<PracticeFill> Fills { get; } = [];
    public List<PracticeLedgerEvent> Events { get; } = [];
    public Dictionary<Guid, ProgressiveProfitProtectionState> ProgressiveStates { get; } = [];
    public Dictionary<Guid, IReadOnlyList<ProgressiveHistoricalClose>> ProgressiveCloses { get; } = [];
    public Task<bool> HasProgressiveProtectionSchemaAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task<ProgressiveProfitProtectionState?> ReadProgressiveProtectionAsync(Guid positionId, CancellationToken ct = default) =>
        Task.FromResult(ProgressiveStates.GetValueOrDefault(positionId));
    public Task SaveProgressiveProtectionAsync(Guid positionId, ProgressiveProfitProtectionState state, CancellationToken ct = default)
    {
        ProgressiveProfitProtection.Validate(state);
        if (ProgressiveStates.TryGetValue(positionId, out var prior))
            ProgressiveProfitProtection.RequirePreserved(prior, state);
        ProgressiveStates[positionId] = state;
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<ProgressiveHistoricalClose>> ReadProgressiveClosesAsync(SystemShadowPositionInfo position,
        DateTime? afterEndUtc, DateTime throughUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProgressiveHistoricalClose>>(ProgressiveCloses.GetValueOrDefault(position.PositionId, [])
            .Where(b => b.EndUtc > (afterEndUtc ?? position.EntryUtc) && b.ReceivedUtc <= throughUtc).ToArray());

    public PracticeShadowStore(Func<DateTime> now, Func<Guid> newId, Guid generationId,
        SystemShadowPolicyConfig policy, Guid policyId)
    {
        this.now = now;
        this.newId = newId;
        SystemShadowPolicy.ValidateConfig(policy);
        foreach (var definition in SystemShadowPortfolioDefinition.Version1)
            Portfolios.Add(new(newId(), generationId, definition.Code, definition.Lens,
                definition.MaximumPositions, "Active", 1000m, 1000m, now())
                { Settings = policy, StrategyVersionId = policyId });
    }

    private void Record(string kind, Guid portfolio, object details) => Events.Add(new(now(), kind, portfolio, details));
    private SystemShadowRuntimePortfolio Portfolio(Guid id) => Portfolios.Single(x => x.PortfolioId == id);
    private decimal Value(Guid id) => Portfolio(id).CashBalance + Positions.Where(x => x.PortfolioId == id && x.Status == "Open").Sum(x => x.Shares * x.LastPrice);
    private static decimal SqlDecimal(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static void Replace<T>(List<T> list, T oldValue, T newValue) => list[list.IndexOf(oldValue)] = newValue;

    public Task<bool> HasSchemaAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyList<SystemShadowRuntimePortfolio>> GetRunnablePortfoliosAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SystemShadowRuntimePortfolio>>(Portfolios.Where(x => x.Status != "Stopped").ToArray());
    public Task<IReadOnlyList<SystemShadowPositionInfo>> GetPositionsAsync(Guid portfolioId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SystemShadowPositionInfo>>(Positions.Where(x => x.PortfolioId == portfolioId).OrderBy(x => x.EntryUtc).ToArray());
    public Task<IReadOnlyList<SystemShadowPendingOrder>> GetPendingOrdersAsync(Guid portfolioId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SystemShadowPendingOrder>>(PendingOrders.Where(x => x.PortfolioId == portfolioId).OrderBy(x => x.EarliestFillUtc).ToArray());
    public Task<IReadOnlyList<SystemShadowRuntimeCandidate>> GetSessionCandidatesAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SystemShadowRuntimeCandidate>>(Candidates.Where(x => x.SessionId == sessionId).OrderBy(x => x.Rank).ToArray());

    public Task<SystemShadowRuntimeSession> EnsureSessionAsync(SystemShadowRuntimePortfolio portfolio, DateTime tradingDate,
        SystemShadowDelphiRun? run, IReadOnlyList<SystemShadowDelphiCandidate> candidates, DateTime? activationBaselineUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = Sessions.SingleOrDefault(x => x.PortfolioId == portfolio.PortfolioId && x.TradingDate == tradingDate.Date);
        if (existing is not null) return Task.FromResult(existing);
        if (run is null && candidates.Count > 0 || candidates.Any(x => x.Rank > portfolio.MaximumPositions))
            throw new InvalidOperationException("Candidates violate the frozen daily source contract.");
        var session = new SystemShadowRuntimeSession(newId(), portfolio.PortfolioId, tradingDate.Date, run?.RunId,
            run is null ? "NoValidDelphiRun" : "Active", activationBaselineUtc, Value(portfolio.PortfolioId), false);
        Sessions.Add(session);
        foreach (var c in candidates)
            Candidates.Add(new(newId(), session.SessionId, c.CandidateId, c.Symbol, c.Rank, c.PreviousSessionClose, "Pending", null, null));
        Record("Session", portfolio.PortfolioId, session);
        return Task.FromResult(session);
    }

    public async Task<bool> TryCreateOrderAsync(Guid portfolioId, Guid? sessionId, Guid? positionId,
        Guid? candidateTrackingId, string symbol, string side, string orderKind, DateTime signalReceivedUtc,
        decimal? budget, string reasonCode, CancellationToken cancellationToken = default,
        SystemShadowPolicyConfig? config = null, Guid? strategyVersionId = null, Guid? operatorRequestId = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        SystemShadowPolicy.ValidateConfig(config);
        if (side is not ("Buy" or "Sell") || signalReceivedUtc.Kind != DateTimeKind.Utc || signalReceivedUtc > now())
            throw new InvalidOperationException("Invalid practice order or future signal.");
        var same = PendingOrders.Where(x => x.PortfolioId == portfolioId && x.Symbol == symbol).ToArray();
        if (side == "Sell")
            foreach (var buy in same.Where(x => x.Side == "Buy"))
                await CancelPendingOrderAsync(buy, "SupersededByProtectiveSell", now(), cancellationToken);
        if (PendingOrders.Any(x => x.PortfolioId == portfolioId && x.Symbol == symbol)) return false;
        var order = new SystemShadowPendingOrder(newId(), portfolioId, sessionId, positionId, candidateTrackingId,
            symbol.ToUpperInvariant(), side, orderKind, signalReceivedUtc,
            SystemShadowPolicy.EarliestFiveMinuteFillBoundary(signalReceivedUtc), budget, reasonCode);
        PendingOrders.Add(order);
        Record("Order", portfolioId, order);
        return true;
    }

    public async Task<bool> FillPendingOrderAsync(SystemShadowPendingOrder order, decimal rawFillPrice, DateTime fillUtc,
        DateTime tradingDate, int sameDayReentryCount, CancellationToken cancellationToken = default,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        SystemShadowPolicy.ValidateConfig(config);
        if (fillUtc.Kind != DateTimeKind.Utc || fillUtc < order.EarliestFillUtc || fillUtc > now() ||
            order.Side == "Buy" && fillUtc != order.EarliestFillUtc)
            throw new InvalidOperationException("Fill violates the observed next-bar boundary.");
        if (!PendingOrders.Contains(order)) return false;
        var portfolio = Portfolio(order.PortfolioId);
        decimal adjusted = order.Side == "Buy" ? SystemShadowPolicy.AdjustedBuyPrice(rawFillPrice, config)
            : SystemShadowPolicy.AdjustedSellPrice(rawFillPrice, config);
        int shares;
        decimal value;
        if (order.Side == "Buy")
        {
            bool guard = Sessions.SingleOrDefault(x => x.SessionId == order.SessionId)?.DailyLossGuardActive ?? false;
            if (portfolio.Status != "Active" || guard)
            {
                await CancelPendingOrderAsync(order, "NewRiskSuspended", now(), cancellationToken);
                return false;
            }
            shares = SystemShadowPolicy.WholeSharesForBuy(System.Math.Min(order.Budget ?? 0m, portfolio.CashBalance), rawFillPrice, config);
            if (shares == 0)
            {
                await CancelPendingOrderAsync(order, "InsufficientCashForWholeShare", now(), cancellationToken);
                return false;
            }
            value = SqlDecimal(shares * adjusted);
            if (order.OrderKind == "AddOn")
            {
                var old = Positions.Single(x => x.PositionId == order.PositionId && x.Status == "Open" && x.AddOnCount == 0);
                Replace(Positions, old, old with { Shares = old.Shares + shares, CostBasis = old.CostBasis + value,
                    AverageCost = SqlDecimal((old.CostBasis + value) / (old.Shares + shares)), AddOnCount = old.AddOnCount + 1,
                    LastPrice = rawFillPrice, LastPriceEventUtc = fillUtc });
            }
            else
            {
                if (Positions.Any(x => x.PortfolioId == order.PortfolioId && x.Symbol == order.Symbol && x.Status == "Open"))
                    throw new InvalidOperationException("Duplicate open practice position.");
                Positions.Add(new(newId(), order.PortfolioId, order.Symbol, "Open", shares, adjusted, value,
                    order.Budget.HasValue ? decimal.Round(order.Budget.Value / config.InitialAllocationFraction, 6) : value,
                    fillUtc, tradingDate.Date, 0, sameDayReentryCount, rawFillPrice, null, null, false,
                    rawFillPrice, fillUtc, 0m, null, null));
            }
            Replace(Portfolios, portfolio, portfolio with { CashBalance = portfolio.CashBalance - value });
        }
        else
        {
            var old = Positions.SingleOrDefault(x => x.PositionId == order.PositionId && x.Status == "Open");
            if (old is null)
            {
                await CancelPendingOrderAsync(order, "PositionAlreadyClosed", now(), cancellationToken);
                return false;
            }
            shares = old.Shares;
            value = SqlDecimal(shares * adjusted);
            Replace(Positions, old, old with { Status = "Closed", RealizedProfitLoss = value - old.CostBasis,
                LastPrice = rawFillPrice, LastPriceEventUtc = fillUtc, ExitUtc = fillUtc, ExitReasonCode = order.ReasonCode });
            Replace(Portfolios, portfolio, portfolio with { CashBalance = portfolio.CashBalance + value });
        }
        foreach (var candidate in Candidates.Where(x => order.Side == "Buy" ? x.CandidateTrackingId == order.CandidateTrackingId
                     : x.SessionId == order.SessionId && x.Symbol == order.Symbol).ToArray())
            Replace(Candidates, candidate, candidate with { State = order.Side == "Buy" ? "Entered" : "Exited" });
        PendingOrders.Remove(order);
        var fill = new PracticeFill(order.OrderId, order.PortfolioId, order.Symbol, order.Side, shares, rawFillPrice, adjusted, fillUtc, now(), order.ReasonCode);
        Fills.Add(fill);
        Record("Fill", order.PortfolioId, fill);
        if (Portfolios.Any(x => x.CashBalance < 0)) throw new InvalidOperationException("Negative practice cash.");
        return true;
    }

    public Task<bool> CancelPendingOrderAsync(SystemShadowPendingOrder order, string reason, DateTime occurredUtc,
        CancellationToken cancellationToken = default)
    {
        bool removed = PendingOrders.Remove(order);
        if (removed) Record("OrderCancelled", order.PortfolioId, new { order.OrderId, reason, occurredUtc });
        return Task.FromResult(removed);
    }
    public Task UpdateCandidateDecisionAsync(Guid candidateTrackingId, string state, string reasonCode,
        DateTime evaluatedUtc, CancellationToken cancellationToken = default)
    {
        var old = Candidates.Single(x => x.CandidateTrackingId == candidateTrackingId);
        Replace(Candidates, old, old with { State = state, ReasonCode = reasonCode, LastEvaluatedUtc = evaluatedUtc });
        if (old.State != state || old.ReasonCode != reasonCode)
            Record("Candidate", Sessions.Single(x => x.SessionId == old.SessionId).PortfolioId,
                new { old.Symbol, state, reasonCode, evaluatedUtc });
        return Task.CompletedTask;
    }
    public Task UpdatePositionEvidenceAsync(Guid positionId, decimal lastPrice, DateTime lastPriceEventUtc,
        SystemShadowTrailingState trailingState, CancellationToken cancellationToken = default)
    {
        var old = Positions.Single(x => x.PositionId == positionId);
        if (old.Status == "Open" && old.LastPriceEventUtc <= lastPriceEventUtc)
            Replace(Positions, old, old with { LastPrice = lastPrice, LastPriceEventUtc = lastPriceEventUtc,
                HighestFifteenClose = trailingState.HighestCompletedFifteenMinuteClose,
                LastFifteenMinuteBarUtc = trailingState.LastProcessedFifteenMinuteBarUtc,
                TrailingStopPrice = trailingState.TrailingStopPrice, ProfitProtectionArmed = trailingState.ProfitProtectionArmed });
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<SystemShadowPortfolioOverview>> GetPortfolioOverviewsAsync(Guid generationId,
        CancellationToken cancellationToken = default)
    {
        var rows = Portfolios.Where(p => p.GenerationId == generationId).Select(p =>
        {
            var open = Positions.Where(x => x.PortfolioId == p.PortfolioId && x.Status == "Open").ToArray();
            var session = Sessions.LastOrDefault(x => x.PortfolioId == p.PortfolioId);
            decimal value = Value(p.PortfolioId);
            return new SystemShadowPortfolioOverview(p.PortfolioId, p.GenerationId, p.PortfolioCode, "Practice — " + p.PortfolioCode,
                p.Lens, p.MaximumPositions, p.Status, p.CashBalance, value, open.Length,
                Positions.Where(x => x.PortfolioId == p.PortfolioId && x.Status == "Closed").Sum(x => x.RealizedProfitLoss),
                open.Sum(x => x.Shares * x.LastPrice - x.CostBasis), value / 1000m - 1m,
                session?.OpeningValue > 0m ? value / session.OpeningValue - 1m : null, value / p.HighestClosingValue - 1m,
                open.Select(x => (DateTime?)x.LastPriceEventUtc).Max(),
                Candidates.Where(x => x.SessionId == session?.SessionId).Select(x => x.LastEvaluatedUtc).Max(), now(), session?.Status);
        }).ToArray();
        return Task.FromResult<IReadOnlyList<SystemShadowPortfolioOverview>>(rows);
    }
    public async Task SetRiskGuardAsync(Guid portfolioId, Guid sessionId, bool dailyLossGuard, bool capitalReviewRequired,
        CancellationToken cancellationToken = default)
    {
        var session = Sessions.Single(x => x.SessionId == sessionId);
        var portfolio = Portfolio(portfolioId);
        if (dailyLossGuard && !session.DailyLossGuardActive)
        {
            Replace(Sessions, session, session with { DailyLossGuardActive = true });
            Record("DailyLossGuard", portfolioId, new { sessionId });
        }
        if (capitalReviewRequired && portfolio.Status != "CapitalReviewRequired")
        {
            Replace(Portfolios, portfolio, portfolio with { Status = "CapitalReviewRequired" });
            Record("CapitalReviewRequired", portfolioId, new { sessionId });
        }
        if (dailyLossGuard || capitalReviewRequired)
            foreach (var buy in PendingOrders.Where(x => x.PortfolioId == portfolioId && x.Side == "Buy").ToArray())
                await CancelPendingOrderAsync(buy, capitalReviewRequired ? "CapitalReviewRequired" : "DailyLossGuard", now(), cancellationToken);
    }
    public Task CompleteSessionAsync(Guid sessionId, decimal closingValue, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        var session = Sessions.Single(x => x.SessionId == sessionId);
        if (session.Status != "Completed")
        {
            Replace(Sessions, session, session with { Status = "Completed" });
            Record("SessionCompleted", session.PortfolioId, new { sessionId, closingValue, completedUtc, Valuation = "ProductionLastKnownMarks" });
        }
        var p = Portfolio(session.PortfolioId);
        Replace(Portfolios, p, p with { HighestClosingValue = System.Math.Max(p.HighestClosingValue, closingValue) });
        return Task.CompletedTask;
    }
    public Task<int> GetPositionSessionOrdinalAsync(Guid portfolioId, DateTime entryTradingDate, DateTime currentTradingDate,
        CancellationToken cancellationToken = default) => Task.FromResult(Sessions.Count(x => x.PortfolioId == portfolioId &&
            x.TradingDate >= entryTradingDate.Date && x.TradingDate <= currentTradingDate.Date));
    public async Task ExpirePendingBuysAndMarkNoEntryAsync(Guid sessionId, DateTime occurredUtc, CancellationToken cancellationToken = default)
    {
        foreach (var buy in PendingOrders.Where(x => x.SessionId == sessionId && x.Side == "Buy").ToArray())
            await CancelPendingOrderAsync(buy, "MarketClosed", occurredUtc, cancellationToken);
        foreach (var c in Candidates.Where(x => x.SessionId == sessionId && x.State is "Pending" or "Qualified" or "Blocked").ToArray())
            Replace(Candidates, c, c with { State = "NoEntry", ReasonCode = c.ReasonCode ?? "MarketClosed", LastEvaluatedUtc = c.LastEvaluatedUtc ?? occurredUtc });
    }
    public async Task ExpirePendingBuysBeforeDateAsync(DateTime currentTradingDate, CancellationToken cancellationToken = default)
    {
        foreach (var buy in PendingOrders.Where(x => x.Side == "Buy" && Sessions.Any(s => s.SessionId == x.SessionId && s.TradingDate < currentTradingDate.Date)).ToArray())
            await CancelPendingOrderAsync(buy, "PriorSessionExpired", now(), cancellationToken);
    }
}
