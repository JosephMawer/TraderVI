using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public class DelphiLiveHistoricalReplayTests
{
    private static readonly DateOnly Date = new(2026, 9, 4);
    private static readonly DateOnly Prior = new(2026, 9, 3);
    private static readonly DateTime Open = new(2026, 9, 4, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Generated = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ReviewedTsxSessionCalendar Calendar = new(new("Test", "Fixture", Prior, Date, [Prior, Date]));

    [Fact]
    public void Replay_UsesOnlyNextMinuteOpenAfterDecision_AndPreservesCapitalLimits()
    {
        var report = Run();
        report.Frames.Count.ShouldBe(78);
        report.StartingCapital.ShouldBe(749.50m);
        report.Kind.ShouldBe(DelphiLiveHistoricalReplay.Kind);
        report.Trades.Where(t => t.Side == "Buy" && t.FilledUtc.HasValue).ShouldNotBeEmpty();
        foreach (var trade in report.Trades.Where(t => t.FilledUtc.HasValue))
        {
            trade.FilledUtc.Value.ShouldBeGreaterThan(trade.DecisionUtc);
            trade.RecordedUtc.ShouldBe(trade.FilledUtc.Value);
            trade.Price.ShouldBe(Source("ABC", true).MinuteBars.Single(b => b.StartUtc == trade.FilledUtc).Open);
            trade.Outcome.ShouldBe("Estimated fill");
        }
        report.Frames.ShouldAllBe(f => f.Cash >= 0 && f.Positions.Count <= 5);
        report.Trades.Count(t => t.Side == "Buy" && t.FilledUtc.HasValue).ShouldBeLessThanOrEqualTo(2);
        var first = report.Trades.First(t => t.FilledUtc.HasValue);
        report.Frames.Where(f => f.SimulatedUtc < first.FilledUtc).ShouldAllBe(f => f.Positions.Count == 0);
    }

    [Fact]
    public void ChangingAfternoonPrices_CannotChangeMorningFramesOrTrades()
    {
        var original = Run();
        var abc = Source("ABC", true);
        var noon = Open.AddHours(2.5);
        DelphiLiveReplayBar Change(DelphiLiveReplayBar b) => b.StartUtc < noon ? b : b with
        { Open = b.Open * 0.5m, High = b.High * 0.5m, Low = b.Low * 0.5m, Close = b.Close * 0.5m };
        var changed = Run(sources: [abc with { FiveMinuteBars = abc.FiveMinuteBars.Select(Change).ToArray(),
            MinuteBars = abc.MinuteBars.Select(Change).ToArray() }, Source("XIU", false)]);
        JsonSerializer.Serialize(original.Frames.Where(f => f.SimulatedUtc < noon)).ShouldBe(
            JsonSerializer.Serialize(changed.Frames.Where(f => f.SimulatedUtc < noon)));
        JsonSerializer.Serialize(original.Trades.Where(t => t.RecordedUtc < noon)).ShouldBe(
            JsonSerializer.Serialize(changed.Trades.Where(t => t.RecordedUtc < noon)));
    }

    [Fact]
    public void MissingMinuteExecutionEvidence_DoesNotInventTrades()
    {
        var report = Run(sources: [Source("ABC", true) with { MinuteBars = [] }, Source("XIU", false)]);
        report.Trades.ShouldAllBe(t => !t.FilledUtc.HasValue && t.Quantity == 0);
        report.Frames.ShouldAllBe(f => f.Cash == 749.50m && f.AccountValue == 749.50m && f.Positions.Count == 0);
        report.AvailableMinuteBars.ShouldBe(390);
    }

    [Fact]
    public void MissingBenchmarkBar_IsVisibleAndCannotCreateEntry()
    {
        var xiu = Source("XIU", false);
        var report = Run(sources: [Source("ABC", true), xiu with { FiveMinuteBars = [] }]);
        report.Frames.ShouldAllBe(f => f.Rows.Single().Confidence == "Missing exact bar");
        report.Trades.ShouldBeEmpty();
    }

    [Fact]
    public void PublishedButIneligiblePick_RemainsVisibleWithoutTrading()
    {
        var preview = Preview();
        preview = preview with { Picks = [preview.Picks[0] with { Lenses = [new("Continuation", 1, false, 1, "Daily gate", "{}")] }] };
        preview.Picks[0].ToSetup(preview.Run).ShouldBeNull();
        var report = Run(preview);
        report.Frames.ShouldAllBe(f => f.Rows.Single().State == "Daily gate blocked");
        report.Trades.ShouldBeEmpty();
    }

    [Fact]
    public void RunCreatedAfterMarketOpen_IsRejected()
    {
        var preview = Preview();
        Should.Throw<ArgumentException>(() => Run(preview with { Run = preview.Run with { CreatedUtc = Open.AddMinutes(1) } }));
    }

    [Fact]
    public void DuplicateOrFutureIntradayBars_AreRejected()
    {
        var abc = Source("ABC", true);
        Should.Throw<ArgumentException>(() => Run(sources: [abc with { FiveMinuteBars = abc.FiveMinuteBars.Append(abc.FiveMinuteBars[0]).ToArray() }, Source("XIU", false)]));
        Should.Throw<ArgumentException>(() => Run(sources: [abc with { FiveMinuteBars = abc.FiveMinuteBars.Append(abc.FiveMinuteBars[0] with { StartUtc = Open.AddHours(7) }).ToArray() }, Source("XIU", false)]));
    }

    private static DelphiLiveReplayReport Run(DelphiLiveWatchlistPreview preview = null, IReadOnlyList<DelphiLiveReplaySource> sources = null)
    {
        DelphiLiveTrueRangeRulerMeasurement Ruler(int n) => new(n, Prior, DelphiLiveScalarMeasurement.Available(0.04m));
        var baseline = new DelphiLiveFrozenBaseline(100m, [], [], new(Ruler(5), Ruler(10), Ruler(14), Ruler(20)));
        return DelphiLiveHistoricalReplay.Run(preview ?? Preview(), new Dictionary<string, DelphiLiveFrozenBaseline>
            { ["ABC"] = baseline, ["XIU"] = baseline }, sources ?? [Source("ABC", true), Source("XIU", false)], Calendar, 749.50m, Generated);
    }
    private static DelphiLiveWatchlistPreview Preview() => new(
        new(Guid.NewGuid(), Guid.NewGuid(), "OfficialPaper", "Valid", Date, Prior, Open.AddHours(-8), Open.AddHours(-8)),
        [new("ABC", Guid.NewGuid(), 0.5m, 100m, [new("Continuation", 1, true, 0.5m, null, "{}")])]);

    private static DelphiLiveReplaySource Source(string symbol, bool rising)
    {
        DelphiLiveReplayBar Bar(int index, int interval)
        {
            decimal open = 100m + (rising ? index * interval / 5m : 0m);
            decimal close = open + (rising ? interval / 5m : 0m);
            return new(Open.AddMinutes(index * interval), open, close + 0.01m, open - 0.01m, close, 100);
        }
        return new(symbol, Generated, Enumerable.Range(0, 78).Select(i => Bar(i, 5)).ToArray(),
            Enumerable.Range(0, 390).Select(i => Bar(i, 1)).ToArray());
    }
}
