#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveAiPortfolioRiskMonitorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-risk-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Friday = new(2026, 9, 25), Monday = new(2026, 9, 28);
    private static readonly DateTime Open = ReviewedTsxSessionCalendar.At(Friday, new(9, 30));
    private readonly Guid portfolio = Guid.NewGuid(), generation = Guid.NewGuid(), position = Guid.NewGuid(), buy = Guid.NewGuid();
    private readonly AiPortfolioRiskPolicy policy = new(new ReviewedTsxSessionCalendar(new("test", "synthetic", Friday, Monday, [Friday, Monday])));
    private AiRiskJournalScope Scope => new(portfolio, portfolio, AiRiskAccountMode.Paper);
    private AiPortfolioRiskJournal Journal(bool initialize = true) => new(directory, Scope, policy, initialize);
    private DelphiLiveAiPortfolioRiskMonitor Monitor(AiPortfolioRiskJournal journal) => new(journal, policy, TimeSpan.FromMinutes(3));
    private DelphiLivePortfolioSnapshot Snapshot()
    {
        var p = DelphiLiveLedgerIntegrity.Create(new(generation, portfolio,
            DelphiLivePolicyDefinition.Version1.PolicyVersionId, "OperationalChampion", null, 10000m, "CAD",
            Friday, Open, Open.AddDays(-1), "Test", "Synthetic paper ledger"));
        return p with
        {
            Revision = 10, CurrentSession = Friday, Cash = 5000m, UpdatedUtc = Open.AddMinutes(12),
            Positions = [new(position, "TEST", 50, 100m, Open.AddMinutes(1), buy, "{}", DelphiLiveProfitProtectionState.Open(position, 100m))],
            Fills = [new(Guid.NewGuid(), buy, position, Guid.NewGuid(), "TEST", DelphiLiveActionSide.Buy, 50, 100m,
                DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.SideSpecific, Open.AddMinutes(1), Friday)],
            Marks = [new(Guid.NewGuid(), Friday, DelphiLivePortfolioMarkKind.Checkpoint, Open.AddMinutes(10), true,
                10000m, [new(position, "TEST", 50, 100m, Open.AddMinutes(10))], "Complete")]
        };
    }
    private static DelphiLivePortfolioSnapshot Advance(DelphiLivePortfolioSnapshot p, decimal price, int minute)
    {
        var at = Open.AddMinutes(minute);
        return p with { Revision = p.Revision + 1, UpdatedUtc = at.AddMinutes(2),
            Marks = p.Marks.Add(new(Guid.NewGuid(), Friday, DelphiLivePortfolioMarkKind.Checkpoint, at, true,
                p.Cash + 50m * price, [new(p.Positions[0].PositionId, "TEST", 50, price, at)], "Complete")) };
    }

    [Fact]
    public void PaperCheckpointUpdatesRiskWithoutAiAndRecoversExactSourceAndClosure()
    {
        var initial = Snapshot();
        var peak = Advance(initial, 120m, 15);
        var hit = Advance(peak, 109m, 20);
        AiRiskObservationResult result;
        using (var journal = Journal())
        {
            var monitor = Monitor(journal);
            Assert.True(monitor.Start(initial, initial.UpdatedUtc, false, []).Accepted);
            Assert.True(monitor.Observe(peak, peak.UpdatedUtc, false, []).Advanced);
            result = monitor.Observe(hit, hit.UpdatedUtc, false, []);
            Assert.True(result.Accepted);
            Assert.True(result.Assessment.RequiresManagedLiquidation);
            Assert.Equal(11000m, result.Snapshot.State!.RoundPeak);
            Assert.Equal(10450m, result.Snapshot.State.ExitTrigger);
            Assert.Equal(0m, result.Assessment.NewBuyAllowanceMultiplier);
            Assert.Equal(3, journal.ReadHistory().Length);
        }
        using var recovered = Journal(false);
        Assert.Equal(result.Snapshot, recovered.ReadCurrent());
        Assert.Equal(DelphiLiveAiPortfolioRiskMonitor.ValuationContract, recovered.ReadCurrent().State!.LastValuation.Source!.Contract);
        var unavailable = Monitor(recovered).Observe(null, hit.UpdatedUtc, true, []);
        Assert.False(unavailable.Accepted);
        Assert.True(unavailable.Assessment.RequiresManagedLiquidation);
        Assert.Equal(result.Snapshot, unavailable.Snapshot);
    }

    [Fact]
    public void SavedEngineHoldsAlwaysBlockBuyingButNotRiskUpdates()
    {
        using var journal = Journal();
        var p = Snapshot() with { Guards = new(true, true, null, null, 10000m) };
        var result = Monitor(journal).Start(p, p.UpdatedUtc, true, ["AccountUnavailable"]);
        Assert.True(result.Accepted);
        Assert.Equal(0m, result.Assessment.NewBuyAllowanceMultiplier);
        Assert.Contains("DailyBuyingPaused", result.Assessment.NewBuyBlocks);
        Assert.Contains("CapitalReviewRequired", result.Assessment.NewBuyBlocks);
        Assert.Contains("AccountUnavailable", result.Assessment.NewBuyBlocks);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Account")]
    [InlineData("Currency")]
    [InlineData("Policy")]
    [InlineData("MarksMissing")]
    [InlineData("Opening")]
    [InlineData("Incomplete")]
    [InlineData("CorporateAction")]
    [InlineData("WrongSession")]
    [InlineData("MarkFuture")]
    [InlineData("ReceiptFuture")]
    [InlineData("UnlabelledUtc")]
    [InlineData("CashMismatch")]
    [InlineData("NavMismatch")]
    [InlineData("MissingPrice")]
    [InlineData("OldPrice")]
    [InlineData("WrongQuantity")]
    [InlineData("DuplicatePrice")]
    [InlineData("UnknownPosition")]
    [InlineData("MissingFills")]
    [InlineData("DuplicateFill")]
    [InlineData("WrongFillQuantity")]
    [InlineData("PostCheckpointFill")]
    [InlineData("MissingGuards")]
    [InlineData("DuplicatePosition")]
    public void InvalidCurrentBookCannotAdvanceRisk(string defect)
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var opening = Snapshot();
        monitor.Start(opening, opening.UpdatedUtc, true, []);
        var before = journal.ReadCurrent();
        var next = Advance(opening, 120m, 15);
        var mark = next.Marks[^1];
        DelphiLivePortfolioSnapshot? bad = defect switch
        {
            "Missing" => null,
            "Account" => next with { PortfolioId = Guid.NewGuid() },
            "Currency" => next with { Currency = "USD" },
            "Policy" => next with { PolicyVersionId = Guid.Empty },
            "MarksMissing" => next with { Marks = [] },
            "Opening" => next with { Marks = [mark with { Kind = DelphiLivePortfolioMarkKind.Opening }] },
            "Incomplete" => next with { Marks = next.Marks.SetItem(1, mark with { Complete = false }) },
            "CorporateAction" => next with { Marks = [mark with { Reason = "CorporateActionUnsupported" }] },
            "WrongSession" => next with { CurrentSession = Monday },
            "MarkFuture" => next with { Marks = [mark with { BarEndUtc = Open.AddMinutes(20) }] },
            "ReceiptFuture" => next with { UpdatedUtc = next.UpdatedUtc.AddMinutes(1) },
            "UnlabelledUtc" => next with { UpdatedUtc = DateTime.SpecifyKind(next.UpdatedUtc, DateTimeKind.Unspecified) },
            "CashMismatch" => next with { Cash = 5001m },
            "NavMismatch" => next with { Marks = [mark with { Nav = mark.Nav + 1m }] },
            "MissingPrice" => next with { Marks = [mark with { Positions = [] }] },
            "OldPrice" => next with { Marks = [mark with { Positions = [mark.Positions[0] with { BarEndUtc = Open.AddMinutes(10) }] }] },
            "WrongQuantity" => next with { Marks = [mark with { Positions = [mark.Positions[0] with { Quantity = 49 }] }] },
            "DuplicatePrice" => next with { Marks = [mark with { Positions = mark.Positions.Add(mark.Positions[0]) }] },
            "UnknownPosition" => next with { Marks = [mark with { Positions = [mark.Positions[0] with { PositionId = Guid.NewGuid() }] }] },
            "MissingFills" => next with { Fills = [] },
            "DuplicateFill" => next with { Fills = next.Fills.Add(next.Fills[0]) },
            "WrongFillQuantity" => next with { Fills = [next.Fills[0] with { Quantity = 49 }] },
            "PostCheckpointFill" => next with { Fills = [next.Fills[0] with { FilledUtc = mark.BarEndUtc.AddSeconds(1) }] },
            "MissingGuards" => next with { Guards = null! },
            "DuplicatePosition" => next with { Positions = next.Positions.Add(next.Positions[0]) },
            _ => throw new InvalidOperationException()
        };
        var rejected = monitor.Observe(bad, next.UpdatedUtc, true, []);
        Assert.False(rejected.Accepted);
        Assert.Equal(0m, rejected.Assessment.NewBuyAllowanceMultiplier);
        Assert.Contains("PortfolioValuationRejected", rejected.Assessment.NewBuyBlocks);
        Assert.Equal(before, journal.ReadCurrent());
    }

    [Fact]
    public void CheckpointRetryNeverRenewsFreshnessOrAcceptsARepaintedRevision()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var p = Snapshot();
        var first = monitor.Start(p, p.UpdatedUtc, true, []);
        Assert.False(monitor.Observe(p, p.UpdatedUtc.AddSeconds(20), true, []).Advanced);
        Assert.False(monitor.Observe(p, p.UpdatedUtc.AddMinutes(2), true, []).Accepted);
        var next = Advance(p, 110m, 15) with { Revision = p.Revision };
        Assert.False(monitor.Observe(next, next.UpdatedUtc, true, []).Accepted);
        Assert.Equal(first.Snapshot, journal.ReadCurrent());
    }

    [Theory]
    [InlineData("Generation")]
    [InlineData("Capital")]
    [InlineData("FillHistory")]
    public void NewCheckpointCannotResetPaperCapitalOrReplaceHistory(string change)
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var p = Snapshot();
        monitor.Start(p, p.UpdatedUtc, true, []);
        var next = Advance(p, 110m, 15);
        next = change switch
        {
            "Generation" => next with { GenerationId = Guid.NewGuid() },
            "Capital" => next with { StartingCapital = 11000m, Cash = 6000m,
                Marks = [next.Marks[^1] with { Nav = next.Marks[^1].Nav + 1000m }] },
            "FillHistory" => next with { Fills = [next.Fills[0] with { QuoteObservationId = Guid.NewGuid() }] },
            _ => throw new InvalidOperationException()
        };
        Assert.False(monitor.Observe(next, next.UpdatedUtc, true, []).Accepted);
        Assert.Single(journal.ReadHistory());
    }

    [Fact]
    public void OrdinarySaleToCashPreservesRoundAndWaitsForANewDatedCheckpoint()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var p = Snapshot();
        var first = monitor.Start(p, p.UpdatedUtc, true, []);
        var sell = Guid.NewGuid();
        var closed = p.Positions[0] with { ClosedUtc = Open.AddMinutes(11), ExitActionId = sell };
        var sold = p with { Cash = 9750m, Revision = p.Revision + 1, Positions = [closed],
            Fills = p.Fills.Add(new(Guid.NewGuid(), sell, position, Guid.NewGuid(), "TEST", DelphiLiveActionSide.Sell,
                50, 95m, DelphiLiveQuoteField.Bid, DelphiLiveFillConfidence.SideSpecific, closed.ClosedUtc!.Value, Friday)) };
        Assert.False(monitor.Observe(sold, sold.UpdatedUtc, true, []).Accepted);
        sold = sold with { UpdatedUtc = Open.AddMinutes(17), Revision = sold.Revision + 1,
            Marks = sold.Marks.Add(new(Guid.NewGuid(), Friday, DelphiLivePortfolioMarkKind.Checkpoint, Open.AddMinutes(15), true, 9750m, [], "Complete")) };
        var result = monitor.Observe(sold, sold.UpdatedUtc, true, []);
        Assert.True(result.Accepted);
        Assert.Equal(first.Snapshot.State!.RoundId, result.Snapshot.State!.RoundId);
        Assert.Equal(10000m, result.Snapshot.State.RoundPeak);
        Assert.Equal(0.5m, result.Assessment.NewBuyAllowanceMultiplier);
    }

    [Theory]
    [InlineData(PaperGateProfile.NormalV1)]
    [InlineData(PaperGateProfile.PortfolioRulesOnlyV1)]
    public void BothGateVariantsRetainExactIdentity(PaperGateProfile profile)
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var p = Snapshot();
        p = p with { Role = "GateComparison", ComparisonAccount = new(portfolio, Guid.NewGuid(), Guid.NewGuid(),
            "DelphiLive", profile, Open, Guid.NewGuid(), p.PolicyVersionId, null, null) };
        var result = monitor.Start(p, p.UpdatedUtc, true, []);
        Assert.True(result.Accepted);
        Assert.Contains(p.ComparisonAccount.PairId.ToString(), result.Snapshot.State!.LastValuation.Source!.EvidenceJson);
        var wrong = Advance(p, 110m, 15) with { ComparisonAccount = p.ComparisonAccount with { AccountId = Guid.NewGuid() } };
        Assert.False(monitor.Observe(wrong, wrong.UpdatedUtc, true, []).Accepted);
    }

    [Fact]
    public void CashOnlyStillNeedsAFreshCheckpointAndExplicitInitialization()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var p = Snapshot();
        p = p with { Positions = [], Fills = [], Cash = 10000m, Marks = [p.Marks[0] with { Positions = [] }] };
        Assert.False(monitor.Observe(p, p.UpdatedUtc, true, []).Accepted);
        Assert.True(monitor.Start(p, p.UpdatedUtc, true, []).Accepted);
        Assert.False(monitor.Start(p, p.UpdatedUtc, true, []).Accepted);
        Assert.False(monitor.Observe(p, p.UpdatedUtc.AddMinutes(2), true, []).Accepted);
        Assert.False(monitor.Observe(p with { Marks = [] }, p.UpdatedUtc, true, []).Accepted);
    }

    [Fact]
    public void SourcePolicyChangeWithFreshEvidenceDoesNotResetRisk()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var p = Snapshot();
        var initial = monitor.Start(p, p.UpdatedUtc, true, []);
        var next = Advance(p, 95m, 15) with { PolicyVersionId = Guid.NewGuid() };
        var result = monitor.Observe(next, next.UpdatedUtc, true, []);
        Assert.True(result.Accepted);
        Assert.Equal(initial.Snapshot.State!.RoundId, result.Snapshot.State!.RoundId);
        Assert.Equal(0.5m, result.Assessment.NewBuyAllowanceMultiplier);
    }

    [Fact]
    public void NextSessionDoesNotRefillTheRound()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var p = Snapshot();
        var first = monitor.Start(p, p.UpdatedUtc, true, []);
        var at = ReviewedTsxSessionCalendar.At(Monday, new(9, 35));
        var next = p with { Revision = p.Revision + 1, CurrentSession = Monday, UpdatedUtc = at.AddMinutes(2),
            Marks = p.Marks.Add(new(Guid.NewGuid(), Monday, DelphiLivePortfolioMarkKind.Checkpoint, at, true, 9750m,
                [new(position, "TEST", 50, 95m, at)], "Complete")) };
        var result = monitor.Observe(next, next.UpdatedUtc, true, []);
        Assert.True(result.Accepted);
        Assert.Equal(first.Snapshot.State!.RoundId, result.Snapshot.State!.RoundId);
        Assert.Equal(10000m, result.Snapshot.State.RoundPeak);
        Assert.Equal(0.5m, result.Assessment.NewBuyAllowanceMultiplier);
    }

    [Fact]
    public void LiveAndOtherPaperAccountsCannotUseTheseSnapshots()
    {
        using (var live = new AiPortfolioRiskJournal(directory, Scope with { Mode = AiRiskAccountMode.Live }, policy, true))
            Assert.Throws<ArgumentException>(() => Monitor(live));
        using var other = new AiPortfolioRiskJournal(Path.Combine(directory, "other"), new(Guid.NewGuid(), portfolio, AiRiskAccountMode.Paper), policy, true);
        Assert.Throws<ArgumentException>(() => Monitor(other));
    }

    [Fact]
    public void DamagedJournalPropagatesAndCannotInitializeAgain()
    {
        using var journal = Journal();
        var monitor = Monitor(journal);
        var p = Snapshot();
        monitor.Start(p, p.UpdatedUtc, true, []);
        File.WriteAllText(Path.Combine(directory, "ai-risk.json.pending"), "interrupted");
        Assert.Throws<InvalidDataException>(() => monitor.Observe(p, p.UpdatedUtc, true, []));
        Assert.Throws<InvalidDataException>(() => monitor.Start(p, p.UpdatedUtc, true, []));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
