#nullable enable
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxObservationContextTests
{
    private static readonly DateTime Start = new(2026, 10, 14, 14, 20, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddMinutes(5);
    private static readonly DateTime Fetch = End.AddMinutes(2).AddSeconds(25);

    [Fact]
    public async Task LiveContextUsesObservedTimeAndCannotSelectTheFollowingFormingBar()
    {
        using var handler = new Handler(); using var client = new TmxClient(handler, () => Fetch);
        var batch = await client.GetIntradayObservationBatchAsync("TEST", 5, Start, End);
        using var requestJson = JsonDocument.Parse(handler.Body);
        Assert.Equal(new DateTimeOffset(End.AddMinutes(2)).ToUnixTimeSeconds(),
            requestJson.RootElement.GetProperty("variables").GetProperty("endDateTime").GetInt64());
        Assert.Equal(1, handler.Calls);
        Assert.Equal(Start, batch.RequestedStartUtc); Assert.Equal(End, batch.RequestedEndUtc);
        Assert.Equal(End.AddMinutes(2), batch.ProviderWindowEndUtc);
        Assert.Equal(End, batch.ProviderLatestAlignedEventUtc);
        Assert.Equal(3, batch.ProviderReturnedPointCount);
        Assert.Equal(new[] { End.AddMinutes(1) }, batch.ExcludedSupplementaryTimestampsUtc);
        Assert.Equal(Start, batch.LatestCompletedEventUtc);
        Assert.True(batch.HasFormingBarAtReceipt);
        var request = new DelphiLiveMarketDataRequest(Guid.NewGuid(), "TEST", Start, End, End.AddMinutes(7), Fetch, 1);
        var receipt = TmxDelphiLiveMarketDataSource.FromBatch(request, batch);
        Assert.Equal("OperationalOnTime", receipt.Disposition);
        Assert.Equal(Start, receipt.ExactCompletedBar!.TimestampUtc);
        Assert.Equal(10m, receipt.ExactCompletedBar.Close);
        Assert.Equal(request, receipt.Request);
        Assert.Equal(batch.ProviderWindowEndUtc, receipt.ProviderWindowEndUtc);
        Assert.Equal(batch.ProviderLatestAlignedEventUtc, receipt.ProviderLatestAlignedEventUtc);
    }

    [Fact]
    public async Task DefaultDiagnosticKeepsItsAuthorizedEndBound()
    {
        using var handler = new Handler(); using var client = new TmxClient(handler, () => Fetch);
        var result = await client.GetIntradayTimeSeriesDiagnosticAsync("TEST", 5, Start, End);
        using var requestJson = JsonDocument.Parse(handler.Body);
        Assert.Equal(new DateTimeOffset(End).ToUnixTimeSeconds(),
            requestJson.RootElement.GetProperty("variables").GetProperty("endDateTime").GetInt64());
        Assert.Equal(End, result.Batch.ProviderWindowEndUtc);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 0)]
    [InlineData(20, 0)]
    [InlineData(-1, 0)]
    public void OnlyTheCurrentlyObservedIntervalCanExtend(int minutesAfterEnd, int expectedExtension)
    {
        Assert.Equal(End.AddMinutes(expectedExtension), TmxClient.IntradayProviderEnd(5, End,
            End.AddMinutes(minutesAfterEnd).AddSeconds(25)));
    }

    [Fact]
    public void MinuteHistoricalPreOpenAndClosingBoundsRemainUnchanged()
    {
        Assert.Equal(End, TmxClient.IntradayProviderEnd(1, End, Fetch));
        Assert.Null(TmxClient.IntradayProviderEnd(5, null, Fetch));
        Assert.Equal(End, TmxClient.IntradayProviderEnd(5, End, Fetch.AddDays(1)));
        DateTime beforeOpen = new(2026, 10, 14, 13, 25, 0, DateTimeKind.Utc);
        Assert.Equal(beforeOpen, TmxClient.IntradayProviderEnd(5, beforeOpen, beforeOpen.AddMinutes(2)));
        DateTime close = new(2026, 10, 14, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(close, TmxClient.IntradayProviderEnd(5, close, close.AddMinutes(2)));
        DateTime winterEnd = new(2026, 12, 1, 15, 25, 0, DateTimeKind.Utc);
        Assert.Equal(winterEnd.AddMinutes(2), TmxClient.IntradayProviderEnd(5, winterEnd, winterEnd.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => TmxClient.IntradayProviderEnd(5, End,
            DateTime.SpecifyKind(Fetch, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void OlderSourceCoverageIsDiagnosedWithoutSupplyingAnExactPrice()
    {
        var request = new DelphiLiveMarketDataRequest(Guid.NewGuid(), "TEST", Start, End, End.AddMinutes(7), Fetch, 1);
        var batch = new TmxIntradayBatch("TEST", 5, Start, End, Fetch, Fetch, 1, 1, Array.Empty<OhlcvBar>())
        { ProviderLatestAlignedEventUtc = Start.AddMinutes(-10), ProviderWindowEndUtc = End.AddMinutes(2) };
        var receipt = TmxDelphiLiveMarketDataSource.FromBatch(request, batch);
        Assert.Equal("StaleNoNewBar", receipt.Disposition);
        Assert.Null(receipt.ExactCompletedBar);
        Assert.Equal(Start.AddMinutes(-10), receipt.ProviderLatestAlignedEventUtc);
        var unavailable = TmxDelphiLiveMarketDataSource.FromBatch(request, batch with { ProviderLatestAlignedEventUtc = null });
        Assert.Equal("NoCompletedBar", unavailable.Disposition);
        Assert.Null(unavailable.ExactCompletedBar);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Body = "";
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"data":{"getTimeSeriesData":[
                {"dateTime":"2026-10-14 10:20:00 AM","open":10,"high":11,"low":9,"close":10,"volume":100},
                {"dateTime":"2026-10-14 10:25:00 AM","open":20,"high":21,"low":19,"close":20,"volume":200},
                {"dateTime":"2026-10-14 10:26:00 AM","open":20,"high":21,"low":19,"close":20,"volume":200}]}}
                """, Encoding.UTF8, "application/json") };
        }
    }
}
