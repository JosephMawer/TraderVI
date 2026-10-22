#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace Core.Runtime;

public enum EngineIntegrityScope { Global, Broker, Paper, Research }
public sealed record EngineIntegrityIncident(Guid IncidentId, EngineIntegrityScope Scope, Guid? OwnerId,
    string EvidenceKey, string FailureCode, DateTime DiscoveredUtc, bool ProtectionUntrusted)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? OriginDiscoveredUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginEvidenceKey { get; init; }
}
public sealed record EngineIntegrityAccess(bool NewExposureAllowed, bool ProtectionAllowed, ImmutableArray<Guid> Incidents)
{
    public static EngineIntegrityAccess Evaluate(ImmutableArray<EngineIntegrityIncident> open, EngineIntegrityScope scope, Guid? owner)
    {
        var affected = open.Where(i => i.Scope == EngineIntegrityScope.Global ||
            (i.Scope == scope || scope == EngineIntegrityScope.Research && i.Scope == EngineIntegrityScope.Paper) &&
            (i.OwnerId is null || owner is null || i.OwnerId == owner)).ToArray();
        return new(affected.Length == 0, affected.All(i => !i.ProtectionUntrusted), affected.Select(i => i.IncidentId).ToImmutableArray());
    }
}

public enum EngineAuditKind { NewHistory, Archive, Recovery }
public enum EngineRecoveryScope { All, Real, Paper }
public enum EngineAuditFamily { PaperRevisions, BrokerRevisions, SharedInputs, SharedContent, LegacyInputs }
public sealed record EngineAuditTarget(EngineAuditFamily Family, Guid? OwnerId, long FromRevision, long ThroughRevision);
public sealed record EngineAuditPlan(EngineAuditKind Kind, DateTime BoundaryUtc, ImmutableArray<EngineAuditTarget> Targets)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public EngineRecoveryScope Scope { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EngineDatabaseIdentity? Database { get; init; }
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Scope) || Scope != EngineRecoveryScope.All && Kind != EngineAuditKind.Recovery ||
            Scope != EngineRecoveryScope.All && Database is null ||
            BoundaryUtc.Kind != DateTimeKind.Utc || BoundaryUtc == default || Targets.IsDefault || Targets.Length > 4096 ||
            Targets.Any(t => !Enum.IsDefined(t.Family) || t.FromRevision < 0 || t.ThroughRevision < 0 || t.ThroughRevision == long.MaxValue ||
                t.FromRevision > t.ThroughRevision + 1 ||
                (t.Family is EngineAuditFamily.PaperRevisions or EngineAuditFamily.BrokerRevisions) != t.OwnerId.HasValue || t.OwnerId == Guid.Empty) ||
            Targets.Select(t => (t.Family, t.OwnerId)).Distinct().Count() != Targets.Length)
            throw new InvalidOperationException("Historical audit boundary is invalid.");
        Database?.Validate();
        if (Targets.Any(t => !Includes(Scope, t.Family)))
            throw new InvalidOperationException("A recovery plan contains another domain's history.");
        if (Scope == EngineRecoveryScope.Real && !Targets.Any(t => t.Family == EngineAuditFamily.BrokerRevisions))
            throw new InvalidOperationException("Real recovery requires an existing managed account.");
    }
    public static bool Includes(EngineRecoveryScope scope, EngineAuditFamily family) => scope switch
    {
        EngineRecoveryScope.All => true,
        EngineRecoveryScope.Real => family is EngineAuditFamily.BrokerRevisions or EngineAuditFamily.SharedInputs or EngineAuditFamily.SharedContent,
        EngineRecoveryScope.Paper => family is EngineAuditFamily.PaperRevisions or EngineAuditFamily.LegacyInputs,
        _ => false
    };
}
public sealed record EngineAuditProgress(int TargetIndex, long NextRevision, string? LastKey, long VerifiedUnits, bool Complete)
{
    public static EngineAuditProgress Start(EngineAuditPlan plan) => new(0, plan.Targets.IsEmpty ? 0 : plan.Targets[0].FromRevision, null, 0, plan.Targets.IsEmpty);
    public void Validate(EngineAuditPlan plan)
    {
        plan.Validate();
        if (TargetIndex < 0 || TargetIndex > plan.Targets.Length || VerifiedUnits < 0 || Complete != (TargetIndex == plan.Targets.Length) ||
            !Complete && (NextRevision < plan.Targets[TargetIndex].FromRevision || NextRevision > plan.Targets[TargetIndex].ThroughRevision + 1))
            throw new InvalidOperationException("Historical audit progress is inconsistent with its fixed boundary.");
    }
    public EngineAuditProgress Verified(EngineAuditPlan plan, long nextRevision, string? lastKey)
    {
        Validate(plan);
        if (Complete || nextRevision < NextRevision || nextRevision > plan.Targets[TargetIndex].ThroughRevision + 1 ||
            nextRevision == NextRevision && (lastKey is null || lastKey == LastKey))
            throw new InvalidOperationException("An audit receipt must advance exactly verified work.");
        return this with { NextRevision = nextRevision, LastKey = lastKey, VerifiedUnits = checked(VerifiedUnits + 1) };
    }
    public EngineAuditProgress NextTarget(EngineAuditPlan plan)
    {
        Validate(plan);
        if (Complete) throw new InvalidOperationException("Completed audits cannot resume.");
        int next = TargetIndex + 1;
        return this with { TargetIndex = next, NextRevision = next == plan.Targets.Length ? 0 : plan.Targets[next].FromRevision,
            LastKey = null, Complete = next == plan.Targets.Length };
    }
}

public sealed record EngineAuditRun(Guid RunId, EngineAuditPlan Plan, EngineAuditProgress Progress,
    long Revision, string Status, DateTime UpdatedUtc);
