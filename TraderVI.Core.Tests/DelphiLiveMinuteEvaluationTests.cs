#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveMinuteEvaluationTests
{
    private static readonly DateTime Open = new(2026, 10, 28, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DelphiLivePolicyDefinition Rules = DelphiLivePolicyDefinition.Version1;
    private static readonly DelphiLiveMinuteStrategy Strategy = new(Rules, "SyntheticV1", TimeSpan.FromSeconds(45));
    private static DelphiLiveTrueRangeRulerMeasurement Ruler(int n) => new(n, new(2026, 10, 27), DelphiLiveScalarMeasurement.Available(.02m));
    private static readonly DelphiLiveVolatilityRulerMeasurements Rulers = new(Ruler(5), Ruler(10), Ruler(14), Ruler(20));

    [Fact]
    public void ActualFourFamilyJudgmentsConfirmRecoveredCurrentPairImmediately()
    {
        var stock = Book("TEST", 67, .1m); var xiu = Book("XIU", 67, 0m);
        var pair = Pair(stock, xiu, 67);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.Confirmed, pair.Confirmation.Status);
        Assert.True(pair.Previous.Judgment.SupportiveFamilies >= 3);
        Assert.True(pair.Latest.Judgment.SupportiveFamilies >= 3);
        Assert.NotEmpty(pair.Confirmation.RecoveredObservationIds);
        Assert.False(stock.Observations[^2].ObservedOnTime);
        Assert.True(stock.Observations[^1].ObservedOnTime);
        Assert.Equal(Open.AddMinutes(65), pair.Latest.Measurements.VwapThroughUtc);
        var diagnostic = global::Core.Runtime.DelphiReportBuilder.DescribeMinuteConfirmation(pair);
        Assert.Contains("Persistence=", diagnostic);
        Assert.Contains("VolumeSupport=", diagnostic);
        Assert.Contains("recovered inputs", diagnostic);
        Assert.Contains("Signal evidence only", diagnostic);
    }

    [Fact]
    public void RestartReplayPreservesPairIdentityAndDoesNotExtendExpiry()
    {
        var stock = Book("TEST", 67, .1m); var xiu = Book("XIU", 67, 0m);
        var restored = JsonSerializer.Deserialize<DelphiLiveMinuteEvidenceBook>(JsonSerializer.Serialize(stock))!;
        var first = Pair(stock, xiu, 67); var second = Pair(restored, xiu, 67);
        Assert.Equal(first.Latest.Judgment.PolicyIdentity, second.Latest.Judgment.PolicyIdentity);
        Assert.Equal(first.Confirmation.ExpiresUtc, second.Confirmation.ExpiresUtc);
        var expired = DelphiLiveMinuteEvaluator.EvaluatePair(Strategy, restored, xiu, Rulers, 99, 100,
            Open.AddMinutes(67), Open.AddMinutes(68));
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.ExpiredLatest, expired.Confirmation.Status);
    }

    [Fact]
    public void LaterBarCannotImproveTheEarlierJudgment()
    {
        var stock = Book("TEST", 67, .1m); var xiu = Book("XIU", 67, 0m);
        var first = Pair(stock, xiu, 67);
        var last = stock.Observations[^1];
        var changed = stock with { Observations = stock.Observations.SetItem(66, last with { Bar = last.Bar with { Low = 1, Close = 1 } }) };
        var second = Pair(changed, xiu, 67);
        Assert.Equal(first.Previous.Measurements.Price, second.Previous.Measurements.Price);
        Assert.Equal(first.Previous.Families.ToArray(), second.Previous.Families.ToArray());
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.NotQualifying, second.Confirmation.Status);
        Assert.DoesNotContain(last.ObservationId, second.Previous.Judgment.StockObservationIds);
    }

    [Fact]
    public void GapsRetainElapsedHorizonsAndOpeningGapMakesVwapUnavailable()
    {
        var stock = Book("TEST", 67, .1m); var xiu = Book("XIU", 67, 0m);
        var missing = stock with { Observations = stock.Observations.RemoveAt(20) };
        var facts = Pair(missing, xiu, 67).Latest.Measurements;
        Assert.Equal(DelphiLiveMeasurementAvailability.Available, facts.Price.TwentyMinute.StockReturn.Availability);
        Assert.Equal(DelphiLiveMeasurementAvailability.Unavailable, facts.Price.OneHour.StockReturn.Availability);
        Assert.Equal(DelphiLiveMeasurementAvailability.Unavailable, facts.Structure.SessionVwap.Availability);
        Assert.Equal(TimeSpan.FromMinutes(60), facts.Price.OneHour.Horizon);
        Assert.Equal(4, facts.Persistence.Intervals.Length);
        Assert.Equal(TimeSpan.FromMinutes(5), facts.Persistence.Intervals[1].IntervalEndUtc - facts.Persistence.Intervals[0].IntervalEndUtc);
    }

    [Fact]
    public void MissingCurrentBenchmarkAndConflictsCannotConfirm()
    {
        var stock = Book("TEST", 67, .1m); var xiu = Book("XIU", 67, 0m);
        Assert.NotEqual(DelphiLiveRecoveredConfirmationStatus.Confirmed, Pair(stock, xiu with { Observations = xiu.Observations.RemoveAt(66) }, 67).Confirmation.Status);
        Assert.Equal(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence,
            Pair(stock with { ConflictingStartsUtc = [Open] }, xiu, 67).Confirmation.Status);
    }

    [Theory]
    [InlineData(20)] [InlineData(25)] [InlineData(60)] [InlineData(120)] [InlineData(180)]
    public void FiveMinuteBoundaryMeasurementsMatchFrozenV1(int minutes)
    {
        var stock = Book("TEST", minutes, .1m); var xiu = Book("XIU", minutes, .01m);
        var s = Canonical(stock); var x = Canonical(xiu); var end = Open.AddMinutes(minutes);
        var facts = DelphiLiveMinuteMeasurements.Calculate(stock, xiu, end, end.AddSeconds(10), 99, 100, Rules);
        var persistence = DelphiLiveMeasurements.CalculatePersistence(s, x, end, Rules);
        Assert.Equal(persistence.Score, facts.Persistence.Score);
        Assert.Equal(persistence.Intervals.ToArray(), facts.Persistence.Intervals.ToArray());
        Assert.Equal(DelphiLiveMeasurements.CalculatePriceMovement(s, x, end, 99, Rules, 100), facts.Price);
        Assert.Equal(DelphiLiveMeasurements.CalculateDirectionalVolume(s, end, Rules), facts.Volume);
        var originalStructure = DelphiLiveMeasurements.CalculatePriceStructure(s, end, 99, Rules);
        Assert.Equal(originalStructure.SessionVwap, facts.Structure.SessionVwap);
        Assert.Equal(originalStructure.CurrentClose, facts.Structure.CurrentClose);
        Assert.Equal(originalStructure.PriorTwentyMinuteRange.Availability, facts.Structure.PriorTwentyMinuteRange.Availability);
        Assert.Equal(originalStructure.PriorTwentyMinuteRange.High, facts.Structure.PriorTwentyMinuteRange.High);
        Assert.Equal(originalStructure.PriorTwentyMinuteRange.Low, facts.Structure.PriorTwentyMinuteRange.Low);
    }

    [Fact]
    public void SuccessorIdentityIncludesRulesAndSourceDeadlineWithoutChangingV1()
    {
        Assert.StartsWith(DelphiLiveMinuteStrategy.Evaluator, Strategy.Identity);
        Assert.NotEqual(Strategy.Identity, (Strategy with { DeadlineOffset = TimeSpan.FromSeconds(40) }).Identity);
        Assert.NotEqual(Strategy.Identity, (Strategy with { SourceContract = "Other" }).Identity);
        Assert.Equal(TimeSpan.FromMinutes(5), Rules.BarInterval);
        Assert.Throws<ArgumentException>(() => Pair(Book("TEST", 67, .1m) with { SourceContract = "Other" }, Book("XIU", 67, 0), 67));
    }

    private static DelphiLiveMinutePair Pair(DelphiLiveMinuteEvidenceBook stock, DelphiLiveMinuteEvidenceBook xiu, int end) =>
        DelphiLiveMinuteEvaluator.EvaluatePair(Strategy, stock, xiu, Rulers, 99, 100, Open.AddMinutes(end), Open.AddMinutes(end).AddSeconds(10));

    private static DelphiLiveMinuteEvidenceBook Book(string symbol, int minutes, decimal rise)
    {
        var batch = new DelphiLiveCollectorBatch(Guid.NewGuid(), "SyntheticV1", symbol, Open, Open.AddHours(6.5), Open.AddMinutes(minutes),
            TimeSpan.FromSeconds(45), Open.AddMinutes(minutes).AddSeconds(5), Open.AddMinutes(minutes).AddSeconds(7), Open.AddMinutes(minutes).AddSeconds(8),
            Enumerable.Range(0, minutes).Select(i => new OhlcvBar(Open.AddMinutes(i), 100 + i * rise,
                100 + (i + 1) * rise + .01m, 100 + i * rise - .01m, 100 + (i + 1) * rise, 100 + i)).ToImmutableArray(), "{}", null);
        return DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(batch), batch);
    }
    private static DelphiLiveFiveMinuteSeries Canonical(DelphiLiveMinuteEvidenceBook book)
    {
        var bars = Enumerable.Range(1, book.Observations.Length / 5).Select(i =>
        {
            var window = DelphiLiveMinuteWindows.BuildRecoveredContext(book, Open.AddMinutes(i * 5), TimeSpan.FromMinutes(5),
                book.Observations[^1].CheckedUtc); var a = window.Aggregate!;
            return new DelphiLiveFiveMinuteBar(Guid.NewGuid(), book.Symbol, new(2026, 10, 28), window.StartUtc, window.EndUtc,
                a.Open, a.High, a.Low, a.Close, a.Volume, window.EndUtc.AddSeconds(5), "Synthetic", 1, DelphiLiveEvidenceDisposition.OperationalOnTime);
        });
        return new(book.Symbol, new(2026, 10, 28), Open, Open, bars);
    }
}
