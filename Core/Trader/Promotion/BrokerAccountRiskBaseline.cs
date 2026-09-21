#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

public enum BrokerRiskReferenceSource
{
    SetupObservation,
    ReportedSessionOpening,
    ReportedClosingHigh,
    RecommendationClosingHigh,
    VerifiedSessionOpening,
    VerifiedSessionClosing
}

/// <summary>A risk reference with its actual evidence boundary; setup is not a historical session open/close.</summary>
public sealed record BrokerRiskReference(decimal Value, BrokerRiskReferenceSource Source,
    Guid EvidenceId, DateTimeOffset EvidenceAt)
{
    public override string ToString() => $"Account risk reference ({Source}; financial data omitted)";
}

/// <summary>
/// Prepared initial references only. Persistence, session rollover, cash-flow adjustment, position
/// handover and execution eligibility are separate operations. Setup never releases carried holds
/// or changes symbol protection; a previously recorded explicit capital review remains authoritative.
/// </summary>
public sealed record BrokerAccountRiskBaseline(Guid BaselineId, Guid BindingId, string AccountLabel,
    Guid ImportRequestId, DateTimeOffset EstablishedAt, DateOnly TradingDate,
    BrokerRiskReference SessionReference, BrokerRiskReference DrawdownReference,
    Guid? AccountReportId, Guid? RecommendationBatchId, TradingRecommendationMemory CarriedMemory)
{
    /// <summary>The broker valuation's actual boundary, distinct from when setup was saved.</summary>
    public DateTimeOffset ObservationAt { get; init; }
    public override string ToString() => "Initial broker risk baseline (financial data omitted)";
}

/// <summary>
/// ReadComplete must describe a successful, consistent read of the effective account report,
/// recommendation memory and existing baseline. A failed read must never be represented as absent history.
/// </summary>
public sealed record BrokerBaselineRiskHistory(bool ReadComplete, ReportedTradingAccount? Report,
    TradingRecommendationBatch? LatestRecommendation, BrokerAccountRiskBaseline? ExistingBaseline)
{
    public override string ToString() => "Account risk history (financial data omitted)";
}

public sealed record BrokerRiskBaselinePreparation(BrokerAccountRiskBaseline? Baseline,
    ImmutableArray<string> Blocks)
{
    public bool CanEstablish => Baseline is not null && Blocks.IsEmpty;
    public override string ToString() => "Initial risk baseline preparation (no trading authority)";
}

