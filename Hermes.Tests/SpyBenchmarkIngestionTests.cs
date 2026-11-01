using Core.Indicators.Granville;
using Core.ML;
using Core.Runtime;
using Core.TMX;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Hermes.Tests;

public class SpyBenchmarkIngestionTests
{
    private static readonly DateTime Target = new(2026, 9, 4);
    private static List<DailyBar> Bars() => Enumerable.Range(0, 230).Select(i => new DailyBar
    {
        Date = Target.AddDays(i - 229), Open = 100 + i, High = 102 + i,
        Low = 99 + i, Close = 101 + i, Volume = 100000
    }).ToList();

    [Fact]
    public void IngestionPreservesStoredRowsAndDropsOutOfRangeObservations()
    {
        var existing = Bars();
        var last = existing[^1];
        var response = new[]
        {
            new UsIndexBar("SPY", Target, 1, 3, 1, 2, 10),
            new UsIndexBar("SPY", Target.AddDays(1), 2, 4, 1, 3, 10),
            new UsIndexBar("SPY", Target.AddDays(2), 3, 5, 1, 4, 10)
        };
        var selected = SpyBenchmarkIngestion.SelectMissingBars(existing, response, Target, Target.AddDays(1));
        selected.Count.ShouldBe(1);
        selected[0].Date.ShouldBe(Target.AddDays(1));
        last.Close.ShouldBe(330);
    }

    [Theory]
    [InlineData("wrong-symbol")]
    [InlineData("invalid")]
    [InlineData("duplicate")]
    public void BadCollectedObservationsCannotBeStored(string fault)
    {
        var bar = new UsIndexBar(fault == "wrong-symbol" ? "^GSPC" : "SPY", Target, 10, 12, 9,
            fault == "invalid" ? double.PositiveInfinity : 11, 100);
        var response = fault == "duplicate" ? new[] { bar, bar } : new[] { bar };
        Should.Throw<InvalidDataException>(() => SpyBenchmarkIngestion.SelectMissingBars([], response, Target, Target));
    }

    [Theory]
    [InlineData("SPY", true)]
    [InlineData("^GSPC", false)]
    public async Task YahooResponseMustAttestToActualSpy(string reported, bool succeeds)
    {
        string json = "{\"chart\":{\"result\":[{\"meta\":{\"symbol\":\"" + reported +
            "\"},\"timestamp\":[1788537600],\"indicators\":{\"quote\":[{\"open\":[100],\"high\":[102],\"low\":[99],\"close\":[101],\"volume\":[1000]}]}}],\"error\":null}}";
        using var http = new HttpClient(new FixtureHandler(json));
        using var source = new YahooChartUsIndexDataSource(http);
        if (succeeds)
            (await source.GetDailyBarsAsync("SPY", Target, Target)).Single().Symbol.ShouldBe("SPY");
        else
            await Should.ThrowAsync<InvalidOperationException>(() => source.GetDailyBarsAsync("SPY", Target, Target));
    }

    private sealed class FixtureHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
