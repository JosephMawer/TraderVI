#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Core.Runtime;

public sealed record EngineResumePaperPendingRecovery(Guid RecoveryRunId,
    ImmutableArray<Guid> Incidents, DateTime ExpiresUtc, string Reason);

/// <summary>Temporary simulation permission, never an integrity resolution or financial grant.</summary>
public sealed record EnginePaperRecoveryContinuation(Guid RecoveryRunId, DateTime CreatedUtc,
    DateTime ExpiresUtc, string Reason, ImmutableArray<EngineIntegrityIncident> Incidents)
{
    public static EnginePaperRecoveryContinuation Prepare(EngineResumePaperPendingRecovery request,
        EngineIntegrityStatus status, EngineDatabaseIdentity database, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc || request.ExpiresUtc.Kind != DateTimeKind.Utc ||
            request.ExpiresUtc <= nowUtc || request.ExpiresUtc > nowUtc.AddHours(24) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500 ||
            request.Incidents.IsDefault || request.Incidents.Length != 2 || request.Incidents.Distinct().Count() != 2)
            throw new InvalidOperationException("Select the reboot and pending incidents with an explicit reason and expiry within 24 hours.");
        var run = status.Runs.SingleOrDefault(r => r.RunId == request.RecoveryRunId)
            ?? throw new InvalidOperationException("The selected recovery is missing.");
        run.Progress.Validate(run.Plan);
        if (run.Plan.Kind != EngineAuditKind.Recovery || run.Plan.Scope != EngineRecoveryScope.All ||
            run.Plan.Database != database || run.Status is not ("Running" or "Complete") ||
            status.Incidents.Length != 2 || status.Incidents.Any(i => !request.Incidents.Contains(i.IncidentId) ||
                i.Scope != EngineIntegrityScope.Global || i.OwnerId is not null))
            throw new InvalidOperationException("Only an exact reboot-only incident set and its current full recovery may permit temporary Paper continuation.");
        var reboot = status.Incidents.SingleOrDefault(i => i.FailureCode == "EngineRecoveryRequired");
        var pending = status.Incidents.SingleOrDefault(i => i.FailureCode == "RecoveryVerificationPending");
        if (reboot is null || !reboot.EvidenceKey.StartsWith("EngineContinuity/", StringComparison.Ordinal) ||
            !Guid.TryParseExact(reboot.EvidenceKey[17..], "D", out _) || reboot.DiscoveredUtc > run.Plan.BoundaryUtc ||
            pending is null || pending.EvidenceKey != "Recovery/" + run.RunId.ToString("D"))
            throw new InvalidOperationException("This is not the selected startup-continuity incident and its recovery-pending hold.");
        return new(run.RunId, nowUtc, request.ExpiresUtc, request.Reason.Trim(), status.Incidents);
    }

    public bool Matches(ImmutableArray<EngineIntegrityIncident> current, DateTime nowUtc) =>
        nowUtc.Kind == DateTimeKind.Utc && nowUtc >= CreatedUtc && nowUtc < ExpiresUtc &&
        current.Length == Incidents.Length && current.All(i => Incidents.Contains(i));

    public bool Allows(ImmutableArray<EngineIntegrityIncident> current, EngineIntegrityScope scope,
        DateTime nowUtc, bool paperSharedInputs = false) =>
        (scope == EngineIntegrityScope.Paper || paperSharedInputs && scope == EngineIntegrityScope.Global) && Matches(current, nowUtc);
}

/// <summary>Explicitly activated in the engine process; restart and expiry revoke it. No saved grant is edited.</summary>
public static class EnginePaperRecoveryContinuationGate
{
    private static EnginePaperRecoveryContinuation? current;
    public static EnginePaperRecoveryContinuation? Current => Volatile.Read(ref current);
    public static void Activate(EnginePaperRecoveryContinuation permission)
    {
        if (Interlocked.CompareExchange(ref current, permission, null) is not null)
            throw new InvalidOperationException("A Paper continuation was already issued in this process; inspect its receipt.");
    }
}
