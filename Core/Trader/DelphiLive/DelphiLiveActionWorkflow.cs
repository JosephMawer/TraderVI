#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

/// <summary>
/// Host-independent, durable paper execution. No decision can spend another
/// policy's cash, and every quote is requested only after its causal predecessor
/// has committed. The injected source and ledger are the only effects.
/// </summary>
public sealed class DelphiLiveActionWorkflow
{
    private readonly IDelphiLiveLedgerStore store;
    private readonly IDelphiLiveMarketDataSource source;
    private readonly IDelphiLiveClock clock;

    public DelphiLiveActionWorkflow(IDelphiLiveLedgerStore store, IDelphiLiveMarketDataSource source, IDelphiLiveClock clock)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DelphiLivePortfolioSnapshot> ProtectHoldingsAsync(
        DelphiLiveProtectionCycleInput input, DelphiLivePolicyDefinition policy,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        ValidateSession(input.TradingDate, input.SessionOpenUtc, input.SessionCloseUtc);
        var state = await Load(input.PortfolioId, input.TradingDate, policy, cancellationToken);
        state = await PrepareSession(state, input.TradingDate, input.IsRestart, lease, cancellationToken);
        if (!WithinRegularSession(input.SessionOpenUtc, input.SessionCloseUtc))
            return await Overnight(state, input.SessionCloseUtc, lease, cancellationToken);

        foreach (var pending in state.PendingActions.Where(a => a.Intent.Side == DelphiLiveActionSide.Sell).ToArray())
            state = await AttemptAction(state, pending.Intent.ActionId, input.CycleId, input.SessionOpenUtc,
                input.SessionCloseUtc, null, policy, lease, cancellationToken);

        foreach (var position in state.OpenPositions.OrderBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (state.PendingActions.Any(a => a.PositionId == position.PositionId))
                continue;
            state = await CheckProtection(state, position.PositionId, input.CycleId, input.TradingDate,
                input.SessionOpenUtc, input.SessionCloseUtc, input.IsWarmingUp, null, input.SessionId, policy, lease, cancellationToken);
        }
        return state;
    }

