#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

/// <summary>Deterministic judgment reconstructed by the successor evaluator from an explicit evidence set.</summary>
public sealed record DelphiLiveRecoveredJudgment(string PolicyIdentity, string SourceContract, string Symbol,
    DateTime SessionOpenUtc, DateTime EndUtc, DateTime CalculatedUtc,
    bool Valid, bool Mature, bool NormalConfidence, bool SafetyVeto, int SupportiveFamilies,
    ImmutableArray<Guid> StockObservationIds, ImmutableArray<Guid> BenchmarkObservationIds);

public enum DelphiLiveRecoveredConfirmationStatus
{
    Confirmed, IdentityMismatch, NonConsecutive, InvalidEvidence, FutureEvidence, ExpiredLatest, NotQualifying
}

/// <summary>Signal qualification only: broker permission, cutoff, orders, cash and protection remain separate.</summary>
public sealed record DelphiLiveRecoveredConfirmationResult(DelphiLiveRecoveredConfirmationStatus Status,
    DateTime DecidedUtc, DateTime LatestEndUtc, DateTime? ExpiresUtc, ImmutableArray<Guid> RecoveredObservationIds);

/// <summary>
/// Offline ADR-0111 successor boundary. Restored judgments may establish a pair without two extra waits.
/// No process-start time enters the calculation; actual input availability and latest expiry do.
/// Call again at submission, and keep ordinary execution/idempotency checks. Not wired to Real yet.
/// </summary>
public static class DelphiLiveRecoveredConfirmation
{
    public static DelphiLiveRecoveredConfirmationResult Assess(string approvedPolicyIdentity,
        DelphiLiveMinuteEvidenceBook stock, DelphiLiveMinuteEvidenceBook benchmark,
        DelphiLiveRecoveredJudgment previous, DelphiLiveRecoveredJudgment latest, DateTime nowUtc)
    {
        DelphiLiveMinuteEvidence.Validate(stock);
        DelphiLiveMinuteEvidence.Validate(benchmark);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(latest);
        DelphiLiveFiveMinuteBar.RequireUtc(nowUtc, nameof(nowUtc));
        if (string.IsNullOrWhiteSpace(approvedPolicyIdentity)) throw new ArgumentException("A reviewed policy identity is required.");
        DelphiLiveRecoveredConfirmationResult Reject(DelphiLiveRecoveredConfirmationStatus reason) =>
            new(reason, nowUtc, latest.EndUtc, null, []);
        if (benchmark.Symbol != "XIU" || stock.SourceContract != benchmark.SourceContract ||
            stock.SessionOpenUtc != benchmark.SessionOpenUtc || stock.SessionCloseUtc != benchmark.SessionCloseUtc ||
            new[] { previous, latest }.Any(j => j.PolicyIdentity != approvedPolicyIdentity ||
                j.SourceContract != stock.SourceContract || j.Symbol != stock.Symbol || j.SessionOpenUtc != stock.SessionOpenUtc))
            return Reject(DelphiLiveRecoveredConfirmationStatus.IdentityMismatch);
        foreach (var j in new[] { previous, latest })
        {
            DelphiLiveFiveMinuteBar.RequireUtc(j.EndUtc, nameof(j.EndUtc));
            DelphiLiveFiveMinuteBar.RequireUtc(j.CalculatedUtc, nameof(j.CalculatedUtc));
            if (j.EndUtc.Ticks % TimeSpan.TicksPerMinute != 0 || j.EndUtc <= stock.SessionOpenUtc ||
                j.EndUtc > stock.SessionCloseUtc || j.SupportiveFamilies is < 0 or > 4)
                return Reject(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence);
            if (j.CalculatedUtc <= j.EndUtc || j.CalculatedUtc > nowUtc)
                return Reject(DelphiLiveRecoveredConfirmationStatus.FutureEvidence);
        }
        if (latest.EndUtc - previous.EndUtc != TimeSpan.FromMinutes(1))
            return Reject(DelphiLiveRecoveredConfirmationStatus.NonConsecutive);
        if (!stock.ConflictingStartsUtc.IsEmpty || !benchmark.ConflictingStartsUtc.IsEmpty)
            return Reject(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence);
        foreach (var j in new[] { previous, latest })
        {
            if (!InputsMatch(stock, j.StockObservationIds, j) || !InputsMatch(benchmark, j.BenchmarkObservationIds, j))
                return Reject(DelphiLiveRecoveredConfirmationStatus.InvalidEvidence);
        }
        var stockNow = DelphiLiveMinuteEvidence.AvailableAt(stock, latest.EndUtc, nowUtc);
        var benchmarkNow = DelphiLiveMinuteEvidence.AvailableAt(benchmark, latest.EndUtc, nowUtc);
        if (stockNow is null || benchmarkNow is null || nowUtc > stock.SessionCloseUtc)
            return Reject(DelphiLiveRecoveredConfirmationStatus.ExpiredLatest);
        if (new[] { previous, latest }.Any(j => !j.Valid || !j.Mature || !j.NormalConfidence ||
            j.SafetyVeto || j.SupportiveFamilies < 3))
            return Reject(DelphiLiveRecoveredConfirmationStatus.NotQualifying);
        var recovered = stock.Observations.Where(o => !o.ObservedOnTime &&
                (previous.StockObservationIds.Contains(o.ObservationId) || latest.StockObservationIds.Contains(o.ObservationId)))
            .Concat(benchmark.Observations.Where(o => !o.ObservedOnTime &&
                (previous.BenchmarkObservationIds.Contains(o.ObservationId) || latest.BenchmarkObservationIds.Contains(o.ObservationId))))
            .Select(o => o.ObservationId).Distinct().ToImmutableArray();
        return new(DelphiLiveRecoveredConfirmationStatus.Confirmed, nowUtc, latest.EndUtc,
            stockNow.DeadlineUtc < benchmarkNow.DeadlineUtc ? stockNow.DeadlineUtc : benchmarkNow.DeadlineUtc, recovered);
    }

    private static bool InputsMatch(DelphiLiveMinuteEvidenceBook book, ImmutableArray<Guid> ids,
        DelphiLiveRecoveredJudgment judgment)
    {
        if (ids.IsDefaultOrEmpty || ids.Distinct().Count() != ids.Length) return false;
        var rows = book.Observations.Where(o => ids.Contains(o.ObservationId)).ToArray();
        return rows.Length == ids.Length && rows.Any(o => o.EndUtc == judgment.EndUtc) &&
            rows.All(o => o.EndUtc <= judgment.EndUtc && o.CheckedUtc <= judgment.CalculatedUtc);
        // Full indicator-window maturity remains the evaluator's job, not inferred from ID count.
    }
}
