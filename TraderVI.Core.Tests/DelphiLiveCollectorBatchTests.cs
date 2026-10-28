#nullable enable
using Core.Db;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveCollectorBatchTests
{
    private static readonly DateTime Open = new(2026, 10, 28, 13, 30, 0, DateTimeKind.Utc);
    private static OhlcvBar Bar(int start, decimal close = 10) => new(Open.AddMinutes(start), 10, 12, 9, close, 100);
    private static DelphiLiveCollectorBatch Batch(int end = 22) => new(Guid.NewGuid(), "SyntheticV1", "TEST",
        Open, Open.AddHours(6.5), Open.AddMinutes(end), TimeSpan.FromSeconds(45),
        Open.AddMinutes(end).AddSeconds(5), Open.AddMinutes(end).AddSeconds(8), Open.AddMinutes(end).AddSeconds(9),
        Enumerable.Range(0, end + 1).Select(i => Bar(i)).ToImmutableArray(), "{}", null);

    [Fact]
    public void OneReceiptRestoresCompleteHistoryWithDistinctStableIdentitiesAndOriginalTiming()
    {
        var batch = Batch();
        var book = DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(batch), batch);
        Assert.Equal(22, book.Observations.Length);
        Assert.Equal(22, book.Observations.Select(o => o.ObservationId).Distinct().Count());
        Assert.Equal(21, book.Observations.Count(o => !o.ObservedOnTime));
        Assert.All(book.Observations, o => Assert.Equal(batch.ReceivedUtc, o.ReceivedUtc));
        Assert.Equal(batch.ExpectedEndUtc, book.Observations[^1].EndUtc);
        var restored = JsonSerializer.Deserialize<DelphiLiveMinuteEvidenceBook>(JsonSerializer.Serialize(book))!;
        var repeated = DelphiLiveCollectorBatches.Apply(restored, batch);
        Assert.Equal(book.Observations.ToArray(), repeated.Observations.ToArray());
        Assert.Empty(repeated.ConflictingStartsUtc);
    }

    [Fact]
    public void ANewRequestDoesNotReplaceFirstArrivalOrRefreshExpiry()
    {
        var batch = Batch();
        var book = DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(batch), batch);
        var repeat = batch with { RequestId = Guid.NewGuid(), FetchStartedUtc = batch.FetchStartedUtc.AddSeconds(50),
            ReceivedUtc = batch.ReceivedUtc.AddSeconds(50), ValidatedUtc = batch.ValidatedUtc.AddSeconds(50) };
        var next = DelphiLiveCollectorBatches.Apply(book, repeat);
        Assert.Equal(book.Observations.ToArray(), next.Observations.ToArray());
        Assert.Null(DelphiLiveMinuteEvidence.AvailableAt(next, batch.ExpectedEndUtc, repeat.ValidatedUtc));
    }

    [Fact]
    public void MissingRowsRemainMissingAndFormingTargetDoesNotMatureDuringResponse()
    {
        var batch = Batch() with { Bars = [Bar(0), Bar(20), Bar(21)] };
        var book = DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(batch), batch);
        Assert.Equal(3, book.Observations.Length);
        Assert.Equal(DelphiLiveMinuteWindowStatus.MissingMinute,
            DelphiLiveMinuteWindows.BuildRecoveredContext(book, batch.ExpectedEndUtc, TimeSpan.FromMinutes(20), batch.ValidatedUtc).Status);
        var early = Batch() with { FetchStartedUtc = Open.AddMinutes(22).AddSeconds(-1) };
        var forming = DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(early), early);
        Assert.Equal(21, forming.Observations.Length);
        Assert.DoesNotContain(forming.Observations, o => o.EndUtc == early.ExpectedEndUtc);
    }

    [Fact]
    public void RevisedHistoricalRowLatchesWithoutOverwritingTheOriginal()
    {
        var batch = Batch();
        var book = DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(batch), batch);
        var revised = Batch(23) with { Bars = [Bar(0, 11), Bar(22)] };
        var blocked = DelphiLiveCollectorBatches.Apply(book, revised);
        Assert.Equal(Open, Assert.Single(blocked.ConflictingStartsUtc));
        Assert.Equal(10, blocked.Observations[0].Bar.Close);
        Assert.Equal(book.Observations.Length, blocked.Observations.Length);
        var restored = JsonSerializer.Deserialize<DelphiLiveMinuteEvidenceBook>(JsonSerializer.Serialize(blocked))!;
        Assert.Equal(blocked.ConflictingStartsUtc.ToArray(), DelphiLiveCollectorBatches.Apply(restored, Batch(24)).ConflictingStartsUtc.ToArray());
    }

    [Fact]
    public void TransportFailureIsExplicitAndCannotContainAcceptedBars()
    {
        var batch = Batch();
        var empty = DelphiLiveCollectorBatches.Empty(batch);
        var failure = batch with { Bars = [], FailureCode = "Timeout" };
        Assert.Equal(empty, DelphiLiveCollectorBatches.Apply(empty, failure));
        Assert.Throws<ArgumentException>(() => DelphiLiveCollectorBatches.Validate(batch with { FailureCode = "Timeout" }));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    [InlineData("deadline")]
    [InlineData("future")]
    [InlineData("missing-id")]
    public void InvalidSourceEvidenceFailsBeforePersistence(string kind)
    {
        var batch = Batch();
        batch = kind switch
        {
            "duplicate" => batch with { Bars = [Bar(0), Bar(0)] },
            "invalid" => batch with { Bars = [Bar(0, 0)] },
            "deadline" => batch with { DeadlineOffset = TimeSpan.FromMinutes(1) },
            "future" => batch with { ReceivedUtc = batch.FetchStartedUtc.AddSeconds(-1) },
            _ => batch with { RequestId = Guid.Empty }
        };
        Assert.Throws<ArgumentException>(() => DelphiLiveCollectorBatches.Validate(batch));
    }

    [Fact]
    public void DifferentSourceSessionAndBackwardClockCannotJoinAnExistingBook()
    {
        var batch = Batch(); var book = DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(batch), batch);
        Assert.Throws<ArgumentException>(() => DelphiLiveCollectorBatches.Apply(book, batch with { SourceContract = "OtherV1" }));
        Assert.Throws<ArgumentException>(() => DelphiLiveCollectorBatches.Apply(book, batch with { SessionCloseUtc = batch.SessionCloseUtc.AddMinutes(-1) }));
        Assert.Throws<ArgumentException>(() => DelphiLiveCollectorBatches.Apply(book, Batch(21)));
    }

    [Fact]
    public void RawReceiptAndTypedValuesBothParticipateInRequestFingerprint()
    {
        var batch = Batch();
        var hash = DelphiLiveCollectorBatches.Hash(JsonSerializer.Serialize(batch));
        Assert.NotEqual(hash, DelphiLiveCollectorBatches.Hash(JsonSerializer.Serialize(batch with { RawResponseJson = "{\"different\":true}" })));
        Assert.NotEqual(hash, DelphiLiveCollectorBatches.Hash(JsonSerializer.Serialize(batch with { Bars = [Bar(0, 11)] })));
        Assert.ThrowsAny<JsonException>(() => DelphiLiveCollectorBatches.Validate(batch with { RawResponseJson = "invalid" }));
        Assert.Throws<ArgumentException>(() => DelphiLiveCollectorBatches.Validate(batch with { RawResponseJson = "[]" + new string(' ', 1048576) }));
    }

    [Fact]
    public async Task InvalidRepositoryCommandsAreRejectedWithoutConnecting()
    {
        var repository = new MarketCollectorRepository("intentionally-invalid-unused-configuration");
        await Assert.ThrowsAsync<ArgumentException>(() => repository.TryAcquireAsync("", Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.TryAcquireAsync("SyntheticV1", Guid.Empty));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.TryAcquireAsync("SyntheticV1", Guid.NewGuid(), 0));
        var lease = new MarketCollectorLease("Other", Guid.NewGuid(), 1, Open);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.AppendAsync(lease, Batch()));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.RenewAsync(lease with { FencingToken = 0 }));
    }
}