    public async Task<DelphiLivePortfolioSnapshot> RunCycleAsync(
        DelphiLivePortfolioCycleInput input, DelphiLivePolicyDefinition policy,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        ValidateSession(input.TradingDate, input.SessionOpenUtc, input.SessionCloseUtc);
        if (input.CycleId == Guid.Empty || input.CheckpointBarEndUtc.Kind != DateTimeKind.Utc ||
            input.CheckpointBarEndUtc <= input.SessionOpenUtc || input.CheckpointBarEndUtc > input.SessionCloseUtc ||
            (input.CheckpointBarEndUtc - input.SessionOpenUtc).Ticks % policy.BarInterval.Ticks != 0 ||
            input.BuyCutoffUtc != input.SessionOpenUtc + (policy.EntryCutoff.ToTimeSpan() - DelphiLiveSchedule.RegularOpen))
            throw new ArgumentException("An exact session checkpoint and policy Buy cutoff are required.");
        var state = await Load(input.PortfolioId, input.TradingDate, policy, cancellationToken);
        if (state.LastCompletedCycleId == input.CycleId)
            return state;
        if (input.Candidates.Select(c => c.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Candidates.Count)
            throw new ArgumentException("A policy may receive only one evaluation per symbol and cycle.");
        foreach (var candidate in input.Candidates)
        {
            if (candidate.EvaluationId == Guid.Empty || candidate.EvidenceId == Guid.Empty || string.IsNullOrWhiteSpace(candidate.DossierJson))
                throw new ArgumentException("Action candidates require already-persisted evaluation evidence and their full dossier.");
        }
        state = await PrepareSession(state, input.TradingDate, input.IsRestart, lease, cancellationToken);
        state = await OpeningMark(state, input, lease, cancellationToken);

        // Persist every consumed close and raised floor before requesting any
        // bid that might test that floor. No earlier quote can be recycled.
        foreach (var position in state.OpenPositions.ToArray())
        {
            var candidate = input.Candidates.SingleOrDefault(c => SameSymbol(c.Symbol, position.Symbol));
            if (candidate?.Safety.CompletedBarClose is not decimal close ||
                input.CheckpointBarEndUtc <= position.OpenedUtc)
                continue;
            var update = DelphiLiveSafetyPolicy.ApplyCompletedClose(position.Protection,
                input.CheckpointBarEndUtc, close, clock.UtcNow, policy);
            if (update.State == position.Protection)
                continue;
            state = await Commit(state, state with
            {
                Positions = ReplacePosition(state, position with { Protection = update.State })
            }, update.FloorChanged ? "ProfitProtectionFloorChanged" : "CompletedCloseConsumed", update, lease, cancellationToken);
        }

        // Existing pending exits and newly proven protection always precede
        // entry sizing. A committed fill is the sole way to free cash or a slot.
        foreach (var pending in state.PendingActions.Where(a => a.Intent.Side == DelphiLiveActionSide.Sell).ToArray())
            state = await AttemptAction(state, pending.Intent.ActionId, input.CycleId, input.SessionOpenUtc,
                input.SessionCloseUtc, input, policy, lease, cancellationToken);
        foreach (var position in state.OpenPositions.OrderBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var candidate = input.Candidates.SingleOrDefault(c => SameSymbol(c.Symbol, position.Symbol));
            state = await CheckProtection(state, position.PositionId, input.CycleId, input.TradingDate,
                input.SessionOpenUtc, input.SessionCloseUtc, candidate?.Safety.IsWarmingUp ?? true,
                candidate, input.SessionId, policy, lease, cancellationToken);
        }

        state = await CheckpointMark(state, input, policy, lease, cancellationToken);
        foreach (var pending in state.PendingActions.Where(a => a.Intent.Side == DelphiLiveActionSide.Buy).ToArray())
        {
            var candidate = input.Candidates.SingleOrDefault(c => SameSymbol(c.Symbol, pending.Intent.Symbol));
            string? reason = BuyBlockReason(state, candidate, input, policy);
            if (reason is not null)
                state = await EndBuy(state, pending, reason, lease, cancellationToken);
            else
                state = await AttemptAction(state, pending.Intent.ActionId, input.CycleId, input.SessionOpenUtc,
                    input.SessionCloseUtc, input, policy, lease, cancellationToken);
        }

        foreach (var candidate in input.Candidates.OrderBy(c => c.LiveRank).ThenBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            string? blocked = BuyBlockReason(state, candidate, input, policy);
            if (blocked is not null || state.Actions.Any(a => a.EvaluationId == candidate.EvaluationId && a.Intent.Side == DelphiLiveActionSide.Buy))
                continue;
            var nav = CurrentNav(state, input);
            DateTime now = clock.UtcNow;
            var intent = new DelphiLiveActionIntent(Guid.NewGuid(), Guid.NewGuid(), candidate.EvidenceId,
                candidate.Symbol, DelphiLiveActionSide.Buy, now, now, input.BuyCutoffUtc, null,
                System.Math.Min(nav.NetAssetValue!.Value * policy.EntryTargetNavFraction, state.Cash));
            bool retry = state.Actions.Any(a => SameSymbol(a.Intent.Symbol, candidate.Symbol) &&
                a.TradingDate == input.TradingDate && a.TerminalReason == "BuyQuoteUnavailableExpired");
            var action = new DelphiLiveLedgerAction(intent, null, candidate.EvaluationId, input.TradingDate,
                input.CycleId, retry ? "BuyRetryFreshObservation" : "StrongConfirmationCompleted",
                ActionDossier(candidate.DossierJson, intent.DecisionId, candidate.EvaluationId, "Buy", now, null, input.SessionId, policy),
                "Pending", 0, null, null, null, []);
            state = await Commit(state, state with { Actions = state.Actions.Add(action),
                CandidateStates = SetLifecycle(state, candidate.Symbol, DelphiLiveRecommendationState.BuyPending) },
                "BuyDecisionPersisted", action, lease, cancellationToken);
            state = await AttemptAction(state, intent.ActionId, input.CycleId, input.SessionOpenUtc,
                input.SessionCloseUtc, input, policy, lease, cancellationToken);
        }
        state = await Overnight(state, input.SessionCloseUtc, lease, cancellationToken);
        return await Commit(state, state with { LastCompletedCycleId = input.CycleId }, "PortfolioCycleCompleted",
            new { input.CycleId, input.CheckpointBarEndUtc }, lease, cancellationToken);
    }

    public async Task<DelphiLivePortfolioSnapshot> ResumeCapitalReviewAsync(
        Guid portfolioId, decimal reviewedCurrentNav, string authorizedBy, string reason,
        DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorizedBy) || string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Capital review requires explicit operator identity and a durable reason.");
        var state = await store.LoadPortfolioAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException("Portfolio does not exist.");
        var guard = DelphiLivePortfolioPolicy.ResumeAfterCapitalReview(state.Guards, reviewedCurrentNav, false);
        return await Commit(state, state with { Guards = guard }, "CapitalReviewResumed",
            new { reviewedCurrentNav, authorizedBy, reason }, lease, cancellationToken);
    }

