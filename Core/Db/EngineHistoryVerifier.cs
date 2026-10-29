#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>One original revision/input per step; immutable content is read once within a fresh audit pass.</summary>
public sealed class EngineHistoryVerifier(ITsxSessionCalendar calendar)
{
    private Guid pass;
    private EngineDatabaseIdentity? database;
    private EnginePaperAuditReader? paperReader;
    private Guid paperOwner;
    private DelphiLivePortfolioSnapshot? previousPaper;
    private BrokerManagedSessionRepository.ArchiveRevision? previousBroker;
    private readonly Queue<InputRow> legacyInputs = new();
    private string? legacyCursor;

    public async Task BeginBatchAsync(EngineAuditRun run, CancellationToken ct = default)
    {
        await using var c = await EngineIntegrityRepository.OpenAsync(ct);
        using var t = c.BeginTransaction();
        var identity = await EngineContinuityRepository.IdentityAsync(c, t, ct);
        if (run.Plan.Database is not null && identity != run.Plan.Database || pass == run.RunId && database != identity)
            throw new InvalidDataException("Database identity changed during the audit pass.");
        if (!await EngineIntegrityRepository.RequiredGuardsAsync(c, t, ct))
            throw new InvalidDataException("Immutable guards are unavailable for audit reuse.");
        t.Commit();
        if (pass != run.RunId)
        {
            paperReader?.Dispose();
            pass = run.RunId; database = identity; paperReader = null; previousPaper = null; previousBroker = null;
            legacyInputs.Clear(); legacyCursor = null;
        }
    }
    public async Task<EngineAuditProgress> VerifyNextAsync(EngineAuditRun run, CancellationToken ct = default)
    {
        var p = run.Progress; p.Validate(run.Plan);
        if (pass != run.RunId) throw new InvalidOperationException("Start a guarded audit batch before reading history.");
        if (p.Complete) return p;
        var target = run.Plan.Targets[p.TargetIndex];
        if (target.Family != EngineAuditFamily.PaperRevisions) { paperReader?.Dispose(); paperReader = null; previousPaper = null; }
        await using var c = await EngineIntegrityRepository.OpenAsync(ct);
        if (target.Family != EngineAuditFamily.LegacyInputs && p.NextRevision > target.ThroughRevision)
            return p.NextTarget(run.Plan);
        switch (target.Family)
        {
            case EngineAuditFamily.PaperRevisions:
                await VerifyPaperAsync(c, target.OwnerId!.Value, p.NextRevision, target.ThroughRevision, ct);
                if (run.Plan.Kind == EngineAuditKind.Recovery && p.NextRevision == target.ThroughRevision)
                    await EnginePaperWorkingStore.VerifyCurrentAsync(c, target.OwnerId.Value, p.NextRevision, ct);
                return p.Verified(run.Plan, p.NextRevision + 1, null);
            case EngineAuditFamily.BrokerRevisions:
                if (p.NextRevision == 0) previousBroker = null;
                else if (previousBroker?.State.BindingId != target.OwnerId || previousBroker?.State.Revision != p.NextRevision - 1)
                    previousBroker = await BrokerManagedSessionRepository.ReadArchiveRevisionAsync(c, target.OwnerId!.Value, p.NextRevision - 1, ct);
                var archive = await BrokerManagedSessionRepository.ReadArchiveRevisionAsync(c, target.OwnerId!.Value, p.NextRevision, ct);
                var broker = archive.State;
                BrokerManagedSessionRepository.ValidateNext(previousBroker?.State, broker, calendar,
                    await BrokerManagedSessionRepository.HistoricalEvidenceAsync(c, previousBroker, archive, target.ThroughRevision, ct));
                if (run.Plan.Kind == EngineAuditKind.Recovery && p.NextRevision == target.ThroughRevision)
                {
                    var observation = (await new EngineRealObservationStore().ReadAsync(ct)).Single(r => r.BindingId == broker.BindingId);
                    if (DelphiLiveSnapshotCodec.Fingerprint(DelphiLiveLedgerJson.Serialize(observation)) !=
                        DelphiLiveSnapshotCodec.Fingerprint(DelphiLiveLedgerJson.Serialize(EngineRealObservation.From(broker))))
                        throw new InvalidDataException("Real observation subscription differs from reconstructed broker history.");
                }
                previousBroker = archive;
                return p.Verified(run.Plan, p.NextRevision + 1, null);
            case EngineAuditFamily.SharedContent:
                var content = await c.QueryFirstOrDefaultAsync<ContentRow>(new CommandDefinition("""
SELECT TOP(1) EvidenceSequence,ContentHash,ContentJson FROM dbo.EngineEvidenceContent
WHERE EvidenceSequence>=@Next AND EvidenceSequence<=@Through ORDER BY EvidenceSequence;
""", new { Next = p.NextRevision, Through = target.ThroughRevision }, cancellationToken: ct));
                if (content is null) return p.NextTarget(run.Plan); // Identity gaps may belong to rolled-back inserts.
                if (DelphiLiveSnapshotCodec.Hash(content.ContentJson) != content.ContentHash)
                    throw new InvalidDataException("Shared archive content checksum failed.");
                using (JsonDocument.Parse(content.ContentJson)) { }
                return p.Verified(run.Plan, content.EvidenceSequence + 1, null);
            case EngineAuditFamily.SharedInputs:
                var input = await c.QueryFirstOrDefaultAsync<InputRow>(new CommandDefinition("""
SELECT TOP(1) PublicationSequence,EvaluationId,SessionId,PolicyVersionId,Symbol,BarEndUtc,ContinuityEpoch,
 ObservedOnTime,ConfirmedLiveEligible,InputJson,ResultJson,RecordedUtc FROM dbo.EngineInputPublication
WHERE PublicationSequence>=@Next AND PublicationSequence<=@Through ORDER BY PublicationSequence;
""", new { Next = p.NextRevision, Through = target.ThroughRevision }, cancellationToken: ct));
                if (input is null) return p.NextTarget(run.Plan);
                await VerifyInputAsync(c, input, true, ct);
                return p.Verified(run.Plan, input.PublicationSequence + 1, null);
            case EngineAuditFamily.LegacyInputs:
                if (legacyCursor != p.LastKey) legacyInputs.Clear();
                if (legacyInputs.Count == 0)
                    foreach (var row in await c.QueryAsync<InputRow>(new CommandDefinition("""
SELECT TOP(32) CONVERT(bigint,0) PublicationSequence,EvaluationId,SessionId,PolicyVersionId,Symbol,BarEndUtc,ContinuityEpoch,
 ObservedOnTime,ConfirmedLiveEligible,InputJson,ResultJson,RecordedUtc FROM dbo.DelphiLiveEvaluation
WHERE @Last IS NULL OR EvaluationId>CONVERT(uniqueidentifier,@Last) ORDER BY EvaluationId;
""", new { Last = p.LastKey }, cancellationToken: ct))) legacyInputs.Enqueue(row);
                var old = legacyInputs.Count == 0 ? null : legacyInputs.Dequeue();
                if (old is null)
                {
                    if (p.NextRevision != target.ThroughRevision) throw new InvalidDataException("Legacy archive coverage differs from its fixed boundary.");
                    return p.NextTarget(run.Plan);
                }
                if (p.NextRevision >= target.ThroughRevision) throw new InvalidDataException("The frozen legacy archive changed during verification.");
                await VerifyInputAsync(c, old, false, ct);
                legacyCursor = old.EvaluationId.ToString("D");
                return p.Verified(run.Plan, p.NextRevision + 1, old.EvaluationId.ToString("D"));
            default: throw new InvalidOperationException("Unsupported historical audit family.");
        }
    }

