#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveReplayBar(DateTime StartUtc, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
public sealed record DelphiLiveReplaySource(string Symbol, DateTime RetrievedUtc,
    IReadOnlyList<DelphiLiveReplayBar> FiveMinuteBars, IReadOnlyList<DelphiLiveReplayBar> MinuteBars);
public sealed record DelphiLiveReplayTrade(string Symbol, string Side, DateTime DecisionUtc, DateTime RecordedUtc, DateTime? FilledUtc,
    int Quantity, decimal? Price, decimal? RealizedProfit, string Reason, string Outcome);
public sealed record DelphiLiveReplayPosition(string Symbol, int Quantity, decimal AverageCost, decimal? Mark, decimal? UnrealizedProfit);
public sealed record DelphiLiveReplayRow(string Symbol, string DailySource, bool DailyEligible, int? Rank,
    decimal? Price, decimal? ChangeFromPreviousClose, string Momentum, string State, string Confidence, string Reason,
    IReadOnlyList<DelphiLiveFamilyJudgment> Families);
public sealed record DelphiLiveReplayFrame(DateTime BarEndUtc, DateTime SimulatedUtc, decimal Cash, decimal? AccountValue,
    decimal RealizedProfit, IReadOnlyList<DelphiLiveReplayRow> Rows, IReadOnlyList<DelphiLiveReplayPosition> Positions);
public sealed record DelphiLiveReplayReport(string Kind, int SchemaVersion, DateOnly SessionDate, DateTime GeneratedUtc,
    decimal StartingCapital, string Currency, Guid DailyRunId, Guid PolicyVersionId, string ExecutionConvention,
    string Assumptions, int ExpectedFiveMinuteBars, int AvailableFiveMinuteBars, int ExpectedMinuteBars, int AvailableMinuteBars,
    IReadOnlyList<DelphiLiveReplayFrame> Frames, IReadOnlyList<DelphiLiveReplayTrade> Trades);

/// <summary>
/// Pure, isolated research simulation. Synthetic timing exists only inside this calculation;
/// it is never a provider receipt, operational portfolio, or clean collection cohort.
/// </summary>
public static class DelphiLiveHistoricalReplay
{
    public const string Kind = "DelphiLiveHistoricalReplay";
    public const string ExecutionConvention = "EstimatedNextMinuteOpenV1";
    public const string Assumptions = "Historical simulation · estimated execution. Five-minute evidence is assumed available two minutes after completion. " +
        "Protection uses completed one-minute closes as bid proxies; fills use the exact next minute's open. " +
        "No spread, commission or slippage is included. Original publication latency and intraminute protection are unknown. " +
        "Missing exact bars remain missing. No forced closing sale; open holdings are marked at the final completed five-minute close. " +
        "These results are not live receipts, achievable returns, shakedown sessions or promotion evidence.";