    private async Task<DelphiLivePortfolioSnapshot> CheckProtection(
        DelphiLivePortfolioSnapshot state, Guid positionId, Guid cycleId, DateOnly tradingDate,
        DateTime openUtc, DateTime closeUtc, bool warmingUp, DelphiLiveActionCandidate? candidate, Guid? sessionId,
        DelphiLivePolicyDefinition policy, DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        var position = state.OpenPositions.SingleOrDefault(p => p.PositionId == positionId);
        if (position is null)
            return state;
        if (candidate is null && (sessionId is null || sessionId == Guid.Empty))
            throw new ArgumentException("Quote-only protection requires the current frozen session identity.");
        var existing = state.PendingActions.SingleOrDefault(a => a.PositionId == positionId && a.Intent.Side == DelphiLiveActionSide.Sell);
        DelphiLiveCausalQuoteObservation? quote = null;
        if (existing is null && WithinRegularSession(openUtc, closeUtc))
        {
            Guid evidenceId = Guid.NewGuid();
            var request = new DelphiLiveQuoteRequest(evidenceId, position.Symbol, "Sell", 1, clock.UtcNow, clock.UtcNow);
            var receipt = await RequestQuote(request, Earlier(closeUtc, request.RequestStartedUtc + policy.QuoteAttemptWindow), cancellationToken);
            quote = new(Guid.NewGuid(), evidenceId, position.Symbol, 1, request.RequestStartedUtc,
                receipt.ReceivedUtc, receipt.Price, receipt.Bid, receipt.Ask, receipt.SourceContractVersion);
            var recorded = new DelphiLiveLedgerQuote(quote.QuoteObservationId, null, positionId, "ProtectionTrigger",
                cycleId, quote, "ProtectionObservation", position.OriginalEntryDossierJson);
            state = await Commit(state, state with { Quotes = state.Quotes.Add(recorded) }, "ProtectionQuotePersisted", recorded, lease, cancellationToken);
        }
        bool usableBid = quote?.Bid is > 0m && quote.ReceivedUtc >= openUtc && quote.ReceivedUtc < closeUtc;
        var safetyInput = candidate?.Safety ?? new DelphiLiveSafetyInput(true, warmingUp,
            position.AveragePurchasePrice, null, null, null, null, false, false, false, false,
            new(DelphiLiveSignalFamily.VolumeSupport, DelphiLiveFamilyState.NotMature, "QuoteProtectionOnly"),
            new(DelphiLiveMomentumState.Neutral, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None, 0, 0, 0, 0), false, position.Protection);
        safetyInput = safetyInput with
        {
            IsHeld = true, AveragePurchasePrice = position.AveragePurchasePrice,
            CurrentBid = usableBid ? quote!.Bid : null,
            CurrentBidReceivedUtc = usableBid ? quote!.ReceivedUtc : null,
            ProfitProtection = position.Protection
        };
        var safety = DelphiLiveSafetyPolicy.Evaluate(safetyInput, policy);
        if (!safety.RequiresProtectiveSell)
            return state;
        if (existing is not null)
        {
            var reasons = existing.SupportingReasons.AddRange(safety.FiredExitRules.Select(x => x.ToString())
                .Where(x => x != existing.PrimaryReason && !existing.SupportingReasons.Contains(x)));
            if (reasons.Length != existing.SupportingReasons.Length)
                state = await Commit(state, state with { Actions = ReplaceAction(state, existing with { SupportingReasons = reasons }) },
                    "PendingExitAdditionalEvidence", new { safety, candidate, quote }, lease, cancellationToken);
            return state;
        }
        DateTime now = clock.UtcNow;
        Guid decisionEvidenceId = safety.PrimaryExitRule is DelphiLiveExitRule.HardLoss5Pct or DelphiLiveExitRule.ProfitProtectionFloorBreach
            ? quote!.QuoteObservationId : candidate!.EvidenceId;
        var intent = new DelphiLiveActionIntent(Guid.NewGuid(), Guid.NewGuid(), decisionEvidenceId,
            position.Symbol, DelphiLiveActionSide.Sell, now, now, null, position.Quantity, null);
        var action = new DelphiLiveLedgerAction(intent, positionId, candidate?.EvaluationId, tradingDate, cycleId,
            safety.PrimaryExitRule!.Value.ToString(),
            ActionDossier(candidate?.DossierJson ?? position.OriginalEntryDossierJson, intent.DecisionId,
                candidate?.EvaluationId, "Sell", now, new { safety, triggerQuote = quote, position.Protection, originalEntry = position.OriginalEntryDossierJson }, sessionId, policy),
            "Pending", 0, null, null, null,
            safety.FiredExitRules.Select(x => x.ToString()).Where(x => x != safety.PrimaryExitRule.ToString()).ToImmutableArray());
        state = await Commit(state, state with { Actions = state.Actions.Add(action),
            CandidateStates = SetLifecycle(state, position.Symbol, DelphiLiveRecommendationState.ExitPending) },
            "ProtectiveSellDecisionPersisted", action, lease, cancellationToken);
        return await AttemptAction(state, intent.ActionId, cycleId, openUtc, closeUtc, null, policy, lease, cancellationToken);
    }

