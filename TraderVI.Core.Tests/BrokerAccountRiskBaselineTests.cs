#nullable enable
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerAccountRiskBaselineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 9, 17);
    private static readonly string Hash = new('A', 64);
    private static BrokerBaselineRiskHistory EmptyHistory => new(true, null, null, null);

    [Fact]
    public void FirstSetupNeedsNoWaitingPeriodAndLabelsObservedRatherThanHistoricalReferences()
    {
        var imported = Imported();
        var result = Prepare(imported);
        Assert.True(result.CanEstablish, string.Join("; ", result.Blocks));
        var baseline = result.Baseline!;
        Assert.Equal(10000m, baseline.SessionReference.Value);
        Assert.Equal(baseline.SessionReference, baseline.DrawdownReference);
        Assert.Equal(BrokerRiskReferenceSource.SetupObservation, baseline.SessionReference.Source);
        Assert.Equal(imported.Latest!.RequestId, baseline.SessionReference.EvidenceId);
        Assert.Equal(imported.Latest.Plan.Observation.SnapshotAt, baseline.SessionReference.EvidenceAt);
        Assert.Equal(Now, baseline.EstablishedAt);
        Assert.Equal(imported.Binding!.BindingId, baseline.BindingId);
        Assert.Equal(Date, baseline.TradingDate);
        Assert.Null(baseline.AccountReportId);
        Assert.Same(TradingRecommendationMemory.Empty, baseline.CarriedMemory);
    }

    [Fact]
    public void ExistingRealReferencesAreNotReplacedByCurrentValueOrPaperCapital()
    {
        var report = Report() with { SessionOpeningValue = 11000m, HighestClosingValue = 13000m };
        var baseline = Prepare(risk: EmptyHistory with { Report = report }).Baseline!;
        Assert.Equal(11000m, baseline.SessionReference.Value);
        Assert.Equal(13000m, baseline.DrawdownReference.Value);
        Assert.Equal(BrokerRiskReferenceSource.ReportedSessionOpening, baseline.SessionReference.Source);
        Assert.Equal(BrokerRiskReferenceSource.ReportedClosingHigh, baseline.DrawdownReference.Source);
        Assert.Equal(report.SnapshotId, baseline.DrawdownReference.EvidenceId);
    }

    [Fact]
    public void RiskHoldsAndPositionProtectionSurviveWithoutNewOwnershipOrRebasing()
    {
        var protection = new SystemShadowTrailingState(100m, 125m, true, 118m, Now.AddMinutes(-30).UtcDateTime);
        var symbol = new TradingSymbolState(Guid.NewGuid(), protection, null, null, null, null, null, ExitReason: "TrailingProfit");
        var memory = TradingRecommendationMemory.Empty with
        {
            TradingDate = Date, DailyLossHold = true, CapitalReviewRequired = true, HighestClosingValue = 14000m,
            Symbols = ImmutableDictionary<string, TradingSymbolState>.Empty.Add("FIX", symbol)
        };
        var batch = Batch(memory);
        var baseline = Prepare(risk: EmptyHistory with { LatestRecommendation = batch }).Baseline!;
        Assert.Same(memory, baseline.CarriedMemory);
        Assert.Same(symbol, baseline.CarriedMemory.Symbols["FIX"]);
        Assert.Equal(14000m, baseline.DrawdownReference.Value);
        Assert.Equal(BrokerRiskReferenceSource.RecommendationClosingHigh, baseline.DrawdownReference.Source);
        Assert.Equal(batch.BatchId, baseline.DrawdownReference.EvidenceId);
        Assert.Equal(BrokerRiskReferenceSource.SetupObservation, baseline.SessionReference.Source);
        // A prepared reference can coexist with a hold; preparation is not permission to buy.
        Assert.True(baseline.CarriedMemory.DailyLossHold);
        Assert.True(baseline.CarriedMemory.CapitalReviewRequired);
    }

    [Fact]
    public void SetupDoesNotPerformOrdinaryDailyHoldRollover()
    {
        var memory = TradingRecommendationMemory.Empty with { TradingDate = Date.AddDays(-1), DailyLossHold = true };
        var baseline = Prepare(risk: EmptyHistory with { LatestRecommendation = Batch(memory) }).Baseline!;
        Assert.Same(memory, baseline.CarriedMemory);
        Assert.True(baseline.CarriedMemory.DailyLossHold);
    }

    [Fact]
    public void APreviouslyReviewedCapitalReleaseIsNotUndoneByAnOlderBatch()
    {
        var batch = Batch(TradingRecommendationMemory.Empty with { CapitalReviewRequired = true, HighestClosingValue = 15000m });
        var report = Report() with { ReportedUtc = Now.AddMinutes(-1).UtcDateTime, HighestClosingValue = 9000m,
            CapitalReviewRequired = false, CapitalReviewReason = "Synthetic explicit prior capital review" };
        var baseline = Prepare(risk: EmptyHistory with { Report = report, LatestRecommendation = batch }).Baseline!;
        Assert.False(baseline.CarriedMemory.CapitalReviewRequired);
        Assert.Equal(9000m, baseline.DrawdownReference.Value);
        Assert.Equal(report.SnapshotId, baseline.DrawdownReference.EvidenceId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ANewerOrdinaryRecordCannotEraseKnownSameSessionHoldsOrLowerTheHigh(bool newerReport)
    {
        var report = Report() with { ReportedUtc = Now.AddMinutes(newerReport ? -1 : -5).UtcDateTime,
            HighestClosingValue = newerReport ? 9000m : 15000m,
            DailyLossHold = !newerReport, CapitalReviewRequired = !newerReport };
        var batch = Batch(TradingRecommendationMemory.Empty with { TradingDate = Date,
            HighestClosingValue = newerReport ? 15000m : 9000m,
            DailyLossHold = newerReport, CapitalReviewRequired = newerReport });
        var baseline = Prepare(risk: EmptyHistory with { Report = report, LatestRecommendation = batch }).Baseline!;
        Assert.Equal(15000m, baseline.DrawdownReference.Value);
        Assert.Equal(15000m, baseline.CarriedMemory.HighestClosingValue);
        Assert.True(baseline.CarriedMemory.DailyLossHold);
        Assert.True(baseline.CarriedMemory.CapitalReviewRequired);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingReferenceInANewerRecordDoesNotHideAKnownHigh(bool newerReport)
    {
        var report = Report() with { HighestClosingValue = newerReport ? null : 15000m,
            ReportedUtc = Now.AddMinutes(newerReport ? -1 : -5).UtcDateTime };
        var batch = Batch(TradingRecommendationMemory.Empty with { HighestClosingValue = newerReport ? 15000m : null });
        var baseline = Prepare(risk: EmptyHistory with { Report = report, LatestRecommendation = batch }).Baseline!;
        Assert.Equal(15000m, baseline.DrawdownReference.Value);
        Assert.Equal(newerReport ? batch.BatchId : report.SnapshotId, baseline.DrawdownReference.EvidenceId);
    }

    [Fact]
    public void OldSessionValueIsNotPresentedAsTodaysOpeningValue()
    {
        var report = Report() with { TradingDate = Date.AddDays(-1), SessionOpeningValue = 12000m, HighestClosingValue = 14000m };
        var baseline = Prepare(risk: EmptyHistory with { Report = report }).Baseline!;
        Assert.Equal(BrokerRiskReferenceSource.SetupObservation, baseline.SessionReference.Source);
        Assert.Equal(10000m, baseline.SessionReference.Value);
        Assert.Equal(14000m, baseline.DrawdownReference.Value);
    }

    [Fact]
    public void FailedHistoryReadCannotBeTreatedAsNoPriorBaseline()
    {
        var result = Prepare(risk: EmptyHistory with { ReadComplete = false });
        Assert.False(result.CanEstablish);
        Assert.Null(result.Baseline);
        Assert.Contains(result.Blocks, b => b.Contains("successfully"));
    }

    [Fact]
    public void RepeatedSetupCannotResetAnEstablishedBaseline()
    {
        var first = Prepare().Baseline!;
        var result = Prepare(risk: EmptyHistory with { ExistingBaseline = first });
        Assert.False(result.CanEstablish);
        Assert.Null(result.Baseline);
        Assert.Equal(10000m, first.DrawdownReference.Value);
        Assert.Contains(result.Blocks, b => b.Contains("cannot reset"));
    }

    [Theory]
    [InlineData("uninstalled")]
    [InlineData("unlinked")]
    [InlineData("missing import")]
    [InlineData("wrong binding")]
    [InlineData("different broker")]
    [InlineData("changed ledger")]
    [InlineData("pending order")]
    [InlineData("history conflict")]
    [InlineData("future save")]
    [InlineData("stale")]
    [InlineData("zero value")]
    public void UnverifiedBrokerFactsCannotInitializeRisk(string problem)
    {
        var imported = Imported();
        imported = problem switch
        {
            "uninstalled" => imported with { Installed = false },
            "unlinked" => imported with { Binding = null },
            "missing import" => imported with { Latest = null },
            "wrong binding" => imported with { Binding = imported.Binding! with { BindingId = Guid.NewGuid() } },
            "different broker" => imported with { Binding = imported.Binding! with { BrokerKey = new string('B', 64) } },
            "changed ledger" => imported with { Legacy = imported.Legacy with { Fingerprint = new string('B', 64) } },
            "future save" => imported with { Latest = imported.Latest! with { SavedAt = Now.AddSeconds(1) } },
            "pending order" => WithObservation(imported, imported.Latest!.Plan.Observation with { Blocks = ["Outstanding broker order."] }),
            "history conflict" => WithObservation(imported, imported.Latest!.Plan.Observation with { HistoryConflicts = ["Changed prior order."] }),
            "zero value" => WithObservation(imported, imported.Latest!.Plan.Observation with { AccountValue = 0m }),
            _ => WithObservation(imported, imported.Latest!.Plan.Observation with { SnapshotAt = Now.AddMinutes(-2).AddTicks(-1) })
        };
        Assert.False(Prepare(imported).CanEstablish);
        Assert.Null(Prepare(imported).Baseline);
    }

    [Theory]
    [InlineData("report account")]
    [InlineData("report currency")]
    [InlineData("report future")]
    [InlineData("batch account")]
    [InlineData("batch future")]
    [InlineData("invalid high")]
    public void RiskEvidenceMustBelongToTheAccountAndAlreadyExist(string problem)
    {
        var report = Report();
        var batch = Batch(TradingRecommendationMemory.Empty);
        switch (problem)
        {
            case "report account": report = report with { AccountLabel = "RRSP" }; break;
            case "report currency": report = report with { Currency = "USD" }; break;
            case "report future": report = report with { ReportedUtc = Now.AddTicks(1).UtcDateTime }; break;
            case "batch account": batch = batch with { AccountLabel = "RRSP" }; break;
            case "batch future": batch = batch with { EvaluatedUtc = Now.AddTicks(1).UtcDateTime }; break;
            default: batch = Batch(TradingRecommendationMemory.Empty with { HighestClosingValue = -1m }); break;
        }
        Assert.False(Prepare(risk: EmptyHistory with { Report = report, LatestRecommendation = batch }).CanEstablish);
    }

    [Fact]
    public void FreshnessIncludesTheBoundaryButDoesNotCrossTheTorontoDate()
    {
        var imported = Imported();
        imported = WithObservation(imported, imported.Latest!.Plan.Observation with { SnapshotAt = Now.AddMinutes(-2) });
        Assert.True(Prepare(imported).CanEstablish);
        var midnight = new DateTimeOffset(2026, 9, 18, 4, 0, 5, TimeSpan.Zero);
        var overnight = Imported(midnight);
        Assert.False(BrokerAccountRiskBaselinePolicy.Prepare(Guid.NewGuid(), overnight, EmptyHistory, midnight).CanEstablish);
    }

    [Fact]
    public void PreparationLeavesTheImportReportsPositionsAndProtectionUntouched()
    {
        var imported = Imported();
        var risk = EmptyHistory with { Report = Report(), LatestRecommendation = Batch(TradingRecommendationMemory.Empty) };
        string before = JsonSerializer.Serialize(new { imported, risk });
        var result = Prepare(imported, risk);
        Assert.True(result.CanEstablish);
        Assert.Equal(before, JsonSerializer.Serialize(new { imported, risk }));
        Assert.DoesNotContain("10000", result.Baseline!.ToString());
        Assert.DoesNotContain("10000", result.Baseline.SessionReference.ToString());
        var recovered = JsonSerializer.Deserialize<BrokerAccountRiskBaseline>(JsonSerializer.Serialize(result.Baseline))!;
        Assert.Equal(result.Baseline.SessionReference, recovered.SessionReference);
        Assert.Equal(result.Baseline.DrawdownReference, recovered.DrawdownReference);
        Assert.Equal(result.Baseline.ImportRequestId, recovered.ImportRequestId);
    }

    private static BrokerRiskBaselinePreparation Prepare(BrokerImportState? imported = null, BrokerBaselineRiskHistory? risk = null) =>
        BrokerAccountRiskBaselinePolicy.Prepare(Guid.NewGuid(), imported ?? Imported(), risk ?? EmptyHistory, Now);

    private static BrokerImportState Imported(DateTimeOffset? at = null)
    {
        var now = at ?? Now;
        var observation = new BrokerAccountReconciliation("fixture-identity", "fixture-account", now.AddSeconds(-9),
            now.AddSeconds(-10), 10000m, 9900m, 10000m, [], [], [], []) { ActivityFingerprint = Hash };
        var empty = new BrokerImportState(true, new("TFSA", Hash, [], []), null, null);
        var plan = BrokerAccountImport.Preview(observation, empty, now);
        var binding = new BrokerImportBinding(Guid.NewGuid(), "TFSA", plan.BrokerKey, Hash, now.AddHours(-1), "fixture");
        return empty with { Binding = binding, Latest = new(Guid.NewGuid(), binding.BindingId, Hash, now.AddSeconds(-5), plan) };
    }

    private static BrokerImportState WithObservation(BrokerImportState imported, BrokerAccountReconciliation observation) =>
        imported with { Latest = imported.Latest! with { Plan = imported.Latest.Plan with { Observation = observation } } };

    private static ReportedTradingAccount Report() => new(Guid.NewGuid(), "TFSA", "CAD", Date,
        Now.AddMinutes(-5).UtcDateTime, "fixture", Hash, 9000m, 10000m, 11000m, 13000m, 0, false, false, []);

    private static TradingRecommendationBatch Batch(TradingRecommendationMemory memory) =>
        new(Guid.NewGuid(), "TFSA", Guid.NewGuid(), null, null, Now.AddMinutes(-2).UtcDateTime, Hash,
            new("Synthetic risk evidence", [], [], memory));
}