    public static DelphiLiveReplayReport Run(DelphiLiveWatchlistPreview preview,
        IReadOnlyDictionary<string, DelphiLiveFrozenBaseline> baselines,
        IReadOnlyList<DelphiLiveReplaySource> sources, ReviewedTsxSessionCalendar calendar,
        decimal startingCapital, DateTime generatedUtc)
    {
        if (startingCapital <= 0) throw new ArgumentOutOfRangeException(nameof(startingCapital));
        DelphiLiveFiveMinuteBar.RequireUtc(generatedUtc, nameof(generatedUtc));
        var run = preview.Run ?? throw new ArgumentException("Replay needs a saved daily run.");
        var bounds = calendar.GetSessionBounds(run.RecommendationDate);
        if (run.Purpose != "OfficialPaper" || run.AuditState != "Valid" || run.CreatedUtc > bounds.OpenUtc ||
            run.MarketDataAsOf != calendar.GetImmediatelyPrecedingSession(run.RecommendationDate))
            throw new ArgumentException("Replay source must be the valid daily run available before the selected session opened.");
        var policy = DelphiLivePolicyDefinition.Version1;
        var sourceMap = sources.ToDictionary(s => s.Symbol, StringComparer.Ordinal);
        var symbols = preview.Picks.Select(p => p.Symbol).Append("XIU").Distinct().ToArray();
        foreach (var symbol in symbols)
        {
            if (!sourceMap.TryGetValue(symbol, out var source) || !baselines.ContainsKey(symbol))
                throw new ArgumentException("Every displayed symbol and XIU need explicit source and baseline records.");
            ValidateBars(source.FiveMinuteBars, 5, bounds);
            ValidateBars(source.MinuteBars, 1, bounds);
            if (baselines[symbol].Bars.Any(b => b.SessionDate >= run.RecommendationDate) ||
                baselines[symbol].Rulers.TenSession.SourceThroughSession >= run.RecommendationDate)
                throw new ArgumentException("Historical daily baselines cannot include the replay session or future sessions.");
        }
        var five = sourceMap.ToDictionary(s => s.Key, s => s.Value.FiveMinuteBars.ToDictionary(b => b.StartUtc), StringComparer.Ordinal);
        var minutes = sourceMap.ToDictionary(s => s.Key, s => s.Value.MinuteBars.ToDictionary(b => b.StartUtc), StringComparer.Ordinal);
        // The evaluator's operational-shaped values never leave this pure replay function.
        // Actual provider retrieval times remain in the input artifact, not overwritten here.
        var assumedBars = symbols.ToDictionary(symbol => symbol, symbol => sourceMap[symbol].FiveMinuteBars.OrderBy(b => b.StartUtc)
            .Select(b => new DelphiLiveFiveMinuteBar(Guid.NewGuid(), symbol, run.RecommendationDate, b.StartUtc, b.StartUtc.AddMinutes(5),
                b.Open, b.High, b.Low, b.Close, b.Volume, b.StartUtc.AddMinutes(7), "HistoricalReplayAssumption", 1,
                DelphiLiveEvidenceDisposition.OperationalOnTime)).ToArray(), StringComparer.Ordinal);
        var states = preview.Picks.ToDictionary(p => p.Symbol, p => DelphiLiveEvaluationState.Initial(p.Eligible), StringComparer.Ordinal);
        var results = new Dictionary<string, DelphiLiveEvaluationResult>(StringComparer.Ordinal);
        var holdings = new Dictionary<string, Position>(StringComparer.Ordinal);
        var pending = new List<Pending>();
        var trades = new List<DelphiLiveReplayTrade>();
        var frames = new List<DelphiLiveReplayFrame>();
        var entryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastExits = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var sessionId = Guid.NewGuid();
        decimal cash = startingCapital, realized = 0m;
        var guards = DelphiLivePortfolioPolicy.EvaluateGuards(startingCapital, startingCapital, startingCapital, false, false, policy);
        var entryStart = ReviewedTsxSessionCalendar.At(run.RecommendationDate, policy.EntryWindowStart);
        var cutoff = ReviewedTsxSessionCalendar.At(run.RecommendationDate, policy.EntryCutoff);
        DateTime lastCheckpoint = bounds.OpenUtc;

        for (var now = bounds.OpenUtc.AddMinutes(1); now <= bounds.CloseUtc.AddMinutes(2); now = now.AddMinutes(1))
        {
            foreach (var order in pending.Where(p => p.FillUtc <= now).OrderBy(p => p.Side == "Sell" ? 0 : 1).ThenBy(p => p.Rank).ThenBy(p => p.Symbol, StringComparer.Ordinal).ToArray())
            {
                bool regular = now < bounds.CloseUtc && (order.Side == "Sell" || now < cutoff);
                var bar = regular ? minutes[order.Symbol].GetValueOrDefault(now) : null;
                if (bar is null)
                {
                    if (order.Side == "Sell" && now < bounds.CloseUtc)
                    {
                        // Pending protective exits may retry; record each miss without inventing a quote.
                        trades.Add(new(order.Symbol, order.Side, order.DecisionUtc, now, null, 0, null, null, order.Reason, "No exact minute · exit remains pending"));
                        pending.Remove(order); pending.Add(order with { FillUtc = now.AddMinutes(1) }); continue;
                    }
                    trades.Add(new(order.Symbol, order.Side, order.DecisionUtc, now, null, 0, null, null, order.Reason,
                        order.Side == "Sell" ? "Exit pending after close" : "No exact executable minute · no fill"));
                    pending.Remove(order); continue;
                }
                if (order.Side == "Sell" && holdings.Remove(order.Symbol, out var owned))
                {
                    decimal profit = owned.Quantity * (bar.Open - owned.Cost);
                    cash += owned.Quantity * bar.Open; realized += profit; lastExits[order.Symbol] = now;
                    states[order.Symbol] = states[order.Symbol] with { Lifecycle = DelphiLiveLifecyclePolicy.AfterCompletedExit(states[order.Symbol].Lifecycle) };
                    trades.Add(new(order.Symbol, "Sell", order.DecisionUtc, now, now, owned.Quantity, bar.Open, profit, order.Reason, "Estimated fill"));
                }
                else if (order.Side == "Buy")
                {
                    var nav = Nav(lastCheckpoint);
                    if (nav.IsComplete)
                        guards = DelphiLivePortfolioPolicy.EvaluateGuards(nav.NetAssetValue!.Value, startingCapital, startingCapital,
                            guards.DailyBuyingPaused, guards.CapitalReviewRequired, policy);
                    var sized = DelphiLivePortfolioPolicy.SizeWholeShareEntry(nav, cash, bar.Open, holdings.Count,
                        holdings.ContainsKey(order.Symbol), guards, policy);
                    int shares = sized.IsAllowed ? System.Math.Min(sized.Quantity, (int)decimal.Floor(order.Budget / bar.Open)) : 0;
                    if (shares > 0)
                    {
                        var id = Guid.NewGuid(); cash -= shares * bar.Open;
                        holdings.Add(order.Symbol, new(id, shares, bar.Open, now, DelphiLiveProfitProtectionState.Open(id, bar.Open)));
                        entryCounts[order.Symbol] = entryCounts.GetValueOrDefault(order.Symbol) + 1;
                        trades.Add(new(order.Symbol, "Buy", order.DecisionUtc, now, now, shares, bar.Open, null, order.Reason, "Estimated fill"));
                    }
                    else trades.Add(new(order.Symbol, "Buy", order.DecisionUtc, now, null, 0, null, null,
                        shares == 0 && sized.IsAllowed ? "Insufficient budget for one share" : sized.ReasonCode, "No fill"));
                }
                pending.Remove(order);
            }

            bool checkpoint = now >= bounds.OpenUtc.AddMinutes(7) && (now - bounds.OpenUtc).TotalMinutes % 5 == 2;
            if (checkpoint)
            {
                lastCheckpoint = now.AddMinutes(-2);
                foreach (var pick in preview.Picks)
                {
                    var owned = holdings.GetValueOrDefault(pick.Symbol);
                    var baseline = baselines[pick.Symbol];
                    var result = DelphiLiveEvaluationEngine.Evaluate(new()
                    {
                        EvaluationId = Guid.NewGuid(), SessionId = sessionId, BarEndUtc = lastCheckpoint, EvaluatedUtc = now,
                        Stock = Series(pick.Symbol, lastCheckpoint), Xiu = Series("XIU", lastCheckpoint),
                        VolatilityRulers = baseline.Rulers, PreviousStockSessionClose = baseline.PreviousClose,
                        PreviousXiuSessionClose = baselines["XIU"].PreviousClose,
                        PreviousState = states[pick.Symbol], Policy = policy, DailySetup = pick.ToSetup(run),
                        IsHeld = owned is not null, HasPendingSell = pending.Any(p => p.Symbol == pick.Symbol && p.Side == "Sell"),
                        AveragePurchasePrice = owned?.Cost, ProfitProtection = owned?.Protection,
                        ExactPairPersistedOnTime = five[pick.Symbol].ContainsKey(lastCheckpoint.AddMinutes(-5)) && five["XIU"].ContainsKey(lastCheckpoint.AddMinutes(-5))
                    });
                    results[pick.Symbol] = result; states[pick.Symbol] = result.NextState;
                }
            }

            // One-minute closes are an explicitly estimated protection proxy, never actual bid observations.
            if (now < bounds.CloseUtc)
                foreach (var pair in holdings.ToArray())
                {
                    var owned = pair.Value;
                    if (now <= owned.OpenedUtc || pending.Any(p => p.Symbol == pair.Key && p.Side == "Sell")) continue;
                    if (!results.TryGetValue(pair.Key, out var result)) continue;
                    var quoteProxy = minutes[pair.Key].GetValueOrDefault(now.AddMinutes(-1));
                    var safety = DelphiLiveSafetyPolicy.Evaluate(result.SafetyInput with
                    {
                        IsHeld = true, AveragePurchasePrice = owned.Cost, ProfitProtection = owned.Protection,
                        CurrentBid = quoteProxy?.Close, CurrentBidReceivedUtc = quoteProxy is null ? null : now
                    }, policy);
                    if (safety.RequiresProtectiveSell)
                        pending.Add(new(pair.Key, "Sell", now, now.AddMinutes(1), 0, 0m, safety.PrimaryExitRule!.Value.ToString()));
                }

            if (!checkpoint) continue;
            foreach (var pair in holdings.ToArray())
                if (lastCheckpoint > pair.Value.OpenedUtc && five[pair.Key].TryGetValue(lastCheckpoint.AddMinutes(-5), out var bar))
                    holdings[pair.Key] = pair.Value with { Protection = DelphiLiveSafetyPolicy.ApplyCompletedClose(
                        pair.Value.Protection, lastCheckpoint, bar.Close, now, policy).State };

            var currentNav = Nav(lastCheckpoint);
            if (currentNav.IsComplete)
                guards = DelphiLivePortfolioPolicy.EvaluateGuards(currentNav.NetAssetValue!.Value, startingCapital, startingCapital,
                    guards.DailyBuyingPaused, guards.CapitalReviewRequired, policy);
            var ranked = preview.Picks.Where(p => p.Eligible).OrderBy(p => results[p.Symbol].RankCandidate, DelphiLiveRankingComparer.Instance)
                .ThenBy(p => p.Symbol, StringComparer.Ordinal).ToArray();
            var ranks = ranked.Select((p, i) => (p.Symbol, Rank: i + 1)).ToDictionary(p => p.Symbol, p => p.Rank);
            if (now >= entryStart && lastCheckpoint >= entryStart && now.AddMinutes(1) < cutoff && currentNav.IsComplete &&
                !guards.DailyBuyingPaused && !guards.CapitalReviewRequired)
                foreach (var pick in ranked)
                {
                    var result = results[pick.Symbol];
                    if (!result.ConfirmedLiveEligible || !result.Lifecycle.MayCreateBuyDecision || holdings.ContainsKey(pick.Symbol) ||
                        pending.Any(p => p.Symbol == pick.Symbol) || entryCounts.GetValueOrDefault(pick.Symbol) >= policy.MaximumSameSessionEntriesPerSymbol) continue;
                    if (lastExits.TryGetValue(pick.Symbol, out var exit) && result.ConfirmationStartedBarEndUtc <= exit) continue;
                    pending.Add(new(pick.Symbol, "Buy", now, now.AddMinutes(1), ranks[pick.Symbol],
                        System.Math.Min(cash, currentNav.NetAssetValue!.Value * policy.EntryTargetNavFraction), "Strong confirmation completed"));
                }

            var rows = preview.Picks.Select(p =>
            {
                var result = results[p.Symbol];
                var price = five[p.Symbol].GetValueOrDefault(lastCheckpoint.AddMinutes(-5))?.Close;
                string state = !p.Eligible ? "Daily gate blocked" : pending.Any(o => o.Symbol == p.Symbol && o.Side == "Sell") ? "Exit pending" :
                    holdings.ContainsKey(p.Symbol) ? "Held" : pending.Any(o => o.Symbol == p.Symbol && o.Side == "Buy") ? "Buy pending" : result.Lifecycle.Snapshot.State.ToString();
                return new DelphiLiveReplayRow(p.Symbol, p.Source, p.Eligible, ranks.GetValueOrDefault(p.Symbol) is int rank && rank > 0 ? rank : null,
                    price, price.HasValue && p.PreviousClose > 0 ? price / p.PreviousClose - 1m : null,
                    result.NextState.Momentum.State.ToString(), state, result.ObservationIsValid ? "Historical bar present" : "Missing exact bar",
                    !p.Eligible ? p.EligibilityReason : result.Lifecycle.Snapshot.ReasonCode, result.NextState.FamilyJudgments);
            }).OrderBy(r => r.Rank ?? int.MaxValue).ThenBy(r => r.Symbol, StringComparer.Ordinal).ToArray();
            var positions = holdings.Select(p =>
            {
                decimal? mark = five[p.Key].GetValueOrDefault(lastCheckpoint.AddMinutes(-5))?.Close;
                return new DelphiLiveReplayPosition(p.Key, p.Value.Quantity, p.Value.Cost, mark, mark.HasValue ? (mark - p.Value.Cost) * p.Value.Quantity : null);
            }).OrderBy(p => p.Symbol, StringComparer.Ordinal).ToArray();
            frames.Add(new(lastCheckpoint, now, cash, currentNav.NetAssetValue, realized, rows, positions));
        }
        foreach (var order in pending)
            trades.Add(new(order.Symbol, order.Side, order.DecisionUtc, bounds.CloseUtc.AddMinutes(2), null, 0, null, null, order.Reason, "Exit pending after close"));
        return new(Kind, 1, run.RecommendationDate, generatedUtc, startingCapital, "CAD", run.RunId, policy.PolicyVersionId,
            ExecutionConvention, Assumptions, symbols.Length * 78, symbols.Sum(s => five[s].Count),
            symbols.Length * 390, symbols.Sum(s => minutes[s].Count), frames, trades);

        DelphiLiveFiveMinuteSeries Series(string symbol, DateTime end) => new(symbol, run.RecommendationDate, bounds.OpenUtc,
            bounds.OpenUtc, assumedBars[symbol].Where(b => b.EndUtc <= end));
        DelphiLiveNavResult Nav(DateTime end) => DelphiLivePortfolioPolicy.CalculateExactNav(cash,
            holdings.Select(p => (p.Value.Id, p.Key, p.Value.Quantity)).ToArray(),
            holdings.Where(p => five[p.Key].ContainsKey(end.AddMinutes(-5))).Select(p => new DelphiLivePositionMark(p.Value.Id, p.Key,
                p.Value.Quantity, five[p.Key][end.AddMinutes(-5)].Close, end)).ToArray(), end);
    }