    private async Task<DelphiLivePortfolioSnapshot> AttemptAction(
        DelphiLivePortfolioSnapshot state, Guid actionId, Guid cycleId, DateTime openUtc, DateTime closeUtc,
        DelphiLivePortfolioCycleInput? input, DelphiLivePolicyDefinition policy, DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        var action = state.Actions.Single(a => a.Intent.ActionId == actionId);
        if (action.Status is not ("Pending" or "ExitPendingOvernight") || !WithinRegularSession(openUtc, closeUtc))
            return state;
        bool buy = action.Intent.Side == DelphiLiveActionSide.Buy;
        bool initialWindow = action.CreatedCycleId == cycleId && clock.UtcNow - action.Intent.DecisionPersistedUtc < policy.QuoteAttemptWindow;
        if (!initialWindow && action.LastAttemptCycleId == cycleId)
            return state;
        int allowance = initialWindow ? System.Math.Max(0, policy.QuoteAttemptCount - action.AttemptCount) : 1;
        for (int remaining = allowance; remaining > 0 && WithinRegularSession(openUtc, closeUtc); remaining--)
        {
            action = state.Actions.Single(a => a.Intent.ActionId == actionId);
            if (buy)
            {
                string? blocked = input is null ? "BuyCancelledPortfolio" :
                    BuyBlockReason(state, input.Candidates.SingleOrDefault(c => SameSymbol(c.Symbol, action.Intent.Symbol)), input, policy, actionId);
                if (blocked is not null)
                    return await EndBuy(state, action, blocked, lease, cancellationToken);
                if (clock.UtcNow - action.Intent.DecisionPersistedUtc >= policy.QuoteAttemptWindow || action.AttemptCount >= policy.QuoteAttemptCount)
                    return await EndBuy(state, action, "BuyQuoteUnavailableExpired", lease, cancellationToken);
            }
            else if (initialWindow && clock.UtcNow - action.Intent.DecisionPersistedUtc >= policy.QuoteAttemptWindow)
                break;
            // Reserving the attempt before the request makes crash/retry behavior
            // explicit even when the source response never reaches the process.
            action = action with { AttemptCount = action.AttemptCount + 1, LastAttemptCycleId = cycleId };
            state = await Commit(state, state with { Actions = ReplaceAction(state, action) }, "QuoteAttemptStarted",
                new { actionId, action.AttemptCount, cycleId }, lease, cancellationToken);
            var request = new DelphiLiveQuoteRequest(action.Intent.DecisionId, action.Intent.Symbol,
                action.Intent.Side.ToString(), action.AttemptCount, action.Intent.DecisionPersistedUtc, clock.UtcNow);
            DateTime attemptDeadline = initialWindow || buy
                ? Earlier(closeUtc, action.Intent.DecisionPersistedUtc + policy.QuoteAttemptWindow)
                : Earlier(closeUtc, request.RequestStartedUtc + policy.QuoteAttemptWindow);
            var receipt = await RequestQuote(request, attemptDeadline, cancellationToken);
            var quote = new DelphiLiveCausalQuoteObservation(Guid.NewGuid(), action.Intent.DecisionId, action.Intent.Symbol,
                action.AttemptCount, request.RequestStartedUtc, receipt.ReceivedUtc, receipt.Price, receipt.Bid, receipt.Ask, receipt.SourceContractVersion);
            var result = DelphiLiveExecutionPolicy.EvaluateQuoteAttempt(action.Intent, quote, policy);
            if (quote.ReceivedUtc >= closeUtc || quote.ReceivedUtc < openUtc)
                result = new(buy ? DelphiLiveQuoteAttemptDisposition.BuyCutoffExpired : DelphiLiveQuoteAttemptDisposition.SellRemainsPending,
                    null, null, null, buy ? "BuyCutoffExpired" : "ExitPendingOvernight");
            var recorded = new DelphiLiveLedgerQuote(quote.QuoteObservationId, actionId, action.PositionId,
                "FillObservation", cycleId, quote, result.ReasonCode, action.DossierJson);
            state = await Commit(state, state with { Quotes = state.Quotes.Add(recorded) }, "QuoteAttemptPersisted", recorded, lease, cancellationToken);
            if (result.HasFill)
            {
                if (buy)
                {
                    string? blocked = BuyBlockReason(state, input!.Candidates.SingleOrDefault(c => SameSymbol(c.Symbol, action.Intent.Symbol)), input, policy, actionId);
                    if (blocked is not null)
                        return await EndBuy(state, action, blocked, lease, cancellationToken);
                }
                return await Fill(state, action, quote, result, input, policy, lease, cancellationToken);
            }
            if (result.Disposition is DelphiLiveQuoteAttemptDisposition.BuyCutoffExpired or DelphiLiveQuoteAttemptDisposition.BuyQuoteUnavailableExpired)
                return await EndBuy(state, action, result.ReasonCode, lease, cancellationToken);
            if (result.Disposition == DelphiLiveQuoteAttemptDisposition.SellRemainsPending)
                break;
        }
        return state;
    }

