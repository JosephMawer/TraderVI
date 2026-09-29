#nullable enable
using Core.TMX;
using Sandbox.Probes;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxQuoteTimingDiagnosticTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-09-29T10:00:00")]
    [InlineData("not a time Z")]
    public void MissingOrAmbiguousTimezoneDoesNotBecomeFresh(string? value) => Assert.Null(TmxQuoteTimingProbe.ReadExplicitUtc(value));

    [Theory]
    [InlineData("2026-09-29T10:00:00-04:00")]
    [InlineData("2026-09-29T14:00:00Z")]
    public void PreservesExplicitSourceTime(string value) =>
        Assert.Equal(new DateTime(2026, 9, 29, 14, 0, 0, DateTimeKind.Utc), TmxQuoteTimingProbe.ReadExplicitUtc(value));

    [Fact]
    public async Task SourceTimesRemainSeparateFromReceiptAndUntimedQuote()
    {
        using var client = new TmxClient(new Handler("""
            {"data":{"quotes":[{"symbol":"TEST","price":10,"bid":9,"ask":11}],
            "delayed":[{"bid":8,"ask":12,"lasttradedatetime":"2026-09-29T13:45:00Z"}],
            "trades":[{"price":10,"volume":100,"datetime":"2026-09-29T14:00:00Z","exchangeCode":"TSX"}]}}
            """));
        var sample = await client.GetQuoteTimingDiagnosticAsync(" test ");
        Assert.Equal("TEST", Assert.Single(sample.Quotes).symbol);
        Assert.Equal("2026-09-29T13:45:00Z", Assert.Single(sample.DelayedQuotes).lasttradedatetime);
        Assert.Equal("2026-09-29T14:00:00Z", Assert.Single(sample.RecentTrades).datetime);
        Assert.True(sample.ReceivedUtc >= sample.FetchStartedUtc);
        Assert.Equal(1, sample.AttemptCount);
        Assert.DoesNotContain("TEST", sample.ToString());
    }

    [Theory]
    [InlineData("""{"data":{"quotes":[{"symbol":"OTHER"}],"delayed":[],"trades":[]}}""")]
    [InlineData("""{"data":{"quotes":[],"delayed":null,"trades":[]}}""")]
    [InlineData("""{"data":{"quotes":[],"delayed":[],"trades":[{},{}]}}""")]
    [InlineData("""{"errors":[{"message":"Unavailable"}],"data":{"quotes":[],"delayed":[],"trades":[]}}""")]
    public async Task RejectsWrongOwnershipMissingDataOversizedOrPartialResponses(string json)
    {
        using var client = new TmxClient(new Handler(json));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetQuoteTimingDiagnosticAsync("TEST"));
    }

    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("getDelayedQuotesFromQMBySymbols", body);
            Assert.Contains("limit: 1", body);
            Assert.Contains("TEST", body);
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
