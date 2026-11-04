#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Core.Runtime;

/// <summary>Process-local operational qualification; never clears an incident or certifies history.</summary>
public sealed record PaperRestartQualification(EngineDatabaseIdentity Database, DateTime QualifiedUtc,
    ImmutableArray<Guid> Portfolios, ImmutableArray<EngineIntegrityIncident> Incidents)
{
    public ImmutableDictionary<Guid, ImmutableArray<Guid>> PendingBuys { get; init; } = ImmutableDictionary<Guid, ImmutableArray<Guid>>.Empty;
    public bool Allows(ImmutableArray<EngineIntegrityIncident> open, EngineIntegrityScope scope, Guid? owner, bool paperSharedInputs = false)
    {
        bool shared = paperSharedInputs && scope == EngineIntegrityScope.Global;
        if (!shared && (scope != EngineIntegrityScope.Paper || owner.HasValue && !Portfolios.Contains(owner.Value))) return false;
        bool Relevant(EngineIntegrityIncident i) => i.Scope == EngineIntegrityScope.Global || !shared &&
            i.Scope == EngineIntegrityScope.Paper && (i.OwnerId == null || owner == null || i.OwnerId == owner);
        return Matches(open, Relevant);
    }
    internal bool AllowsWorker(ImmutableArray<EngineIntegrityIncident> open)
        => Matches(open, i => i.Scope == EngineIntegrityScope.Global || i.Scope == EngineIntegrityScope.Paper && i.OwnerId == null);
    private bool Matches(ImmutableArray<EngineIntegrityIncident> open, Func<EngineIntegrityIncident, bool> relevant)
        => open.Where(relevant).SequenceEqual(Incidents.Where(relevant)) && open.Where(relevant).All(IsStartupFinding);

    internal static bool IsStartupFinding(EngineIntegrityIncident i) =>
        i.Scope == EngineIntegrityScope.Global && i.FailureCode == "EngineRecoveryRequired" && i.EvidenceKey.StartsWith("EngineContinuity/", StringComparison.Ordinal) ||
        i.Scope == EngineIntegrityScope.Paper && i.FailureCode == "PaperRestartQualificationFailed" && i.EvidenceKey == "PaperCurrentState" ||
        i.Scope == EngineIntegrityScope.Paper && i.FailureCode == "PaperRecoveryRequired" && i.EvidenceKey.StartsWith("ScopedFrom/", StringComparison.Ordinal) &&
        i.OriginDiscoveredUtc != null && i.OriginEvidenceKey?.StartsWith("EngineContinuity/", StringComparison.Ordinal) == true;

    private static PaperRestartQualification? current;
    public static PaperRestartQualification? Current => Volatile.Read(ref current);
    public static string? LastFailure { get; private set; }
    internal static void Fail(string explanation) { Set(null); LastFailure = explanation; }
    internal static void Set(PaperRestartQualification? value) { LastFailure = null; Volatile.Write(ref current, value); }
}