    private async Task<DelphiLivePortfolioSnapshot> Fill(DelphiLivePortfolioSnapshot state, DelphiLiveLedgerAction action,
        DelphiLiveCausalQuoteObservation quote, DelphiLiveQuoteAttemptDecision result, DelphiLivePortfolioCycleInput? input,
        DelphiLivePolicyDefinition policy, DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        bool buy = action.Intent.Side == DelphiLiveActionSide.Buy;
        decimal price = result.FillPrice!.Value;
        Guid positionId;
        int quantity;
        var positions = state.Positions;
        if (buy)
        {
            var sizing = DelphiLivePortfolioPolicy.SizeWholeShareEntry(CurrentNav(state, input!), state.Cash, price,
                state.OpenPositions.Count(), state.OpenPositions.Any(p => SameSymbol(p.Symbol, action.Intent.Symbol)), state.Guards, policy);
            if (!sizing.IsAllowed)
                return await EndBuy(state, action, sizing.ReasonCode, lease, cancellationToken);
            quantity = sizing.Quantity;
            positionId = Guid.NewGuid();
            positions = positions.Add(new(positionId, action.Intent.Symbol, quantity, price, quote.ReceivedUtc,
                action.Intent.ActionId, action.DossierJson, DelphiLiveProfitProtectionState.Open(positionId, price)));
        }
        else
        {
            var position = state.OpenPositions.Single(p => p.PositionId == action.PositionId);
            positionId = position.PositionId;
            quantity = position.Quantity;
            positions = ReplacePosition(state, position with { ClosedUtc = quote.ReceivedUtc, ExitActionId = action.Intent.ActionId });
        }
        var fill = new DelphiLiveLedgerFill(Guid.NewGuid(), action.Intent.ActionId, positionId, quote.QuoteObservationId,
            action.Intent.Symbol, action.Intent.Side, quantity, price, result.SelectedField!.Value,
            result.Confidence!.Value, quote.ReceivedUtc, state.CurrentSession!.Value);
        state = await Commit(state, state with
        {
            Cash = state.Cash + (buy ? -1m : 1m) * price * quantity,
            Positions = positions, Fills = state.Fills.Add(fill),
            CandidateStates = SetLifecycle(state, action.Intent.Symbol, buy ? DelphiLiveRecommendationState.Held : DelphiLiveRecommendationState.Watching, completedExit: !buy),
            Actions = ReplaceAction(state, action with { Status = "Filled", CompletedUtc = quote.ReceivedUtc, TerminalReason = result.ReasonCode })
        }, buy ? "BuyFillCommitted" : "SellFillCommitted", new { fill, action.DossierJson }, lease, cancellationToken);
        // Quote spread can change NAV enough to trip a guard. Revalue the exact
        // checkpoint after the fill before the next ranked candidate is sized.
        return input is not null
            ? await CheckpointMark(state, input, policy, lease, cancellationToken)
            : state;
    }

    private string? BuyBlockReason(DelphiLivePortfolioSnapshot state, DelphiLiveActionCandidate? candidate,
        DelphiLivePortfolioCycleInput input, DelphiLivePolicyDefinition policy, Guid? pendingActionId = null)
    {
        if (candidate is null || !candidate.ConfirmedEntryEligible || !candidate.Safety.Momentum.IsEntryEligibleStrong ||
            !candidate.ConfirmationStartedBarEndUtc.HasValue)
            return "BuyCancelledSignal";
        if (!candidate.Confidence.AllowsNewRisk)
            return "BuyCancelledDataConfidence";
        if (DelphiLiveSafetyPolicy.Evaluate(candidate.Safety with { IsHeld = false, AveragePurchasePrice = null, ProfitProtection = null }, policy).EntrySafetyVetoActive)
            return "BuyCancelledSafety";
        if (clock.UtcNow >= input.BuyCutoffUtc)
            return "BuyCutoffExpired";
        if (clock.UtcNow < input.SessionOpenUtc + (policy.EntryWindowStart.ToTimeSpan() - DelphiLiveSchedule.RegularOpen) ||
            input.CheckpointBarEndUtc < input.SessionOpenUtc + (policy.EntryWindowStart.ToTimeSpan() - DelphiLiveSchedule.RegularOpen))
            return "EntryWindowNotOpen";
        if (input.CorporateActionUnsupported || state.Guards.CapitalReviewRequired || state.Guards.DailyBuyingPaused)
            return "BuyCancelledPortfolio";
        if (state.OpenPositions.Any(p => SameSymbol(p.Symbol, candidate.Symbol)) ||
            state.PendingActions.Any(a => SameSymbol(a.Intent.Symbol, candidate.Symbol) && a.Intent.ActionId != pendingActionId) ||
            state.OpenPositions.Count() >= policy.MaximumHoldings)
            return "BuyCancelledPortfolio";
        if (state.Fills.Count(f => f.Side == DelphiLiveActionSide.Buy && f.TradingDate == input.TradingDate && SameSymbol(f.Symbol, candidate.Symbol)) >= policy.MaximumSameSessionEntriesPerSymbol)
            return "EntryLimitReached";
        DateTime? lastExit = state.Positions.Where(p => SameSymbol(p.Symbol, candidate.Symbol)).Select(p => p.ClosedUtc).Max();
        if (lastExit.HasValue && candidate.ConfirmationStartedBarEndUtc <= lastExit.Value)
            return "FreshPostExitConfirmationRequired";
        if (state.Cash <= 0m)
            return "InsufficientCashForOneShare";
        if (state.OpeningNav is null || !CurrentNav(state, input).IsComplete)
            return "PortfolioNavUnavailable";
        return null;
    }

