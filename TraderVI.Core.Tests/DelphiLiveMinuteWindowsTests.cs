#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveMinuteWindowsTests
{
    private static readonly DateTime Open = new(2026, 10, 27, 13, 30, 0, DateTimeKind.Utc);
    private static DelphiLiveMinuteEvidenceBook Book(int minutes) => new("TEST", "SyntheticMinuteSourceV1", Open,
        Open.AddHours(6.5), Enumerable.Range(0, minutes).Select(i => new DelphiLiveMinuteObservation(Guid.NewGuid(),
            new OhlcvBar(Open.AddMinutes(i), 100 + i, 102 + i, 99 + i, 101 + i, i + 1),
            Open.AddMinutes(i + 1).AddSeconds(5), Open.AddMinutes(i + 1).AddSeconds(8),
            Open.AddMinutes(i + 1).AddSeconds(9), Open.AddMinutes(i + 1).AddSeconds(45))).ToImmutableArray(), []);
    private static DelphiLiveMinuteWindow Window(DelphiLiveMinuteEvidenceBook book, int end, int horizon) =>
        DelphiLiveMinuteWindows.Build(book, Open.AddMinutes(end), TimeSpan.FromMinutes(horizon), Open.AddMinutes(end).AddSeconds(10));

    [Theory]
    [InlineData(20)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(180)]
    public void LongerHorizonsRemainElapsedMinutesRatherThanOldSampleCounts(int horizon)
    {
        var book = Book(190);
        var window = Window(book, 190, horizon);
        Assert.Equal(DelphiLiveMinuteWindowStatus.Available, window.Status);
        Assert.Equal(TimeSpan.FromMinutes(horizon), window.EndUtc - window.StartUtc);
        Assert.Equal(horizon, window.ObservationIds.Length);
        Assert.Equal(100 + 190 - horizon, window.Aggregate!.Open);
        Assert.Equal(290, window.Aggregate.Close);
        Assert.Equal(book.Observations.Skip(190 - horizon).Sum(o => o.Bar.Volume), window.Aggregate.Volume);
    }

    [Fact]
    public void MinuteEndpointKeepsFourFiveMinuteLegsAndExactOhlcv()
    {
        var book = Book(21);
        var result = DelphiLiveMinuteWindows.FiveMinuteLegs(book, Open.AddMinutes(21), 4, Open.AddMinutes(21).AddSeconds(10));
        Assert.Equal(DelphiLiveMinuteWindowStatus.Available, result.Status);
        var legs = result.Legs;
        Assert.Equal(4, legs.Length);
        Assert.Equal(Open.AddMinutes(1), legs[0].StartUtc);
        Assert.Equal(Open.AddMinutes(21), legs[^1].EndUtc);
        Assert.All(legs, leg => { Assert.Equal(TimeSpan.FromMinutes(5), leg.EndUtc - leg.StartUtc); Assert.Equal(5, leg.ObservationIds.Length); });
        Assert.Equal(new OhlcvBar(Open.AddMinutes(1), 101, 107, 100, 106, 20), legs[0].Aggregate);
        Assert.Equal(20, legs.SelectMany(l => l.ObservationIds).Distinct().Count());
        Assert.Equal(TimeSpan.FromMinutes(5), DelphiLivePolicyDefinition.Version1.BarInterval);
        Assert.Equal(4, DelphiLivePolicyDefinition.Version1.PersistenceObservationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(19)]
    public void MissingBeginningMiddleOrEndNeverProducesSyntheticPrices(int missing)
    {
        var book = Book(20);
        var result = Window(book with { Observations = book.Observations.RemoveAt(missing) }, 20, 20);
        Assert.Equal(DelphiLiveMinuteWindowStatus.MissingMinute, result.Status);
        Assert.Null(result.Aggregate);
        Assert.Empty(result.ObservationIds);
    }

    [Fact]
    public void OpeningWindowCannotShrinkToAvailableSamples()
    {
        var result = Window(Book(4), 4, 20);
        Assert.Equal(DelphiLiveMinuteWindowStatus.NotMature, result.Status);
        Assert.Null(result.Aggregate);
        var legs = DelphiLiveMinuteWindows.FiveMinuteLegs(Book(4), Open.AddMinutes(4), 4, Open.AddMinutes(4).AddSeconds(10));
        Assert.Equal(DelphiLiveMinuteWindowStatus.NotMature, legs.Status);
        Assert.Empty(legs.Legs);
    }

    [Fact]
    public void BackfillCannotRepairHistoricalOperationalWindow()
    {
        var book = Book(20);
        var late = book.Observations[7] with { CheckedUtc = Open.AddMinutes(8).AddSeconds(46) };
        var result = Window(book with { Observations = book.Observations.SetItem(7, late) }, 20, 20);
        Assert.Equal(DelphiLiveMinuteWindowStatus.LateMinute, result.Status);
        Assert.Null(result.Aggregate);
    }

    [Fact]
    public void FutureAndConflictingEvidenceAreUnavailable()
    {
        var book = Book(20);
        Assert.Equal(DelphiLiveMinuteWindowStatus.FutureEvidence,
            DelphiLiveMinuteWindows.Build(book, Open.AddMinutes(20), TimeSpan.FromMinutes(20), Open.AddMinutes(20).AddSeconds(7)).Status);
        Assert.Equal(DelphiLiveMinuteWindowStatus.SourceConflict,
            Window(book with { ConflictingStartsUtc = [Open] }, 20, 20).Status);
        Assert.Throws<ArgumentException>(() => DelphiLiveMinuteWindows.Build(book,
            Open.AddMinutes(20), TimeSpan.FromSeconds(1199), Open.AddMinutes(20).AddSeconds(10)));
    }

    [Fact]
    public void HistoricalContextDoesNotRenewCurrentPriceAuthority()
    {
        var book = Book(20); var later = Open.AddMinutes(22);
        Assert.Equal(DelphiLiveMinuteWindowStatus.Available,
            DelphiLiveMinuteWindows.Build(book, Open.AddMinutes(20), TimeSpan.FromMinutes(20), later).Status);
        Assert.Null(DelphiLiveMinuteEvidence.AvailableAt(book, Open.AddMinutes(20), later));
        var huge = book with { Observations = book.Observations.Select(o => o with { Bar = o.Bar with { Volume = long.MaxValue } }).ToImmutableArray() };
        Assert.Throws<OverflowException>(() => Window(huge, 20, 20));
    }
}
