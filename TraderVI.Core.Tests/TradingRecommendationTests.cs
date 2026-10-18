#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradingRecommendationTests
{
    [Fact]
    public void ProgressiveDailyAdviceUsesTheActualBidAndPreservesBlendedHoldingProtection()
    {
        var i = HeldInput(105m);
        var strategy = i.Assignment.Strategy with { ShadowPolicy = i.Assignment.Strategy.ReadShadowPolicy() with { UseProgressiveProfitProtection = true } };
        i = i with { Assignment = i.Assignment with { Strategy = strategy }, EntriesPaused = true,
            Quotes = [new("ABC", Now, Now.AddSeconds(1), 105m, 104.475m, 104.49m, "TmxQuoteFieldsV1")] };
        var result = TradingRecommendationEngine.Evaluate(i);
        var row = Assert.Single(result.Recommendations);
        Assert.Equal("Sell", row.Action);
        Assert.Equal(104.475m, row.ReferencePrice);
        var progressive = result.Memory.Symbols["ABC"].ShadowProtection!.Progressive!;
        Assert.Equal(0.005m, progressive.TightestGap);
        Assert.True(progressive.ExitTriggered);
        var added = i.CurrentHoldings[0] with { Quantity = 15, CostBasis = 1515m };
        var later = TradingRecommendationEngine.Evaluate(i with { PreviousMemory = result.Memory, CurrentHoldings = [added], Quotes = [] });
        Assert.Equal(104.475m, later.Memory.Symbols["ABC"].ShadowProtection!.Progressive!.StopPrice);
        Assert.Equal("Sell", Assert.Single(later.Recommendations).Action);
        Assert.Equal(15, Assert.Single(later.Recommendations).Quantity);
    }
    [Fact]
    public void DailyRecommendationsUseRealCashAndLeaveAllFinancialFactsUntouched()
    {
        var input = Input(); string before = JsonSerializer.Serialize(input);
        var result = TradingRecommendationEngine.Evaluate(input);
        var buy = Assert.Single(result.Recommendations);
        Assert.Equal("Buy", buy.Action); Assert.Equal(7, buy.Quantity);
        Assert.Equal(before, JsonSerializer.Serialize(input));
        Assert.Empty(input.ActualFills); Assert.Empty(input.CurrentHoldings);
        var richer = TradingRecommendationEngine.Evaluate(input with { Account = input.Account! with { AvailableCash = 6000m } });
        Assert.Equal(14, Assert.Single(richer.Recommendations).Quantity);
    }
    [Theory]
    [InlineData("pause")]
    [InlineData("missing")]
    [InlineData("account")]
    [InlineData("daily")]
    [InlineData("run")]
    [InlineData("code")]
    [InlineData("orders")]
    [InlineData("ledger")]
    [InlineData("currency")]
    [InlineData("capital")]
    public void NewRiskWaitsForTheActualApprovedAccountAndInputs(string missing)
    {
        var i = Input(); i = missing switch
        {
            "pause" => i with { EntriesPaused = true },
            "missing" => i with { Account = null },
            "account" => i with { Account = i.Account! with { AccountLabel = "RRSP" } },
            "daily" => i with { CurrentDailyStrategyId = Guid.NewGuid() },
            "run" => i with { FrozenRunId = null, Candidates = [] },
            "code" => i with { ImplementationIdentity = "unapproved-build" },
            "orders" => i with { Account = i.Account! with { PendingOrderCount = 1 } },
            "ledger" => i with { CurrentLedgerFingerprint = new string('B', 64) },
            "currency" => i with { Account = i.Account! with { Currency = "USD" } },
            _ => i with { PreviousMemory = TradingRecommendationMemory.Empty with { CapitalReviewRequired = true } }
        };
        Assert.DoesNotContain(TradingRecommendationEngine.Evaluate(i).Recommendations, r => r.Action is "Buy" or "Add");
    }
    [Fact]
    public void EffectiveBoundaryCannotUseEarlierEntryEvidence()
    {
        var i = Input(); var boundary = i.Bars["ABC"].Last().StartUtc.AddMinutes(5);
        Assert.Empty(TradingRecommendationEngine.Evaluate(i with { Assignment = i.Assignment with { EffectiveUtc = boundary } }).Recommendations);
        Assert.Throws<ArgumentException>(() => TradingRecommendationEngine.Evaluate(i with { EvaluatedUtc = i.Assignment.EffectiveUtc.AddTicks(-1) }));
    }
    [Fact]
    public void PausedOrStaleAccountStillGetsProtectiveAdviceAndExitStaysUntilReportedSale()
    {
        var i = HeldInput(94m) with { EntriesPaused = true, Account = null };
        var result = TradingRecommendationEngine.Evaluate(i);
        var exit = Assert.Single(result.Recommendations); Assert.Equal("Sell", exit.Action); Assert.Equal(10, exit.Quantity);
        var recovered = i with { PreviousMemory = result.Memory, Bars = Prices(105m) };
        Assert.Equal("Sell", Assert.Single(TradingRecommendationEngine.Evaluate(recovered).Recommendations).Action);
        var sold = recovered with { CurrentHoldings = [], Candidates = [] };
        var after = TradingRecommendationEngine.Evaluate(sold);
        Assert.Empty(after.Recommendations); Assert.Null(after.Memory.Symbols["ABC"].PositionId);
    }
    [Fact]
    public void ChangedLedgerDoesNotLatchAnInventedCapitalLossUsingOldCash()
    {
        var i = Input() with { CurrentLedgerFingerprint = new string('B', 64) };
        i = i with { Account = i.Account! with { AvailableCash = 1m } };
        var result = TradingRecommendationEngine.Evaluate(i);
        Assert.False(result.Memory.CapitalReviewRequired); Assert.False(result.Memory.DailyLossHold);
    }
    [Fact]
    public void FiveMinuteCloseFromBeforeReportedEntryCannotTriggerAProtectiveExit()
    {
        var i = HeldInput(94m); var holding = i.CurrentHoldings[0];
        i = i with { ActualFills = [new(Guid.NewGuid(), holding.PositionId, holding.Symbol, "BUY", Now.AddSeconds(-1), i.TradingDate, 10, 100m, null, "Initial", 1000m)] };
        Assert.Equal("Hold", Assert.Single(TradingRecommendationEngine.Evaluate(i).Recommendations.Where(r => r.PositionId == holding.PositionId)).Action);
    }
    [Fact]
    public void DuplicateEvaluationDoesNotSpendCashOrCreateActualFills()
    {
        var i = Input(); var first = TradingRecommendationEngine.Evaluate(i);
        var repeat = TradingRecommendationEngine.Evaluate(i with { PreviousMemory = first.Memory });
        Assert.Equal(first.Recommendations.ToArray(), repeat.Recommendations.ToArray()); Assert.Empty(i.ActualFills);
    }
    [Fact]
    public void SeveralRecommendationsShareOneActualCashAndCapacityBudget()
    {
        var i = Input(); i = i with
        {
            Candidates = [i.Candidates[0], i.Candidates[0] with { Symbol = "DEF", Rank = 2 }, i.Candidates[0] with { Symbol = "GHI", Rank = 3 }, i.Candidates[0] with { Symbol = "JKL", Rank = 4 }],
            Bars = i.Bars.Add("DEF", i.Bars["ABC"]).Add("GHI", i.Bars["ABC"]).Add("JKL", i.Bars["ABC"])
        };
        var result = TradingRecommendationEngine.Evaluate(i);
        Assert.Equal(3, result.Recommendations.Length);
        Assert.True(result.Recommendations.Sum(r => r.Quantity * r.ReferencePrice) <= i.Account!.AvailableCash);
    }
    [Fact]
    public void AddOnNeedsActualInitialTargetAndCannotBorrowOneFromPaperState()
    {
        var i = HeldInput(105m); i = i with { CurrentHoldings = [i.CurrentHoldings[0] with { EntryDate = i.TradingDate.AddDays(-1) }] };
        var without = TradingRecommendationEngine.Evaluate(i); Assert.DoesNotContain(without.Recommendations, r => r.Action == "Add");
        var holding = i.CurrentHoldings[0];
        i = i with { ActualFills = [new(Guid.NewGuid(), holding.PositionId, "ABC", "BUY", Open.AddDays(-1), i.TradingDate.AddDays(-1), 10, 100m, null, "Initial", 4000m)] };
        Assert.Contains(TradingRecommendationEngine.Evaluate(i).Recommendations, r => r.Action == "Add" && r.Quantity > 0);
    }
    [Fact]
    public void RestartRoundTripPreservesProtectiveStateAndImmutableStrategyIdentity()
    {
        var i = HeldInput(106m); i = i with { Bars = i.Bars.SetItem("ABC", i.Bars["ABC"].Add(new(Guid.NewGuid(), Open.AddMinutes(15), Now, 15, 100m, 107m, 100m, 106m))) };
        var result = TradingRecommendationEngine.Evaluate(i);
        var reloaded = JsonSerializer.Deserialize<TradingRecommendationInput>(JsonSerializer.Serialize(i with { PreviousMemory = result.Memory }))!;
        Assert.Equal(i.Assignment.Strategy.Fingerprint, reloaded.Assignment.Strategy.Fingerprint);
        Assert.Equal(result.Memory.Symbols["ABC"].ShadowProtection, TradingRecommendationEngine.Evaluate(reloaded).Memory.Symbols["ABC"].ShadowProtection);
    }
    [Fact]
    public void ExplicitCapitalReviewOverridesOlderBatchWithoutLosingDailyHoldOrSymbolProtection()
    {
        var input = HeldInput(94m); var result = TradingRecommendationEngine.Evaluate(input);
        result = result with { Memory = result.Memory with { CapitalReviewRequired = true, DailyLossHold = true, HighestClosingValue = 5000m } };
        var batch = new TradingRecommendationBatch(Guid.NewGuid(), "TFSA", input.Assignment.PromotionId, input.Account!.SnapshotId, null, Now, "fixture", result);
        var report = input.Account with { ReportedUtc = Now.AddSeconds(1), DailyLossHold = true, CapitalReviewRequired = false, HighestClosingValue = 3000m, CapitalReviewReason = "Reviewed actual drawdown" };
        var memory = TradingRecommendationMemory.Reconcile(batch, report);
        Assert.False(memory.CapitalReviewRequired); Assert.True(memory.DailyLossHold); Assert.Equal(3000m, memory.HighestClosingValue);
        Assert.Equal(result.Memory.Symbols, memory.Symbols);
        Assert.False(TradingRecommendationMemory.Reconcile(batch, report with { CapitalReviewReason = null, ReportedUtc = Now.AddSeconds(2) }).CapitalReviewRequired);
        Assert.True(TradingRecommendationMemory.Reconcile(batch with { EvaluatedUtc = Now.AddSeconds(3) }, report).CapitalReviewRequired);
    }
    [Fact]
    public void LiveReevaluatesItsOwnConfirmationAndSizesAgainstRealAccount()
    {
        var i = LiveInput(); var result = TradingRecommendationEngine.Evaluate(i);
        var buy = Assert.Single(result.Recommendations); Assert.Equal("Buy", buy.Action); Assert.True(buy.Quantity > 0);
        Assert.True(buy.Quantity * buy.ReferencePrice <= i.Account!.AvailableCash);
        Assert.Empty(i.CurrentHoldings); Assert.Empty(i.ActualFills);
    }
    [Fact]
    public void LiveCannotCopyConfirmedPaperStateIntoAnUnconfirmedRealAccount()
    {
        var i = LiveInput(); var last = i.LiveInputs.Last();
        var paperResult = DelphiLiveEvaluationEngine.Evaluate(last.Input);
        Assert.True(paperResult.ConfirmedLiveEligible);
        i = i with { LiveInputs = [last with { Input = last.Input with { PreviousState = paperResult.NextState, IsHeld = true, AveragePurchasePrice = 1m } }] };
        Assert.DoesNotContain(TradingRecommendationEngine.Evaluate(i).Recommendations, r => r.Action == "Buy");
    }
    [Fact]
    public void LiveRepeatedQuoteUsesOwnSavedConfirmationWithoutReplayingPaperLifecycle()
    {
        var i = LiveInput(); var first = TradingRecommendationEngine.Evaluate(i);
        var repeat = TradingRecommendationEngine.Evaluate(i with { PreviousMemory = first.Memory });
        Assert.Equal(first.Recommendations.ToArray(), repeat.Recommendations.ToArray());
        var noQuote = TradingRecommendationEngine.Evaluate(i with { Quotes = [] });
        Assert.Empty(noQuote.Recommendations);
    }
    [Fact]
    public void LiveProtectsActualCostFromBidEvenWithoutPaperCandidateOrAccountReport()
    {
        var i = LiveInput(); var holding = new ReportedTradingHolding(Guid.NewGuid(), "HELD", 4, 400m, i.TradingDate);
        i = i with { Account = null, EntriesPaused = true, CurrentHoldings = [holding], LiveInputs = [], Candidates = [], Quotes = [new("HELD", Now, Now.AddSeconds(1), 50m, 94m, 95m, "TmxQuoteFieldsV1")] };
        var exit = Assert.Single(TradingRecommendationEngine.Evaluate(i).Recommendations);
        Assert.Equal("Sell", exit.Action); Assert.Equal(4, exit.Quantity); Assert.Equal(94m, exit.ReferencePrice);
        Assert.Equal("Hold", Assert.Single(TradingRecommendationEngine.Evaluate(i with { Quotes = [] }).Recommendations).Action);
    }
    internal static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
    internal static readonly DateTime Now = Open.AddMinutes(32);
    internal static TradingRecommendationInput Input()
    {
        var s = TradingStrategyResolutionTests.Strategy(); var a = TradingStrategyResolutionTests.Assignment(s, Open.AddMinutes(1)); var run = Guid.NewGuid();
        return new(Guid.NewGuid(), a, Now, new(2026, 9, 8), Open, Open.AddHours(6.5), s.Daily.StrategyVersionId, run, s.ImplementationIdentity,
            TradingPromotionTests.Account(), TradingPromotionTests.Hash, false, [], [], [new(Guid.NewGuid(), run, "ABC", 1, 100m)], Prices(102m), [],
            ImmutableDictionary<Guid, int>.Empty, TradingRecommendationMemory.Empty, []);
    }
    private static ImmutableDictionary<string, ImmutableArray<TradingPriceBar>> Prices(decimal close) =>
        ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("ABC", [
            new(Guid.NewGuid(),Open.AddMinutes(20),Open.AddMinutes(26),5,100m,close,close-1,close-1),
            new(Guid.NewGuid(),Open.AddMinutes(25),Open.AddMinutes(31),5,100m,close,close,close)]);
    private static TradingRecommendationInput HeldInput(decimal close)
    {
        var i = Input(); var h = new ReportedTradingHolding(Guid.NewGuid(), "ABC", 10, 1000m, i.TradingDate);
        return i with { CurrentHoldings = [h], Bars = Prices(close), HoldingSessionOrdinals = ImmutableDictionary<Guid, int>.Empty.Add(h.PositionId, 1) };
    }
    internal static TradingRecommendationInput LiveInput()
    {
        var i = Input(); var policy = DelphiLivePolicyDefinition.Version1;
        var strategy = i.Assignment.Strategy with { Family = TradingStrategyFamily.DelphiLive, ShadowPolicy = null, LivePolicy = policy, PolicyVersionId = policy.PolicyVersionId, Selection = CompleteTradingStrategy.LiveSelection, MaximumHoldings = policy.MaximumHoldings };
        var session = Guid.NewGuid();
        DelphiLiveTrueRangeRulerMeasurement Ruler(int n) => new(n, new(2026, 9, 4), DelphiLiveScalarMeasurement.Available(.04m));
        DelphiLiveFiveMinuteSeries Series(string symbol, int n, bool rising) => new(symbol, i.TradingDate, Open, Open,
            Enumerable.Range(0, n).Select(k => new DelphiLiveFiveMinuteBar(Guid.NewGuid(), symbol, i.TradingDate, Open.AddMinutes(k * 5), Open.AddMinutes((k + 1) * 5),
                100m + (rising ? k : 0), 100.01m + (rising ? k + 1 : 0), 99.99m + (rising ? k : 0), 100m + (rising ? k + 1 : 0), 100, Open.AddMinutes((k + 1) * 5 + 2), "TMX", 1, DelphiLiveEvidenceDisposition.OperationalOnTime)));
        DelphiLiveEvaluationInput Source(int n) => new()
        {
            EvaluationId = Guid.NewGuid(),
            SessionId = session,
            BarEndUtc = Open.AddMinutes(n * 5),
            EvaluatedUtc = Open.AddMinutes(n * 5 + 2),
            Stock = Series("ABC", n, true),
            Xiu = Series("XIU", n, false),
            VolatilityRulers = new(Ruler(5), Ruler(10), Ruler(14), Ruler(20)),
            PreviousState = DelphiLiveEvaluationState.Initial(true),
            Policy = policy,
            PreviousStockSessionClose = 100m,
            PreviousXiuSessionClose = 100m,
            MedianFullDayVolume20 = 10000m,
            ExactPairPersistedOnTime = true,
            DailySetup = new(i.FrozenRunId!.Value, i.Candidates[0].CandidateId, strategy.Daily.StrategyVersionId, .5m, [new(DelphiLiveSourceLens.Continuation, true, true, 1, .5m, "DailyReasons", "DailyGates")])
        };
        var first = Source(4); var result = DelphiLiveEvaluationEngine.Evaluate(first);
        var second = Source(5) with { PreviousState = result.NextState }; var confirmed = DelphiLiveEvaluationEngine.Evaluate(second);
        return i with { Assignment = i.Assignment with { Strategy = strategy }, LiveInputs = [new(first, 0), new(second, 0)], Quotes = [new("ABC", Now, Now.AddSeconds(1), 105m, 104.99m, 105.01m, "TmxQuoteFieldsV1")] };
    }
}
