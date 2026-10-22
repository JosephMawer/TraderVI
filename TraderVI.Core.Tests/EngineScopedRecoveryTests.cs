#nullable enable
using Core.Runtime;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineScopedRecoveryTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Account = Guid.NewGuid();
    private static EngineAuditPlan RealPlan() => new(EngineAuditKind.Recovery, Now,
        [new(EngineAuditFamily.BrokerRevisions, Account, 0, 9), new(EngineAuditFamily.SharedInputs, null, 1, 0), new(EngineAuditFamily.SharedContent, null, 1, 0)])
        { Scope = EngineRecoveryScope.Real, Database = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) };
    private static EngineAuditRun Complete(EngineAuditPlan plan) => new(Guid.NewGuid(), plan,
        new(plan.Targets.Length, 0, null, 10, true), 2, "Complete", Now);
    private static EngineIntegrityIncident Global(string code = "EngineRecoveryRequired", string? key = null) =>
        new(Guid.NewGuid(), EngineIntegrityScope.Global, null, key ?? "EngineContinuity/" + Guid.NewGuid(), code, Now.AddMinutes(-10), true);

    [Fact]
    public void ScopedPlansExcludeUnrelatedHistoryAndLegacyJsonKeepsItsOriginalShape()
    {
        var plan = RealPlan(); plan.Validate();
        Assert.Throws<InvalidOperationException>(() => (plan with { Targets = plan.Targets.Add(new(EngineAuditFamily.PaperRevisions, Guid.NewGuid(), 0, 100)) }).Validate());
        Assert.Throws<InvalidOperationException>(() => (plan with { Targets = plan.Targets.Add(new(EngineAuditFamily.LegacyInputs, null, 0, 500)) }).Validate());
        Assert.Throws<InvalidOperationException>(() => (plan with { Database = null }).Validate());
        Assert.Throws<InvalidOperationException>(() => (plan with { Targets = [] }).Validate());
        Assert.Throws<InvalidOperationException>(() => (plan with { Kind = EngineAuditKind.Archive }).Validate());
        var legacy = new EngineAuditPlan(EngineAuditKind.Recovery, Now, plan.Targets);
        var json = JsonSerializer.Serialize(legacy);
        Assert.DoesNotContain("Scope", json); Assert.DoesNotContain("Database", json);
        Assert.Equal(EngineRecoveryScope.All, JsonSerializer.Deserialize<EngineAuditPlan>(json)!.Scope);
    }

    [Fact]
    public void PaperAndResearchHoldsDoNotBlockRealButSharedOrMatchingBrokerHoldsDo()
    {
        var paper = Global() with { Scope = EngineIntegrityScope.Paper, OwnerId = Guid.NewGuid() };
        Assert.True(EngineIntegrityAccess.Evaluate([paper], EngineIntegrityScope.Broker, Account).ProtectionAllowed);
        Assert.True(EngineIntegrityAccess.Evaluate([paper], EngineIntegrityScope.Global, null).ProtectionAllowed);
        Assert.False(EngineIntegrityAccess.Evaluate([paper], EngineIntegrityScope.Paper, null).ProtectionAllowed);
        Assert.False(EngineIntegrityAccess.Evaluate([paper], EngineIntegrityScope.Research, null).ProtectionAllowed);
        Assert.False(EngineIntegrityAccess.Evaluate([Global()], EngineIntegrityScope.Broker, Account).ProtectionAllowed);
        Assert.False(EngineIntegrityAccess.Evaluate([paper with { Scope = EngineIntegrityScope.Broker, OwnerId = Account }], EngineIntegrityScope.Broker, Account).ProtectionAllowed);
    }

    [Fact]
    public void RealProofCannotClearPaperUnknownSharedIncompleteOrNewerIncidents()
    {
        var run = Complete(RealPlan()); var incident = Global();
        EngineRecoveryPolicy.RequireClear(run, [incident], [incident.IncidentId], run.Plan.Targets, EngineRecoveryScope.Real);
        Assert.True(EngineRecoveryPolicy.RetainedPaperHold(incident));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [incident], [incident.IncidentId], run.Plan.Targets));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run with { Status = "Failed" }, [incident], [incident.IncidentId], run.Plan.Targets, EngineRecoveryScope.Real));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [incident with { DiscoveredUtc = Now.AddMinutes(1) }], [incident.IncidentId], run.Plan.Targets, EngineRecoveryScope.Real));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [incident], [incident.IncidentId], run.Plan.Targets.SetItem(0, run.Plan.Targets[0] with { ThroughRevision = 10 }), EngineRecoveryScope.Real));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RetainedPaperHold(incident with { Scope = EngineIntegrityScope.Paper }));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RetainedPaperHold(Global("ArchiveVerificationFailed", "SharedInputs/1")));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RetainedPaperHold(Global("Unknown", "Other")));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RetainedPaperHold(Global("Unknown", "Other") with { Scope = EngineIntegrityScope.Broker }));
    }

    [Theory]
    [InlineData("EngineRecoveryRequired", "EngineContinuity/original")]
    [InlineData("EngineContinuityLost", "EngineContinuity/original")]
    [InlineData("RecoveryVerificationPending", "Recovery/original")]
    [InlineData("AuditReceiptUnverifiable", "AuditReceipt")]
    [InlineData("ArchiveVerificationFailed", "PaperRevisions/original/0/")]
    public void KnownGlobalRecoveryFindingsAlwaysRetainPaperRestriction(string code, string key) =>
        Assert.True(EngineRecoveryPolicy.RetainedPaperHold(Global(code, key)));

    [Fact]
    public void SplitPaperFindingRetainsOriginalDiscoveryBoundaryWithoutRestartingExistingAudit()
    {
        var paperTarget = new EngineAuditTarget(EngineAuditFamily.PaperRevisions, Guid.NewGuid(), 0, 50);
        var plan = new EngineAuditPlan(EngineAuditKind.Recovery, Now, [paperTarget, .. RealPlan().Targets]);
        var run = Complete(plan);
        var inherited = Global() with { Scope = EngineIntegrityScope.Paper, FailureCode = "PaperRecoveryRequired",
            EvidenceKey = "ScopedFrom/" + Guid.NewGuid().ToString("D"), DiscoveredUtc = Now.AddMinutes(10), OriginDiscoveredUtc = Now.AddMinutes(-10) };
        EngineRecoveryPolicy.RequireClear(run, [inherited], [inherited.IncidentId], [paperTarget], EngineRecoveryScope.Paper);
        var ownPending = inherited with { OriginDiscoveredUtc = Now.AddMilliseconds(1), OriginEvidenceKey = "Recovery/" + run.RunId.ToString("D") };
        EngineRecoveryPolicy.RequireClear(run, [ownPending], [ownPending.IncidentId], [paperTarget], EngineRecoveryScope.Paper);
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [ownPending with { OriginEvidenceKey = "Recovery/" + Guid.NewGuid() }], [ownPending.IncidentId], [paperTarget], EngineRecoveryScope.Paper));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [inherited with { OriginDiscoveredUtc = null }], [inherited.IncidentId], [paperTarget], EngineRecoveryScope.Paper));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [inherited with { Scope = EngineIntegrityScope.Broker }], [inherited.IncidentId], [paperTarget], EngineRecoveryScope.Paper));
    }
}