    private async Task<DelphiLivePortfolioSnapshot> PrepareSession(DelphiLivePortfolioSnapshot state, DateOnly date,
        bool restart, DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        if (state.CurrentSession != date)
        {
            if (state.CurrentSession.HasValue && date < state.CurrentSession.Value)
                throw new InvalidOperationException("A portfolio cannot move back to an earlier session.");
            var next = state with { CurrentSession = date, SessionOpeningCash = state.Cash,
                SessionOpeningPositions = state.OpenPositions.ToImmutableArray(), OpeningNav = null,
                Guards = state.Guards with { DailyBuyingPaused = false, DailyReturn = null }, LastCompletedCycleId = null,
                CandidateStates = ImmutableDictionary<string, DelphiLivePortfolioCandidateState>.Empty };
            state = await Commit(state, next, "PortfolioSessionStarted", new { date }, lease, cancellationToken);
        }
        foreach (var buy in state.PendingActions.Where(a => a.Intent.Side == DelphiLiveActionSide.Buy && (restart || a.TradingDate != date)).ToArray())
            state = await EndBuy(state, buy, restart ? "BuyRestartExpired" : "BuyCutoffExpired", lease, cancellationToken);
        return state;
    }

    private async Task<DelphiLivePortfolioSnapshot> OpeningMark(DelphiLivePortfolioSnapshot state, DelphiLivePortfolioCycleInput input,
        DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        if (state.OpeningNav is not null)
            return state;
        var nav = DelphiLivePortfolioPolicy.CalculateExactNav(state.SessionOpeningCash,
            state.SessionOpeningPositions.Select(p => (p.PositionId, p.Symbol, p.Quantity)).ToArray(),
            input.ExactOpeningMarks.ToArray(), input.SessionOpenUtc + DelphiLiveSchedule.BarInterval);
        var mark = new DelphiLiveLedgerMark(Guid.NewGuid(), input.TradingDate, DelphiLivePortfolioMarkKind.Opening,
            input.SessionOpenUtc + DelphiLiveSchedule.BarInterval, nav.IsComplete, nav.NetAssetValue,
            input.ExactOpeningMarks.ToImmutableArray(), nav.ReasonCode);
        return await Commit(state, state with { OpeningNav = nav.NetAssetValue, Marks = state.Marks.Add(mark) },
            "OpeningNavObserved", mark, lease, cancellationToken);
    }

    private async Task<DelphiLivePortfolioSnapshot> CheckpointMark(DelphiLivePortfolioSnapshot state, DelphiLivePortfolioCycleInput input,
        DelphiLivePolicyDefinition policy, DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        var nav = CurrentNav(state, input);
        var guards = nav.NetAssetValue is > 0m
            ? DelphiLivePortfolioPolicy.EvaluateGuards(nav.NetAssetValue.Value, state.OpeningNav, state.Guards.HighestClosingNav,
                state.Guards.DailyBuyingPaused, state.Guards.CapitalReviewRequired, policy) : state.Guards;
        bool closing = input.CheckpointBarEndUtc == input.SessionCloseUtc;
        var mark = new DelphiLiveLedgerMark(Guid.NewGuid(), input.TradingDate,
            closing ? DelphiLivePortfolioMarkKind.Closing : DelphiLivePortfolioMarkKind.Checkpoint,
            input.CheckpointBarEndUtc, nav.IsComplete && !input.CorporateActionUnsupported,
            nav.NetAssetValue, input.ExactCheckpointMarks.ToImmutableArray(), input.CorporateActionUnsupported ? "CorporateActionUnsupported" : nav.ReasonCode);
        if (closing && mark.Complete)
            guards = guards with { HighestClosingNav = System.Math.Max(guards.HighestClosingNav, nav.NetAssetValue!.Value) };
        return await Commit(state, state with { Guards = guards, Marks = state.Marks.Add(mark),
            PreviousClosingNav = closing && mark.Complete ? nav.NetAssetValue : state.PreviousClosingNav },
            "PortfolioNavObserved", mark, lease, cancellationToken);
    }

    private static DelphiLiveNavResult CurrentNav(DelphiLivePortfolioSnapshot state, DelphiLivePortfolioCycleInput input)
    {
        var marks = new List<DelphiLivePositionMark>();
        foreach (var position in state.OpenPositions)
        {
            var mark = input.ExactCheckpointMarks.SingleOrDefault(m => m.PositionId == position.PositionId);
            if (mark is not null)
                marks.Add(mark);
            else if (position.OpenedUtc > input.CheckpointBarEndUtc)
            {
                // New fills still need the exact same checkpoint's symbol close.
                var candidate = input.Candidates.SingleOrDefault(c => SameSymbol(c.Symbol, position.Symbol));
                if (candidate?.Safety.CompletedBarClose is decimal close)
                    marks.Add(new(position.PositionId, position.Symbol, position.Quantity, close, input.CheckpointBarEndUtc));
            }
        }
        return DelphiLivePortfolioPolicy.CalculateExactNav(state.Cash,
            state.OpenPositions.Select(p => (p.PositionId, p.Symbol, p.Quantity)).ToArray(), marks, input.CheckpointBarEndUtc);
    }