    private static async Task VerifyInputAsync(SqlConnection c, InputRow row, bool shared, CancellationToken ct)
    {
        var input = shared ? DelphiLiveInputCodec.Read(row.InputJson, await EngineEvidenceStorage.ReadAsync(c, null, [row.InputJson], ct))
            : DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationInput>(row.InputJson);
        var result = DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationResult>(row.ResultJson);
        if (input.EvaluationId != row.EvaluationId || result.EvaluationId != row.EvaluationId || input.SessionId != row.SessionId ||
            input.Policy.PolicyVersionId != row.PolicyVersionId || input.Stock.Symbol != row.Symbol || input.BarEndUtc != row.BarEndUtc ||
            row.ContinuityEpoch < 1 || row.RecordedUtc <= row.BarEndUtc || row.RecordedUtc < input.EvaluatedUtc ||
            row.ObservedOnTime != result.ObservationIsValid || row.ConfirmedLiveEligible != result.ConfirmedLiveEligible)
            throw new InvalidDataException("Historical input/result identity or receipt differs from its durable row.");
        input.Policy.Validate();
        // Never rerun today's strategy implementation as a substitute for a historical decision.
    }

    private async Task VerifyPaperAsync(SqlConnection c, Guid owner, long revision, long through, CancellationToken ct)
    {
        if (paperReader is null || paperOwner != owner || previousPaper is null || previousPaper.Revision != revision - 1)
        {
            paperReader?.Dispose();
            paperReader = new(owner, through); paperOwner = owner;
            previousPaper = revision > 0 ? (await paperReader.ReadAsync(c, revision - 1, ct)).State : null;
        }
        var next = await paperReader.ReadAsync(c, revision, ct);
        if (revision == 0)
        {
            var authorization = await c.QuerySingleAsync<AuthorizationRow>(new CommandDefinition(
                "SELECT AuthorizationJson,EngineeringTrialId FROM dbo.DelphiLivePortfolioGeneration WHERE GenerationId=@Generation;",
                new { Generation = next.State.GenerationId }, cancellationToken: ct));
            var created = authorization.EngineeringTrialId is null
                ? DelphiLiveLedgerIntegrity.Create(DelphiLiveLedgerJson.Deserialize<DelphiLiveGenerationRequest>(authorization.AuthorizationJson))
                : ReadEngineeringInitial(authorization.EngineeringTrialId.Value, authorization.AuthorizationJson);
            RequireSame(created, next.State); previousPaper = next.State; return;
        }
        var previous = previousPaper!;
        if (next.Row.SettingsAssignmentId is Guid assignment)
        {
            var settings = await c.QuerySingleAsync<SettingsRow>(new CommandDefinition("""
SELECT v.SettingsJson,a.TargetId,a.VersionId,a.AssignedUtc FROM dbo.EngineStrategyAssignment a
JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId
WHERE a.AssignmentId=@Assignment AND v.Family=N'DelphiLive'
 AND v.SettingsHash=CONVERT(char(64),HASHBYTES('SHA2_256',v.SettingsJson),2);
""", new { Assignment = assignment }, cancellationToken: ct));
            if (settings.TargetId != owner || settings.VersionId != next.State.PolicyVersionId ||
                !EngineIntegrityRepository.MatchesStoredBoundary(next.State.UpdatedUtc, settings.AssignedUtc))
                throw new InvalidDataException("Historical settings assignment identity failed.");
            var policy = (DelphiLivePolicyDefinition)EngineStrategySettings.Read(EngineStrategySettings.Live, settings.SettingsJson);
            RequireSame(EngineStrategyReassignment.Rebase(previous, policy, next.State.UpdatedUtc), next.State);
        }
        else if (previous.PolicyVersionId != next.State.PolicyVersionId)
        {
            DelphiLiveLedgerIntegrity.ValidatePolicyPromotion(previous, next.State);
            if (await c.QuerySingleAsync<int>(new CommandDefinition("""
SELECT COUNT(*) FROM dbo.DelphiLiveLedgerEvent WHERE PortfolioId=@Owner AND Revision=@Revision AND EventKind=N'HumanApprovedPolicyPromotion';
""", new { Owner = owner, Revision = revision }, cancellationToken: ct)) != 1)
                throw new InvalidDataException("Historical policy promotion has no original event.");
        }
        else DelphiLiveLedgerIntegrity.ValidateTransition(previous, next.State);
        previousPaper = next.State;
    }
    internal static void RequireSame(DelphiLivePortfolioSnapshot expected, DelphiLivePortfolioSnapshot actual)
    {
        // Stored archive bytes have already passed their original hash check. Compare the authorized
        // typed state by value: SQL authorization JSON may preserve decimal scale (1000.000000)
        // while the original snapshot used 1000. Neither representation changes the financial value.
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected, DelphiLiveLedgerJson.Options),
            JsonSerializer.SerializeToElement(actual, DelphiLiveLedgerJson.Options)))
            throw new InvalidDataException("Historical portfolio differs from its authorized transition.");
    }
    internal static DelphiLivePortfolioSnapshot ReadEngineeringInitial(Guid trial, string json)
    {
        // Migration031 is an explicit historical exception. It never grants a new activation or today's permission.
        var a = JsonSerializer.Deserialize<EngineeringAuthorization>(json, new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, RespectRequiredConstructorParameters = true }) ?? throw new InvalidDataException("Engineering authorization is missing.");
        var authorized = DateTime.SpecifyKind(a.AuthorizedUtc, DateTimeKind.Utc);
        var open = new DateTime(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);
        if (trial != DelphiLiveDecisionDossierBuilder.EngineeringTrialId || a.TrialId != trial ||
            a.GenerationId != Guid.Parse("8b8f1320-1e13-4168-aa48-32e0ae4f1908") || a.PortfolioId != Guid.Parse("ae6e0f58-a5f0-44b4-8c73-ea2563ff1908") ||
            a.EffectiveSession != new DateOnly(2026, 9, 8) || a.EffectiveSessionOpenUtc != open || a.StartingCapital != 1000m || a.Currency != "CAD" ||
            authorized < open.AddHours(4).AddMinutes(20) || authorized >= open.AddHours(5).AddMinutes(45))
            throw new InvalidDataException("Engineering history differs from the one authorized September 8 trial.");
        return DelphiLiveLedgerIntegrity.Create(new(a.GenerationId, a.PortfolioId, a.PolicyVersionId, "OperationalChampion", null,
            a.StartingCapital, a.Currency, a.EffectiveSession, open, open.AddTicks(-1), a.AuthorizedBy, a.Reason)) with { UpdatedUtc = authorized };
    }
    private sealed record AuthorizationRow(string AuthorizationJson, Guid? EngineeringTrialId);
    private sealed record EngineeringAuthorization(Guid TrialId, Guid GenerationId, Guid PortfolioId, Guid PolicyVersionId,
        DateOnly EffectiveSession, DateTime EffectiveSessionOpenUtc, DateTime AuthorizedUtc, string AuthorizedBy,
        decimal StartingCapital, string Currency, string Reason);
    private sealed record SettingsRow(string SettingsJson, Guid TargetId, Guid VersionId, DateTime AssignedUtc);
    private sealed record ContentRow(long EvidenceSequence, string ContentHash, string ContentJson);
    private sealed record InputRow(long PublicationSequence, Guid EvaluationId, Guid SessionId, Guid PolicyVersionId, string Symbol,
        DateTime BarEndUtc, int ContinuityEpoch, bool ObservedOnTime, bool ConfirmedLiveEligible, string InputJson, string ResultJson, DateTime RecordedUtc);
}
