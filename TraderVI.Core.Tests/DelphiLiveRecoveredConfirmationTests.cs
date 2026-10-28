#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveRecoveredConfirmationTests
{
    private static readonly DateTime Open = new(2026, 10, 28, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Open.AddMinutes(22).AddSeconds(20);
    private const string Policy = "MinuteRecoverySuccessorTestV1";

    private static DelphiLiveMinuteEvidenceBook Book(string symbol, bool recovered = true) => new(symbol,
        "SyntheticMinuteV1", Open, Open.AddHours(6.5), Enumerable.Range(0, 22).Select(i =>
        {
            var end = Open.AddMinutes(i + 1);
            var request = recovered ? Now.AddSeconds(-10) : end.AddSeconds(5);
            return new DelphiLiveMinuteObservation(Guid.NewGuid(),
                new OhlcvBar(end.AddMinutes(-1), 10 + i, 11 + i, 9 + i, 10 + i, 100),
                request, request.AddSeconds(1), request.AddSeconds(2), end.AddSeconds(45));
        }).ToImmutableArray(), []);

    private static DelphiLiveRecoveredJudgment Judgment(DelphiLiveMinuteEvidenceBook stock,
        DelphiLiveMinuteEvidenceBook benchmark, int endpoint, int votes = 3) => new(Policy, stock.SourceContract,
        stock.Symbol, Open, Open.AddMinutes(endpoint), Now.AddSeconds(-5), true, true, true, false, votes,
        stock.Observations.Where(o => o.EndUtc <= Open.AddMinutes(endpoint)).Select(o => o.ObservationId).ToImmutableArray(),
        benchmark.Observations.Where(o => o.EndUtc <= Open.AddMinutes(endpoint)).Select(o => o.ObservationId).ToImmutableArray());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredCurrentPairConfirmsImmediatelyWithoutTwoExtraObservations(bool recovered)
    {
        var stock = Book("TEST", recovered);
        var benchmark = Book("XIU", recovered);
        var previous = Judgment(stock, benchmark, 21);
        var latest = Judgment(stock, benchmark, 22);
        stock = JsonSerializer.Deserialize<DelphiLiveMinuteEvidenceBook>(JsonSerializer.Serialize(stock))!;
        benchmark = JsonSerializer.Deserialize<DelphiLiveMinuteEvidenceBook>(JsonSerializer.Serialize(benchmark))!;
        var result = DelphiLiveRecoveredConfirmation.Assess(Policy, stock, benchmark, previous, latest, Now);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.Confirmed, result.Status);
        Assert.Equal(Now, result.DecidedUtc);
        Assert.Equal(Open.AddMinutes(22).AddSeconds(45), result.ExpiresUtc);
        Assert.Equal(recovered ? 42 : 0, result.RecoveredObservationIds.Length);
        Assert.Equal(recovered, stock.Observations.Any(o => !o.ObservedOnTime));
    }

    [Theory]
    [InlineData(21, 21)]
    [InlineData(20, 22)]
    [InlineData(22, 21)]
    public void RepeatedSkippedAndReversedEndpointsCannotConfirm(int first, int second)
    {
        var s = Book("TEST"); var b = Book("XIU");
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.NonConsecutive,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, Judgment(s, b, first), Judgment(s, b, second), Now).Status);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    public void RecoveryDoesNotLowerThreeOfFour(int first, int second)
    {
        var s = Book("TEST"); var b = Book("XIU");
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.NotQualifying,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, Judgment(s, b, 21, first), Judgment(s, b, 22, second), Now).Status);
    }

    [Fact]
    public void ExpiryIsRecheckedAtSubmissionEvenAfterSuccessfulRecovery()
    {
        var s = Book("TEST"); var b = Book("XIU");
        var previous = Judgment(s, b, 21); var latest = Judgment(s, b, 22);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.Confirmed,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, previous, latest, Now).Status);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.ExpiredLatest,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, previous, latest, Open.AddMinutes(22).AddSeconds(46)).Status);
    }

    [Fact]
    public void LateNewestReceiptCannotRenewItsOriginalDeadline()
    {
        var s = Book("TEST"); var b = Book("XIU");
        s = s with { Observations = s.Observations.SetItem(21, s.Observations[21] with { CheckedUtc = Now.AddSeconds(30) }) };
        var previous = Judgment(s, b, 21) with { CalculatedUtc = Now.AddSeconds(31) };
        var latest = Judgment(s, b, 22) with { CalculatedUtc = Now.AddSeconds(31) };
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.ExpiredLatest,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, previous, latest, Now.AddSeconds(32)).Status);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("source")]
    [InlineData("session")]
    [InlineData("symbol")]
    public void SavedJudgmentCannotCrossReviewedIdentity(string changed)
    {
        var s = Book("TEST"); var b = Book("XIU"); var previous = Judgment(s, b, 21);
        previous = changed switch
        {
            "policy" => previous with { PolicyIdentity = "OldPolicy" },
            "source" => previous with { SourceContract = "OtherSource" },
            "session" => previous with { SessionOpenUtc = Open.AddDays(-1) },
            _ => previous with { Symbol = "OTHER" }
        };
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.IdentityMismatch,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, previous, Judgment(s, b, 22), Now).Status);
    }

    [Fact]
    public void RecoveredJudgmentCannotUseLaterBarOrUnavailableReceipt()
    {
        var s = Book("TEST"); var b = Book("XIU"); var previous = Judgment(s, b, 21); var latest = Judgment(s, b, 22);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b,
                previous with { StockObservationIds = latest.StockObservationIds }, latest, Now).Status);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b,
                previous with { CalculatedUtc = Open.AddMinutes(21).AddSeconds(20) }, latest, Now).Status);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.FutureEvidence,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, previous,
                latest with { CalculatedUtc = Now.AddSeconds(1) }, Now).Status);
    }

    [Fact]
    public void MissingBenchmarkDuplicateInputAndCompletedConflictBlock()
    {
        var s = Book("TEST"); var b = Book("XIU"); var previous = Judgment(s, b, 21); var latest = Judgment(s, b, 22);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b with { Observations = b.Observations.RemoveAt(21) }, previous, latest, Now).Status);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, previous,
                latest with { StockObservationIds = latest.StockObservationIds.Add(latest.StockObservationIds[0]) }, Now).Status);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s with { ConflictingStartsUtc = [Open] }, b, previous, latest, Now).Status);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("immature")]
    [InlineData("confidence")]
    [InlineData("veto")]
    public void OtherQualificationRulesRemainRequired(string changed)
    {
        var s = Book("TEST"); var b = Book("XIU"); var latest = Judgment(s, b, 22);
        latest = changed switch
        {
            "invalid" => latest with { Valid = false }, "immature" => latest with { Mature = false },
            "confidence" => latest with { NormalConfidence = false }, _ => latest with { SafetyVeto = true }
        };
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.NotQualifying,
            DelphiLiveRecoveredConfirmation.Assess(Policy, s, b, Judgment(s, b, 21), latest, Now).Status);
    }

    [Fact]
    public void RecoveredWindowsKeepEventHorizonAndHistoricalLateStatus()
    {
        var s = Book("TEST");
        var recovered = DelphiLiveMinuteWindows.BuildRecoveredContext(s, Open.AddMinutes(21), TimeSpan.FromMinutes(20), Now);
        Assert.Equal(DelphiLiveMinuteWindowStatus.Available, recovered.Status);
        Assert.Equal(20, recovered.RecoveredObservationIds.Length);
        Assert.Equal(11, recovered.Aggregate!.Open);
        Assert.Equal(30, recovered.Aggregate.Close); // Later minute's close is 31 and must not enter this endpoint.
        Assert.Equal(DelphiLiveMinuteWindowStatus.LateMinute,
            DelphiLiveMinuteWindows.Build(s, Open.AddMinutes(21), TimeSpan.FromMinutes(20), Now).Status);
        Assert.Equal(DelphiLiveMinuteWindowStatus.FutureEvidence,
            DelphiLiveMinuteWindows.BuildRecoveredContext(s, Open.AddMinutes(21), TimeSpan.FromMinutes(20), Open.AddMinutes(21).AddSeconds(20)).Status);
        Assert.Equal(DelphiLiveMinuteWindowStatus.MissingMinute,
            DelphiLiveMinuteWindows.BuildRecoveredContext(s with { Observations = s.Observations.RemoveAt(10) },
                Open.AddMinutes(21), TimeSpan.FromMinutes(20), Now).Status);
        var legs = DelphiLiveMinuteWindows.RecoveredFiveMinuteLegs(s, Open.AddMinutes(21), 4, Now);
        Assert.Equal(4, legs.Legs.Length);
        Assert.All(legs.Legs, l => Assert.Equal(TimeSpan.FromMinutes(5), l.EndUtc - l.StartUtc));
        Assert.Equal(TimeSpan.FromMinutes(5), DelphiLivePolicyDefinition.Version1.BarInterval);
    }
}
