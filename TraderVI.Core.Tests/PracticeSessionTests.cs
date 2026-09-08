#nullable enable
using Core.Calibration;
using Core.Db;
using Core.ML;
using Core.ML.Engine.Profit;
using Core.Practice;
using Core.Runtime;
using Core.TMX.Models.Domain;
using Core.Trader;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class PracticeSessionTests
{
    private static readonly DateOnly First = new(2026, 8, 10);
    private static readonly DateOnly Last = new(2026, 9, 4);
    private static DateTime At(DateOnly day, int hour, int minute) => ReviewedTsxSessionCalendar.At(day, new(hour, minute));

    internal static PracticeDataset Fixture(Func<OhlcvBar, OhlcvBar?>? transform = null, bool noBars = false)
    {
        var dates = Enumerable.Range(0, 33).Select(i => new DateOnly(2026, 8, 3).AddDays(i))
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToArray();
        var batches = new List<TmxIntradayBatch>();
        foreach (var day in dates.Where(d => d >= First))
            foreach (int interval in new[] { 5, 15 })
            {
                var bars = Enumerable.Range(0, 390 / interval).Select(i =>
                {
                    decimal price = 10m + (day.DayNumber - First.DayNumber) * 0.002m + i * 0.002m;
                    return new OhlcvBar(At(day, 9, 30).AddMinutes(i * interval), price, price + 0.01m, price - 0.01m, price + 0.005m, 100_000);
                }).Select(b => transform is null ? b : transform(b)).Where(b => b is not null).Cast<OhlcvBar>().ToArray();
                batches.Add(new("ABC", interval, At(day, 9, 30), At(day, 16, 0), At(Last, 17, 0), At(Last, 17, 1), 1, 1, bars));
            }
        var strategy = new StrategyVersionInfo { VersionId = new("10000000-0000-0000-0000-000000000001"), DecisionRef = "ADR-0058" };
        var inputs = new DelphiHistoricalInputs { Strategy = strategy, Models = new(strategy.VersionId,
            new("20000000-0000-0000-0000-000000000001"), ProfitFeatureInputs.Contract, "Fixture", "Fixture", At(Last, 17, 0), "Synthetic", []) };
        return new(new PracticeDatasetDocument
        {
            Calendar = new("Synthetic reviewed sessions", "Fixture only", dates[0], dates[^1], dates), Daily = inputs,
            Provenance = new("Synthetic unit fixture", At(Last, 17, 0), "Synthetic", "Synthetic", "No market calls"),
            CoreAssemblySha256 = "SyntheticTestIdentity", ShadowPolicyId = new("30000000-0000-0000-0000-000000000001"),
            Intraday = noBars ? [] : batches
        }, "StableFixtureIdentity");
    }
    private static Task<DelphiWorkflowRunResult> Daily(DateOnly day, CancellationToken ct) => Task.FromResult(new DelphiWorkflowRunResult(
        true, "Synthetic daily evaluator", CalibrationRunPurpose.ExploratoryReplay, day.ToDateTime(TimeOnly.MinValue),
        day.AddDays(-1).ToDateTime(TimeOnly.MinValue), At(day, 9, 0), At(day, 9, 0), 1, 1, null, null, null)
        { EvaluatedPicks = [new("Continuation", "ABC", 1, true, 10m), new("Breakout", "ABC", 1, true, 10m)] });
    private static async Task<List<PracticeFrame>> Run(PracticeSession session)
    {
        var frames = new List<PracticeFrame>();
        while (!session.Completed) frames.Add(await session.StepAsync());
        return frames;
    }

    [Fact]
    public async Task TwentySessionsUseFourIndependentBalancesAndCarryHoldingsWithoutWarmupTrades()
    {
        var session = new PracticeSession(Fixture(), Daily);
        session.Ledger.Portfolios.Count.ShouldBe(4);
        session.Ledger.Portfolios.ShouldAllBe(p => p.CashBalance == 1000m);
        var frames = await Run(session);
        session.DailyResults.Count.ShouldBe(20);
        session.Ledger.Sessions.Count.ShouldBe(80);
        session.Ledger.Fills.ShouldNotBeEmpty();
        session.Ledger.Fills.ShouldAllBe(f => f.FillUtc >= At(First, 9, 30) && f.ObservedUtc >= f.FillUtc.AddMinutes(7));
        frames.SelectMany(f => f.Accounts).ShouldAllBe(p => p.Cash >= 0m && p.Positions.Count <= (p.Code.EndsWith("3") ? 3 : 5));
        frames.First(f => f.Session == First.AddDays(1)).Accounts.ShouldAllBe(p => p.Positions.Count > 0 && p.Cash < 1000m);
        foreach (var p in session.Ledger.Portfolios)
        {
            var fills = session.Ledger.Fills.Where(f => f.PortfolioId == p.PortfolioId);
            decimal expected = 1000m + fills.Sum(f => f.Shares * f.AdjustedPrice * (f.Side == "Sell" ? 1m : -1m));
            p.CashBalance.ShouldBe(expected, 0.00001m);
        }
    }

    [Fact]
    public async Task AllMissingDataStillVisitsEverySessionAndKeepsAllAccountsCashOnly()
    {
        var session = new PracticeSession(Fixture(noBars: true)); // real daily input validation, no fake evaluator
        var frames = await Run(session);
        session.DailyResults.Count.ShouldBe(20);
        session.DailyResults.ShouldAllBe(d => !d.Published && d.Reason.Contains("unavailable"));
        session.Ledger.Sessions.Count.ShouldBe(80);
        session.Ledger.Fills.ShouldBeEmpty();
        frames.SelectMany(f => f.Accounts).ShouldAllBe(a => a.Cash == 1000m && a.NetAssetValue == 1000m);
    }

    [Fact]
    public async Task MissingExactFillBarCannotBeReplacedByAnotherBar()
    {
        var session = new PracticeSession(Fixture(b => b.TimestampUtc == At(First, 9, 55) ? null : b), Daily);
        await Run(session);
        session.Ledger.Fills.ShouldAllBe(f => f.FillUtc != At(First, 9, 55));
        session.Ledger.Events.ShouldContain(e => e.Kind == "OrderCancelled" && JsonSerializer.Serialize(e.Details).Contains("StaleBuyRequiresRequalification"));
        session.Ledger.Fills.ShouldContain(f => f.FillUtc > At(First, 10, 0));
    }

    [Fact]
    public async Task MissingHeldMarksRemainUnknownThenRecoverWithoutInventingData()
    {
        var session = new PracticeSession(Fixture(b => b.TimestampUtc >= At(First, 11, 0) && b.TimestampUtc < At(First, 12, 0) ? null : b), Daily);
        var frames = await Run(session);
        frames.Single(f => f.SimulatedUtc == At(First, 11, 30)).Accounts.ShouldAllBe(a => a.NetAssetValue == null && a.Return == null);
        frames.Single(f => f.SimulatedUtc == At(First, 12, 15)).Accounts.ShouldAllBe(a => a.NetAssetValue.HasValue);
        session.Ledger.Sessions.Count.ShouldBe(80);
    }

    [Fact]
    public async Task FuturePricesCannotChangeEarlierFramesAndRerunsAreDeterministic()
    {
        var before = await Run(new(Fixture(), Daily));
        var replay = await Run(new(Fixture(), Daily));
        JsonSerializer.Serialize(before).ShouldBe(JsonSerializer.Serialize(replay));
        var cutoff = At(First, 12, 0);
        var after = await Run(new(Fixture(b => b.TimestampUtc >= cutoff ? b with
            { Open = b.Open / 2, High = b.High / 2, Low = b.Low / 2, Close = b.Close / 2 } : b), Daily));
        JsonSerializer.Serialize(before.Where(f => f.SimulatedUtc < cutoff)).ShouldBe(JsonSerializer.Serialize(after.Where(f => f.SimulatedUtc < cutoff)));
    }

    [Fact]
    public async Task MissingDailyPublicationStillRunsProtectionForExistingHoldings()
    {
        Task<DelphiWorkflowRunResult> Evaluate(DateOnly day, CancellationToken ct) => day == First ? Daily(day, ct) :
            Task.FromResult(DelphiWorkflowRunResult.Failed(new(CalibrationRunPurpose.ExploratoryReplay), At(day, 9, 0),
                day.ToDateTime(TimeOnly.MinValue), "Missing daily inputs") with { CompletedUtc = At(day, 9, 0) });
        var session = new PracticeSession(Fixture(b => b.TimestampUtc >= At(First.AddDays(1), 9, 30) ? b with
            { Open = 8m, High = 8.1m, Low = 7.9m, Close = 8m } : b), Evaluate);
        await Run(session);
        session.Ledger.Fills.ShouldContain(f => f.Side == "Sell" && f.Reason == "HardLoss");
        session.DailyResults.Skip(1).ShouldAllBe(d => !d.Published);
    }

    [Fact]
    public async Task UnhandledDailyFailureIsATestFailureNotAMissingDataDay()
    {
        var session = new PracticeSession(Fixture(), (_, _) => throw new ArithmeticException("Synthetic accounting fault"));
        await Should.ThrowAsync<ArithmeticException>(() => session.StepAsync());
        session.Faulted.ShouldBeTrue();
        session.Completed.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => session.StepAsync());
    }

    [Fact]
    public async Task HistoricalDelphiRejectsOperationalWritesBeforeAnyDatabaseAccess()
    {
        var fixture = Fixture();
        var workflow = new DelphiWorkflow(fixture.Document.Daily, fixture.Calendar, () => At(First, 9, 0));
        await Should.ThrowAsync<InvalidOperationException>(() => workflow.RunAsync(new()));
        await Should.ThrowAsync<InvalidOperationException>(() => workflow.RunAsync(new(CalibrationRunPurpose.ExploratoryReplay,
            First.ToDateTime(TimeOnly.MinValue), SaveToDatabase: true)));
    }

    [Fact]
    public void DailySourceExcludesFuturePricesAndFutureMembership()
    {
        var today = First.ToDateTime(TimeOnly.MinValue);
        var inputs = Fixture().Document.Daily with
        {
            Daily = new() { ["ABC"] = [new() { Date = today.AddDays(-1), Close = 10 }, new() { Date = today, Close = 99 }] },
            Membership = [new(today.AddDays(-1), [new SymbolInfo { Symbol = "ABC" }], [], []), new(today, [new SymbolInfo { Symbol = "FUTURE" }], [], [])]
        };
        inputs.Bars("ABC", today).Single().Close.ShouldBe(10f);
        inputs.Members(today).Equities.Single().Symbol.ShouldBe("ABC");
    }

    [Fact]
    public async Task PinnedPortfolioPolicyControlsItsFillsIndependently()
    {
        var original = Fixture();
        var dataset = new PracticeDataset(original.Document with { PortfolioPolicies =
            [new("ContinuationTop3", Guid.NewGuid(), SystemShadowPolicyConfig.Version1 with { EntryFrictionRate = .01m })] }, original.Identity);
        var session = new PracticeSession(dataset, Daily);
        while (session.UtcNow < At(First, 10, 15)) await session.StepAsync();
        var changedId = session.Ledger.Portfolios.Single(p => p.PortfolioCode == "ContinuationTop3").PortfolioId;
        session.Ledger.Fills.Single(f => f.PortfolioId == changedId).AdjustedPrice.ShouldBe(
            SystemShadowPolicy.AdjustedBuyPrice(session.Ledger.Fills.Single(f => f.PortfolioId == changedId).RawPrice,
                SystemShadowPolicyConfig.Version1 with { EntryFrictionRate = .01m }));
        session.Ledger.Fills.Where(f => f.PortfolioId != changedId).ShouldAllBe(f =>
            f.AdjustedPrice == SystemShadowPolicy.AdjustedBuyPrice(f.RawPrice));
    }
}
