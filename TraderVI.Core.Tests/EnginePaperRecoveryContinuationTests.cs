#nullable enable
using Core.Runtime;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EnginePaperRecoveryContinuationTests
{
    [Fact]
    public void ExistingDesktopIntegrityCallSignaturesRemainAvailable()
    {
        Assert.NotNull(typeof(global::Core.Db.EngineIntegrityRepository).GetMethod("ReadAccessAsync",
            [typeof(EngineIntegrityScope), typeof(Guid?), typeof(System.Threading.CancellationToken)]));
        Assert.NotNull(typeof(EngineIntegrityAccess).GetMethod("Evaluate",
            [typeof(ImmutableArray<EngineIntegrityIncident>), typeof(EngineIntegrityScope), typeof(Guid?)]));
    }
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);
    private static readonly EngineDatabaseIdentity Database = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly Guid RunId = Guid.NewGuid();
    private static EngineAuditRun Run => new(RunId,
        new(EngineAuditKind.Recovery, Now.AddMinutes(-1), [new(EngineAuditFamily.PaperRevisions, Guid.NewGuid(), 0, 100)])
        { Database = Database }, new(0, 5, null, 5, false), 1, "Running", Now);
    private static readonly ImmutableArray<EngineIntegrityIncident> Incidents = [
        new(Guid.NewGuid(), EngineIntegrityScope.Global, null, "EngineContinuity/" + Guid.NewGuid(), "EngineRecoveryRequired", Now.AddMinutes(-2), true),
        new(Guid.NewGuid(), EngineIntegrityScope.Global, null, "Recovery/" + RunId, "RecoveryVerificationPending", Now.AddMinutes(-1), true)];
    private static EngineResumePaperPendingRecovery Request => new(RunId, Incidents.Select(i => i.IncidentId).ToImmutableArray(), Now.AddHours(13), "Explicit temporary Paper-only continuation");
    private static EngineIntegrityStatus Status => new(Incidents, [Run]);
    private static EnginePaperRecoveryContinuation Permit() => EnginePaperRecoveryContinuation.Prepare(Request, Status, Database, Now);
    private static EngineIntegrityAccess Access(EngineIntegrityScope scope, EnginePaperRecoveryContinuation? permission, DateTime? now = null,
        ImmutableArray<EngineIntegrityIncident>? incidents = null, bool shared = false) =>
        EngineIntegrityAccess.EvaluateWithContinuation(incidents ?? Incidents, scope, null, permission, now ?? Now, shared);

    [Fact]
    public void ExplicitPaperPermissionRetainsIncidentsAndDoesNotReleaseRealResearchOrGenericGlobalChecks()
    {
        var permission = Permit();
        Assert.True(Access(EngineIntegrityScope.Paper, permission).NewExposureAllowed);
        Assert.True(Access(EngineIntegrityScope.Paper, permission).ProtectionAllowed);
        Assert.Equal(2, Access(EngineIntegrityScope.Paper, permission).Incidents.Length);
        foreach (var scope in new[] { EngineIntegrityScope.Broker, EngineIntegrityScope.Research, EngineIntegrityScope.Global })
        {
            Assert.False(Access(scope, permission).NewExposureAllowed);
            Assert.False(Access(scope, permission).ProtectionAllowed);
        }
        Assert.True(Access(EngineIntegrityScope.Global, permission, shared: true).ProtectionAllowed);
        Assert.False(Access(EngineIntegrityScope.Broker, permission, shared: true).ProtectionAllowed);
        Assert.False(Access(EngineIntegrityScope.Research, permission, shared: true).ProtectionAllowed);
        Assert.False(Access(EngineIntegrityScope.Paper, null).ProtectionAllowed); // new process has no permission
    }

    [Fact]
    public void ExpiryClockRollbackAndAnyChangedIncidentFailClosed()
    {
        var permission = Permit();
        Assert.False(Access(EngineIntegrityScope.Paper, permission, permission.ExpiresUtc).ProtectionAllowed);
        Assert.False(Access(EngineIntegrityScope.Paper, permission, Now.AddTicks(-1)).ProtectionAllowed);
        foreach (var code in new[] { "ArchiveVerificationFailed", "EngineContinuityLost", "ImmutableGuardsUnavailable", "UnknownFailure" })
        {
            var extra = new EngineIntegrityIncident(Guid.NewGuid(), EngineIntegrityScope.Global, null, "test", code, Now, true);
            Assert.False(Access(EngineIntegrityScope.Paper, permission, incidents: Incidents.Add(extra)).ProtectionAllowed);
            Assert.False(Access(EngineIntegrityScope.Global, permission, incidents: Incidents.Add(extra), shared: true).ProtectionAllowed);
        }
        Assert.False(Access(EngineIntegrityScope.Paper, permission, incidents: Incidents.SetItem(0, Incidents[0] with { ProtectionUntrusted = false })).NewExposureAllowed);
        Assert.False(Access(EngineIntegrityScope.Paper, permission, incidents: [Incidents[0]]).ProtectionAllowed);
        Assert.True(Access(EngineIntegrityScope.Paper, permission, incidents: Incidents.Reverse().ToImmutableArray()).ProtectionAllowed);
        Assert.True(Access(EngineIntegrityScope.Paper, permission, incidents: []).NewExposureAllowed); // actual clearance works normally
    }

    [Fact]
    public void RejectsWrongScopeDatabaseRunIncidentsAndNonRebootFailures()
    {
        foreach (var run in new[] { Run with { Status = "Failed" }, Run with { Plan = Run.Plan with { Database = Database with { Database = Guid.NewGuid() } } },
            Run with { Plan = Run.Plan with { Scope = EngineRecoveryScope.Paper } }, Run with { Plan = Run.Plan with { Kind = EngineAuditKind.Archive } } })
            Assert.Throws<InvalidOperationException>(() => EnginePaperRecoveryContinuation.Prepare(Request, Status with { Runs = [run] }, Database, Now));
        Assert.Throws<InvalidOperationException>(() => EnginePaperRecoveryContinuation.Prepare(Request with { RecoveryRunId = Guid.NewGuid() }, Status, Database, Now));
        Assert.Throws<InvalidOperationException>(() => EnginePaperRecoveryContinuation.Prepare(Request with { Incidents = [Incidents[0].IncidentId, Incidents[0].IncidentId] }, Status, Database, Now));
        foreach (var replacement in new[] { Incidents[0] with { FailureCode = "EngineContinuityLost" },
            Incidents[0] with { Scope = EngineIntegrityScope.Paper }, Incidents[0] with { EvidenceKey = "EngineContinuity/not-a-guid" },
            Incidents[0] with { DiscoveredUtc = Now }, Incidents[1] with { EvidenceKey = "Recovery/" + Guid.NewGuid() } })
        {
            int index = replacement.IncidentId == Incidents[0].IncidentId ? 0 : 1;
            Assert.Throws<InvalidOperationException>(() => EnginePaperRecoveryContinuation.Prepare(Request,
                Status with { Incidents = Incidents.SetItem(index, replacement) }, Database, Now));
        }
    }

    [Fact]
    public void RequiresBoundedExplicitExpiryAndKeepsClearanceProofUnchanged()
    {
        foreach (var expiry in new[] { Now, Now.AddHours(24).AddTicks(1), DateTime.SpecifyKind(Now.AddHours(1), DateTimeKind.Unspecified) })
            Assert.Throws<InvalidOperationException>(() => EnginePaperRecoveryContinuation.Prepare(Request with { ExpiresUtc = expiry }, Status, Database, Now));
        Assert.Throws<InvalidOperationException>(() => EnginePaperRecoveryContinuation.Prepare(Request with { Reason = " " }, Status, Database, Now));
        _ = Permit();
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(Run, Incidents, Request.Incidents, Run.Plan.Targets));
        var run = Run;
        var complete = run with { Status = "Complete", Progress = new(1, 0, null, 101, true) };
        var changedHeads = run.Plan.Targets.SetItem(0, run.Plan.Targets[0] with { ThroughRevision = 101 });
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(complete, Incidents, Request.Incidents, changedHeads));
    }
}
