#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using System;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxAiRiskPriceSourceTests
{
    private static readonly DateOnly Date = new(2026, 9, 28);
    private static readonly DateTime Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private readonly Clock clock = new(Open.AddMinutes(10).AddSeconds(10));
    private readonly ITsxSessionCalendar calendar = new ReviewedTsxSessionCalendar(new("test", "synthetic", Date, Date, [Date]));
    private static OhlcvBar Bar(int minute) => new(Open.AddMinutes(minute), 10, 10, 10, 10, 100);
    private TmxIntradayBatch Batch(string symbol, DateTime start, DateTime end, params OhlcvBar[] bars) =>
        new(symbol, 1, start, end, clock.Now, clock.Now, 1, 1, bars);
    private TmxAiRiskPriceSource Source(Func<string, DateTime, DateTime, CancellationToken, Task<TmxIntradayBatch>> fetch) =>
        new(fetch, calendar, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), clock);

    [Fact]
    public async Task KeepsActualCompletedBarAndSeparateReceiptTimeAndExpiresAtUse()
    {
        var source = Source((s, a, b, _) => Task.FromResult(Batch(s, a, b, Bar(9), Bar(10))));
        var read = await source.ReadAsync(Date, [" test "]);
        var result = Assert.Single(read.Results);
        Assert.Equal(AiRiskPriceStatus.Fresh, result.Status);
        Assert.Equal(Open.AddMinutes(9), result.Evidence!.Bar.TimestampUtc);
        Assert.Equal(Open.AddMinutes(10), result.Evidence.CompletedUtc);
        Assert.Equal(clock.Now, result.Evidence.ReceivedUtc);
        Assert.Equal(Open.AddMinutes(12), result.Evidence.PriceExpiresUtc);
        Assert.True(read.IsCompleteAndFreshAt(clock.Now));
        Assert.False(read.IsCompleteAndFreshAt(clock.Now.AddSeconds(31)));
        Assert.False(read.IsCompleteAndFreshAt(clock.Now.AddSeconds(-1)));
    }

    [Fact]
    public async Task RepeatedOldPriceDoesNotBecomeFreshThroughANewReceipt()
    {
        var source = Source((s, a, b, _) => Task.FromResult(Batch(s, a, b, Bar(8))));
        var first = await source.ReadAsync(Date, ["TEST"]);
        Assert.True(first.IsCompleteAndFreshAt(clock.Now));
        clock.Now = Open.AddMinutes(11).AddSeconds(10);
        var repeat = await source.ReadAsync(Date, ["TEST"]);
        Assert.Equal(first.Results[0].Evidence!.PriceExpiresUtc, repeat.Results[0].Evidence!.PriceExpiresUtc);
        Assert.Equal(AiRiskPriceStatus.Stale, repeat.Results[0].Status);
        Assert.False(repeat.IsCompleteAndFreshAt(clock.Now));
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("interval")]
    [InlineData("window")]
    [InlineData("future")]
    [InlineData("cached")]
    [InlineData("unspecified_window")]
    [InlineData("duplicate")]
    [InlineData("bad_latest")]
    public async Task RejectsMismatchedOrInvalidEvidenceWithoutOlderFallback(string fault)
    {
        var source = Source((s, a, b, _) =>
        {
            var batch = Batch(s, a, b, Bar(8), Bar(9));
            batch = fault switch
            {
                "symbol" => batch with { Symbol = "OTHER" },
                "interval" => batch with { IntervalMinutes = 5 },
                "window" => batch with { RequestedEndUtc = b.AddMinutes(-1) },
                "future" => batch with { ReceivedUtc = clock.Now.AddSeconds(1) },
                "cached" => batch with { FetchStartedUtc = clock.Now.AddSeconds(-1) },
                "unspecified_window" => batch with { RequestedStartUtc = DateTime.SpecifyKind(a, DateTimeKind.Unspecified) },
                "duplicate" => batch with { Bars = [Bar(9), Bar(9)] },
                _ => batch with { Bars = [Bar(8), Bar(9) with { Close = 0 }] }
            };
            return Task.FromResult(batch);
        });
        var read = await source.ReadAsync(Date, ["TEST"]);
        Assert.Equal(AiRiskPriceStatus.InvalidResponse, read.Results[0].Status);
        Assert.Null(read.Results[0].Evidence);
        Assert.False(read.IsCompleteAndFreshAt(clock.Now));
    }

    [Fact]
    public async Task FailedRefreshNeverReusesPreviousSuccessAndDoesNotExposeProviderDetails()
    {
        bool fail = false;
        var source = Source((s, a, b, _) => fail
            ? Task.FromException<TmxIntradayBatch>(new HttpRequestException("private provider details"))
            : Task.FromResult(Batch(s, a, b, Bar(9))));
        Assert.True((await source.ReadAsync(Date, ["TEST"])).IsCompleteAndFreshAt(clock.Now));
        fail = true;
        var read = await source.ReadAsync(Date, ["TEST"]);
        Assert.Equal(AiRiskPriceStatus.SourceUnavailable, read.Results[0].Status);
        Assert.Null(read.Results[0].Evidence);
        Assert.DoesNotContain("private provider details", read.Results[0].ToString());
        Assert.False(read.IsCompleteAndFreshAt(clock.Now));
    }

    [Fact]
    public async Task WholeReadRechecksEarlierPricesAfterSlowLaterSymbol()
    {
        var source = Source((s, a, b, _) =>
        {
            if (s == "B") clock.Now = clock.Now.AddSeconds(31);
            return Task.FromResult(Batch(s, a, b, Bar(9)));
        });
        var read = await source.ReadAsync(Date, ["A", "B"]);
        Assert.Equal(AiRiskPriceStatus.Stale, read.Results[0].Status);
        Assert.Equal(AiRiskPriceStatus.Fresh, read.Results[1].Status);
        Assert.False(read.IsCompleteAndFreshAt(clock.Now));
    }

    [Fact]
    public async Task MissingOrFormingOnlySymbolMakesWholeReadIncomplete()
    {
        var source = Source((s, a, b, _) => Task.FromResult(Batch(s, a, b, s == "A" ? Bar(9) : Bar(10))));
        var read = await source.ReadAsync(Date, ["A", "B"]);
        Assert.Equal(AiRiskPriceStatus.MissingCompletedPrice, read.Results[1].Status);
        Assert.False(read.IsCompleteAndFreshAt(clock.Now));
    }

    [Fact]
    public async Task RejectsInvalidRequestsOutsideSessionAndCancellationWithoutFetching()
    {
        int calls = 0;
        var source = Source((s, a, b, _) => { calls++; return Task.FromResult(Batch(s, a, b, Bar(9))); });
        await Assert.ThrowsAsync<ArgumentException>(() => source.ReadAsync(Date, []));
        await Assert.ThrowsAsync<ArgumentException>(() => source.ReadAsync(Date, ["TEST", " test "]));
        await Assert.ThrowsAsync<OperationCanceledException>(() => source.ReadAsync(Date, ["TEST"], new CancellationToken(true)));
        clock.Now = Open.AddHours(7);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(Date, ["TEST"]));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CallerCancellationDuringReadPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var source = Source((s, a, b, ct) => { cancellation.Cancel(); return Task.FromResult(Batch(s, a, b, Bar(9))); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(Date, ["TEST"], cancellation.Token));
    }

    [Fact]
    public async Task RequestDeadlineReturnsUnavailableEvenIfTransportDoesNotFinish()
    {
        var pending = new TaskCompletionSource<TmxIntradayBatch>();
        var source = new TmxAiRiskPriceSource((_, _, _, _) => pending.Task, calendar, TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10), clock);
        try
        {
            var read = await source.ReadAsync(Date, ["TEST"]);
            Assert.Equal(AiRiskPriceStatus.SourceUnavailable, read.Results[0].Status);
            Assert.False(read.IsCompleteAndFreshAt(clock.Now));
        }
        finally { pending.TrySetCanceled(); }
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
