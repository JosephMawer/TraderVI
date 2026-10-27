#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveMinuteEvidenceTests
{
    private static readonly DateTime Open = new(2026, 10, 27, 13, 30, 0, DateTimeKind.Utc);
    private static DelphiLiveMinuteEvidenceBook Empty => new("TEST", "SyntheticMinuteSourceV1", Open, Open.AddHours(6.5), [], []);
    private static OhlcvBar Bar(int minute, decimal price = 10) => new(Open.AddMinutes(minute), price, price, price, price, 100);
    private static DelphiLiveMinuteRead Read(int start, params OhlcvBar[] bars) => new(Guid.NewGuid(), "TEST", "SyntheticMinuteSourceV1",
        Open.AddMinutes(start + 1), Open.AddMinutes(start + 1).AddSeconds(45),
        Open.AddMinutes(start + 1).AddSeconds(5), Open.AddMinutes(start + 1).AddSeconds(8), bars.ToImmutableArray());
    private static DelphiLiveMinuteEvidenceResult Observe(DelphiLiveMinuteEvidenceBook book, DelphiLiveMinuteRead read) =>
        DelphiLiveMinuteEvidence.Observe(book, read, read.ReceivedUtc.AddSeconds(1));

    [Fact]
    public void DistinctMinuteArrivalsAreAcceptedAndOriginalDeadlineExpiresAtUse()
    {
        var read = Read(0, Bar(0), Bar(1)); // The adjacent unfinished row is never selected.
        var first = Observe(Empty, read);
        Assert.True(first.NewOnTimeMinute);
        Assert.Single(first.Book.Observations);
        Assert.Equal(Bar(0), first.Book.Observations[0].Bar);
        Assert.NotNull(DelphiLiveMinuteEvidence.AvailableAt(first.Book, read.ExpectedEndUtc, read.DeadlineUtc));
        Assert.Null(DelphiLiveMinuteEvidence.AvailableAt(first.Book, read.ExpectedEndUtc, read.DeadlineUtc.AddTicks(1)));
        var next = Observe(first.Book, Read(1, Bar(0), Bar(1)));
        Assert.True(next.NewOnTimeMinute);
        Assert.Equal(2, next.Book.Observations.Length);
    }

    [Fact]
    public void RereadWithNewIdentityCannotRefreshEvidenceOrAdvanceAfterRestart()
    {
        var read = Read(0, Bar(0));
        var first = Observe(Empty, read);
        var restored = JsonSerializer.Deserialize<DelphiLiveMinuteEvidenceBook>(JsonSerializer.Serialize(first.Book))!;
        var repeated = Observe(restored, read with { ObservationId = Guid.NewGuid(), FetchStartedUtc = read.DeadlineUtc.AddSeconds(5), ReceivedUtc = read.DeadlineUtc.AddSeconds(6) });
        Assert.Equal(DelphiLiveMinuteDisposition.Repeated, repeated.Disposition);
        Assert.False(repeated.NewOnTimeMinute);
        Assert.Equal(first.Book.Observations[0], Assert.Single(repeated.Book.Observations));
        Assert.Null(DelphiLiveMinuteEvidence.AvailableAt(repeated.Book, read.ExpectedEndUtc, read.DeadlineUtc.AddSeconds(7)));
    }

    [Fact]
    public void AReadStartedBeforeCompletionCannotBecomeFreshByWaiting()
    {
        var read = Read(0, Bar(0)) with { FetchStartedUtc = Open.AddSeconds(50) };
        var result = Observe(Empty, read);
        Assert.Equal(DelphiLiveMinuteDisposition.UnfinishedRead, result.Disposition);
        Assert.Empty(result.Book.Observations);
    }

    [Fact]
    public void StalePreviousBarCannotFillRequestedMissingMinute()
    {
        var result = Observe(Empty, Read(2, Bar(0), Bar(3)));
        Assert.Equal(DelphiLiveMinuteDisposition.Missing, result.Disposition);
        Assert.Empty(result.Book.Observations);
    }

    [Fact]
    public void HistoricalBackfillAndSlowPublicationRetainLateStatus()
    {
        var read = Read(0, Bar(0));
        var late = DelphiLiveMinuteEvidence.Observe(Empty, read, read.DeadlineUtc.AddTicks(1));
        Assert.Equal(DelphiLiveMinuteDisposition.Late, late.Disposition);
        Assert.False(Assert.Single(late.Book.Observations).ObservedOnTime);
        var reread = Observe(late.Book, read with { ObservationId = Guid.NewGuid(), FetchStartedUtc = read.DeadlineUtc.AddSeconds(5), ReceivedUtc = read.DeadlineUtc.AddSeconds(6) });
        Assert.Equal(DelphiLiveMinuteDisposition.Repeated, reread.Disposition);
        Assert.False(Assert.Single(reread.Book.Observations).ObservedOnTime);
    }

    [Fact]
    public void CompletedRevisionLatchesAcrossRestartAndLaterCleanReads()
    {
        var first = Observe(Empty, Read(0, Bar(0)));
        var conflict = Observe(first.Book, Read(1, Bar(0, 11), Bar(1)));
        Assert.Equal(DelphiLiveMinuteDisposition.Conflict, conflict.Disposition);
        Assert.Equal(Bar(0), Assert.Single(conflict.Book.Observations).Bar);
        Assert.Equal(Open, Assert.Single(conflict.Book.ConflictingStartsUtc));
        var restored = JsonSerializer.Deserialize<DelphiLiveMinuteEvidenceBook>(JsonSerializer.Serialize(conflict.Book))!;
        var later = Observe(restored, Read(2, Bar(2)));
        Assert.Equal(DelphiLiveMinuteDisposition.Conflict, later.Disposition);
        Assert.Null(DelphiLiveMinuteEvidence.AvailableAt(later.Book, Open.AddMinutes(1), Open.AddMinutes(1).AddSeconds(20)));
    }

    [Fact]
    public void MissingMinuteIsNotSynthesizedBetweenTwoAcceptedMinutes()
    {
        var first = Observe(Empty, Read(0, Bar(0)));
        var later = Observe(first.Book, Read(2, Bar(2)));
        Assert.Equal(2, later.Book.Observations.Length);
        Assert.Null(DelphiLiveMinuteEvidence.AvailableAt(later.Book, Open.AddMinutes(2), Open.AddMinutes(2).AddSeconds(20)));
    }

    [Fact]
    public void WrongSourceSymbolIdentityDuplicatesAndInvalidPricesFailClosed()
    {
        var read = Read(0, Bar(0));
        Assert.Throws<ArgumentException>(() => Observe(Empty, read with { SourceContract = "ChangedSource" }));
        Assert.Throws<ArgumentException>(() => Observe(Empty, read with { Symbol = "OTHER" }));
        Assert.Throws<ArgumentException>(() => Observe(Empty, read with { Bars = [Bar(0), Bar(0)] }));
        Assert.Throws<ArgumentException>(() => Observe(Empty, read with { Bars = [Bar(0, 0)] }));
        var first = Observe(Empty, read);
        Assert.Throws<ArgumentException>(() => Observe(first.Book, Read(1, Bar(1)) with { ObservationId = read.ObservationId }));
    }

    [Fact]
    public void AmbiguousClockSessionDeadlineAndRestoredStateAreRejected()
    {
        var read = Read(0, Bar(0));
        Assert.Throws<ArgumentException>(() => Observe(Empty, read with { DeadlineUtc = read.ExpectedEndUtc.AddMinutes(1) }));
        Assert.Throws<ArgumentException>(() => Observe(Empty, read with { ExpectedEndUtc = read.ExpectedEndUtc.AddTicks(1) }));
        Assert.Throws<ArgumentException>(() => Observe(Empty, read with { ReceivedUtc = DateTime.SpecifyKind(read.ReceivedUtc, DateTimeKind.Unspecified) }));
        Assert.Throws<ArgumentException>(() => DelphiLiveMinuteEvidence.Observe(Empty, read, read.ReceivedUtc.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Observe(Empty with { SessionCloseUtc = Open }, read));
        var first = Observe(Empty, read);
        Assert.Throws<ArgumentException>(() => Observe(first.Book with { Observations = [first.Book.Observations[0], first.Book.Observations[0]] }, Read(1, Bar(1))));
    }
}