    private async Task<DelphiLivePortfolioSnapshot> Overnight(DelphiLivePortfolioSnapshot state, DateTime closeUtc,
        DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        if (clock.UtcNow < closeUtc)
            return state;
        foreach (var action in state.PendingActions.ToArray())
        {
            if (action.Intent.Side == DelphiLiveActionSide.Buy)
                state = await EndBuy(state, action, "BuyCutoffExpired", lease, cancellationToken);
            else if (action.Status != "ExitPendingOvernight")
                state = await Commit(state, state with { Actions = ReplaceAction(state, action with { Status = "ExitPendingOvernight" }) },
                    "ExitPendingOvernight", new { action.Intent.ActionId }, lease, cancellationToken);
        }
        return state;
    }

    private Task<DelphiLivePortfolioSnapshot> EndBuy(DelphiLivePortfolioSnapshot state, DelphiLiveLedgerAction action,
        string reason, DelphiLiveLease lease, CancellationToken cancellationToken) =>
        Commit(state, state with { Actions = ReplaceAction(state, action with { Status = "Expired", CompletedUtc = clock.UtcNow, TerminalReason = reason }),
            CandidateStates = EndBuyLifecycle(state, action.Intent.Symbol, reason) },
            reason, new { action.Intent.ActionId }, lease, cancellationToken);

    private static ImmutableDictionary<string, DelphiLivePortfolioCandidateState> SetLifecycle(
        DelphiLivePortfolioSnapshot state, string symbol, DelphiLiveRecommendationState lifecycle, bool completedExit = false)
    {
        if (!state.CandidateStates.TryGetValue(symbol, out var current)) return state.CandidateStates;
        var updated = completedExit ? DelphiLiveLifecyclePolicy.AfterCompletedExit(current.Lifecycle) :
            current.Lifecycle with { State = lifecycle, ReasonCode = lifecycle.ToString() };
        return state.CandidateStates.SetItem(symbol, current with { Lifecycle = updated });
    }

    private static ImmutableDictionary<string, DelphiLivePortfolioCandidateState> EndBuyLifecycle(
        DelphiLivePortfolioSnapshot state, string symbol, string reason)
    {
        if (!state.CandidateStates.TryGetValue(symbol, out var current)) return state.CandidateStates;
        var pending = current.Lifecycle with { State = DelphiLiveRecommendationState.BuyPending };
        var updated = Enum.TryParse<DelphiLivePendingBuyEndReason>(reason, out var endReason)
            ? DelphiLiveLifecyclePolicy.ApplyPendingBuyEnd(pending, endReason)
            : pending with { State = DelphiLiveRecommendationState.EntryEligible, ReasonCode = reason };
        return state.CandidateStates.SetItem(symbol, current with { Lifecycle = updated });
    }

    private async Task<DelphiLiveQuoteReceipt> RequestQuote(DelphiLiveQuoteRequest request, DateTime deadlineUtc, CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadlineUtc - clock.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return new(request, null, null, null, clock.UtcNow, DelphiLiveIdentities.QuoteFill);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(remaining);
        try
        {
            var quote = await source.GetQuoteAsync(request, bounded.Token).WaitAsync(remaining, cancellationToken);
            if (quote.Request != request || quote.ReceivedUtc.Kind != DateTimeKind.Utc || quote.ReceivedUtc < request.RequestStartedUtc)
                throw new InvalidOperationException("Quote receipt does not match its causal request.");
            return quote;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && bounded.IsCancellationRequested)
        {
            return new(request, null, null, null, clock.UtcNow, DelphiLiveIdentities.QuoteFill);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            bounded.Cancel();
            return new(request, null, null, null, clock.UtcNow, DelphiLiveIdentities.QuoteFill);
        }
    }

    private async Task<DelphiLivePortfolioSnapshot> Load(Guid portfolioId, DateOnly date,
        DelphiLivePolicyDefinition policy, CancellationToken cancellationToken)
    {
        policy.Validate();
        var state = await store.LoadPortfolioAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException("An explicitly activated Delphi Live portfolio is required.");
        if (state.PolicyVersionId != policy.PolicyVersionId || date < state.EffectiveSession)
            throw new InvalidOperationException("Portfolio policy or effective session does not permit this operation.");
        return state;
    }

    private Task<DelphiLivePortfolioSnapshot> Commit<T>(DelphiLivePortfolioSnapshot prior, DelphiLivePortfolioSnapshot next,
        string kind, T data, DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        DateTime now = clock.UtcNow;
        next = next with { Revision = prior.Revision + 1, UpdatedUtc = now };
        DelphiLiveLedgerIntegrity.ValidateTransition(prior, next);
        return store.CommitAsync(prior.Revision, next,
            new[] { new DelphiLiveLedgerEvent(Guid.NewGuid(), kind, now, DelphiLiveLedgerJson.Serialize(data)) }, lease, cancellationToken);
    }

