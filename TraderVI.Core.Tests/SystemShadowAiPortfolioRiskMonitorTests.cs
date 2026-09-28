#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class SystemShadowAiPortfolioRiskMonitorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-risk-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Date = new(2026, 9, 25);
    private static readonly DateTime Open = ReviewedTsxSessionCalendar.At(Date, new(9, 30));
    private readonly Guid portfolio = Guid.NewGuid(), generation = Guid.NewGuid(), position = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private readonly AiPortfolioRiskPolicy policy = new(new ReviewedTsxSessionCalendar(new("test", "synthetic", Date, Monday, [Date, Monday])));
    private AiPortfolioRiskJournal Journal(bool initialize = true) => new(directory, new(portfolio, portfolio, AiRiskAccountMode.Paper), policy, initialize);
    private SystemShadowAiPortfolioRiskMonitor Monitor(AiPortfolioRiskJournal journal) => new(journal, policy, TimeSpan.FromMinutes(3));
    private ShadowPaperRiskObservation Observation() => new(Guid.NewGuid(), Date, Open.AddMinutes(10), Open.AddMinutes(12),
        new(generation, SystemShadowVersions.Policy, SystemShadowGenerationStatus.Active, 10000m, 10000m,
            Open.AddDays(-1), Open.AddDays(-1), Open.AddDays(-1)),
        new(portfolio, generation, "BreakoutTop3", "Breakout", 3, "Active", 5000m, 10000m, Open.AddDays(-1)),
        new(Guid.NewGuid(), portfolio, Date.ToDateTime(TimeOnly.MinValue), null, "Monitoring", null, 10000m, false),
        [new(position, portfolio, "TEST", "Open", 50, 100m, 5000m, 5000m, Open.AddMinutes(1), Date.ToDateTime(TimeOnly.MinValue),
            0, 0, null, null, null, false, 100m, Open.AddMinutes(5), 0m, null, null)],
        [new("TEST", new(Guid.NewGuid(), Open.AddMinutes(5), Open.AddMinutes(12), 5, 100m, 100m, 100m, 100m))]);
    private static ShadowPaperRiskObservation Advance(ShadowPaperRiskObservation o, decimal price, int minute) => o with
    {
        EvidenceId = Guid.NewGuid(), At = Open.AddMinutes(minute), ObservedUtc = Open.AddMinutes(minute + 2),
        Positions = [o.Positions[0] with { LastPrice = price, LastPriceEventUtc = Open.AddMinutes(minute - 5) }],
        Prices = [new("TEST", new(Guid.NewGuid(), Open.AddMinutes(minute - 5), Open.AddMinutes(minute + 2), 5, price, price, price, price))]
    };

    [Fact]
    public void CompletePaperBooksUseSharedRiskWriterAndRecoverWithoutAiReview()
    {
        var opening = Observation();
        var peak = Advance(opening, 120m, 15);
        var hit = Advance(peak, 109m, 20);
        AiRiskObservationResult result;
        using (var journal = Journal())
        {
            var monitor = Monitor(journal);
            Assert.True(monitor.Start(opening, opening.ObservedUtc, false, []).Accepted);
            Assert.True(monitor.Observe(peak, peak.ObservedUtc, false, []).Advanced);
            result = monitor.Observe(hit, hit.ObservedUtc, false, []);
            Assert.True(result.Accepted);
            Assert.True(result.Assessment.RequiresManagedLiquidation);
            Assert.Equal(11000m, result.Snapshot.State!.RoundPeak);
            Assert.Equal(10450m, result.Snapshot.State.ExitTrigger);
        }
        using var recovered = Journal(false);
        Assert.Equal(result.Snapshot, recovered.ReadCurrent());
        Assert.Equal(SystemShadowAiPortfolioRiskMonitor.ValuationContract, result.Snapshot.State!.LastValuation.Source!.Contract);
        var unavailable = Monitor(recovered).Observe(null, hit.ObservedUtc, true, []);
        Assert.False(unavailable.Accepted);
        Assert.True(unavailable.Assessment.RequiresManagedLiquidation);
    }

    [Theory]
    [InlineData("BreakoutTop3", -1)]
    [InlineData("BreakoutTop5", -1)]
    [InlineData("ContinuationTop3", -1)]
    [InlineData("ContinuationTop5", -1)]
    [InlineData("BreakoutTop3", 0)]
    [InlineData("BreakoutTop5", 0)]
    [InlineData("ContinuationTop3", 0)]
    [InlineData("ContinuationTop5", 0)]
    [InlineData("BreakoutTop3", 1)]
    [InlineData("BreakoutTop5", 1)]
    [InlineData("ContinuationTop3", 1)]
    [InlineData("ContinuationTop5", 1)]
    public void CurrentShadowFamiliesAndGateVariantsPreserveIdentity(string code, int profile)
    {
        using var journal = Journal();
        var o = Observation();
        var strategy = Guid.NewGuid();
        o = o with { Portfolio = o.Portfolio with { PortfolioCode = code, StrategyVersionId = strategy,
            Lens = code.StartsWith("Breakout") ? "Breakout" : "Continuation", MaximumPositions = code.EndsWith("3") ? 3 : 5,
            ComparisonAccount = profile < 0 ? null : new(portfolio, Guid.NewGuid(), Guid.NewGuid(), code,
                (PaperGateProfile)profile, Open, Guid.NewGuid(), null, strategy, "{}") } };
        var result = Monitor(journal).Start(o, o.ObservedUtc, true, []);
        Assert.True(result.Accepted);
        Assert.Contains(code, result.Snapshot.State!.LastValuation.Source!.EvidenceJson);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Scope")]
    [InlineData("Session")]
    [InlineData("Generation")]
    [InlineData("Capital")]
    [InlineData("MissingPositions")]
    [InlineData("MissingPrice")]
    [InlineData("DuplicatePrice")]
    [InlineData("WrongSymbol")]
    [InlineData("BarStartAsFreshTime")]
    [InlineData("StoredPriceMismatch")]
    [InlineData("FuturePriceReceipt")]
    [InlineData("InvalidOhlc")]
    [InlineData("WrongQuantity")]
    [InlineData("LaterEntry")]
    [InlineData("DuplicatePosition")]
    [InlineData("UnlabelledUtc")]
    public void InvalidOrUnprovenBookCannotAdvanceRisk(string defect)
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var initial = Observation();
        monitor.Start(initial, initial.ObservedUtc, true, []);
        var before = journal.ReadCurrent();
        var o = Advance(initial, 120m, 15);
        var price = o.Prices[0];
        ShadowPaperRiskObservation? bad = defect switch
        {
            "Missing" => null,
            "Scope" => o with { Portfolio = o.Portfolio with { PortfolioId = Guid.NewGuid() } },
            "Session" => o with { Session = o.Session with { PortfolioId = Guid.NewGuid() } },
            "Generation" => o with { Generation = o.Generation with { GenerationId = Guid.NewGuid() } },
            "Capital" => o with { Portfolio = o.Portfolio with { CashBalance = 6000m } },
            "MissingPositions" => o with { Positions = [] },
            "MissingPrice" => o with { Prices = [] },
            "DuplicatePrice" => o with { Prices = o.Prices.Add(price) },
            "WrongSymbol" => o with { Prices = [price with { Symbol = "OTHER" }] },
            "BarStartAsFreshTime" => o with { At = price.Bar.StartUtc },
            "StoredPriceMismatch" => o with { Positions = [o.Positions[0] with { LastPrice = 121m }] },
            "FuturePriceReceipt" => o with { Prices = [price with { Bar = price.Bar with { ReceivedUtc = o.ObservedUtc.AddSeconds(1) } }] },
            "InvalidOhlc" => o with { Prices = [price with { Bar = price.Bar with { Low = 0m } }] },
            "WrongQuantity" => o with { Positions = [o.Positions[0] with { Shares = 0 }] },
            "LaterEntry" => o with { Positions = [o.Positions[0] with { EntryUtc = o.At.AddSeconds(1) }] },
            "DuplicatePosition" => o with { Positions = o.Positions.Add(o.Positions[0]) },
            "UnlabelledUtc" => o with { At = DateTime.SpecifyKind(o.At, DateTimeKind.Unspecified) },
            _ => throw new InvalidOperationException()
        };
        var result = monitor.Observe(bad, o.ObservedUtc, true, []);
        Assert.False(result.Accepted);
        Assert.Equal(0m, result.Assessment.NewBuyAllowanceMultiplier);
        Assert.Equal(before, journal.ReadCurrent());
    }

    [Fact]
    public void OneFreshHoldingCannotMaskAnotherStaleHolding()
    {
        using var journal = Journal();
        var o = Observation();
        var second = o.Positions[0] with { PositionId = Guid.NewGuid(), Symbol = "SECOND", Shares = 10, CostBasis = 1000m,
            LastPriceEventUtc = Open };
        o = o with { Portfolio = o.Portfolio with { CashBalance = 4000m }, Positions = o.Positions.Add(second),
            Prices = o.Prices.Add(new("SECOND", o.Prices[0].Bar with { EvidenceId = Guid.NewGuid(), StartUtc = Open })) };
        Assert.False(Monitor(journal).Start(o, o.ObservedUtc, true, []).Accepted);
        o = o with { Positions = [o.Positions[0], second with { LastPriceEventUtc = Open.AddMinutes(5) }],
            Prices = [o.Prices[0], o.Prices[1] with { Bar = o.Prices[1].Bar with { StartUtc = Open.AddMinutes(5) } }] };
        Assert.True(Monitor(journal).Start(o, o.ObservedUtc, true, []).Accepted);
        Assert.Equal(10000m, journal.ReadCurrent().State!.LastValuation.Value);
    }

    [Fact]
    public void ExistingHoldsArePreservedAndRepeatedReceiptDoesNotRenewFreshness()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var o = Observation();
        o = o with { Session = o.Session with { DailyLossGuardActive = true },
            Portfolio = o.Portfolio with { Status = "CapitalReviewRequired" } };
        var result = monitor.Start(o, o.ObservedUtc, true, []);
        Assert.True(result.Accepted);
        Assert.Contains("DailyBuyingPaused", result.Assessment.NewBuyBlocks);
        Assert.Contains("CapitalReviewRequired", result.Assessment.NewBuyBlocks);
        Assert.Equal(0m, result.Assessment.NewBuyAllowanceMultiplier);
        Assert.False(monitor.Observe(o, o.ObservedUtc, true, []).Advanced);
        Assert.False(monitor.Observe(o, o.ObservedUtc.AddMinutes(2), true, []).Accepted);
        Assert.False(monitor.Observe(o with { ObservedUtc = o.ObservedUtc.AddSeconds(1) }, o.ObservedUtc.AddSeconds(1), true, []).Accepted);
        Assert.Single(journal.ReadHistory());
    }

    [Fact]
    public void NextSessionRetainsRoundPeakAndAllowance()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var o = Observation();
        var first = monitor.Start(o, o.ObservedUtc, true, []);
        var mondayOpen = ReviewedTsxSessionCalendar.At(Monday, new(9, 30));
        var next = o with { EvidenceId = Guid.NewGuid(), TradingDate = Monday, At = mondayOpen.AddMinutes(5),
            ObservedUtc = mondayOpen.AddMinutes(7), Session = o.Session with { SessionId = Guid.NewGuid(), TradingDate = Monday.ToDateTime(TimeOnly.MinValue) },
            Positions = [o.Positions[0] with { LastPrice = 95m, LastPriceEventUtc = mondayOpen }],
            Prices = [new("TEST", new(Guid.NewGuid(), mondayOpen, mondayOpen.AddMinutes(7), 5, 95m, 95m, 95m, 95m))] };
        var result = monitor.Observe(next, next.ObservedUtc, true, []);
        Assert.True(result.Accepted);
        Assert.Equal(first.Snapshot.State!.RoundId, result.Snapshot.State!.RoundId);
        Assert.Equal(10000m, result.Snapshot.State.RoundPeak);
        Assert.Equal(0.5m, result.Assessment.NewBuyAllowanceMultiplier);
    }

    [Theory]
    [InlineData("OtherFamily")]
    [InlineData("MalformedSource")]
    public void UnqualifiedSourceHandoverCannotReplaceRiskHistory(string defect)
    {
        using var journal = Journal();
        var valuation = new PortfolioRiskValuation(portfolio, Guid.NewGuid(), Date, Open.AddMinutes(5), Open.AddMinutes(8), 10000m, true, true)
        {
            Source = new(defect == "OtherFamily" ? DelphiLiveAiPortfolioRiskMonitor.ValuationContract : SystemShadowAiPortfolioRiskMonitor.ValuationContract,
                "{\"Generation\":null}", new string('A', 64))
        };
        var before = journal.Apply(new(Guid.NewGuid(), 0, AiRiskTransitionKind.Start, valuation), valuation.At);
        var o = Observation();
        var result = Monitor(journal).Observe(o, o.ObservedUtc, true, []);
        Assert.False(result.Accepted);
        Assert.Equal(0m, result.Assessment.NewBuyAllowanceMultiplier);
        Assert.Equal(before, journal.ReadCurrent());
    }

    [Fact]
    public void CompleteCashOnlyBookPreservesLossHistoryAndCannotDropClosedPositions()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var o = Observation();
        var start = monitor.Start(o, o.ObservedUtc, true, []);
        var closed = o.Positions[0] with { Status = "Closed", RealizedProfitLoss = -250m, ExitUtc = Open.AddMinutes(11) };
        var cash = o with { EvidenceId = Guid.NewGuid(), At = Open.AddMinutes(15), ObservedUtc = Open.AddMinutes(17),
            Positions = [closed], Prices = [], Portfolio = o.Portfolio with { CashBalance = 9750m } };
        var result = monitor.Observe(cash, cash.ObservedUtc, true, []);
        Assert.True(result.Accepted);
        Assert.Equal(start.Snapshot.State!.RoundId, result.Snapshot.State!.RoundId);
        Assert.Equal(0.5m, result.Assessment.NewBuyAllowanceMultiplier);
        var replaced = cash with { EvidenceId = Guid.NewGuid(), At = Open.AddMinutes(20), ObservedUtc = Open.AddMinutes(22),
            Positions = [], Generation = cash.Generation with { TotalAccountValue = 9750m } };
        Assert.False(monitor.Observe(replaced, replaced.ObservedUtc, true, []).Accepted);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
