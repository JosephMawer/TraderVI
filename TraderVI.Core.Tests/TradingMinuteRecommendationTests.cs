#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradingMinuteRecommendationTests
{
    [Fact]
    public void RecoveredPairBuysImmediatelyAndRestartRetainsTheSameEvidenceAndPriceEpisode()
    {
        var input = Input(); var first = TradingRecommendationEngine.Evaluate(input);
        var buy = Assert.Single(first.Recommendations.Where(r => r.Action == "Buy"));
        Assert.Equal(input.SessionOpenUtc.AddMinutes(67).AddSeconds(45), buy.SignalExpiresUtc);
        var restored = JsonSerializer.Deserialize<TradingRecommendationInput>(JsonSerializer.Serialize(input with { PreviousMemory = first.Memory }))!;
        var second = TradingRecommendationEngine.Evaluate(restored);
        Assert.Equal(buy, Assert.Single(second.Recommendations.Where(r => r.Action == "Buy")));
        Assert.Equal(first.Memory.Symbols["ABC"].MinuteSignal, second.Memory.Symbols["ABC"].MinuteSignal);
        Assert.Null(second.Memory.Symbols["ABC"].LastLiveResult); // No Paper/V1 confirmation imported.
    }

    [Theory]
    [InlineData("pause")][InlineData("pending")][InlineData("risk")][InlineData("missing")]
    [InlineData("stale")][InlineData("quote-expired")][InlineData("source")][InlineData("assignment")]
    [InlineData("sold")][InlineData("daily")][InlineData("weak")]
    public void CurrentPairNeverOverridesAccountSourceOrTimingChecks(string blocker)
    {
        var i = Input(); var observation = i.MinuteInputs!.Value[0];
        i = blocker switch
        {
            "pause" => i with { EntriesPaused = true },
            "pending" => i with { Account = i.Account! with { PendingOrderCount = 1 } },
            "risk" => i with { Account = i.Account! with { CapitalReviewRequired = true } },
            "missing" => i with { MinuteInputs = [] },
            "stale" => i with { EvaluatedUtc = i.EvaluatedUtc.AddMinutes(1) },
            "quote-expired" => i with { Quotes = [i.Quotes[0] with { ReceivedUtc = i.SessionOpenUtc.AddMinutes(67).AddSeconds(46) }] },
            "source" => i with { MinuteInputs = [observation with { Stock = observation.Stock with { SourceContract = "Wrong" } }] },
            "assignment" => i with { Assignment = i.Assignment with { EffectiveUtc = i.SessionOpenUtc.AddMinutes(66) } },
            "sold" => i with { ActualFills = [new(Guid.NewGuid(), Guid.NewGuid(), "ABC", "SELL", i.SessionOpenUtc.AddMinutes(66), i.TradingDate, 1, 100, "Exit", "Initial", 100)] },
            "daily" => i with { FrozenRunId = Guid.NewGuid() },
            "weak" => i with { MinuteInputs = [observation with { Stock = observation.Stock with { Observations = observation.Stock.Observations.SetItem(66,
                observation.Stock.Observations[66] with { Bar = observation.Stock.Observations[66].Bar with { Low = 1, Close = 1 } }) } }] },
            _ => throw new Exception()
        };
        var result = TradingRecommendationEngine.Evaluate(i);
        Assert.DoesNotContain(result.Recommendations, r => r.Action == "Buy");
        if (blocker == "missing") Assert.Contains(result.Warnings, w => w.Contains("local collector evidence"));
        else Assert.NotEmpty(result.EntryDecisions!.Value);
    }

    [Fact]
    public void MissingEvidenceCannotEraseTheOriginalRejectedPriceEpisode()
    {
        var input = Input(); var first = TradingRecommendationEngine.Evaluate(input);
        var missing = TradingRecommendationEngine.Evaluate(input with { PreviousMemory = first.Memory, MinuteInputs = [] });
        Assert.False(missing.Memory.Symbols["ABC"].MinuteSignal!.Confirmed);
        Assert.Equal(first.Memory.Symbols["ABC"].MinuteSignal!.EpisodeStartUtc, missing.Memory.Symbols["ABC"].MinuteSignal!.EpisodeStartUtc);
        var restored = TradingRecommendationEngine.Evaluate(input with { PreviousMemory = missing.Memory });
        Assert.Equal(first.Memory.Symbols["ABC"].MinuteSignal!.EpisodeStartUtc, restored.Memory.Symbols["ABC"].MinuteSignal!.EpisodeStartUtc);
    }

    [Fact]
    public void SuccessorMustBeExplicitAndLegacySerializationAndFingerprintRemainUnchanged()
    {
        var old = TradingRecommendationTests.LiveInput().Assignment.Strategy;
        var bytes = JsonSerializer.Serialize(old); Assert.DoesNotContain("MinuteEntry", bytes);
        Assert.Equal(old.Fingerprint, JsonSerializer.Deserialize<CompleteTradingStrategy>(bytes)!.Fingerprint);
        var next = Input().Assignment.Strategy;
        Assert.NotEqual(old.Fingerprint, next.Fingerprint);
        Assert.Throws<ArgumentException>(() => (next with { Selection = CompleteTradingStrategy.LiveSelection }).Validate());
        Assert.Throws<ArgumentException>(() => (next with { MinuteEntry = null }).Validate());
        var ignored = TradingRecommendationTests.LiveInput() with { MinuteInputs = Input().MinuteInputs };
        Assert.DoesNotContain(TradingRecommendationEngine.Evaluate(ignored).Recommendations, r => r.SignalExpiresUtc.HasValue);
    }

    internal static TradingRecommendationInput Input()
    {
        var old = TradingRecommendationTests.LiveInput(); var open = old.SessionOpenUtc; var now = open.AddMinutes(67).AddSeconds(10);
        var strategy = old.Assignment.Strategy with { Selection = CompleteTradingStrategy.MinuteSelection,
            MinuteEntry = new(old.Assignment.Strategy.ReadLivePolicy(), "SyntheticV1", TimeSpan.FromSeconds(45)) };
        var context = old.LiveInputs[0].Input;
        DelphiLiveMinuteEvidenceBook Book(string symbol, decimal rise)
        {
            var batch = new DelphiLiveCollectorBatch(Guid.NewGuid(), "SyntheticV1", symbol, open, old.SessionCloseUtc, open.AddMinutes(67),
                TimeSpan.FromSeconds(45), now.AddSeconds(-5), now.AddSeconds(-3), now.AddSeconds(-2), Enumerable.Range(0, 67).Select(n =>
                    new OhlcvBar(open.AddMinutes(n), 100 + n * rise, 100 + (n + 1) * rise + .01m,
                        100 + n * rise - .01m, 100 + (n + 1) * rise, 100)).ToImmutableArray(), "{}", null);
            return DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(batch), batch);
        }
        return old with { Assignment = old.Assignment with { Strategy = strategy }, EvaluatedUtc = now, LiveInputs = [], Candidates = [],
            MinuteInputs = [new(Book("ABC", .1m), Book("XIU", 0), context.VolatilityRulers, 100, 100, context.DailySetup)],
            Quotes = [new("ABC", now, now.AddSeconds(1), 106.7m, 106.69m, 106.71m, "TmxQuoteFieldsV1")] };
    }
}
