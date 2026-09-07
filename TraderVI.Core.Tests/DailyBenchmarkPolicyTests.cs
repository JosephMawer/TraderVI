using Core.Indicators.Granville;
using Core.ML;
using Core.ML.Engine.Profit;
using Core.Runtime;
using Core.TMX;
using Core.Trader;
using Core.Trader.DelphiLive;
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

namespace TraderVI.Core.Tests;

public class DailyBenchmarkPolicyTests
{
    private static readonly DateTime Target = new(2026, 9, 4);
    private static ReviewedTsxSessionCalendar Calendar() => new(new("fixture", "synthetic calendar",
        new(2026, 1, 1), new(2026, 12, 23), [new(2026, 9, 3), new(2026, 9, 4), new(2026, 9, 8)]));
    private static List<DailyBar> Bars() => Enumerable.Range(0, 230).Select(i => new DailyBar
    {
        Date = Target.AddDays(i - 229), Open = 100 + i, High = 102 + i,
        Low = 99 + i, Close = 101 + i, Volume = 100000
    }).ToList();

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void WeekendAndHolidayUseFridayAndPreserveCompleteDataCalculations(int day)
    {
        var xiu = Bars();
        var spy = Bars();
        var snapshot = DailyBenchmarkPolicy.Prepare(new(2026, 9, day), Calendar(), xiu, spy);
        snapshot.Evidence.MarketSession.ShouldBe(Target);
        snapshot.Evidence.PolicyVersion.ShouldBe(DailyBenchmarkPolicy.Version);
        snapshot.ComputeRegime().ShouldBe(TradeDecisionEngine.ComputeRegime(xiu, spy));
        xiu[^1].Close = 1;
        snapshot.Xiu[^1].Close.ShouldBe(330);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("short")]
    [InlineData("stale")]
    [InlineData("invalid")]
    [InlineData("nonfinite")]
    [InlineData("duplicate")]
    public void UnusableSpyCannotBecomePositiveConfirmation(string fault)
    {
        var spy = Bars();
        switch (fault)
        {
            case "missing": spy.Clear(); break;
            case "short": spy = spy.TakeLast(199).ToList(); break;
            case "stale": spy.RemoveAt(spy.Count - 1); break;
            case "invalid": spy[^1].Low = spy[^1].High + 1; break;
            case "nonfinite": spy[^1].Close = float.NaN; break;
            case "duplicate": spy.Add(spy[^200]); break;
        }
        Should.Throw<InvalidDataException>(() => DailyBenchmarkPolicy.Prepare(new(2026, 9, 6), Calendar(), Bars(), spy));
    }

    [Fact]
    public void CalendarDetectsStaleSharedXiuEvenWhenStocksWouldMatchItsOldDate()
    {
        var xiu = Bars();
        xiu.RemoveAt(xiu.Count - 1);
        Should.Throw<InvalidDataException>(() => DailyBenchmarkPolicy.Prepare(new(2026, 9, 6), Calendar(), xiu, Bars()))
            .Message.ShouldContain("XIU confirmation stale");
    }

    [Fact]
    public void FutureRowsDoNotEnterTheBenchmarkWindow()
    {
        var spy = Bars();
        var before = DailyBenchmarkPolicy.Prepare(new(2026, 9, 6), Calendar(), Bars(), spy).ComputeRegime();
        spy.Add(new DailyBar { Date = new(2026, 9, 8), Close = float.NaN });
        DailyBenchmarkPolicy.Prepare(new(2026, 9, 6), Calendar(), Bars(), spy).ComputeRegime().ShouldBe(before);
    }

    [Fact]
    public void NewDecisionRequiresDatedModelsAndDoesNotChangeOldPolicyDispatch()
    {
        DailyBenchmarkPolicy.IsRequired("ADR-0056").ShouldBeFalse();
        DailyBenchmarkPolicy.IsRequired(DailyBenchmarkPolicy.DecisionRef).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => ReviewedProfitInputBinding.Resolve(Guid.NewGuid(), DailyBenchmarkPolicy.DecisionRef, null));
    }

    [Fact]
    public void ReportsRetainObservedBenchmarkDatesAndSourceIdentity()
    {
        var snapshot = DailyBenchmarkPolicy.Prepare(new(2026, 9, 6), Calendar(), Bars(), Bars());
        var reports = new DelphiReportBuilder { BenchmarkEvidence = snapshot.Evidence, Regime = snapshot.ComputeRegime() };
        reports.BuildDiagnostic().ShouldContain("DailyBars:SPY (Yahoo daily chart)");
        reports.BuildDiagnostic().ShouldContain("2026-09-04");
        reports.BuildSummary().ShouldContain("XIU and SPY confirmation uses observed data through 2026-09-04");
    }

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
