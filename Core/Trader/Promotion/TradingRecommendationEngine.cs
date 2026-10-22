#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Account-specific advice. No paper account, order writer, SQL connection or WPF dependency.</summary>
public static class TradingRecommendationEngine
{
    public static TradingRecommendationResult Evaluate(TradingRecommendationInput input)
    {
        var strategy = input.Assignment.Strategy;
        strategy.Validate();
        if(input.CurrentHoldings.Any(h=>h.Quantity<=0 || h.CostBasis<=0) ||
            input.CurrentHoldings.Select(h=>h.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=input.CurrentHoldings.Length ||
            input.Candidates.Any(c=>c.RunId!=input.FrozenRunId))
            throw new ArgumentException("Account holdings and candidates must have consistent financial and run identities.");
        if (input.BatchId == Guid.Empty || input.EvaluatedUtc.Kind != DateTimeKind.Utc ||
            input.EvaluatedUtc < input.Assignment.EffectiveUtc || input.SessionCloseUtc <= input.SessionOpenUtc)
            throw new ArgumentException("Evaluation must follow its approved assignment and have a valid session boundary.");
        var warnings = input.SourceWarnings.ToBuilder();
        var rows = ImmutableArray.CreateBuilder<TradingRecommendation>();
        var entries = ImmutableArray.CreateBuilder<TradingEntryDecision>();
        var memory = input.PreviousMemory;
        if (memory.TradingDate != input.TradingDate)
            memory = memory with { TradingDate = input.TradingDate, DailyLossHold = false };
        var symbols = memory.Symbols.ToBuilder();
        foreach (var key in symbols.Keys.ToArray())
            if (symbols[key].PositionId is Guid position && !input.CurrentHoldings.Any(h => h.PositionId == position))
                symbols[key] = symbols[key] with { PositionId = null, ShadowProtection = null, LiveProtection = null, ExitReason = null };

        if (strategy.ImplementationIdentity != input.ImplementationIdentity)
        {
            warnings.Add("The running implementation differs from the approved strategy. New entries wait for review; existing holdings receive current protective checks.");
        }

        var readiness = TradingAccountReadinessPolicy.Evaluate(input.Account, input.Assignment.AccountLabel,
            input.TradingDate, input.EvaluatedUtc, input.CurrentLedgerFingerprint, input.EntriesPaused);
        warnings.AddRange(readiness.NewRiskBlocks);
        var entryBlocks = readiness.NewRiskBlocks.ToBuilder();
        if (strategy.ImplementationIdentity != input.ImplementationIdentity) entryBlocks.Add("ImplementationMismatch");
        bool riskAllowed = readiness.CanConsiderNewRisk && strategy.ImplementationIdentity == input.ImplementationIdentity;
        if (input.CurrentDailyStrategyId != strategy.Daily.StrategyVersionId || input.FrozenRunId is null)
        {
            warnings.Add("New entries need a valid frozen daily run using the approved daily model/strategy version.");
            riskAllowed = false;
            entryBlocks.Add(input.CurrentDailyStrategyId != strategy.Daily.StrategyVersionId ? "DailyDefinitionMismatch" : "FrozenRunUnavailable");
        }
        if (input.EvaluatedUtc < input.SessionOpenUtc || input.EvaluatedUtc > input.SessionCloseUtc.AddMinutes(45))
        {
            warnings.Add("Outside the regular-session evidence window. Resume evaluation in the next session.");
            return new("Waiting", [], warnings.Distinct().ToImmutableArray(), memory);
        }
        // Revalue reported cash with exact current marks; a stale manually typed NAV never overrides missing holding prices.
        decimal? nav = input.Account is {Currency:"CAD"} reported && reported.TradingDate==input.TradingDate &&
            string.Equals(reported.AccountLabel,input.Assignment.AccountLabel,StringComparison.OrdinalIgnoreCase) &&
            reported.ReportedUtc<=input.EvaluatedUtc && reported.LedgerFingerprint==input.CurrentLedgerFingerprint
            ? reported.ValuationCash ?? reported.AvailableCash : null;
        foreach (var holding in input.CurrentHoldings)
        {
            var price = Latest(input, holding.Symbol, 5);
            if (price is null) { nav = null; warnings.Add($"{holding.Symbol}: current holding price is unavailable; new risk waits."); }
            else if (nav.HasValue) nav += holding.Quantity * price.Close;
        }
        if (nav is not > 0m) { riskAllowed = false; entryBlocks.Add("NetAssetValueUnavailable"); }
        if (nav is > 0m && input.Account?.SessionOpeningValue is > 0m && input.Account.HighestClosingValue is > 0m)
        {
            decimal high = System.Math.Max(input.Account.HighestClosingValue.Value, memory.HighestClosingValue ?? 0m);
            if (strategy.Family == TradingStrategyFamily.DailyShadow)
            {
                var guard = SystemShadowPolicy.EvaluateGuards(nav.Value, input.Account.SessionOpeningValue.Value, high, strategy.ReadShadowPolicy());
                memory = memory with { DailyLossHold = memory.DailyLossHold || input.Account.DailyLossHold || guard.DailyBuyingPaused,
                    CapitalReviewRequired = memory.CapitalReviewRequired || input.Account.CapitalReviewRequired || guard.CapitalReviewRequired,
                    HighestClosingValue = high };
            }
            else
            {
                var guard = DelphiLivePortfolioPolicy.EvaluateGuards(nav.Value, input.Account.SessionOpeningValue, high,
                    memory.DailyLossHold || input.Account.DailyLossHold, memory.CapitalReviewRequired || input.Account.CapitalReviewRequired, strategy.ReadLivePolicy());
                memory = memory with { DailyLossHold = guard.DailyBuyingPaused, CapitalReviewRequired = guard.CapitalReviewRequired, HighestClosingValue = high };
            }
        }
        if (memory.DailyLossHold || memory.CapitalReviewRequired)
        {
            riskAllowed = false;
            if (memory.DailyLossHold) entryBlocks.Add("DailyLossHold");
            if (memory.CapitalReviewRequired) entryBlocks.Add("CapitalReviewRequired");
            warnings.Add("The account's loss/capital-review hold is active. Promotion and ordinary resume do not clear it.");
        }
        if (strategy.Family == TradingStrategyFamily.DailyShadow)
            Shadow(input, nav, riskAllowed, symbols, rows, warnings);
        else
            Live(input, nav, riskAllowed, symbols, rows, warnings, entries);
        memory = memory with { Symbols = symbols.ToImmutable() };
        if(strategy.Family==TradingStrategyFamily.DailyShadow && riskAllowed && nav is >0m &&
            input.EvaluatedUtc>=input.SessionOpenUtc.AddMinutes(20) && input.EvaluatedUtc<input.SessionCloseUtc)
            memory=memory with {ShadowOpeningReviewed=input.TradingDate};
        return new(rows.Any(r => r.Action is "Buy" or "Add" or "Sell") ? "Recommendations ready" : warnings.Count > 0 ? "Waiting / review notes" : "No qualifying action",
            rows.ToImmutable(), warnings.Distinct().ToImmutableArray(), memory)
        {
            EntryDecisions = strategy.Family == TradingStrategyFamily.DelphiLive ? entries.ToImmutable() : null,
            EntryRiskBlocks = strategy.Family == TradingStrategyFamily.DelphiLive ? entryBlocks.Distinct().ToImmutableArray() : null
        };
    }

    private static void Shadow(TradingRecommendationInput input, decimal? nav, bool riskAllowed,
        ImmutableDictionary<string, TradingSymbolState>.Builder states,
        ImmutableArray<TradingRecommendation>.Builder rows, ImmutableArray<string>.Builder warnings)
    {
        var policy = input.Assignment.Strategy.ReadShadowPolicy();
        var entry = input.Candidates.ToDictionary(c => c.Symbol, c => Entry(input, c), StringComparer.OrdinalIgnoreCase);
        foreach (var holding in input.CurrentHoldings)
        {
            var prior = states.GetValueOrDefault(holding.Symbol);
            decimal average = holding.CostBasis / holding.Quantity;
            var trail = prior?.PositionId == holding.PositionId && prior.ShadowProtection is not null
                ? prior.ShadowProtection with { AverageCost = average } : SystemShadowTrailingState.Open(average);
            var five = Latest(input, holding.Symbol, 5);
            var fifteen = Latest(input, holding.Symbol, 15);
            DateTime entered = LastEntry(input, holding.PositionId) ?? input.Assignment.EffectiveUtc;
            if (input.ProtectionBoundaries.TryGetValue(holding.PositionId, out var boundary) && boundary > entered) entered = boundary;
            if(five?.StartUtc.AddMinutes(5)<=entered)five=null;
            if(fifteen?.StartUtc.AddMinutes(15)<=entered)fifteen=null;
            string? exit = prior?.PositionId == holding.PositionId ? prior.ExitReason : null;
            if (five is null) warnings.Add($"{holding.Symbol}: protection is waiting for valid five-minute evidence.");
            else
            {
                var protectedState = SystemShadowProtection.Evaluate(trail,
                    new(five.StartUtc, five.Low, five.Close),
                    fifteen is null ? null : new(fifteen.StartUtc, fifteen.Low, fifteen.Close),
                    entered, policy);
                trail = protectedState.State;
                if (protectedState.ExitReason != SystemShadowExitReason.None) exit ??= protectedState.ExitReason.ToString();
                if (input.EvaluatedUtc >= input.SessionCloseUtc &&
                    input.HoldingSessionOrdinals.TryGetValue(holding.PositionId, out int ordinal) &&
                    SystemShadowPolicy.ShouldExitAtSessionTwoClose(five.Close, average, ordinal, policy)) exit ??= "SessionTwoUnprofitable";
            }
            TradingQuote? progressiveQuote = null;
            if (policy.UseProgressiveProfitProtection || trail.Progressive is not null)
            {
                var progressive = trail.Progressive ?? ProgressiveProfitProtection.Carry(average, trail.TrailingStopPrice, input.EvaluatedUtc);
                DateTime ownedSince = input.ActualFills.Where(f => f.PositionId == holding.PositionId && f.Side == "BUY")
                    .Select(f => (DateTime?)f.FillUtc).Min() ?? entered;
                if (input.Bars.TryGetValue(holding.Symbol, out var prices))
                    foreach (var bar in prices.Where(b => b.Minutes == 5 && b.StartUtc.AddMinutes(5) > ownedSince &&
                        b.StartUtc.AddMinutes(5) <= input.EvaluatedUtc && b.ReceivedUtc <= input.EvaluatedUtc &&
                        b.ReceivedUtc >= b.StartUtc.AddMinutes(5) && b.Close > 0m).OrderBy(b => b.StartUtc))
                    {
                        var buys = input.ActualFills.Where(f => f.PositionId == holding.PositionId && f.Side == "BUY" && f.FillUtc <= bar.StartUtc.AddMinutes(5)).ToArray();
                        decimal cost = buys.Length > 0 ? buys.Sum(f => f.Price * f.Quantity) / buys.Sum(f => f.Quantity) : average;
                        progressive = ProgressiveProfitProtection.ObserveClose(progressive, cost, bar.StartUtc.AddMinutes(5), bar.Close,
                            input.EvaluatedUtc, trail.TrailingStopPrice);
                    }
                progressive = ProgressiveProfitProtection.Preserve(progressive with { AverageCost = average }, trail.TrailingStopPrice, input.EvaluatedUtc);
                progressiveQuote = Quote(input, holding.Symbol);
                if (progressiveQuote is not null)
                {
                    var bid = ProgressiveProfitProtection.ObserveBid(progressive, progressiveQuote.Bid,
                        progressiveQuote.RequestedUtc, progressiveQuote.ReceivedUtc, progressiveQuote.ReceivedUtc, progressiveQuote.SourceContract);
                    progressive = bid.State;
                }
                if (progressive.ExitTriggered) exit ??= SystemShadowExitReason.TrailingProfit.ToString();
                trail = trail with { Progressive = progressive };
            }
            states[holding.Symbol] = new(holding.PositionId, trail, null, null, null, null, null, ExitReason: exit);
            rows.Add(new(holding.Symbol, exit is null ? "Hold" : "Sell", exit is null ? 0 : holding.Quantity,
                progressiveQuote?.Bid ?? five?.Close, exit ?? "No protective exit established", holding.PositionId, null, five?.EvidenceId,
                progressiveQuote?.ReceivedUtc ?? five?.StartUtc.AddMinutes(5), exit is null ? "Holding" : "RiskExit"));
        }
        bool entryWindow = input.EvaluatedUtc >= input.SessionOpenUtc.AddMinutes(20) && input.EvaluatedUtc < input.SessionCloseUtc;
        if (!entryWindow || !riskAllowed || nav is null) return;
        decimal cash = input.Account!.AvailableCash!.Value;
        int vacancies = input.Assignment.Strategy.MaximumHoldings - input.CurrentHoldings.Length;
        // Rotation never frees a slot or cash until a separately reported sale changes the financial ledger.
        if (input.PreviousMemory.ShadowOpeningReviewed != input.TradingDate && vacancies <= 0)
        {
            var contender = input.Candidates.OrderBy(c => c.Rank).FirstOrDefault(c => entry[c.Symbol].IsEligible && !Held(input, c.Symbol));
            if (contender is not null)
            {
                var incumbent = input.CurrentHoldings.OrderBy(h => (Latest(input,h.Symbol,5)?.Close ?? decimal.MaxValue) / (h.CostBasis/h.Quantity))
                    .FirstOrDefault(h => Latest(input,h.Symbol,5) is { } price && input.HoldingSessionOrdinals.TryGetValue(h.PositionId,out int ordinal) &&
                        SystemShadowPolicy.ShouldReplaceAtSessionTwoOpening(price.Close,h.CostBasis/h.Quantity,
                            entry.GetValueOrDefault(h.Symbol) ?? new(false,SystemShadowEntryReason.MissingEvidence),true,ordinal,policy));
                if (incumbent is not null)
                {
                    int i = Find(rows,incumbent.Symbol); var row = rows[i];
                    if(row.Action != "Sell") rows[i] = row with { Action="Sell",Quantity=incumbent.Quantity,Reason="SessionTwoRotation",Kind="RotationExit" };
                    states[incumbent.Symbol] = states[incumbent.Symbol] with { ExitReason=rows[i].Reason };
                }
            }
        }
        foreach (var candidate in input.Candidates.OrderBy(c => c.Rank))
        {
            if (!entry[candidate.Symbol].IsEligible) continue;
            var five = Latest(input,candidate.Symbol,5)!;
            var holding = input.CurrentHoldings.FirstOrDefault(h=>Same(h.Symbol,candidate.Symbol));
            decimal target; decimal budget; string kind;
            if (holding is not null)
            {
                if (rows[Find(rows,holding.Symbol)].Action == "Sell" || holding.EntryDate >= input.TradingDate ||
                    SystemShadowPolicy.AdjustedSellPrice(five.Close,policy) <= holding.CostBasis/holding.Quantity) continue;
                var purchases = input.ActualFills.Where(f=>f.PositionId==holding.PositionId && f.Side=="BUY").OrderBy(f=>f.FillUtc).ToArray();
                if (purchases.Length != 1 || purchases[0].RecommendationKind is not ("Initial" or "Reentry") || purchases[0].PositionTarget is not > 0m)
                { warnings.Add($"{holding.Symbol}: add-on needs its reported initial fill and original position target; no paper target is substituted."); continue; }
                target=purchases[0].PositionTarget!.Value; budget=SystemShadowPolicy.AddOnBudget(target,policy); kind="AddOn";
            }
            else
            {
                if(vacancies<=0) continue;
                var today=input.ActualFills.Where(f=>Same(f.Symbol,candidate.Symbol)&&f.TradingDate==input.TradingDate).ToArray();
                int entries=today.Count(f=>f.Side=="BUY" && f.RecommendationKind!="AddOn");
                string? reason=today.Where(f=>f.Side=="SELL").OrderByDescending(f=>f.FillUtc).FirstOrDefault()?.ExitReason;
                if(!SystemShadowPolicy.CanEnterAgainToday(entries,reason is "HardLoss" or "TrailingProfit" or "SessionTwoUnprofitable" or "SessionTwoRotation" or "CarriedProtectionFloor",policy)) continue;
                target=SystemShadowPolicy.PositionTarget(nav.Value,input.Assignment.Strategy.MaximumHoldings);
                budget=SystemShadowPolicy.InitialBudget(target,policy); kind=entries==0?"Initial":"Reentry";
            }
            budget=System.Math.Min(cash,budget);
            int quantity=SystemShadowPolicy.WholeSharesForBuy(budget,five.Close,policy);
            if(quantity<1) continue;
            rows.Add(new(candidate.Symbol,holding is null?"Buy":"Add",quantity,five.Close,
                holding is null?"Qualified daily candidate":"DelphiReaffirmed",holding?.PositionId,candidate.CandidateId,
                five.EvidenceId,five.StartUtc.AddMinutes(5),kind,target));
            cash-=quantity*SystemShadowPolicy.AdjustedBuyPrice(five.Close,policy);
            if(holding is null) vacancies--;
        }
    }

    private static void Live(TradingRecommendationInput input, decimal? nav, bool riskAllowed,
        ImmutableDictionary<string,TradingSymbolState>.Builder states, ImmutableArray<TradingRecommendation>.Builder rows,
        ImmutableArray<string>.Builder warnings, ImmutableArray<TradingEntryDecision>.Builder entries)
    {
        var policy=input.Assignment.Strategy.ReadLivePolicy();
        var results=new Dictionary<string,DelphiLiveEvaluationResult>(StringComparer.OrdinalIgnoreCase);
        foreach(var group in input.LiveInputs.GroupBy(e=>e.Input.Stock.Symbol,StringComparer.OrdinalIgnoreCase))
        {
            string symbol=group.Key; var holding=input.CurrentHoldings.FirstOrDefault(h=>Same(h.Symbol,symbol));
            var memory=states.GetValueOrDefault(symbol) ?? new(null,null,null,null,null,null,null);
            var protection=holding is null?null:memory.PositionId==holding.PositionId && memory.LiveProtection is not null
                ? memory.LiveProtection with {AveragePurchasePrice=holding.CostBasis/holding.Quantity}
                : DelphiLiveProfitProtectionState.Open(holding.PositionId,holding.CostBasis/holding.Quantity);
            foreach(var saved in group.OrderBy(e=>e.Input.BarEndUtc))
            {
                var source=saved.Input;
                // A newly discovered conflict can invalidate an already-consumed observation.
                // Invalidate before the progress check, so a cached confirmation cannot survive it.
                if (saved.EvidenceConflict && source.SessionId == memory.LiveSessionId &&
                    source.BarEndUtc >= memory.LastLiveBarUtc && source.EvaluatedUtc <= input.EvaluatedUtc)
                    memory = memory with { LiveEvaluation = null, LastLiveResult = null };
                if(source.BarEndUtc <= input.Assignment.EffectiveUtc || holding is null && source.BarEndUtc < input.SessionOpenUtc ||
                    source.EvaluatedUtc>input.EvaluatedUtc || source.BarEndUtc<=memory.LastLiveBarUtc) continue;
                var previous=memory.LiveSessionId==source.SessionId && memory.LiveEpoch==saved.ContinuityEpoch
                    ? memory.LiveEvaluation ?? DelphiLiveEvaluationState.Initial(source.DailySetup is not null)
                    : DelphiLiveEvaluationState.Initial(source.DailySetup is not null);
                var evaluated=DelphiLiveEvaluationEngine.Evaluate(source with { Policy=policy, PreviousState=previous,
                    IsHeld=holding is not null,HasPendingBuy=false,HasPendingSell=false,
                    AveragePurchasePrice=holding is null?null:holding.CostBasis/holding.Quantity,ProfitProtection=protection });
                if(holding is not null && evaluated.SafetyInput.CompletedBarClose is decimal close &&
                    source.BarEndUtc>(LastEntry(input,holding.PositionId) ?? input.Assignment.EffectiveUtc))
                    protection=DelphiLiveSafetyPolicy.ApplyCompletedClose(protection!,source.BarEndUtc,close,input.EvaluatedUtc,policy).State;
                string? exit = memory.PositionId == holding?.PositionId ? memory.ExitReason : null;
                if (holding is not null && evaluated.ObservationIsValid &&
                    source.BarEndUtc > (LastEntry(input, holding.PositionId) ?? input.Assignment.EffectiveUtc))
                    exit ??= evaluated.Safety.PrimaryExitRule?.ToString();
                memory=memory with {PositionId=holding?.PositionId,LiveProtection=protection,LiveEvaluation=evaluated.NextState,
                    LiveSessionId=source.SessionId,LastLiveBarUtc=source.BarEndUtc,LiveEpoch=saved.ContinuityEpoch,LastLiveResult=evaluated,ExitReason=exit};
            }
            states[symbol]=memory;
            if(memory.LastLiveResult is { } latest && memory.LastLiveBarUtc>=input.SessionOpenUtc &&
                input.EvaluatedUtc-memory.LastLiveBarUtc.Value <= policy.BarInterval+policy.CollectionOffset)
                results[symbol]=latest;
            else if (memory.LastLiveResult?.ConfirmedLiveEligible == true)
                entries.Add(new(symbol, memory.LastLiveBarUtc,
                    memory.LastLiveBarUtc < input.SessionOpenUtc ? "SignalOutsideSession" : "SignalTooOld"));
        }
        foreach(var holding in input.CurrentHoldings)
        {
            var memory=states.GetValueOrDefault(holding.Symbol) ?? new(holding.PositionId,null,
                DelphiLiveProfitProtectionState.Open(holding.PositionId,holding.CostBasis/holding.Quantity),null,null,null,null);
            if(memory.PositionId!=holding.PositionId || memory.LiveProtection is null)
                memory=memory with {PositionId=holding.PositionId,LiveProtection=DelphiLiveProfitProtectionState.Open(holding.PositionId,holding.CostBasis/holding.Quantity),ExitReason=null};
            memory = memory with { LiveProtection = memory.LiveProtection! with { AveragePurchasePrice = holding.CostBasis / holding.Quantity } };
            results.TryGetValue(holding.Symbol,out var observed);
            if(states.GetValueOrDefault(holding.Symbol)?.LastLiveBarUtc <= (LastEntry(input,holding.PositionId)??input.Assignment.EffectiveUtc))observed=null;
            var quote=Quote(input,holding.Symbol);
            var safety=observed?.SafetyInput ?? new(true,true,holding.CostBasis/holding.Quantity,null,null,null,null,false,false,false,false,
                new(DelphiLiveSignalFamily.VolumeSupport,DelphiLiveFamilyState.NotMature,"Missing live observations"),
                new(DelphiLiveMomentumState.Neutral,DelphiLiveStrongTier.None,DelphiLiveNeutralDetail.None,0,0,0,0),false,memory.LiveProtection);
            decimal? usableBid = quote?.Bid;
            if (policy.UseProgressiveProfitProtection || memory.LiveProtection?.Progressive is not null)
            {
                var protection = Core.Runtime.EngineStrategyReassignment.Rebase(memory.LiveProtection!, policy, input.EvaluatedUtc);
                if (quote is not null)
                {
                    var observedBid = ProgressiveProfitProtection.ObserveBid(protection.Progressive!, quote.Bid, quote.RequestedUtc,
                        quote.ReceivedUtc, quote.ReceivedUtc, quote.SourceContract);
                    protection = protection with { Progressive = observedBid.State };
                    if (!observedBid.Usable) usableBid = null;
                }
                memory = memory with { LiveProtection = protection };
            }
            var decision=DelphiLiveSafetyPolicy.Evaluate(safety with {IsHeld=true,AveragePurchasePrice=holding.CostBasis/holding.Quantity,
                ProfitProtection=memory.LiveProtection,CurrentBid=usableBid,CurrentBidReceivedUtc=quote?.ReceivedUtc},policy);
            string? exit=memory.PositionId==holding.PositionId?memory.ExitReason:null;
            exit ??= decision.PrimaryExitRule?.ToString();
            if(quote?.Bid is not >0m) warnings.Add($"{holding.Symbol}: bid-based protection is waiting for an observed bid.");
            if(observed is null) warnings.Add($"{holding.Symbol}: live signal protection is waiting for current persisted observations.");
            states[holding.Symbol]=memory with {PositionId=holding.PositionId,ExitReason=exit};
            rows.Add(new(holding.Symbol,exit is null?"Hold":"Sell",exit is null?0:holding.Quantity,quote?.Bid,
                exit??"No protective exit established",holding.PositionId,null,observed?.CurrentStockObservationId,
                quote?.ReceivedUtc,exit is null?"Holding":"RiskExit"));
        }
        if(!riskAllowed || nav is null)
        {
            foreach (var pair in results.Where(p => p.Value.ConfirmedLiveEligible))
                entries.Add(new(pair.Key, states[pair.Key].LastLiveBarUtc, "NewRiskBlocked"));
            return;
        }
        decimal cash=input.Account!.AvailableCash!.Value; int occupied=input.CurrentHoldings.Length;
        var ranking=DelphiLiveRanking.Order(results.Values.Where(r=>r.RankCandidate is not null).Select(r=>r.RankCandidate!));
        foreach(var rank in ranking)
        {
            var result=results[rank.Symbol]; var quote=Quote(input,rank.Symbol);
            void Record(string reason) => entries.Add(new(rank.Symbol, states[rank.Symbol].LastLiveBarUtc, reason));
            if(Held(input,rank.Symbol)||!result.ConfirmedLiveEligible || quote is null)
            {
                if (result.ConfirmedLiveEligible) Record(Held(input,rank.Symbol) ? "ExistingPosition" : QuoteRejection(input,rank.Symbol));
                continue;
            }
            if(rank.DailySetup?.DailyStrategyVersionId!=input.Assignment.Strategy.Daily.StrategyVersionId || rank.DailySetup.DelphiRunId!=input.FrozenRunId)
            { Record("DailyCandidateIdentityMismatch"); continue; }
            DateTime entryStart=input.SessionOpenUtc+(policy.EntryWindowStart.ToTimeSpan()-TimeSpan.FromHours(9.5));
            DateTime cutoff=input.SessionOpenUtc+(policy.EntryCutoff.ToTimeSpan()-TimeSpan.FromHours(9.5));
            if(input.EvaluatedUtc<entryStart || quote.ReceivedUtc>=cutoff)
            { Record(input.EvaluatedUtc<entryStart ? "BeforeEntryWindow" : "EntryCutoffPassed"); continue; }
            var fills=input.ActualFills.Where(f=>Same(f.Symbol,rank.Symbol)).ToArray();
            if(fills.Count(f=>f.Side=="BUY"&&f.TradingDate==input.TradingDate)>=policy.MaximumSameSessionEntriesPerSymbol)
            { Record("SameSessionEntryLimit"); continue; }
            DateTime? lastExit=fills.Where(f=>f.Side=="SELL").Select(f=>(DateTime?)f.FillUtc).Max();
            if(lastExit.HasValue && (result.ConfirmationStartedBarEndUtc is null || result.ConfirmationStartedBarEndUtc<=lastExit))
            { Record("ReentryNeedsFreshConfirmation"); continue; }
            decimal? price=quote.Ask is >0m?quote.Ask:quote.Price;
            if(price is not >0m) { Record("QuotePriceUnavailable"); continue; }
            var sized=DelphiLivePortfolioPolicy.SizeWholeShareEntry(new(true,nav,[],"Reported account marks"),cash,price.Value,
                occupied,false,new(false,false,0m,0m,input.Account.HighestClosingValue!.Value),policy);
            if(!sized.IsAllowed) { Record(sized.ReasonCode); continue; }
            rows.Add(new(rank.Symbol,"Buy",sized.Quantity,price,"StrongConfirmationCompleted",null,rank.DailySetup.CandidateId,
                result.CurrentStockObservationId,quote.ReceivedUtc,"Initial",sized.TargetNotional));
            cash-=sized.RequiredCash; occupied++;
            Record("Recommended");
        }
    }

    private static string QuoteRejection(TradingRecommendationInput input, string symbol)
    {
        var quote = input.Quotes.LastOrDefault(q => Same(q.Symbol, symbol));
        if (quote is null) return "QuoteNotReturned";
        if (quote.SourceContract.StartsWith("Unavailable:", StringComparison.Ordinal)) return "QuoteUnavailable";
        if (quote.SourceContract != "TmxQuoteFieldsV1") return "QuoteContractMismatch";
        if (quote.RequestedUtc < input.EvaluatedUtc) return "QuoteRequestedBeforeEvaluation";
        if (quote.ReceivedUtc < quote.RequestedUtc) return "QuoteReceiptBeforeRequest";
        if (quote.ReceivedUtc > input.EvaluatedUtc + input.Assignment.Strategy.ReadLivePolicy().QuoteAttemptWindow) return "QuoteAttemptWindowExceeded";
        if (quote.ReceivedUtc >= input.SessionCloseUtc) return "QuoteAfterSessionClose";
        return "QuoteNotUsable";
    }

    private static TradingQuote? Quote(TradingRecommendationInput input,string symbol) => input.Quotes.LastOrDefault(q=>Same(q.Symbol,symbol)&&
        q.SourceContract=="TmxQuoteFieldsV1" && q.RequestedUtc>=input.EvaluatedUtc && q.ReceivedUtc>=q.RequestedUtc &&
        q.ReceivedUtc<=input.EvaluatedUtc+(input.Assignment.Strategy.Family == TradingStrategyFamily.DelphiLive
            ? input.Assignment.Strategy.ReadLivePolicy().QuoteAttemptWindow : ProgressiveProfitProtection.MaximumReceiptAge) && q.ReceivedUtc<input.SessionCloseUtc);
    private static bool Held(TradingRecommendationInput i,string symbol)=>i.CurrentHoldings.Any(h=>Same(h.Symbol,symbol));
    private static bool Same(string left,string right)=>string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
    private static int Find(ImmutableArray<TradingRecommendation>.Builder rows,string symbol)=>Enumerable.Range(0,rows.Count).First(i=>Same(rows[i].Symbol,symbol));
    private static DateTime? LastEntry(TradingRecommendationInput i,Guid position)=>i.ActualFills.Where(f=>f.PositionId==position&&f.Side=="BUY").Select(f=>(DateTime?)f.FillUtc).Max();
    private static TradingPriceBar? Latest(TradingRecommendationInput i,string symbol,int minutes)=>
        (i.Bars.GetValueOrDefault(symbol).IsDefault?[]:i.Bars[symbol]).Where(b=>b.Minutes==minutes && b.StartUtc>=i.SessionOpenUtc &&
            b.StartUtc.AddMinutes(minutes)<=b.ReceivedUtc && b.ReceivedUtc<=i.EvaluatedUtc &&
            i.EvaluatedUtc-b.StartUtc.AddMinutes(minutes)<=TimeSpan.FromMinutes(45)).OrderBy(b=>b.StartUtc).LastOrDefault();
    private static SystemShadowEntryDecision Entry(TradingRecommendationInput i,TradingCandidate candidate)
    {
        var latest=Latest(i,candidate.Symbol,5);
        var bars=i.Bars.GetValueOrDefault(candidate.Symbol);
        var prior=bars.IsDefault?null:bars.LastOrDefault(b=>b.Minutes==5&&latest is not null&&b.StartUtc==latest.StartUtc.AddMinutes(-5)&&b.ReceivedUtc<=i.EvaluatedUtc);
        if(latest is null||prior is null||latest.StartUtc.AddMinutes(5)<=i.Assignment.EffectiveUtc ||
            latest.StartUtc.AddMinutes(5)<=i.CandidateEffectiveUtc) return new(false,SystemShadowEntryReason.MissingEvidence);
        return SystemShadowPolicy.EvaluateEntry(new(candidate.PreviousClose,prior.Close,latest.Close,latest.StartUtc.AddMinutes(5),latest.ReceivedUtc));
    }
    private static ImmutableArray<string> AddAndReturn(this ImmutableArray<string>.Builder b,string message) { b.Add(message); return b.ToImmutable(); }
}
