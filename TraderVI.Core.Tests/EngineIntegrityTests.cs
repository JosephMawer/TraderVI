#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineIntegrityTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 2, 0, 0, DateTimeKind.Utc);
    private static EngineAuditPlan Plan(EngineAuditKind kind = EngineAuditKind.Recovery) => new(kind, Now,
        [new(EngineAuditFamily.SharedInputs, null, 1, 2)]);
    private static EngineIntegrityIncident Incident() => new(Guid.NewGuid(), EngineIntegrityScope.Global, null, "SharedInputs/1", "ChecksumFailed", Now.AddHours(-1), true);
    [Fact]
    public void ResumedCursorCannotTurnPartialCoverageIntoCompleteOrCrossItsFixedBoundary()
    {
        var plan = Plan(); var p = EngineAuditProgress.Start(plan);
        p = p.Verified(plan, 2, null);
        var resumed = JsonSerializer.Deserialize<EngineAuditProgress>(JsonSerializer.Serialize(p))!;
        Assert.Equal(1, resumed.VerifiedUnits); Assert.False(resumed.Complete);
        Assert.Throws<InvalidOperationException>(() => resumed.Verified(plan, 4, null));
        Assert.Throws<InvalidOperationException>(() => (resumed with { Complete = true }).Validate(plan));
        p = resumed.Verified(plan, 3, null).NextTarget(plan);
        Assert.True(p.Complete); Assert.Equal(2, p.VerifiedUnits);
        Assert.Throws<InvalidOperationException>(() => p.Verified(plan, 3, null));
    }
    [Fact]
    public void AuditCompletionOrOverdueCoverageDoesNotClearIncident()
    {
        var incident = Incident();
        var run = new EngineAuditRun(Guid.NewGuid(), Plan(EngineAuditKind.Archive), new(1, 0, null, 2, true), 3, "Complete", Now);
        var status = new EngineIntegrityStatus([incident], [run]);
        Assert.Contains("overdue", status.Summary(Now.AddDays(8)));
        Assert.False(EngineIntegrityAccess.Evaluate(status.Incidents, EngineIntegrityScope.Broker, Guid.NewGuid()).NewExposureAllowed);
        Assert.True(EngineIntegrityAccess.Evaluate([], EngineIntegrityScope.Broker, Guid.NewGuid()).NewExposureAllowed);
    }
    [Fact]
    public void ScopeAndProtectionTrustAreSeparateFromNewExposure()
    {
        Guid owner = Guid.NewGuid();
        var incident = Incident() with { Scope = EngineIntegrityScope.Broker, OwnerId = owner, ProtectionUntrusted = false };
        var affected = EngineIntegrityAccess.Evaluate([incident], EngineIntegrityScope.Broker, owner);
        Assert.False(affected.NewExposureAllowed); Assert.True(affected.ProtectionAllowed);
        Assert.True(EngineIntegrityAccess.Evaluate([incident], EngineIntegrityScope.Broker, Guid.NewGuid()).NewExposureAllowed);
        Assert.True(EngineIntegrityAccess.Evaluate([incident with { Scope = EngineIntegrityScope.Research }], EngineIntegrityScope.Broker, owner).NewExposureAllowed);
        Assert.False(EngineIntegrityAccess.Evaluate([incident with { ProtectionUntrusted = true }], EngineIntegrityScope.Broker, owner).ProtectionAllowed);
    }
    [Fact]
    public void ClearRequiresExplicitCompleteRecoveryOriginalIncidentsAndUnchangedHeads()
    {
        var plan = Plan(); var incident = Incident();
        var run = new EngineAuditRun(Guid.NewGuid(), plan, new(1, 0, null, 2, true), 3, "Complete", Now);
        EngineRecoveryPolicy.RequireClear(run, [incident], [incident.IncidentId], plan.Targets);
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run with { Status = "Failed" }, [incident], [incident.IncidentId], plan.Targets));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run with { Plan = Plan(EngineAuditKind.Archive) }, [incident], [incident.IncidentId], plan.Targets));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [incident], [incident.IncidentId], [plan.Targets[0] with { ThroughRevision = 3 }]));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [incident with { DiscoveredUtc = Now.AddSeconds(1) }], [incident.IncidentId], plan.Targets));
        Assert.Throws<InvalidOperationException>(() => EngineRecoveryPolicy.RequireClear(run, [], [incident.IncidentId], plan.Targets));
    }
    [Fact]
    public void ReceiptChecksumsAndCompletedFlagAreVerifiedBeforeUse()
    {
        var plan = Plan(); var p = EngineAuditProgress.Start(plan);
        string pj = JsonSerializer.Serialize(plan), cursor = JsonSerializer.Serialize(p);
        var row = new EngineIntegrityRepository.RunRow(Guid.NewGuid(), "Recovery", Now, pj, DelphiLiveSnapshotCodec.Hash(pj),
            cursor, DelphiLiveSnapshotCodec.Hash(cursor), 0, "Running", Now);
        Assert.Equal(p, EngineIntegrityRepository.Decode(row).Progress);
        Assert.Throws<System.IO.InvalidDataException>(() => EngineIntegrityRepository.Decode(row with { ProgressJson = cursor + " " }));
        Assert.Throws<System.IO.InvalidDataException>(() => EngineIntegrityRepository.Decode(row with { Status = "Complete" }));
    }
    [Fact]
    public void SeptemberEngineeringAuthorizationIsHistoricalAndCannotAuthorizeAnotherDate()
    {
        var trial = DelphiLiveDecisionDossierBuilder.EngineeringTrialId;
        var authorization = new { TrialId = trial, GenerationId = Guid.Parse("8b8f1320-1e13-4168-aa48-32e0ae4f1908"),
            PortfolioId = Guid.Parse("ae6e0f58-a5f0-44b4-8c73-ea2563ff1908"), PolicyVersionId = DelphiLivePolicyDefinition.Version1.PolicyVersionId,
            EffectiveSession = new DateOnly(2026, 9, 8), EffectiveSessionOpenUtc = new DateTime(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc),
            AuthorizedUtc = new DateTime(2026, 9, 8, 18, 0, 0, DateTimeKind.Utc), AuthorizedBy = "test", StartingCapital = 1000m, Currency = "CAD", Reason = "original trial" };
        var initial = EngineHistoryVerifier.ReadEngineeringInitial(trial, JsonSerializer.Serialize(authorization));
        Assert.Equal(1000m, initial.Cash); Assert.Empty(initial.Fills); Assert.Equal(authorization.AuthorizedUtc, initial.UpdatedUtc);
        Assert.Throws<System.IO.InvalidDataException>(() => EngineHistoryVerifier.ReadEngineeringInitial(trial,
            JsonSerializer.Serialize(authorization with { EffectiveSession = new DateOnly(2026, 10, 22) })));
    }
}
