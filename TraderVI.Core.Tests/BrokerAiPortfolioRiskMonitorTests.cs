#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerAiPortfolioRiskMonitorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-risk-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Friday = new(2026, 9, 25);
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateTimeOffset Open = ReviewedTsxSessionCalendar.At(Friday, new(9, 30));
    private readonly AiRiskJournalScope scope = new(Guid.NewGuid(), Guid.NewGuid(), AiRiskAccountMode.Live);
    private readonly AiPortfolioRiskPolicy policy = new(new ReviewedTsxSessionCalendar(new(
        "test", "synthetic", Friday, Monday, [Friday, Monday])));

    private AiPortfolioRiskJournal Journal(bool initialize = true) => new(directory, scope, policy, initialize);
    private BrokerAiPortfolioRiskMonitor Monitor(AiPortfolioRiskJournal journal) => new(journal, policy, TimeSpan.FromMinutes(2));
    private BrokerAiRiskObservation Observation(decimal price = 100m, int minute = 0, DateTimeOffset? from = null)
    {
        var at = Open.AddMinutes(minute);
        return new(scope.PortfolioId, Friday,
            new(Guid.NewGuid(), scope.AccountScopeId, at, at, 5000m,
                [new(Guid.NewGuid(), "TEST", 50, price, Guid.NewGuid(), at)]),
            new(Guid.NewGuid(), scope.AccountScopeId, from ?? at, at, at, new string('A', 64)));
    }

    [Fact]
    public void ChecksEveryMinuteWithoutAiAndPersistsClosureAndSourceAcrossRestart()
    {
        BrokerAiRiskObservation opening = Observation(), gain = Observation(120m, 1, Open),
            hit = Observation(109m, 2, Open.AddMinutes(1));
        AiRiskObservationResult final;
        using (var journal = Journal())
        {
            var monitor = Monitor(journal);
            Assert.True(monitor.Start(opening, Open, false, []).Accepted);
            var peak = monitor.Observe(gain, gain.Book.At, false, []);
            Assert.True(peak.Advanced);
            Assert.Equal(11000m, peak.Snapshot.State!.RoundPeak);
            Assert.Equal(10450m, peak.Snapshot.State.ExitTrigger);
            Assert.Equal(0m, peak.Assessment.NewBuyAllowanceMultiplier);
            final = monitor.Observe(hit, hit.Book.At, false, ["CapitalReviewRequired"]);
            Assert.True(final.Assessment.RequiresManagedLiquidation);
            Assert.Contains("CapitalReviewRequired", final.Assessment.NewBuyBlocks);
            Assert.Equal(3, journal.ReadHistory().Length);
        }
        using var recovered = Journal(false);
        Assert.Equal(final.Snapshot, recovered.ReadCurrent());
        var source = recovered.ReadCurrent().State!.LastValuation.Source!;
        Assert.Equal(BrokerAiPortfolioRiskMonitor.ValuationContract, source.Contract);
        var evidence = JsonSerializer.Deserialize<BrokerAiRiskObservation>(source.EvidenceJson)!;
        Assert.Equal(hit.Book.EvidenceId, evidence.Book.EvidenceId);
        Assert.Equal(hit.NoFlows, evidence.NoFlows);
        Assert.Equal(hit.Book.Holdings[0], evidence.Book.Holdings[0]);
        var unavailable = Monitor(recovered).Observe(null, hit.Book.At.AddSeconds(1), true, []);
        Assert.False(unavailable.Accepted);
        Assert.True(unavailable.Assessment.RequiresManagedLiquidation);
        Assert.Equal(0m, unavailable.Assessment.NewBuyAllowanceMultiplier);
        Assert.Equal(final.Snapshot, recovered.ReadCurrent());
    }

    [Fact]
    public void ReboundRecoversAllowanceBeforeLimitWithoutClearingIndependentHolds()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        monitor.Start(Observation(), Open, true, []);
        var loss = Observation(95m, 1, Open);
        var half = monitor.Observe(loss, loss.Book.At, true, []);
        Assert.Equal(0.5m, half.Assessment.NewBuyAllowanceMultiplier);
        var rebound = Observation(98m, 2, loss.Book.At);
        var held = monitor.Observe(rebound, rebound.Book.At, true, ["OperatorBuyingPause"]);
        Assert.Equal(0m, held.Assessment.NewBuyAllowanceMultiplier);
        Assert.Contains("OperatorBuyingPause", held.Assessment.NewBuyBlocks);
        Assert.Equal(0.8m, monitor.Observe(rebound, rebound.Book.At, true, []).Assessment.NewBuyAllowanceMultiplier);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Portfolio")]
    [InlineData("Account")]
    [InlineData("FlowAccount")]
    [InlineData("BookIdentity")]
    [InlineData("FlowIdentity")]
    [InlineData("CoverageGap")]
    [InlineData("CoverageEnd")]
    [InlineData("FlowFingerprint")]
    [InlineData("FutureBook")]
    [InlineData("FutureFlow")]
    [InlineData("UnverifiedBook")]
    [InlineData("StalePrice")]
    [InlineData("MissingPrice")]
    [InlineData("MissingPriceIdentity")]
    [InlineData("DuplicateHolding")]
    [InlineData("DuplicateSymbol")]
    [InlineData("MissingHoldings")]
    [InlineData("NegativeCash")]
    [InlineData("WrongSession")]
    [InlineData("Overflow")]
    public void BadObservationBlocksNewRiskWithoutChangingSavedState(string defect)
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        monitor.Start(Observation(), Open, true, []);
        var before = journal.ReadCurrent();
        var o = Observation(120m, 1, Open);
        var b = o.Book;
        var f = o.NoFlows;
        var h = b.Holdings[0];
        BrokerAiRiskObservation? invalid = defect switch
        {
            "Missing" => null,
            "Portfolio" => o with { PortfolioId = Guid.NewGuid() },
            "Account" => o with { Book = b with { BindingId = Guid.NewGuid() } },
            "FlowAccount" => o with { NoFlows = f with { BindingId = Guid.NewGuid() } },
            "BookIdentity" => o with { Book = b with { EvidenceId = Guid.Empty } },
            "FlowIdentity" => o with { NoFlows = f with { EvidenceId = Guid.Empty } },
            "CoverageGap" => o with { NoFlows = f with { From = Open.AddSeconds(1) } },
            "CoverageEnd" => o with { NoFlows = f with { Through = Open } },
            "FlowFingerprint" => o with { NoFlows = f with { SourceFingerprint = "net deposits unchanged" } },
            "FutureBook" => o with { Book = b with { VerifiedAt = b.At.AddMinutes(1) } },
            "FutureFlow" => o with { NoFlows = f with { ObservedAt = b.At.AddMinutes(1) } },
            "UnverifiedBook" => o with { Book = b with { VerifiedAt = Open } },
            "StalePrice" => o with { Book = b with { Holdings = [h with { SavedPriceAt = Open }] } },
            "MissingPrice" => o with { Book = b with { Holdings = [h with { SavedPrice = null }] } },
            "MissingPriceIdentity" => o with { Book = b with { Holdings = [h with { SavedPriceEvidenceId = null }] } },
            "DuplicateHolding" => o with { Book = b with { Holdings = [h, h] } },
            "DuplicateSymbol" => o with { Book = b with { Holdings = [h, h with { PositionId = Guid.NewGuid(), Symbol = "test" }] } },
            "MissingHoldings" => o with { Book = b with { Holdings = default } },
            "NegativeCash" => o with { Book = b with { Cash = -1m } },
            "WrongSession" => o with { TradingDate = Monday },
            "Overflow" => o with { Book = b with { Holdings = [h with { SavedPrice = decimal.MaxValue }] } },
            _ => throw new InvalidOperationException()
        };
        var rejected = monitor.Observe(invalid, o.Book.At, true, ["ExistingHold"]);
        Assert.False(rejected.Accepted);
        Assert.False(rejected.Advanced);
        Assert.Equal(0m, rejected.Assessment.NewBuyAllowanceMultiplier);
        Assert.Contains(BrokerAiPortfolioRiskMonitor.RejectedObservation, rejected.Assessment.NewBuyBlocks);
        Assert.Contains("ExistingHold", rejected.Assessment.NewBuyBlocks);
        Assert.Equal(before, rejected.Snapshot);
        Assert.Equal(before, journal.ReadCurrent());
    }

    [Fact]
    public void DuplicateDoesNotRefreshExpiryAndChangedSourceCannotReuseIdentity()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var opening = Observation();
        var initial = monitor.Start(opening, Open, true, []);
        var retry = monitor.Observe(opening, Open.AddMinutes(1), true, []);
        Assert.True(retry.Accepted);
        Assert.False(retry.Advanced);
        Assert.Equal(initial.Snapshot, retry.Snapshot);
        var changed = opening with { NoFlows = opening.NoFlows with { SourceFingerprint = new string('B', 64) } };
        Assert.False(monitor.Observe(changed, Open.AddMinutes(1), true, []).Accepted);
        var stale = monitor.Observe(opening, Open.AddMinutes(3), true, []);
        Assert.False(stale.Accepted);
        Assert.Contains("ValuationNotCurrent", stale.Assessment.NewBuyBlocks);
        Assert.Equal(initial.Snapshot, journal.ReadCurrent());
        Assert.Single(journal.ReadHistory());
    }

    [Fact]
    public void SameValueWithNewEvidenceAdvancesButEqualTimeAndOlderEvidenceDoNot()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var opening = Observation();
        monitor.Start(opening, Open, true, []);
        Assert.False(monitor.Observe(Observation(), Open, true, []).Accepted);
        var later = Observation(100m, 1, Open);
        Assert.True(monitor.Observe(later, later.Book.At, true, []).Advanced);
        Assert.False(monitor.Observe(opening, later.Book.At, true, []).Accepted);
        Assert.Equal(2, journal.ReadCurrent().Revision);
    }

    [Fact]
    public void CashOnlyNeedsDatedBookAndCoverageButNoInventedPrice()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var original = Observation();
        var cash = original with { Book = original.Book with { Holdings = [], Cash = 10000m } };
        Assert.True(monitor.Start(cash, Open, true, []).Accepted);
        var later = Observation(100m, 1, Open);
        later = later with { Book = later.Book with { Holdings = [], Cash = 9500m } };
        var failed = monitor.Observe(later, later.Book.At, false, []);
        Assert.True(failed.Assessment.RequiresManagedLiquidation);
        Assert.Equal(10000m, failed.Snapshot.State!.RoundPeak);
    }

    [Fact]
    public void OvernightCoverageAndRecoveredJournalPreserveRoundPeak()
    {
        Guid round;
        using (var journal = Journal())
        {
            round = Monitor(journal).Start(Observation(), Open, true, []).Snapshot.State!.RoundId;
        }
        var mondayOpen = ReviewedTsxSessionCalendar.At(Monday, new(9, 30));
        var sample = Observation();
        var monday = sample with
        {
            TradingDate = Monday,
            Book = sample.Book with { At = mondayOpen, VerifiedAt = mondayOpen,
                Holdings = [sample.Book.Holdings[0] with { SavedPriceAt = mondayOpen, SavedPrice = 95m }] },
            NoFlows = sample.NoFlows with { From = Open, Through = mondayOpen, ObservedAt = mondayOpen }
        };
        using var recovered = Journal(false);
        var monitor = Monitor(recovered);
        Assert.False(monitor.Start(monday, mondayOpen, true, []).Accepted);
        var result = monitor.Observe(monday, mondayOpen, true, []);
        Assert.True(result.Advanced);
        Assert.Equal(round, result.Snapshot.State!.RoundId);
        Assert.Equal(10000m, result.Snapshot.State.RoundPeak);
        Assert.Equal(0.5m, result.Assessment.NewBuyAllowanceMultiplier);
    }

    [Fact]
    public void InitializationIsExplicitAndPaperJournalCannotConsumeBrokerFacts()
    {
        using (var journal = Journal())
        {
            var result = Monitor(journal).Observe(Observation(), Open, true, []);
            Assert.False(result.Accepted);
            Assert.Null(journal.ReadCurrent().State);
            Assert.Equal(0m, result.Assessment.NewBuyAllowanceMultiplier);
            Assert.Throws<ArgumentException>(() => Monitor(journal).Start(Observation(), Open, true, default));
            Assert.Throws<ArgumentException>(() => new BrokerAiPortfolioRiskMonitor(journal, policy, TimeSpan.Zero));
        }
        using var paper = new AiPortfolioRiskJournal(Path.Combine(directory, "paper"),
            scope with { Mode = AiRiskAccountMode.Paper }, policy, true);
        Assert.Throws<ArgumentException>(() => Monitor(paper));
    }

    [Fact]
    public void PendingStorageFailurePropagatesInsteadOfReturningAllowanceOrResetting()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        monitor.Start(Observation(), Open, true, []);
        File.WriteAllText(Path.Combine(directory, "ai-risk.json.pending"), "interrupted");
        Assert.Throws<InvalidDataException>(() => monitor.Observe(Observation(120m, 1, Open), Open.AddMinutes(1), true, []));
    }

    [Fact]
    public void WholeBookIncludesEveryHoldingAndOrderingDoesNotChangeEvidence()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var o = Observation();
        var other = new BrokerRiskHolding(Guid.NewGuid(), "SECOND", 25, 80m, Guid.NewGuid(), Open);
        o = o with { Book = o.Book with { Holdings = o.Book.Holdings.Add(other) } };
        var start = monitor.Start(o, Open, true, []);
        Assert.Equal(12000m, start.Snapshot.State!.LastValuation.Value);
        var reordered = o with { Book = o.Book with { Holdings = o.Book.Holdings.Reverse().ToImmutableArray() } };
        Assert.True(monitor.Observe(reordered, Open.AddSeconds(1), true, []).Accepted);
        var partial = o with { Book = o.Book with { Holdings = [other] } };
        Assert.False(monitor.Observe(partial, Open.AddSeconds(1), true, []).Accepted);
        Assert.Single(journal.ReadHistory());
    }

    [Fact]
    public void ExistingHistoryWithoutSourceCanContinueWithoutReinitializing()
    {
        Guid round;
        using (var journal = Journal())
        {
            var legacy = new PortfolioRiskValuation(scope.PortfolioId, Guid.NewGuid(), Friday,
                Open, Open.AddMinutes(2), 10000m, true, true);
            round = journal.Apply(new(Guid.NewGuid(), 0, AiRiskTransitionKind.Start, legacy), Open).State!.RoundId;
        }
        using var recovered = Journal(false);
        var next = Observation(110m, 1, Open);
        var result = Monitor(recovered).Observe(next, next.Book.At, true, []);
        Assert.True(result.Accepted);
        Assert.Equal(round, result.Snapshot.State!.RoundId);
        Assert.Equal(10000m, result.Snapshot.State.OriginalCapital);
        Assert.Null(recovered.ReadHistory()[0].State.LastValuation.Source);
        Assert.NotNull(result.Snapshot.State.LastValuation.Source);
    }

    [Fact]
    public void ProvenanceIsPrivateAndLegacyValuationsKeepTheirSerializedShape()
    {
        var old = new PortfolioRiskValuation(scope.PortfolioId, Guid.NewGuid(), Friday, Open, Open.AddMinutes(2), 12345m, true, true);
        Assert.DoesNotContain("Source", JsonSerializer.Serialize(old));
        Assert.DoesNotContain("12345", old.ToString());
        using var journal = Journal();
        var opening = Observation(12345m);
        var result = Monitor(journal).Start(opening, Open, true, []);
        Assert.DoesNotContain("12345", opening.ToString());
        Assert.DoesNotContain("12345", result.Snapshot.State!.LastValuation.Source!.ToString());
        Assert.DoesNotContain("12345", result.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