    private static void ValidateBars(IReadOnlyList<DelphiLiveReplayBar> bars, int interval, DelphiLiveSessionBounds bounds)
    {
        if (bars.Select(b => b.StartUtc).Distinct().Count() != bars.Count) throw new ArgumentException("Duplicate historical bars.");
        foreach (var b in bars)
        {
            DelphiLiveFiveMinuteBar.RequireUtc(b.StartUtc, nameof(b.StartUtc));
            if (b.StartUtc < bounds.OpenUtc || b.StartUtc.AddMinutes(interval) > bounds.CloseUtc || b.StartUtc.Second != 0 ||
                b.StartUtc.Ticks % TimeSpan.TicksPerMinute != 0 || b.StartUtc.Minute % interval != 0 ||
                b.Open <= 0 || b.High < System.Math.Max(b.Open, b.Close) || b.Low <= 0 || b.Low > System.Math.Min(b.Open, b.Close) || b.Volume < 0)
                throw new ArgumentException("Invalid or out-of-session historical bar.");
        }
    }
    private sealed record Position(Guid Id, int Quantity, decimal Cost, DateTime OpenedUtc, DelphiLiveProfitProtectionState Protection);
    private sealed record Pending(string Symbol, string Side, DateTime DecisionUtc, DateTime FillUtc, int Rank, decimal Budget, string Reason);
}