/// <summary>
/// Offline initial-setup policy. Reuses the linked import checks and effective recommendation risk
/// state; it neither writes a baseline nor projects broker facts into the recommendation engine.
/// </summary>
public static class BrokerAccountRiskBaselinePolicy
{
    public static BrokerRiskBaselinePreparation Prepare(Guid baselineId, BrokerImportState imported,
        BrokerBaselineRiskHistory riskHistory, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(riskHistory);
        if (baselineId == Guid.Empty || now == default)
            throw new ArgumentException("Baseline preparation requires an identity and evaluation time.");

        var blocks = ImmutableArray.CreateBuilder<string>();
        if (!riskHistory.ReadComplete)
            blocks.Add("Read existing account risk history successfully before establishing a baseline.");
        if (riskHistory.ExistingBaseline is not null)
            blocks.Add("This account already has a risk baseline. Continue from its saved references; setup cannot reset it.");
        if (!imported.Installed || imported.Binding is null || imported.Latest is null)
            blocks.Add("A saved, linked broker observation is required before baseline setup.");
        if (blocks.Count > 0) return new(null, blocks.ToImmutable());

        var binding = imported.Binding!;
        var receipt = imported.Latest!;
        var observation = receipt.Plan.Observation;
        if (binding.BindingId == Guid.Empty || receipt.RequestId == Guid.Empty ||
            receipt.BindingId != binding.BindingId || receipt.Plan.AccountLabel != binding.AccountLabel ||
            receipt.Plan.BrokerKey != binding.BrokerKey || !receipt.Plan.CanImport ||
            binding.CreatedAt > receipt.SavedAt || receipt.SavedAt < observation.CheckedAt || receipt.SavedAt > now)
            blocks.Add("The saved broker observation and account binding are inconsistent.");
        // Import eligibility is necessary evidence for this initial setup, never sufficient for execution.
        blocks.AddRange(BrokerAccountImport.Preview(observation, imported, now).Blocks);
        if (observation.AccountValue is not > 0m)
            blocks.Add("A positive verified broker account value is required for loss references.");

        var tradingDate = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(observation.SnapshotAt.UtcDateTime)) != tradingDate)
            blocks.Add("Baseline setup requires an observation from the current Toronto date.");
        var report = riskHistory.Report;
        var previous = riskHistory.LatestRecommendation;
        if (report is not null)
        {
            report.Validate();
            if (report.AccountLabel != binding.AccountLabel || report.Currency != "CAD" ||
                report.ReportedUtc > now.UtcDateTime || report.TradingDate > tradingDate)
                blocks.Add("The risk report belongs to another account, currency or future boundary.");
        }
        if (previous is not null && (previous.BatchId == Guid.Empty || previous.AccountLabel != binding.AccountLabel ||
            previous.EvaluatedUtc.Kind != DateTimeKind.Utc || previous.EvaluatedUtc > now.UtcDateTime ||
            previous.Result.Memory.HighestClosingValue is <= 0m || previous.Result.Memory.TradingDate > tradingDate))
            blocks.Add("The recommendation risk state belongs to another account or has invalid references.");
        if (blocks.Count > 0) return new(null, blocks.Distinct().ToImmutableArray());

        bool reportIsLatest = report is not null && (previous is null || report.ReportedUtc > previous.EvaluatedUtc);
        bool reviewedRelease = reportIsLatest && !string.IsNullOrWhiteSpace(report!.CapitalReviewReason);
        if (reviewedRelease && (report!.HighestClosingValue is not > 0m || report.CapitalReviewRequired))
            return new(null, ["The recorded capital review has incomplete or conflicting risk references."]);

        // Keep the existing effective-state rule, including a separately recorded capital review.
        // Incomplete fields or an ordinary newer report cannot silently weaken known risk state.
        var memory = TradingRecommendationMemory.Reconcile(previous, report);
        var setup = new BrokerRiskReference(observation.AccountValue!.Value,
            BrokerRiskReferenceSource.SetupObservation, receipt.RequestId, observation.SnapshotAt);
        var session = report is { SessionOpeningValue: > 0m } && report.TradingDate == tradingDate
            ? new BrokerRiskReference(report.SessionOpeningValue.Value, BrokerRiskReferenceSource.ReportedSessionOpening,
                report.SnapshotId, report.ReportedUtc)
            : setup;
        BrokerRiskReference? reportedHigh = report?.HighestClosingValue is decimal reportValue
            ? new(reportValue, BrokerRiskReferenceSource.ReportedClosingHigh, report.SnapshotId, report.ReportedUtc) : null;
        BrokerRiskReference? evaluatedHigh = previous?.Result.Memory.HighestClosingValue is decimal previousValue
            ? new(previousValue, BrokerRiskReferenceSource.RecommendationClosingHigh, previous.BatchId, previous.EvaluatedUtc) : null;
        var knownHigh = reviewedRelease ? reportedHigh : new[] { reportedHigh, evaluatedHigh }
            .OfType<BrokerRiskReference>().OrderByDescending(r => r.Value).ThenByDescending(r => r.EvidenceAt).FirstOrDefault();
        var drawdown = knownHigh ?? setup;
        bool capitalHold = reviewedRelease ? memory.CapitalReviewRequired :
            memory.CapitalReviewRequired || report?.CapitalReviewRequired == true || previous?.Result.Memory.CapitalReviewRequired == true;
        bool dailyHold = memory.DailyLossHold ||
            (report?.TradingDate == memory.TradingDate && report?.DailyLossHold == true) ||
            (previous?.Result.Memory.TradingDate == memory.TradingDate && previous?.Result.Memory.DailyLossHold == true);
        if (memory.CapitalReviewRequired != capitalHold || memory.DailyLossHold != dailyHold ||
            (knownHigh is not null && memory.HighestClosingValue != knownHigh.Value))
            memory = memory with { CapitalReviewRequired = capitalHold, DailyLossHold = dailyHold,
                HighestClosingValue = knownHigh?.Value ?? memory.HighestClosingValue };

        return new(new(baselineId, binding.BindingId, binding.AccountLabel, receipt.RequestId, now, tradingDate,
            session, drawdown, report?.SnapshotId, previous?.BatchId, memory) { ObservationAt = observation.SnapshotAt }, []);
    }
}
