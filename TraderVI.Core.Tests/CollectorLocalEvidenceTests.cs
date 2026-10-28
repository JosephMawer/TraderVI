#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class CollectorLocalEvidenceTests
{
    [Fact]
    public void LocalRawBooksRecoverConfirmedEntriesWithoutFiveMinutePublicationOrPaperMemory()
    {
        var (input, account, source, books) = Fixture();
        var bound = Bind(input, account, source, books);
        var result = TradingRecommendationEngine.Evaluate(input with { LiveInputs = [], MinuteInputs = bound });
        Assert.Contains(result.Recommendations, r => r.Action == "Buy");
        Assert.Equal(source.RevisionId, bound[0].DailySetup!.ComparisonAdmission!.SourceRevisionId);
        Assert.NotEmpty(result.MinuteEvidence!.Value[0].Confirmation.RecoveredObservationIds);
    }

    [Theory]
    [InlineData("missing-stock")][InlineData("missing-xiu")][InlineData("baseline")][InlineData("gap")][InlineData("conflict")]
    public void MissingOrConflictedLocalContextCannotBecomeAQualifyingEntry(string problem)
    {
        var (input, account, source, books) = Fixture();
        if (problem == "missing-stock") books.Remove("ABC");
        if (problem == "missing-xiu") books.Remove("XIU");
        if (problem == "baseline") source = source with { Baselines = new Dictionary<string, DelphiLiveFrozenBaseline>() };
        if (problem == "gap") books["ABC"] = books["ABC"] with { Observations = books["ABC"].Observations.RemoveAt(20) };
        if (problem == "conflict") books["ABC"] = books["ABC"] with { ConflictingStartsUtc = [input.SessionOpenUtc] };
        var result = TradingRecommendationEngine.Evaluate(input with { MinuteInputs = Bind(input, account, source, books) });
        Assert.DoesNotContain(result.Recommendations, r => r.Action == "Buy");
    }

    [Theory]
    [InlineData("run")][InlineData("source")][InlineData("future")][InlineData("session")]
    public void WrongOrFutureSourceCannotReuseTheApprovedIdentity(string problem)
    {
        var (input, account, source, books) = Fixture();
        if (problem == "run") source = source with { RunId = Guid.NewGuid() };
        if (problem == "source") books["ABC"] = books["ABC"] with { SourceContract = "Other" };
        if (problem == "future") source = source with { EffectiveUtc = input.EvaluatedUtc.AddSeconds(1) };
        if (problem == "session") books["ABC"] = books["ABC"] with { SessionCloseUtc = input.SessionCloseUtc.AddMinutes(-5) };
        Assert.Throws<ArgumentException>(() => Bind(input, account, source, books));
    }

    [Fact]
    public void AdmissionKeepsNormalAndPortfolioRulesOnlyProfilesDistinct()
    {
        var (input, account, source, books) = Fixture(); var candidate = source.Candidates[0];
        source = source with { Candidates = [candidate with { SourceLenses = [candidate.SourceLenses[0] with { Eligible = false }] }] };
        Assert.Empty(Bind(input, account, source, books));
        Assert.Single(Bind(input, account with { Profile = PaperGateProfile.PortfolioRulesOnlyV1 }, source, books));
    }

    [Fact]
    public void ProtectionUsesOnlyCompleteFiveMinuteClosesWithActualLateAvailability()
    {
        var (input, _, _, books) = Fixture(); var book = books["ABC"];
        var bars = CollectorLocalEvidence.ProtectionBars(book, input.EvaluatedUtc);
        Assert.Equal(13, bars.Length); Assert.All(bars, b => Assert.Equal(5, b.Minutes));
        Assert.Equal(input.SessionOpenUtc.AddMinutes(60), bars[^1].StartUtc);
        Assert.Equal(book.Observations[64].Bar.Close, bars[^1].Close);
        Assert.All(bars, b => Assert.Equal(book.Observations[0].CheckedUtc, b.ReceivedUtc));
        Assert.Empty(CollectorLocalEvidence.ProtectionBars(book with { ConflictingStartsUtc = [input.SessionOpenUtc] }, input.EvaluatedUtc));
        var missing = CollectorLocalEvidence.ProtectionBars(book with { Observations = book.Observations.RemoveAt(21) }, input.EvaluatedUtc);
        Assert.DoesNotContain(missing, b => b.StartUtc == input.SessionOpenUtc.AddMinutes(20));
        Assert.Equal(bars[^1].EvidenceId, missing[^1].EvidenceId);
    }

    [Fact]
    public void EmptyDailyCandidatesNeverRemoveVerifiedHoldingsAndPendingOrders()
    {
        var (input, account, _, _) = Fixture();
        var strategy = new BrokerManagedStrategy(input.Assignment.Strategy, account.AccountId, account.Profile, account);
        var real = new EngineRealObservation(Guid.NewGuid(), Guid.NewGuid(), 3, strategy,
            [new("HELD", "BrokerManaged", Guid.NewGuid(), false), new("EXIT", "BrokerPending", Guid.NewGuid(), false)]);
        var snapshot = CollectorSubscriptionRepository.FromVerified(new(input.TradingDate, input.SessionOpenUtc, input.SessionCloseUtc), [real], []);
        Assert.Equal(new[] { "XIU", "HELD", "EXIT" }, snapshot.Symbols());
        var nextDay = CollectorSubscriptionRepository.FromVerified(new(input.TradingDate.AddDays(1), input.SessionOpenUtc.AddDays(1), input.SessionCloseUtc.AddDays(1)), [real], ["NEW"]);
        Assert.Contains("HELD", nextDay.Symbols()); Assert.Contains("EXIT", nextDay.Symbols()); Assert.Contains("NEW", nextDay.Symbols());
    }

    private static ImmutableArray<TradingMinuteObservation> Bind(TradingRecommendationInput input, PaperComparisonAccount account,
        PaperComparisonSource source, Dictionary<string, DelphiLiveMinuteEvidenceBook> books) =>
        CollectorLocalEvidence.Bind(input.Assignment.Strategy, account, source, input.FrozenRunId!.Value,
            input.SessionOpenUtc, input.SessionCloseUtc, input.EvaluatedUtc, books);

    private static (TradingRecommendationInput, PaperComparisonAccount, PaperComparisonSource, Dictionary<string, DelphiLiveMinuteEvidenceBook>) Fixture()
    {
        var input = TradingMinuteRecommendationTests.Input(); var observation = input.MinuteInputs!.Value[0]; var daily = observation.DailySetup!;
        var account = new PaperComparisonAccount(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "DelphiLive", PaperGateProfile.NormalV1,
            input.SessionOpenUtc, input.Assignment.Strategy.Daily.StrategyVersionId, input.Assignment.Strategy.PolicyVersionId, null, null);
        var candidate = new DelphiLiveFrozenCandidate(daily.CandidateId, "ABC", .5m, "{}",
            [new(Guid.NewGuid(), daily.CandidateId, "Continuation", true, true, 1, .5m, null, "Synthetic")]);
        var baseline = new DelphiLiveFrozenBaseline(100, [], [], observation.Rulers);
        var source = new PaperComparisonSource(Guid.NewGuid(), account.BatchId, Guid.NewGuid(), daily.DelphiRunId, daily.DailyStrategyVersionId,
            input.SessionOpenUtc.AddMinutes(-1), input.SessionOpenUtc, input.SessionOpenUtc, [candidate],
            new Dictionary<string, DelphiLiveFrozenBaseline> { ["ABC"] = baseline, ["XIU"] = baseline });
        return (input, account, source, new() { ["ABC"] = observation.Stock, ["XIU"] = observation.Xiu });
    }
}
