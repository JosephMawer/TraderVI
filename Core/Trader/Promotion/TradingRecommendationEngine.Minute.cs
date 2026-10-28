#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

public static partial class TradingRecommendationEngine
{
    private static void MinuteEntries(TradingRecommendationInput input, decimal? nav, bool riskAllowed,
        ImmutableDictionary<string, TradingSymbolState>.Builder states, ImmutableArray<TradingRecommendation>.Builder rows,
        ImmutableArray<string>.Builder warnings, ImmutableArray<TradingEntryDecision>.Builder entries,
        ImmutableArray<TradingMinuteDecisionTrace>.Builder traces)
    {
        var strategy = input.Assignment.Strategy.MinuteEntry!; var policy = strategy.Rules;
        var observations = input.MinuteInputs ?? [];
        if (observations.Select(o => o.Stock.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != observations.Length)
            throw new ArgumentException("A symbol requires one coherent local minute evidence snapshot.");
        // A missing or stale read cannot resurrect a cached eligible result or reset a rejected-price episode.
        foreach (var symbol in states.Keys.ToArray())
            if (states[symbol].MinuteSignal is { } prior)
                states[symbol] = states[symbol] with { MinuteSignal = prior with { Confirmed = false, ExpiresUtc = null } };
        if (observations.IsEmpty) warnings.Add("Minute confirmation is waiting for verified local collector evidence.");
        DateTime end = input.EvaluatedUtc.AddTicks(-(input.EvaluatedUtc - input.SessionOpenUtc).Ticks % TimeSpan.TicksPerMinute);
        if (end < input.SessionOpenUtc.AddMinutes(2) || end > input.SessionCloseUtc) return;
        var qualified = new Dictionary<string, (TradingMinuteObservation Observation, DelphiLiveMinutePair Pair)>(StringComparer.OrdinalIgnoreCase);
        foreach (var observation in observations)
        {
            string symbol = observation.Stock.Symbol;
            if (observation.Stock.SessionOpenUtc != input.SessionOpenUtc || observation.Stock.SessionCloseUtc != input.SessionCloseUtc)
            { entries.Add(new(symbol, end, "MinuteSessionMismatch")); continue; }
            DelphiLiveMinutePair pair;
            try { pair = DelphiLiveMinuteEvaluator.EvaluatePair(strategy, observation.Stock, observation.Xiu, observation.Rulers,
                observation.PreviousStockClose, observation.PreviousXiuClose, end, input.EvaluatedUtc); }
            catch (ArgumentException) { entries.Add(new(symbol, end, "MinuteContractMismatch")); continue; }
            traces.Add(new(pair.Previous.Judgment, pair.Latest.Judgment, pair.Confirmation,
                DelphiLiveCollectorBatches.Hash(System.Text.Json.JsonSerializer.Serialize(observation)),
                pair.Latest.Families, pair.Latest.Measurements.VwapThroughUtc,
                pair.Latest.Measurements.Structure.SessionVwap.Availability));
            var memory = states.GetValueOrDefault(symbol) ?? new(null, null, null, null, null, null, null);
            var old = memory.MinuteSignal;
            DateTime? episode = old is not null && old.PolicyIdentity == strategy.Identity && old.SessionOpenUtc == input.SessionOpenUtc
                ? old.EpisodeStartUtc : null;
            bool confirmed = pair.Confirmation.Status == DelphiLiveRecoveredConfirmationStatus.Confirmed;
            // Only an actual valid, mature weak judgment ends the price episode. Missing/stale evidence
            // cannot erase the fixed ask and obtain a higher reference on recovery.
            if (pair.Latest.Judgment.Valid && pair.Latest.Judgment.Mature &&
                (pair.Latest.Judgment.SupportiveFamilies < 3 || pair.Latest.Judgment.SafetyVeto)) episode = null;
            if (confirmed) episode ??= pair.Previous.Judgment.EndUtc;
            states[symbol] = memory with { MinuteSignal = new(strategy.Identity, input.SessionOpenUtc, end,
                episode, pair.Confirmation.ExpiresUtc, confirmed) };
            if (!confirmed) { entries.Add(new(symbol, end, "Minute" + pair.Confirmation.Status)); continue; }
            qualified.Add(symbol, (observation, pair));
        }
        if (!riskAllowed || nav is null)
        { foreach (var symbol in qualified.Keys) entries.Add(new(symbol, end, "NewRiskBlocked")); return; }
        decimal cash = input.Account!.AvailableCash!.Value; int occupied = input.CurrentHoldings.Length;
        var ranks = DelphiLiveRanking.Order(qualified.Select(p => new DelphiLiveRankCandidate(p.Key,
            p.Value.Pair.Latest.Momentum, p.Value.Pair.Latest.Measurements.Persistence.Score, p.Value.Observation.DailySetup,
            p.Value.Observation.DailySetup is null)));
        foreach (var rank in ranks)
        {
            var item = qualified[rank.Symbol]; var pair = item.Pair;
            void Record(string reason) => entries.Add(new(rank.Symbol, end, reason));
            if (Held(input, rank.Symbol)) { Record("ExistingPosition"); continue; }
            if (rank.DailySetup is null || rank.DailySetup.DailyStrategyVersionId != input.Assignment.Strategy.Daily.StrategyVersionId ||
                rank.DailySetup.DelphiRunId != input.FrozenRunId)
            { Record("DailyCandidateIdentityMismatch"); continue; }
            // Recovery may cross a process restart, never a changed assignment/candidate source or a prior sale.
            if (pair.Previous.Judgment.EndUtc <= input.Assignment.EffectiveUtc || pair.Previous.Judgment.EndUtc <= input.CandidateEffectiveUtc)
            { Record("ConfirmationBeforeAssignmentOrSource"); continue; }
            var quote = Quote(input, rank.Symbol);
            if (quote is null) { Record(QuoteRejection(input, rank.Symbol)); continue; }
            if (quote.ReceivedUtc > pair.Confirmation.ExpiresUtc || quote.RequestedUtc > pair.Confirmation.ExpiresUtc)
            { Record("MinuteExpiredDuringQuote"); continue; }
            var entryStart = input.SessionOpenUtc + (policy.EntryWindowStart.ToTimeSpan() - TimeSpan.FromHours(9.5));
            var cutoff = input.SessionOpenUtc + (policy.EntryCutoff.ToTimeSpan() - TimeSpan.FromHours(9.5));
            if (input.EvaluatedUtc < entryStart || quote.ReceivedUtc >= cutoff)
            { Record(input.EvaluatedUtc < entryStart ? "BeforeEntryWindow" : "EntryCutoffPassed"); continue; }
            var fills = input.ActualFills.Where(f => Same(f.Symbol, rank.Symbol)).ToArray();
            if (fills.Count(f => f.Side == "BUY" && f.TradingDate == input.TradingDate) >= policy.MaximumSameSessionEntriesPerSymbol)
            { Record("SameSessionEntryLimit"); continue; }
            var exit = fills.Where(f => f.Side == "SELL").Select(f => (DateTime?)f.FillUtc).Max();
            if (exit.HasValue && pair.Previous.Judgment.EndUtc <= exit)
            { Record("ReentryNeedsFreshConfirmation"); continue; }
            decimal? price = quote.Ask is > 0m ? quote.Ask : quote.Price;
            if (price is not > 0m) { Record("QuotePriceUnavailable"); continue; }
            var sized = DelphiLivePortfolioPolicy.SizeWholeShareEntry(new(true, nav, [], "Reported account marks"), cash, price.Value,
                occupied, false, new(false, false, 0m, 0m, input.Account.HighestClosingValue!.Value), policy);
            if (!sized.IsAllowed) { Record(sized.ReasonCode); continue; }
            var evidence = item.Observation.Stock.Observations.Single(o => o.EndUtc == end).ObservationId;
            rows.Add(new(rank.Symbol, "Buy", sized.Quantity, price, "RecoveredMinuteConfirmationCompleted", null,
                rank.DailySetup.CandidateId, evidence, quote.ReceivedUtc, "Initial", sized.TargetNotional)
                { SignalExpiresUtc = pair.Confirmation.ExpiresUtc });
            cash -= sized.RequiredCash; occupied++; Record("Recommended");
        }
    }
}
