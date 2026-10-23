#nullable enable
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradingDecisionDiagnosticsTests
{
    [Fact]
    public void MarketDiagnosticsDistinguishMissingStaleAndFreshWithoutRequiringNewRowsEveryPoll()
    {
        var input = TradingRecommendationTests.LiveInput();
        var result = TradingRecommendationEngine.Evaluate(input);
        var fresh = TradingMarketInputTrace.Capture(input, result)!;
        Assert.Equal(2, fresh.SuppliedObservations); Assert.Equal(1, fresh.EvaluatedSymbols);
        Assert.Equal(1, fresh.FreshSymbols); Assert.Equal(input.LiveInputs[^1].Input.BarEndUtc, fresh.LatestBarUtc);
        var next = input with { LiveInputs = [], PreviousMemory = result.Memory };
        var retained = TradingMarketInputTrace.Capture(next, TradingRecommendationEngine.Evaluate(next))!;
        Assert.Equal(0, retained.SuppliedObservations); Assert.Equal(1, retained.FreshSymbols);
        var staleInput = next with { EvaluatedUtc = input.EvaluatedUtc.AddTicks(1) };
        var stale = TradingMarketInputTrace.Capture(staleInput, TradingRecommendationEngine.Evaluate(staleInput))!;
        Assert.Equal(1, stale.EvaluatedSymbols); Assert.Equal(0, stale.FreshSymbols);
        var missingInput = input with { LiveInputs = [] };
        var missing = TradingMarketInputTrace.Capture(missingInput, TradingRecommendationEngine.Evaluate(missingInput))!;
        Assert.Equal(0, missing.EvaluatedSymbols); Assert.Null(missing.LatestBarUtc);
        var trace = new BrokerDecisionTrace(input.EvaluatedUtc, input.EvaluatedUtc, [], [], [], "NoRecommendation", null, null);
        Assert.Contains("no current-session market input", (trace with { MarketInputs = missing }).Summary);
        Assert.Contains("stale", (trace with { MarketInputs = stale }).Summary);
        Assert.StartsWith("Fresh shared market inputs evaluated", (trace with { MarketInputs = retained }).Summary);
        Assert.Contains("uncertain", (trace with { MarketInputs = missing, SelectionOutcome = "SubmissionOutcomeUnknown" }).Summary);
        var legacyJson = JsonSerializer.Serialize(trace);
        Assert.DoesNotContain("MarketInputs", legacyJson);
        Assert.Equal(legacyJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<BrokerDecisionTrace>(legacyJson)));
        foreach (var outside in new[] { input.SessionOpenUtc.AddDays(-1), input.EvaluatedUtc.AddMinutes(1) })
        {
            var memory = result.Memory with { Symbols = result.Memory.Symbols.SetItem("ABC", result.Memory.Symbols["ABC"] with { LastLiveBarUtc = outside }) };
            Assert.Equal(0, TradingMarketInputTrace.Capture(input, result with { Memory = memory })!.EvaluatedSymbols);
        }
        Assert.Null(TradingMarketInputTrace.Capture(TradingRecommendationTests.Input(), result));
    }
    [Theory]
    [InlineData("missing", "QuoteNotReturned")]
    [InlineData("failed", "QuoteUnavailable")]
    [InlineData("contract", "QuoteContractMismatch")]
    [InlineData("old-request", "QuoteRequestedBeforeEvaluation")]
    [InlineData("backward-receipt", "QuoteReceiptBeforeRequest")]
    [InlineData("late", "QuoteAttemptWindowExceeded")]
    [InlineData("no-price", "QuotePriceUnavailable")]
    [InlineData("daily", "DailyCandidateIdentityMismatch")]
    [InlineData("run", "DailyCandidateIdentityMismatch")]
    [InlineData("pause", "NewRiskBlocked")]
    [InlineData("sizing", "InsufficientCashForOneShare")]
    [InlineData("stale", "SignalTooOld")]
    public void ConfirmedRejectionsRetainSignalAndExactFailedCheckWithoutCreatingOrders(string change, string reason)
    {
        var input = TradingRecommendationTests.LiveInput();
        var quote = input.Quotes[0];
        input = change switch
        {
            "missing" => input with { Quotes = [] },
            "failed" => input with { Quotes = [quote with { SourceContract = "Unavailable:TimeoutException" }] },
            "contract" => input with { Quotes = [quote with { SourceContract = "Unqualified" }] },
            "old-request" => input with { Quotes = [quote with { RequestedUtc = input.EvaluatedUtc.AddTicks(-1) }] },
            "backward-receipt" => input with { Quotes = [quote with { ReceivedUtc = quote.RequestedUtc.AddTicks(-1) }] },
            "late" => input with { Quotes = [quote with { ReceivedUtc = input.EvaluatedUtc.AddMinutes(1).AddTicks(1) }] },
            "no-price" => input with { Quotes = [quote with { Ask = null, Price = null }] },
            "pause" => input with { EntriesPaused = true },
            "sizing" => input with { Quotes = [quote with { Ask = 10000m }] },
            "stale" => input with { EvaluatedUtc = input.EvaluatedUtc.AddTicks(1) },
            _ => input with { LiveInputs = input.LiveInputs.Select(o => o with { Input = o.Input with
            {
                DailySetup = new(change == "run" ? Guid.NewGuid() : o.Input.DailySetup!.DelphiRunId,
                    o.Input.DailySetup!.CandidateId, change == "daily" ? Guid.NewGuid() : o.Input.DailySetup!.DailyStrategyVersionId,
                    o.Input.DailySetup!.CommonDelphiComposite, o.Input.DailySetup!.SourceLenses)
            } }).ToImmutableArray() }
        };
        string original = JsonSerializer.Serialize(input);
        var result = TradingRecommendationEngine.Evaluate(input);
        Assert.True(result.Memory.Symbols["ABC"].LastLiveResult!.ConfirmedLiveEligible);
        Assert.Equal(reason, Assert.Single(result.EntryDecisions!.Value).Outcome);
        Assert.DoesNotContain(result.Recommendations, r => r.Action == "Buy");
        Assert.Equal(original, JsonSerializer.Serialize(input));
        if (change == "pause") Assert.Contains("Trading entries are paused.", result.EntryRiskBlocks!.Value);
    }

    [Theory]
    [InlineData(true, "EntryCutoffPassed")]
    [InlineData(false, "BeforeEntryWindow")]
    public void DiagnosticsPreserveExactPolicyEntryBoundaries(bool cutoff, string reason)
    {
        var input = TradingRecommendationTests.LiveInput();
        // An explicit fixture policy puts the unchanged comparison at the observation boundary.
        var policy = input.Assignment.Strategy.ReadLivePolicy() with
        {
            EntryCutoff = cutoff ? new TimeOnly(10, 2) : new TimeOnly(15, 45),
            EntryWindowStart = cutoff ? new TimeOnly(9, 50) : new TimeOnly(10, 3)
        };
        input = input with { Assignment = input.Assignment with { Strategy = input.Assignment.Strategy with { LivePolicy = policy } } };
        var result = TradingRecommendationEngine.Evaluate(input);
        Assert.Equal(reason, Assert.Single(result.EntryDecisions!.Value).Outcome);
        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public void DiagnosticsDoNotTurnARecommendationOrAcknowledgementIntoAFill()
    {
        var input = TradingRecommendationTests.LiveInput();
        var result = TradingRecommendationEngine.Evaluate(input);
        Assert.Equal("Recommended", Assert.Single(result.EntryDecisions!.Value).Outcome);
        Assert.Equal("Buy", Assert.Single(result.Recommendations).Action);
        Assert.Empty(input.ActualFills);
        var legacy = result with { EntryDecisions = null, EntryRiskBlocks = null };
        string json = JsonSerializer.Serialize(legacy);
        Assert.DoesNotContain("EntryDecisions", json);
        Assert.DoesNotContain("EntryRiskBlocks", json);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<TradingRecommendationResult>(json)));
    }

    [Theory]
    [InlineData("success", "Returned")]
    [InlineData("failure", "Failed")]
    [InlineData("deadline", "NotRequestedQuoteWindowEnded")]
    [InlineData("pause", "NotRequestedNewRiskBlocked")]
    [InlineData("stale", "NotRequestedSignalNotFresh")]
    public async Task QuoteEvidenceDistinguishesAnAttemptFromEachReasonForNoAttempt(string scenario, string outcome)
    {
        var input = TradingRecommendationTests.LiveInput();
        if (scenario == "pause") input = input with { EntriesPaused = true };
        if (scenario == "stale") input = input with { EvaluatedUtc = input.EvaluatedUtc.AddTicks(1) };
        var clock = new Clock(input.EvaluatedUtc.AddMinutes(scenario == "deadline" ? 1 : 0));
        int calls = 0;
        var result = await TradingRecommendationQuoteSource.ObserveAsync(input, (symbol, requested, ct) =>
        {
            calls++;
            if (scenario == "failure") throw new TimeoutException("PRIVATE PROVIDER PAYLOAD MUST NOT BE SAVED");
            return Task.FromResult(new TradingQuote(symbol, requested, requested.AddSeconds(1), 105m, 104.99m, 105.01m, "TmxQuoteFieldsV1"));
        }, clock, default);
        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(outcome, attempt.Outcome);
        Assert.Equal(scenario is "success" or "failure" ? 1 : 0, calls);
        Assert.Equal(scenario == "failure" ? "TimeoutException" : null, attempt.ErrorType);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(result.Attempts));
        if (calls == 0) { Assert.Null(attempt.RequestedUtc); Assert.Null(attempt.ReceivedUtc); Assert.Empty(result.Quotes); }
    }

    private sealed class Clock(DateTime utc) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(utc); }

    [Fact]
    public async Task PausedEntriesStillRequestIndependentHoldingProtectionQuotes()
    {
        var input = TradingRecommendationTests.LiveInput() with { EntriesPaused = true,
            CurrentHoldings = [new(Guid.NewGuid(), "HELD", 4, 400m, new(2026,9,8))] };
        var requested = ImmutableArray.CreateBuilder<string>();
        var read = await TradingRecommendationQuoteSource.ObserveAsync(input, (symbol, at, token) =>
        {
            requested.Add(symbol);
            return Task.FromResult(new TradingQuote(symbol, at, at.AddSeconds(1), 105m, 104.99m, 105.01m, "TmxQuoteFieldsV1"));
        }, new Clock(input.EvaluatedUtc), default);
        Assert.Equal(new[] { "HELD" }, requested.ToArray());
        Assert.Contains(read.Attempts, a => a.Symbol == "ABC" && a.Outcome == "NotRequestedNewRiskBlocked");
        Assert.Contains(read.Attempts, a => a.Symbol == "HELD" && a.Outcome == "Returned");
    }
}