public sealed record EngineIntegrityStatus(ImmutableArray<EngineIntegrityIncident> Incidents,
    ImmutableArray<EngineAuditRun> Runs)
{
    public string Summary(DateTime now)
    {
        int incomplete = Runs.Count(r => r.Status != "Complete");
        var last = Runs.Where(r => r.Plan.Kind == EngineAuditKind.Archive && r.Status == "Complete")
            .Select(r => (DateTime?)r.Plan.BoundaryUtc).Max();
        return $"Integrity: {Incidents.Length} unresolved incident(s); {incomplete} incomplete latest audit(s). " +
            (last is null ? "Older archive has no completed audit." :
                $"Older archive verified through {last:yyyy-MM-dd HH:mm} UTC" + (now - last > TimeSpan.FromDays(7) ? " (overdue)." : "."));
    }
}

public static class EngineRecoveryPolicy
{
    public static void RequireClear(EngineAuditRun run, ImmutableArray<EngineIntegrityIncident> open,
        ImmutableArray<Guid> selected, ImmutableArray<EngineAuditTarget> currentHeads,
        EngineRecoveryScope scope = EngineRecoveryScope.All)
    {
        run.Progress.Validate(run.Plan);
        if (run.Status != "Complete" || !run.Progress.Complete || run.Plan.Kind != EngineAuditKind.Recovery || selected.IsDefaultOrEmpty ||
            selected.Distinct().Count() != selected.Length)
            throw new InvalidOperationException("Complete explicit recovery verification and select original incidents before clearing a hold.");
        if (!Enum.IsDefined(scope) || scope == EngineRecoveryScope.All && run.Plan.Scope != EngineRecoveryScope.All ||
            scope == EngineRecoveryScope.Real && run.Plan.Scope != EngineRecoveryScope.Real ||
            scope == EngineRecoveryScope.Paper && run.Plan.Scope == EngineRecoveryScope.Real)
            throw new InvalidOperationException("The completed recovery does not qualify the requested domain.");
        if (selected.Any(id => !open.Any(i => i.IncidentId == id && (DiscoveryBoundary(i) <= run.Plan.BoundaryUtc ||
            i.EvidenceKey == "Recovery/" + run.RunId.ToString("D") ||
            i.OriginDiscoveredUtc is not null && i.OriginEvidenceKey == "Recovery/" + run.RunId.ToString("D")))))
            throw new InvalidOperationException("An incident is absent, cleared, or newer than this recovery proof.");
        if (!currentHeads.SequenceEqual(run.Plan.Targets.Where(t => EngineAuditPlan.Includes(scope, t.Family))))
            throw new InvalidOperationException("History advanced after recovery verification. Verify the new state before clearing.");
        foreach (var incident in open.Where(i => selected.Contains(i.IncidentId)))
        {
            if (scope == EngineRecoveryScope.Real) _ = RetainedPaperHold(incident);
            if (scope == EngineRecoveryScope.Paper && incident.Scope is not (EngineIntegrityScope.Paper or EngineIntegrityScope.Research))
                throw new InvalidOperationException("Paper recovery cannot release a real-account or shared integrity hold.");
        }
    }

    // A completed real proof can narrow known recovery findings, never dismiss unrelated uncertainty.
    // The repository atomically retains the paper restriction before resolving the original global row.
    public static bool RetainedPaperHold(EngineIntegrityIncident incident)
    {
        if (incident.Scope == EngineIntegrityScope.Broker)
        {
            if (incident.FailureCode == "RecoveryVerificationPending" && incident.EvidenceKey.StartsWith("Recovery/", StringComparison.Ordinal) ||
                incident.FailureCode == "CurrentSubscriptionInvalid" && incident.EvidenceKey.StartsWith("RealObservation/", StringComparison.Ordinal) ||
                incident.FailureCode == "ArchiveVerificationFailed" && incident.EvidenceKey.StartsWith("BrokerRevisions/", StringComparison.Ordinal)) return false;
            throw new InvalidOperationException("The real-account recovery verifier does not qualify this unknown finding.");
        }
        if (incident.Scope != EngineIntegrityScope.Global)
            throw new InvalidOperationException("Real recovery cannot release paper or research findings.");
        if ((incident.FailureCode is "EngineRecoveryRequired" or "EngineContinuityLost") &&
            incident.EvidenceKey.StartsWith("EngineContinuity/", StringComparison.Ordinal) ||
            incident.FailureCode == "RecoveryVerificationPending" && incident.EvidenceKey.StartsWith("Recovery/", StringComparison.Ordinal) ||
            incident.FailureCode == "AuditReceiptUnverifiable" && incident.EvidenceKey == "AuditReceipt") return true;
        if (incident.FailureCode == "ArchiveVerificationFailed" &&
            incident.EvidenceKey.StartsWith("PaperRevisions/", StringComparison.Ordinal)) return true;
        throw new InvalidOperationException("This shared or unknown finding requires full recovery; real-only verification cannot clear it.");
    }
    private static DateTime DiscoveryBoundary(EngineIntegrityIncident incident)
    {
        if (incident.OriginDiscoveredUtc is not DateTime origin) return incident.DiscoveredUtc;
        if (incident.Scope != EngineIntegrityScope.Paper || incident.FailureCode != "PaperRecoveryRequired" ||
            !incident.EvidenceKey.StartsWith("ScopedFrom/", StringComparison.Ordinal) ||
            !Guid.TryParseExact(incident.EvidenceKey[11..], "D", out _) || origin > incident.DiscoveredUtc)
            throw new InvalidOperationException("Invalid inherited recovery finding.");
        return origin;
    }
}
