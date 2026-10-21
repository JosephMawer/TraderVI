#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Trader.DelphiLive;
using Core.Runtime;

namespace Core.Trader.Promotion;

/// <summary>One supervised account owner. Startup/recovery never retransmits a saved intent.</summary>
public sealed class BrokerManagedSessionWorkflow(IBrokerManagedSessionStore store, IBrokerManagedAccountSource source,
    IBrokerManagedOrderTransport transport, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public async Task<BrokerManagedSession> ApproveAsync(BrokerExecutionApproval approval, CancellationToken ct = default)
    {
        if (approval.Review.NoTransferScope?.Continuation is not null)
            throw new InvalidOperationException("Use the existing-assignment continuation workflow; a dated continuation cannot create a new initial session.");
        if (string.IsNullOrWhiteSpace(approval.Reviewer) || approval.Reviewer.Length > 128 ||
            string.IsNullOrWhiteSpace(approval.Reason) || approval.Reason.Length > 1024)
            throw new ArgumentException("Explicit execution approval needs its reviewer and reason.");
        await using var lease = await store.AcquireAsync(approval.Review.BindingId, ct);
        if (lease.Current is { } existing)
        {
            if (existing.Approval.Fingerprint == approval.Fingerprint) return existing;
            throw new InvalidOperationException("This account already has a durable strategy session. Review its recovery or replacement separately.");
        }
        var account = await source.SynchronizeAsync([], ct);
        approval.Review.Validate(account, clock.GetUtcNow());
        if (source.ImplementationIdentity != approval.Review.Strategy.Strategy.ImplementationIdentity)
            throw new InvalidOperationException("Execution approval must identify the running strategy implementation.");
        var state = new BrokerManagedSession(approval.Review.RequestId, approval.Review.BindingId, 0,
            approval.Review.RequestId, null, "ExecutionApproved", clock.GetUtcNow(), approval,
            BrokerManagedPhase.Transition, null, account, approval.Review.CurrentProtection,
            TradingRecommendationMemory.Empty with { TradingDate = account.Risk.TradingDate,
                DailyLossHold = account.Risk.DailyLossHold, CapitalReviewRequired = account.Risk.CapitalReviewRequired,
                HighestClosingValue = account.Risk.DrawdownReference.Value }, [], []);
        await lease.AppendAsync(state, ct);
        return state; // Approval itself does not send orders; the reviewed supervised host must explicitly tick.
    }

    public async Task<BrokerManagedSession> PauseAsync(Guid bindingId, CancellationToken ct = default)
    {
        await using var lease = await store.AcquireAsync(bindingId, ct);
        var state = lease.Current ?? throw new InvalidOperationException("No managed session exists.");
        return await Save(lease, state with { Phase = BrokerManagedPhase.Paused, Blocks = ["Execution paused by operator. Outstanding orders still require recovery."] }, "OperatorPaused", ct);
    }

    public Task<BrokerManagedSession> StopNewBuysAsync(Guid bindingId, CancellationToken ct = default) =>
        SetNewBuysStoppedAsync(bindingId, true, ct);

    public Task<BrokerManagedSession> ResumeBuyingAsync(Guid bindingId, CancellationToken ct = default) =>
        SetNewBuysStoppedAsync(bindingId, false, ct);

    private async Task<BrokerManagedSession> SetNewBuysStoppedAsync(Guid bindingId, bool stopped, CancellationToken ct)
    {
        // Serialize with transmission: an order already sent still needs normal status recovery.
        await using var lease = await store.AcquireAsync(bindingId, ct);
        // Change only the operator's purchase control. A later tick must synchronize afresh;
        // resume neither sends an order nor clears execution blocks, references or protection.
        return await BrokerManagedSessionControls.ChangeAsync(lease, stopped, clock.GetUtcNow(), ct);
    }

    public async Task<BrokerManagedSession> TickAsync(Guid bindingId, CancellationToken ct = default)
    {
        await using var lease = await store.AcquireAsync(bindingId, ct);
        var state = lease.Current ?? throw new InvalidOperationException("Explicit reviewed execution approval is required.");
        if (state.EffectiveReview.NoTransferScope?.Continuation is { PreOpenApproved: true } armed)
        {
            var open = Core.Trader.DelphiLive.ReviewedTsxSessionCalendar.At(BrokerOctober21Continuation.ExecutionDate, new TimeOnly(9, 30));
            if (clock.GetUtcNow() < open) return state; // No broker read, save, protection action or order before the open.
        }
        bool paused = state.Phase == BrokerManagedPhase.Paused;
        bool fullFillTrial = state.EffectiveReview.NoTransferScope?.FullFillOnlyNativeTrial == true;
        bool NeedsTrialReview()
        {
            try { return state.Orders.Any(o => o.Submission != WealthsimpleSubmissionStatus.Acknowledged) || source.RequiresTrialOrderReview(state.Orders); }
            catch { return true; } // A corrupt/unreadable owned-order journal cannot restore trial progression.
        }
        BrokerAccountProjection account;
        try
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerAccountRead);
            account = await source.SynchronizeAsync(state.Orders, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return await Save(lease, state with { TrialOrderReviewRequired = state.TrialOrderReviewRequired || fullFillTrial && NeedsTrialReview(),
                Phase = paused ? BrokerManagedPhase.Paused : BrokerManagedPhase.NeedsAttention,
                Blocks = ["Broker status/account synchronization is incomplete. Preserve every original order ID and retry reads only."] }, "SynchronizationUnavailable", ct);
        }
        if (account.Risk.BindingId != bindingId || account.Risk.BaselineId != state.Approval.Review.BaselineId)
            throw new InvalidOperationException("The account or initial risk baseline changed. No execution is allowed.");
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (state.EffectiveReview.NoTransferScope?.Continuation?.PreOpenApproved == true && state.Orders.IsEmpty)
        {
            try
            {
                BrokerContinuationReviewPolicy.RequireCurrentAccount(account, now);
                BrokerTrialCarryForward.RequireUnchangedBook(state.EffectiveReview.NoTransferScope.CashOnlyAnchor, account.Import.Plan.Observation);
            }
            catch (InvalidOperationException)
            {
                return await Save(lease, state with { Phase = BrokerManagedPhase.NeedsAttention,
                    Blocks = ["Armed execution requires a fresh unchanged cash-only account with no unresolved orders or risk holds."] }, "PreOpenAccountRecheckBlocked", ct);
            }
        }
        if (account.Account.TradingDate != today || account.Account.ReportedUtc > now.UtcDateTime ||
            now.UtcDateTime - account.Account.ReportedUtc > TimeSpan.FromSeconds(30) ||
            account.Risk.TradingDate > today || account.Risk.TradingDate < state.Account.Risk.TradingDate ||
            account.Risk.Through < state.Account.Risk.Through || account.Risk.Through > now)
            return await Save(lease, state with { Phase = paused ? BrokerManagedPhase.Paused : BrokerManagedPhase.NeedsAttention,
                Blocks = ["Current account facts and forward-only risk history are required before execution can resume."] }, "AccountContinuationBlocked", ct);
        var orders = state.Orders.ToBuilder();
        if (fullFillTrial && NeedsTrialReview()) state = state with { TrialOrderReviewRequired = true };
        for (int i = 0; i < orders.Count; i++)
        {
            var old = orders[i];
            var fact = account.Import.Plan.Observation.Orders.SingleOrDefault(o => o.ExternalId == old.ExternalId);
            if (fact is null || fact.SecurityId != old.SecurityId || fact.Side != old.Side || fact.SubmittedQuantity != old.Quantity ||
                fact.FilledQuantity < 0 || fact.FilledQuantity > old.Quantity ||
                old.Latest is { } prior && (fact.FilledQuantity < prior.FilledQuantity || prior.IsTerminal && !fact.IsSameOrPostedSuccessor(prior)))
                return await Save(lease, state with { TrialOrderReviewRequired = state.TrialOrderReviewRequired || fullFillTrial,
                    Phase = paused ? BrokerManagedPhase.Paused : BrokerManagedPhase.NeedsAttention,
                    Blocks = ["An original order is missing or conflicts with saved facts. No replacement order is permitted."] }, "OrderRecoveryBlocked", ct);
            orders[i] = old with { Latest = fact, Reconciled = fact.IsTerminal };
            if (fullFillTrial && (old.Submission != WealthsimpleSubmissionStatus.Acknowledged ||
                fact.FilledQuantity > 0 && fact.FilledQuantity < old.Quantity ||
                fact.IsTerminal && (fact.Status is not ("FILLED" or "POSTED") || fact.FilledQuantity != old.Quantity)))
                state = state with { TrialOrderReviewRequired = true };
        }
        // The imported history owns financial truth; restore strategy attribution only for exact owned orders.
        var fills = account.Fills.Select(fill =>
        {
            var owned = orders.SingleOrDefault(o => BrokerManagedStrategy.StableId($"{bindingId:D}:{o.ExternalId}") == fill.TradeId);
            return owned is null ? fill : fill with { RecommendationKind = owned.Kind, PositionTarget = owned.PositionTarget,
                ExitReason = owned.Side == "SELL" ? owned.Kind : null };
        }).ToImmutableArray();
        account = account with { Fills = fills };
        var oldFacts = state.Account.Import.Plan.Observation.Orders.ToDictionary(o => o.ExternalId);
        bool externalChange = account.Import.Plan.Observation.Orders.Any(f => f.FilledQuantity > 0 &&
            !orders.Any(o => o.ExternalId == f.ExternalId) &&
            (!oldFacts.TryGetValue(f.ExternalId, out var old) || f.FilledQuantity != old.FilledQuantity ||
                f.NetValue != old.NetValue || f.SecurityId != old.SecurityId || f.Side != old.Side)) ||
            oldFacts.Values.Any(f => f.FilledQuantity > 0 && !account.Import.Plan.Observation.Orders.Any(n => n.ExternalId == f.ExternalId));
        if (state.ReviewInvalidated || externalChange)
            return await Save(lease, state with { Account = account, Orders = orders.ToImmutable(), ReviewInvalidated = true,
                Phase = BrokerManagedPhase.NeedsAttention, Blocks = ["An outside fill or financial correction changed the approved account. Explicit review is required; no further managed orders are permitted."] }, "AccountReviewInvalidated", ct);
        state = await Save(lease, state with { Account = account, Orders = orders.ToImmutable(),
            Memory = state.Memory with { TradingDate = account.Risk.TradingDate,
                DailyLossHold = account.Risk.DailyLossHold || state.Memory.TradingDate == account.Risk.TradingDate && state.Memory.DailyLossHold,
                CapitalReviewRequired = account.Risk.CapitalReviewRequired || state.Memory.CapitalReviewRequired,
                HighestClosingValue = account.Risk.DrawdownReference.Value },
            Phase = paused || state.TrialOrderReviewRequired ? BrokerManagedPhase.Paused : state.ActivatedAt.HasValue ? BrokerManagedPhase.Active : BrokerManagedPhase.Transition,
            Blocks = state.TrialOrderReviewRequired ? ["The full-fill-only trial observed a partial, cancelled, conflicting or uncertain order. Original-ID reads may recover facts; operator review is required before any further automatic order."]
                : paused ? state.Blocks : account.NewRiskBlocks }, "AccountSynchronized", ct);
        if (paused || state.TrialOrderReviewRequired || state.Orders.Any(o => !o.Reconciled)) return state;
        if (source.ImplementationIdentity != state.Strategy.Strategy.ImplementationIdentity)
            return await Save(lease, state with { Phase = BrokerManagedPhase.NeedsAttention,
                Blocks = ["The running strategy implementation changed. Review execution authority before placing further orders."] }, "ImplementationChanged", ct);
        if (account.Account.TradingDate < state.Approval.Review.Session ||
            !state.IsOngoing && state.Approval.Review.Session != account.Account.TradingDate)
            return await Save(lease, state with { Phase = BrokerManagedPhase.Paused,
                Blocks = ["The approved supervised session ended. Continue risk tracking and review the next execution session explicitly."] }, "SessionAuthorityEnded", ct);
        if (!state.ActivatedAt.HasValue)
        {
            var unexpected = account.Positions.Where(p => !state.Approval.Review.RequiredExits.Contains(p.PositionId) &&
                !state.Approval.Review.RetainedPositions.Contains(p.PositionId)).ToArray();
            if (unexpected.Length > 0)
                return await Save(lease, state with { Phase = BrokerManagedPhase.NeedsAttention,
                    Blocks = ["External holdings changed the reviewed transition. A new review is required."] }, "TransitionChanged", ct);
            var exit = account.Positions.FirstOrDefault(p => state.Approval.Review.RequiredExits.Contains(p.PositionId));
            if (exit is not null)
            {
                var original = state.Orders.Where(o => o.PositionId == exit.PositionId && o.Side == "SELL").ToArray();
                var reviewed = state.Approval.Review.ReviewedPositions.Single(p => p.PositionId == exit.PositionId);
                if (exit.Quantity + original.Sum(o => o.Latest?.FilledQuantity ?? 0) != reviewed.Quantity)
                    return await Save(lease, state with { Phase = BrokerManagedPhase.NeedsAttention, ReviewInvalidated = true,
                        Blocks = ["The reviewed exit quantity changed. Review the transition again."] }, "ExitQuantityChanged", ct);
                if (original.Any(o => o.Latest is null || !o.Reconciled)) return state;
                var decision = BrokerManagedStrategy.StableId($"{state.SessionId:D}:transition:{exit.PositionId:D}:{account.Fingerprint}");
                if (state.Orders.Any(o => o.DecisionId == decision)) return state;
                return await Submit(lease, state, new(BrokerManagedStrategy.StableId(decision.ToString("D")), decision,
                    exit.PositionId, exit.SecurityId, exit.Symbol, "SELL", exit.Quantity, 0, "TransitionExit", null, clock.GetUtcNow()), ct);
            }
            if (!account.NewRiskBlocks.IsEmpty) return state;
            if (!state.Approval.Review.RetainedPositions.Order().SequenceEqual(account.Positions.Select(p => p.PositionId).Order()))
                return await Save(lease, state with { Phase = BrokerManagedPhase.NeedsAttention,
                    Blocks = ["The retained position set changed. Review protection ownership again."] }, "RetainedSetChanged", ct);
            var marks = await source.ReadProtectionMarksAsync(account, ct);
            var at = clock.GetUtcNow();
            var protection = BrokerProtectionHandover.Transfer(state.Strategy, account.Positions, state.Protection,
                marks, at);
            state = await Save(lease, state with { Phase = BrokerManagedPhase.Active, ActivatedAt = at, Protection = protection,
                Memory = state.Memory with { Symbols = protection.ToImmutableDictionary(p => account.Positions.Single(h => h.PositionId == p.PositionId).Symbol,
                    p => p.State, StringComparer.OrdinalIgnoreCase) } }, "ProtectionTransferredAndStrategyActivated", ct);
        }
        // A strategy fill opens a real cycle at its own fill boundary, independently of the activation time.
        var acquired = account.Positions.Where(p => !state.Protection.Any(s => s.PositionId == p.PositionId)).ToArray();
        if (acquired.Length > 0)
        {
            var protections = acquired.Select(p => state.Strategy.Strategy.Family == TradingStrategyFamily.DelphiLive
                ? BrokerLiveProtection.Transfer(state.Strategy, p, null, p.EntryCompletedAt)
                : new BrokerPositionProtection(p.PositionId, state.Strategy.VersionId.ToString("D"),
                    null, p.EntryCompletedAt, new(p.PositionId, SystemShadowTrailingState.Open(p.CostBasis / p.Quantity), null, null, null, null, null)));
            state = await Save(lease, state with { Protection = state.Protection.AddRange(protections) }, "FilledPositionsProtected", ct);
        }
        TradingRecommendationInput input;
        try
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerInputRead);
            input = await source.ReadStrategyInputAsync(state, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return await Save(lease, state with { Phase = BrokerManagedPhase.NeedsAttention,
                Blocks = ["Current strategy and protection-recovery evidence could not be read. Preserve the saved assignment and retry the read."] }, "StrategyEvidenceUnavailable", ct);
        }
        if (input.Assignment.Strategy.Fingerprint != state.Strategy.Strategy.Fingerprint ||
            input.CurrentLedgerFingerprint != account.Account.LedgerFingerprint || input.Account != account.Account ||
            input.Assignment.PromotionId != state.SessionId || input.TradingDate != account.Account.TradingDate)
            throw new InvalidOperationException("Strategy evaluation must use this exact approved strategy and reconciled broker account.");
        // Recommendation-only mode may still advise protection after a build change. Real execution
        // must use the exact implementation that received operational approval, on every trading day.
        if (input.ImplementationIdentity != state.Strategy.Strategy.ImplementationIdentity)
            return await Save(lease, state with { Phase = BrokerManagedPhase.NeedsAttention,
                Blocks = ["The running strategy implementation changed. Review execution authority before placing further orders."] }, "ImplementationChanged", ct);
        bool currentRiskDay = account.Risk.TradingDate == account.Account.TradingDate;
        bool trialPurchasesEnded = state.EffectiveReview.NoTransferScope is { LimitedFirstSessionTrial: true } trial &&
            (trial.Session != today || trial.PurchaseExpiresAt is null || now >= trial.PurchaseExpiresAt);
        var savedRiskTradingDate = state.Memory.TradingDate;
        input = input with { EntriesPaused = input.EntriesPaused || state.NewBuysStopped || trialPurchasesEnded || !currentRiskDay || !account.NewRiskBlocks.IsEmpty,
            PreviousMemory = state.Memory, ActualFills = account.Fills,
            ProtectionBoundaries = state.Protection.ToImmutableDictionary(p => p.PositionId, p => p.EstablishedAt.UtcDateTime) };
        if (!currentRiskDay)
            // Keep protective exits available without treating yesterday's reference as today's opening
            // or allowing the recommendation engine's date rollover to release the saved daily hold.
            input = input with { Account = input.Account! with { SessionOpeningValue = null },
                PreviousMemory = state.Memory with { TradingDate = input.TradingDate } };
        var recoveryBlocks = BrokerProtectionHandover.RecoveryBlocks(input, state.Protection);
        if (!recoveryBlocks.IsEmpty)
            return await Save(lease, state with { Phase = BrokerManagedPhase.NeedsAttention, Blocks = recoveryBlocks }, "ProtectionRecoveryBlocked", ct);
        var replay = BrokerProtectionHandover.Replay(input, state.Protection);
        input = input with { PreviousMemory = replay.Memory };
        TradingQuoteReadResult? observedQuotes = null;
        if (state.Strategy.Strategy.Family == TradingStrategyFamily.DelphiLive)
        {
            TradingRecommendationResult prepared;
            using (PaperCycleTimings.Start(PaperCycleStage.BrokerLivePrepare))
                prepared = TradingRecommendationEngine.Evaluate(input with { Quotes = [] });
            await source.PreserveRiskHoldsAsync(account, prepared.Memory, ct);
            state = await Save(lease, state with { Memory = prepared.Memory, Protection = replay.Protection,
                LatestDecision = CaptureTrace(input, prepared, null, null, "QuoteObservationPending") }, "LiveProtectionPrepared", ct);
            // Persist raised stops and observed exits before requesting a bid that can trigger them.
            input = input with { EvaluatedUtc = state.SavedAt.UtcDateTime, PreviousMemory = prepared.Memory };
            try
            {
                using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerQuoteRead);
                observedQuotes = await source.ReadQuoteObservationAsync(input, ct);
            }
            catch (Exception error)
            {
                await RecordDecisionFailure(lease, state, error is OperationCanceledException ? "QuoteObservationCancelled" : "QuoteObservationFailed", error);
                throw;
            }
            input = input with { Quotes = observedQuotes.Quotes };
            if (state.LatestDecision is { } awaiting)
                state = state with { LatestDecision = awaiting with { QuoteAttempts = observedQuotes.Attempts,
                    QuoteReadBlocks = observedQuotes.NewRiskBlocks, SelectionOutcome = "QuotesObservedPendingEvaluation" } };
            try { replay = BrokerProtectionHandover.Replay(input, state.Protection); }
            catch (Exception error)
            {
                await RecordDecisionFailure(lease, state, "ProtectionReplayFailed", error);
                throw;
            }
            input = input with { PreviousMemory = replay.Memory };
        }
        TradingRecommendationResult result;
        try
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerEvaluate);
            result = TradingRecommendationEngine.Evaluate(input);
        }
        catch (Exception error)
        {
            await RecordDecisionFailure(lease, state, "FinalEvaluationFailed", error);
            throw;
        }
        if (!currentRiskDay)
            result = result with { Memory = result.Memory with { TradingDate = savedRiskTradingDate,
                DailyLossHold = state.Memory.DailyLossHold || result.Memory.DailyLossHold } };
        if (state.LatestDecision is not null)
            state = state with { LatestDecision = CaptureTrace(input, result, observedQuotes, null, "RiskHoldPersistencePending") };
        try { await source.PreserveRiskHoldsAsync(account, result.Memory, ct); }
        catch (Exception error)
        {
            await RecordDecisionFailure(lease, state, "RiskHoldPersistenceFailed", error);
            throw;
        }
        var recommendation = result.Recommendations.Where(r => r.Action is "Sell" or "Buy" or "Add")
            .OrderBy(r => r.Action == "Sell" ? 0 : 1).FirstOrDefault();
        var trace = CaptureTrace(input, result, observedQuotes, recommendation, recommendation is null ? "NoRecommendation" : "SelectedPendingFinalChecks");
        state = await Save(lease, state with { Memory = result.Memory, Protection = replay.Protection, LatestDecision = trace }, "StrategyEvaluated", ct);
        if (recommendation is null) return state;
        bool sell = recommendation.Action == "Sell";
        if (!sell && (state.NewBuysStopped || trialPurchasesEnded || !currentRiskDay || !account.NewRiskBlocks.IsEmpty || state.Memory.DailyLossHold || state.Memory.CapitalReviewRequired))
            return await SaveDecisionOutcome(lease, state, "NewRiskBlockedBeforeReservation", ct);
        string security;
        try
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerSecurityRead);
            security = account.Positions.SingleOrDefault(p => p.Symbol == recommendation.Symbol)?.SecurityId ??
                await source.ResolveSecurityAsync(recommendation.Symbol, ct);
        }
        catch (Exception error)
        {
            await RecordDecisionFailure(lease, state, "SecurityResolutionFailed", error);
            throw;
        }
        Guid decisionId = BrokerManagedStrategy.StableId($"{state.SessionId:D}:{recommendation.Symbol}:{recommendation.Action}:{recommendation.Kind}:{recommendation.PositionId}:{recommendation.EvidenceId}:{recommendation.CandidateId}");
        if (state.Orders.Any(o => o.DecisionId == decisionId))
            return await SaveDecisionOutcome(lease, state, "OriginalDecisionAlreadyReserved", ct);
        decimal price = recommendation.ReferencePrice ?? 0;
        if (recommendation.Quantity < 1 || !sell && (price <= 0 || recommendation.Quantity * price >
            System.Math.Min(account.Account.AvailableCash ?? 0, state.EffectiveReview.PurchaseCeiling - state.CommittedPurchaseCost)))
            return await SaveDecisionOutcome(lease, state, recommendation.Quantity < 1 ? "InvalidOrderQuantity" : "PurchaseCeilingOrCashExceeded", ct);
        return await Submit(lease, state, new(BrokerManagedStrategy.StableId(decisionId.ToString("D")), decisionId,
            recommendation.PositionId, security, recommendation.Symbol, sell ? "SELL" : "BUY", recommendation.Quantity,
            price, sell ? recommendation.Reason : recommendation.Kind, recommendation.PositionTarget, clock.GetUtcNow()), ct, evaluatedDecision: true);
    }

    private async Task<BrokerManagedSession> Submit(IBrokerManagedSessionLease lease, BrokerManagedSession state,
        BrokerStrategyOrder order, CancellationToken ct, bool evaluatedDecision = false)
    {
        IBrokerManagedOrderPreflight prepared;
        try
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerPreflight);
            prepared = await transport.PrepareAsync(order, state.Account, ct);
        }
        catch (Exception error)
        {
            if (evaluatedDecision) await RecordDecisionFailure(lease, state, "PreflightFailed", error);
            throw;
        }
        await using var preflight = prepared;
        ct.ThrowIfCancellationRequested();
        if (order.Side == "BUY" && state.EffectiveReview.NoTransferScope is { LimitedFirstSessionTrial: true } trial &&
            (trial.Session != DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(clock.GetUtcNow().UtcDateTime)) ||
             trial.PurchaseExpiresAt is null || clock.GetUtcNow() >= trial.PurchaseExpiresAt))
            return await Save(lease, Outcome(state, evaluatedDecision, "PermissionExpiredBeforeReservation") with { Blocks = state.Blocks.Add("Limited first-session purchase permission ended; no order was reserved or sent.") },
                "TrialPurchasesEnded", ct);
        state = await Save(lease, Outcome(state, evaluatedDecision, "IntentReserved") with { Orders = state.Orders.Add(order) }, "OrderIntentReserved", ct);
        if (order.Side == "BUY" && state.EffectiveReview.NoTransferScope is { LimitedFirstSessionTrial: true } beforeSend &&
            (beforeSend.Session != DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(clock.GetUtcNow().UtcDateTime)) ||
             beforeSend.PurchaseExpiresAt is null || clock.GetUtcNow() >= beforeSend.PurchaseExpiresAt))
            return await Save(lease, Outcome(state, evaluatedDecision, "PermissionExpiredAfterReservationNoSend") with { TrialOrderReviewRequired = beforeSend.FullFillOnlyNativeTrial,
                Phase = BrokerManagedPhase.NeedsAttention, Blocks = ["Purchase permission expired after intent save. No transmission was attempted; recover this original intent before further orders."] },
                "ReservedBuyExpiredBeforeSend", CancellationToken.None);
        WealthsimpleSubmissionStatus result;
        try
        {
            using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerSubmit);
            result = await preflight.SubmitOnceAsync(ct);
        }
        catch (Exception error)
        {
            result = WealthsimpleSubmissionStatus.OutcomeUnknown;
            if (evaluatedDecision) state = DecisionFailure(state, "SubmissionOutcomeUnknown", error);
        }
        // If this save fails, the durable intent remains unresolved. Recovery only reads its original ID.
        bool trialReview = state.EffectiveReview.NoTransferScope?.FullFillOnlyNativeTrial == true && result != WealthsimpleSubmissionStatus.Acknowledged;
        return await Save(lease, Outcome(state, evaluatedDecision, "Submission" + result) with { Orders = state.Orders.SetItem(state.Orders.Length - 1, order with { Submission = result }),
            TrialOrderReviewRequired = state.TrialOrderReviewRequired || trialReview,
            Phase = trialReview ? BrokerManagedPhase.Paused : state.Phase },
            "OrderTransmissionObserved", CancellationToken.None);
    }
    private async Task<BrokerManagedSession> Save(IBrokerManagedSessionLease lease, BrokerManagedSession state, string reason, CancellationToken ct)
    {
        using var timing = PaperCycleTimings.Start(PaperCycleStage.BrokerStateSave);
        var current = lease.Current!;
        var next = state with { Revision = current.Revision + 1, PreviousRevisionId = current.RevisionId,
            RevisionId = Guid.NewGuid(), SavedAt = clock.GetUtcNow(), Event = reason };
        await lease.AppendAsync(next, ct); return next;
    }

    private static BrokerManagedSession Outcome(BrokerManagedSession state, bool applies, string outcome) =>
        applies && state.LatestDecision is { } trace ? state with { LatestDecision = trace with { SelectionOutcome = outcome } } : state;

    private BrokerDecisionTrace? CaptureTrace(TradingRecommendationInput input, TradingRecommendationResult result,
        TradingQuoteReadResult? quotes, TradingRecommendation? selected, string outcome) => result.EntryDecisions is { } entries
        ? new(input.EvaluatedUtc, clock.GetUtcNow(), entries.Select(e => e.Outcome != "Recommended" ? e : e with
        {
            ExecutionSelection = selected is null ? null : selected.Symbol == e.Symbol && selected.Action is "Buy" or "Add"
                ? "Selected" : selected?.Action == "Sell" ? "DeferredForProtectiveExit" : "DeferredForEarlierRecommendation"
        }).ToImmutableArray(), quotes?.Attempts ?? [], result.EntryRiskBlocks ?? [], outcome, selected?.Symbol, selected?.Action)
        { QuoteReadBlocks = quotes?.NewRiskBlocks ?? [] } : null;

    private Task<BrokerManagedSession> SaveDecisionOutcome(IBrokerManagedSessionLease lease, BrokerManagedSession state,
        string outcome, CancellationToken ct) => state.LatestDecision is null ? Task.FromResult(state)
        : Save(lease, Outcome(state, true, outcome), "DecisionDeferred", ct);

    private async Task RecordDecisionFailure(IBrokerManagedSessionLease lease, BrokerManagedSession state, string outcome, Exception error)
    {
        if (state.LatestDecision is not { } trace) return;
        try
        {
            await Save(lease, DecisionFailure(state, outcome, error),
                "DecisionStageFailed", CancellationToken.None);
        }
        catch { /* Preserve the original failure and any already-saved intent; never retry a financial action for telemetry. */ }
    }

    private static BrokerManagedSession DecisionFailure(BrokerManagedSession state, string outcome, Exception error)
    {
        if (state.LatestDecision is not { } trace) return state;
        var known = BrokerOrderFailureEvidence.Read(error);
        return state with { LatestDecision = trace with { SelectionOutcome = outcome, FailureType = error.GetType().Name,
            FailedOrderCheck = known?.Check, LocalCheckExplanation = known?.LocalExplanation,
            FailureSqlNumber = error is Microsoft.Data.SqlClient.SqlException sql ? sql.Number : null } };
    }
}