    private static ImmutableArray<DelphiLiveLedgerPosition> ReplacePosition(DelphiLivePortfolioSnapshot state, DelphiLiveLedgerPosition position) =>
        state.Positions.Select(p => p.PositionId == position.PositionId ? position : p).ToImmutableArray();
    private static ImmutableArray<DelphiLiveLedgerAction> ReplaceAction(DelphiLivePortfolioSnapshot state, DelphiLiveLedgerAction action) =>
        state.Actions.Select(a => a.Intent.ActionId == action.Intent.ActionId ? action : a).ToImmutableArray();
    private bool WithinRegularSession(DateTime openUtc, DateTime closeUtc) => clock.UtcNow >= openUtc && clock.UtcNow < closeUtc;
    private static bool SameSymbol(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static DateTime Earlier(DateTime left, DateTime right) => left < right ? left : right;
    private static string ActionDossier(string sourceDossier, Guid decisionId, Guid? evaluationId, string action, DateTime time,
        object? protection, Guid? sessionId, DelphiLivePolicyDefinition policy)
    {
        // Preserve the actual dossier schema: a position's entry thesis must be
        // readable directly after restart, without an incompatible wrapper.
        var document = System.Text.Json.Nodes.JsonNode.Parse(sourceDossier)?.AsObject()
            ?? throw new ArgumentException("An action requires its persisted source dossier.");
        document["decisionId"] = decisionId;
        document["decisionUtc"] = time;
        document["evaluationId"] = evaluationId ?? decisionId;
        if (sessionId.HasValue) document["delphiLiveSessionId"] = sessionId.Value;
        document["delphiLivePolicyVersionId"] = policy.PolicyVersionId;
        document["policyDefinitionName"] = policy.PolicyDefinitionName;
        document["evaluatorVersion"] = policy.EvaluatorVersion;
        document["collectorVersion"] = policy.CollectorVersion;
        document["quoteFillVersion"] = policy.QuoteFillVersion;
        document["requestedAction"] = action;
        document["actionState"] = "Pending";
        if (action == "Sell")
        {
            document["recommendationBefore"] = (int)DelphiLiveRecommendationState.Held;
            document["recommendationAfter"] = (int)DelphiLiveRecommendationState.ExitPending;
        }
        else document["recommendationAfter"] = (int)DelphiLiveRecommendationState.BuyPending;
        if (protection is not null)
        {
            var facts = document["derivedFacts"]?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
            facts["protectionDecisionEvidence"] = DelphiLiveLedgerJson.Serialize(protection);
            document["derivedFacts"] = facts.DeepClone();
            var evidence = System.Text.Json.Nodes.JsonNode.Parse(DelphiLiveLedgerJson.Serialize(protection))!;
            document["firedExitRules"] = evidence["safety"]?["firedExitRules"]?.DeepClone();
            document["primaryExitRule"] = evidence["safety"]?["primaryExitRule"]?.DeepClone();
            if (evidence["triggerQuote"]?["quoteObservationId"] is { } quoteId)
                document["evidenceQuoteIds"] = new System.Text.Json.Nodes.JsonArray(quoteId.DeepClone());
            if (!evaluationId.HasValue)
            {
                if (document["originalEntryThesis"] is null && document["calibrationRunId"] is not null)
                    document["originalEntryThesis"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["entryDecisionId"] = System.Text.Json.Nodes.JsonNode.Parse(sourceDossier)?["decisionId"]?.DeepClone(),
                        ["calibrationRunId"] = document["calibrationRunId"]?.DeepClone(),
                        ["calibrationCandidateId"] = document["calibrationCandidateId"]?.DeepClone(),
                        ["dailyStrategyVersionId"] = document["dailyStrategyVersionId"]?.DeepClone(),
                        ["sourceLenses"] = document["sourceLenses"]?.DeepClone()
                    };
                document["calibrationRunId"] = null;
                document["calibrationCandidateId"] = null;
                document["dailyStrategyVersionId"] = null;
                document["sourceLenses"] = new System.Text.Json.Nodes.JsonArray();
                document["barEndUtc"] = null;
                document["familyJudgments"] = System.Text.Json.Nodes.JsonNode.Parse(DelphiLiveLedgerJson.Serialize(
                    Enum.GetValues<DelphiLiveSignalFamily>().Select(f => new DelphiLiveFamilyJudgment(f, DelphiLiveFamilyState.NotMature, "QuoteProtectionOnly"))));
                document["momentum"] = System.Text.Json.Nodes.JsonNode.Parse(DelphiLiveLedgerJson.Serialize(
                    new DelphiLiveMomentumJudgment(DelphiLiveMomentumState.Neutral, DelphiLiveStrongTier.None, DelphiLiveNeutralDetail.None, 0, 0, 0, 0)));
                document["rawValues"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["protectionBid"] = evidence["triggerQuote"]?["bid"]?.DeepClone()
                };
            }
        }
        return document.ToJsonString(DelphiLiveLedgerJson.Options);
    }

    private static void ValidateSession(DateOnly tradingDate, DateTime openUtc, DateTime closeUtc)
    {
        if (openUtc.Kind != DateTimeKind.Utc || closeUtc.Kind != DateTimeKind.Utc || closeUtc <= openUtc)
            throw new ArgumentException("UTC regular-session boundaries are required.");
        TimeZoneInfo toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(openUtc, toronto);
        if (DateOnly.FromDateTime(local) != tradingDate || local.TimeOfDay != DelphiLiveSchedule.RegularOpen)
            throw new ArgumentException("Session boundary must match the Toronto trading date and 09:30 open.");
    }
}
