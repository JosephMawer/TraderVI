#nullable enable
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxIntradayNormalizationTests
{
    private static readonly DateTime Open = new(2026, 10, 13, 13, 30, 0, DateTimeKind.Utc);
    private static OhlcvBar Bar(int minute) => new(Open.AddMinutes(minute), 10, 11, 9, 10, 100);

    [Fact]
    public void TrailingSupplementDoesNotDiscardIndependentAlignedBarsOrBecomeEvidence()
    {
        var (bars, excluded) = TmxClient.NormalizeIntradaySource([Bar(0), Bar(5), Bar(9)], 5, Open, Open.AddMinutes(10));
        bars.ShouldBe([Bar(0), Bar(5)]);
        excluded.ShouldBe([Open.AddMinutes(9)]);
        // The original strict guard remains strict for callers validating bars.
        Should.Throw<InvalidOperationException>(() => TmxClient.ValidateIntradayResponse(
            [Bar(0), Bar(5), Bar(9)], 5, Open, Open.AddMinutes(10)));
    }

    [Fact]
    public void InteriorOffGridRowsStillRejectTheResponse()
    {
        Should.Throw<InvalidOperationException>(() => TmxClient.NormalizeIntradaySource(
            [Bar(0), Bar(5), Bar(4)], 5, Open, Open.AddMinutes(15)));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(18)]
    public void TrailingSupplementAfterMissingBucketsDoesNotInventThoseBars(int minute)
    {
        var (bars, excluded) = TmxClient.NormalizeIntradaySource(
            [Bar(0), Bar(5), Bar(minute)], 5, Open, Open.AddMinutes(20));
        bars.ShouldBe([Bar(0), Bar(5)]);
        excluded.ShouldBe([Open.AddMinutes(minute)]);
        bars.ShouldNotContain(bar => bar.TimestampUtc == Open.AddMinutes(10));
        var request = new DelphiLiveMarketDataRequest(Guid.NewGuid(), "TEST", Open.AddMinutes(10),
            Open.AddMinutes(15), Open.AddMinutes(17), Open.AddMinutes(16), 1);
        var receipt = TmxDelphiLiveMarketDataSource.FromBatch(request, new TmxIntradayBatch(
            "TEST", 5, request.BarStartUtc, request.BarEndUtc, Open.AddMinutes(16), Open.AddMinutes(16).AddSeconds(1),
            1, 1, bars.Where(bar => bar.TimestampUtc >= request.BarStartUtc && bar.TimestampUtc <= request.BarEndUtc).ToArray())
            { ProviderWindowStartUtc = Open, ExcludedSupplementaryTimestampsUtc = excluded });
        receipt.Disposition.ShouldBe("NoCompletedBar");
        receipt.ExactCompletedBar.ShouldBeNull();
    }

    [Fact]
    public void EntirelyOffGridResponseSuppliesNoPriceInsteadOfInventingAnAlignedBar()
    {
        var (bars, excluded) = TmxClient.NormalizeIntradaySource([Bar(2), Bar(4)], 5, Open, Open.AddMinutes(5));
        bars.ShouldBeEmpty(); excluded.ShouldBe([Open.AddMinutes(2), Open.AddMinutes(4)]);
    }

    [Fact]
    public void InvalidSupplementValuesAndDuplicateTimestampsCannotBeHidden()
    {
        var invalid = Bar(9) with { High = 8 };
        Should.Throw<InvalidOperationException>(() => TmxClient.NormalizeIntradaySource(
            [Bar(5), invalid], 5, Open, Open.AddMinutes(10)));
        Should.Throw<InvalidOperationException>(() => TmxClient.NormalizeIntradaySource(
            [Bar(5), Bar(9), Bar(9)], 5, Open, Open.AddMinutes(10)));
        Should.Throw<InvalidOperationException>(() => TmxClient.NormalizeIntradaySource(
            [Bar(5), Bar(9) with { Volume = -1 }], 5, Open, Open.AddMinutes(10)));
    }

    [Fact]
    public void BoundsUtcPrecisionAndDailyFallbackGuardsRemainRequired()
    {
        Should.Throw<InvalidOperationException>(() => TmxClient.NormalizeIntradaySource(
            [Bar(0), Bar(20)], 5, Open, Open.AddMinutes(5)));
        Should.Throw<InvalidOperationException>(() => TmxClient.NormalizeIntradaySource(
            [Bar(0) with { TimestampUtc = DateTime.SpecifyKind(Open, DateTimeKind.Unspecified) }], 5, Open, Open.AddMinutes(5)));
        Should.Throw<InvalidOperationException>(() => TmxClient.NormalizeIntradaySource(
            [Bar(5), Bar(9) with { TimestampUtc = Open.AddMinutes(9).AddSeconds(1) }], 5, Open, Open.AddMinutes(10)));
        Should.Throw<InvalidOperationException>(() => TmxClient.NormalizeIntradaySource(
            [Bar(390), Bar(390) with { TimestampUtc = Open.AddDays(1).AddMinutes(390) }], 5, Open, Open.AddDays(2)));
    }

    [Fact]
    public void SupplementsAreSeparatedForEachSessionAndOneMinuteRequestsAreUnchanged()
    {
        var (bars, excluded) = TmxClient.NormalizeIntradaySource(
            [Bar(5), Bar(9), Bar(5) with { TimestampUtc = Open.AddDays(1).AddMinutes(5) }], 5, Open, Open.AddDays(1).AddMinutes(10));
        bars.Count.ShouldBe(2); excluded.ShouldBe([Open.AddMinutes(9)]);
        TmxClient.NormalizeIntradaySource([Bar(2), Bar(4)], 1, Open, Open.AddMinutes(5)).Bars.ShouldBe([Bar(2), Bar(4)]);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void ProviderContextStartsAtSessionOpenWithoutChangingMinuteRequests(int interval)
    {
        TmxClient.IntradayProviderStart(interval, Open.AddMinutes(80)).ShouldBe(Open);
        TmxClient.IntradayProviderStart(1, Open.AddMinutes(80)).ShouldBe(Open.AddMinutes(80));
        var winterOpen = new DateTime(2026, 12, 1, 14, 30, 0, DateTimeKind.Utc);
        TmxClient.IntradayProviderStart(interval, winterOpen.AddMinutes(80)).ShouldBe(winterOpen);
    }

    [Fact]
    public async Task TransportUsesSessionContextButReturnsOnlyTheLogicalWindowAndKeepsSourceMetadata()
    {
        using var handler = new Handler(); using var client = new TmxClient(handler);
        var batch = await client.GetIntradayTimeSeriesBatchAsync("XIU", 5, Open.AddMinutes(5), Open.AddMinutes(10));
        handler.Body.ShouldContain("\"startDateTime\":" + new DateTimeOffset(Open).ToUnixTimeSeconds());
        batch.RequestedStartUtc.ShouldBe(Open.AddMinutes(5)); batch.ProviderWindowStartUtc.ShouldBe(Open);
        batch.Bars.Single().ShouldBe(Bar(5)); batch.ExcludedSupplementaryTimestampsUtc.ShouldBe([Open.AddMinutes(9)]);
        var request = new DelphiLiveMarketDataRequest(Guid.NewGuid(), "XIU", Open.AddMinutes(5), Open.AddMinutes(10),
            batch.ReceivedUtc.AddMinutes(1), batch.FetchStartedUtc, 1);
        var receipt = TmxDelphiLiveMarketDataSource.FromBatch(request, batch);
        receipt.Disposition.ShouldBe("OperationalOnTime"); receipt.ExactCompletedBar.ShouldBe(Bar(5));
        receipt.ProviderWindowStartUtc.ShouldBe(Open); receipt.ExcludedSupplementaryTimestampsUtc.ShouldBe([Open.AddMinutes(9)]);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"data":{"getTimeSeriesData":[
                {"dateTime":"2026-10-13T09:30:00-04:00","open":10,"high":11,"low":9,"close":10,"volume":100},
                {"dateTime":"2026-10-13T09:35:00-04:00","open":10,"high":11,"low":9,"close":10,"volume":100},
                {"dateTime":"2026-10-13T09:39:00-04:00","open":10,"high":11,"low":9,"close":10,"volume":100}]}}
                """, Encoding.UTF8, "application/json") };
        }
    }
}
