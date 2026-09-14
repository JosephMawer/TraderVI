using Core.TMX;
using GraphQL.Client.Http;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class TmxHistoricalTransportTests
{
    [Fact]
    public async Task HistoricalReadRecoversFromBadGatewayWithoutChangingRequest()
    {
        var handler = new SyntheticHandler(attempt => attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : Json("{\"data\":{\"getTimeSeriesData\":[]}}"));
        using var client = new TmxClient(handler);
        var bars = await client.GetHistoricalTimeSeriesAsync("TEST", "day", "2026-09-11", "2026-09-11");
        Assert.Empty(bars);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0], handler.Requests[1]);
        Assert.Contains("getTimeSeriesData", handler.Requests[0]);
        Assert.Contains("2026-09-11", handler.Requests[0]);
    }

    [Fact]
    public async Task PersistentServerFailureStopsAfterThreeReads()
    {
        var handler = new SyntheticHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var client = new TmxClient(handler);
        await Assert.ThrowsAsync<GraphQLHttpRequestException>(() =>
            client.GetHistoricalTimeSeriesAsync("TEST", "day", "2026-09-11", "2026-09-11"));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task BadRequestIsNotRetried()
    {
        var handler = new SyntheticHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("synthetic bad request", System.Text.Encoding.UTF8, "text/plain") });
        using var client = new TmxClient(handler);
        await Assert.ThrowsAsync<GraphQLHttpRequestException>(() =>
            client.GetHistoricalTimeSeriesAsync("TEST", "day", "2026-09-11", "2026-09-11"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GraphQlErrorsRemainFailuresWithoutRetry()
    {
        var handler = new SyntheticHandler(_ => Json("{\"errors\":[{\"message\":\"synthetic query error\"}]}"));
        using var client = new TmxClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetHistoricalTimeSeriesAsync("TEST", "day", "2026-09-11", "2026-09-11"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationStartsNoRead()
    {
        var handler = new SyntheticHandler(_ => Json("{\"data\":{\"getTimeSeriesData\":[]}}"));
        using var client = new TmxClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetHistoricalTimeSeriesAsync(
            "TEST", "day", "2026-09-11", "2026-09-11", cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class SyntheticHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(token));
            return response(Requests.Count);
        }
    }
}
