#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Runtime;

public enum EngineIntegrityScope { Global, Broker, Paper, Research }
public sealed record EngineIntegrityIncident(Guid IncidentId, EngineIntegrityScope Scope, Guid? OwnerId,
    string EvidenceKey, string FailureCode, DateTime DiscoveredUtc, bool ProtectionUntrusted);
public sealed record EngineIntegrityAccess(bool NewExposureAllowed, bool ProtectionAllowed, ImmutableArray<Guid> Incidents)
{
    public static EngineIntegrityAccess Evaluate(ImmutableArray<EngineIntegrityIncident> open, EngineIntegrityScope scope, Guid? owner)
    {
        var affected = open.Where(i => i.Scope == EngineIntegrityScope.Global ||
            i.Scope == scope && (i.OwnerId is null || i.OwnerId == owner)).ToArray();
        return new(affected.Length == 0, affected.All(i => !i.ProtectionUntrusted), affected.Select(i => i.IncidentId).ToImmutableArray());
    }
}

public enum EngineAuditKind { NewHistory, Archive, Recovery }
public enum EngineAuditFamily { PaperRevisions, BrokerRevisions, SharedInputs, SharedContent, LegacyInputs }
public sealed record EngineAuditTarget(EngineAuditFamily Family, Guid? OwnerId, long FromRevision, long ThroughRevision);
public sealed record EngineAuditPlan(EngineAuditKind Kind, DateTime BoundaryUtc, ImmutableArray<EngineAuditTarget> Targets)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || BoundaryUtc.Kind != DateTimeKind.Utc || BoundaryUtc == default || Targets.IsDefault || Targets.Length > 4096 ||
            Targets.Any(t => !Enum.IsDefined(t.Family) || t.FromRevision < 0 || t.ThroughRevision < 0 || t.ThroughRevision == long.MaxValue ||
                t.FromRevision > t.ThroughRevision + 1 ||
                (t.Family is EngineAuditFamily.PaperRevisions or EngineAuditFamily.BrokerRevisions) != t.OwnerId.HasValue || t.OwnerId == Guid.Empty) ||
            Targets.Select(t => (t.Family, t.OwnerId)).Distinct().Count() != Targets.Length)
            throw new InvalidOperationException("Historical audit boundary is invalid.");
    }
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
        ImmutableArray<Guid> selected, ImmutableArray<EngineAuditTarget> currentHeads)
    {
        run.Progress.Validate(run.Plan);
        if (run.Status != "Complete" || !run.Progress.Complete || run.Plan.Kind != EngineAuditKind.Recovery || selected.IsDefaultOrEmpty ||
            selected.Distinct().Count() != selected.Length)
            throw new InvalidOperationException("Complete explicit recovery verification and select original incidents before clearing a hold.");
        if (selected.Any(id => !open.Any(i => i.IncidentId == id && (i.DiscoveredUtc <= run.Plan.BoundaryUtc ||
            i.EvidenceKey == "Recovery/" + run.RunId.ToString("D")))))
            throw new InvalidOperationException("An incident is absent, cleared, or newer than this recovery proof.");
        if (!currentHeads.SequenceEqual(run.Plan.Targets))
            throw new InvalidOperationException("History advanced after recovery verification. Verify the new state before clearing.");
    }
}
