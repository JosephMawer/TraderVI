#nullable enable
using Core.Trader.Promotion;
using Core.Runtime;
using Core.WSTrade.Accounts;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

public sealed record BrokerManagedStorageReadiness(bool BaselineInstalled, bool ContinuationInstalled,
    bool SessionInstalled, bool ImmutableStorageVerified = false);

/// <summary>One session lease per permanent broker binding; durable appends commit before broker transmission.</summary>
public sealed class BrokerManagedSessionRepository : IBrokerManagedSessionStore
{
    private readonly Func<CancellationToken, Task<SqlConnection>> open;
    private readonly Core.Trader.DelphiLive.ITsxSessionCalendar? calendar;
    private readonly bool sharedStorage;
    private readonly BrokerCurrentStateCache? currentCache;
    private readonly Func<Guid, CancellationToken, Task<bool>>? mayReuse;
    public BrokerManagedSessionRepository() : this(Open) { }
    public BrokerManagedSessionRepository(Core.Trader.DelphiLive.ITsxSessionCalendar calendar, bool sharedStorage = false,
        Func<Guid, CancellationToken, Task<bool>>? mayReuse = null) : this(Open)
    {
        if (mayReuse is not null && !sharedStorage) throw new ArgumentException("Verified current-state reuse requires the independent shared-storage path.", nameof(sharedStorage));
        this.calendar = calendar; this.sharedStorage = sharedStorage; this.mayReuse = mayReuse;
        currentCache = mayReuse is null ? null : new();
    }
    // Instance-scoped seam for isolated SQL rehearsals; never changes the application's connection.
    internal BrokerManagedSessionRepository(Func<CancellationToken, Task<SqlConnection>> open) =>
        this.open = open ?? throw new ArgumentNullException(nameof(open));

    public const string RequiredMigration = "20260923_040_AddBrokerManagedSession.sql";
    public async Task<EngineBrokerDiagnostics> ReadDiagnosticsAsync(Guid binding, CancellationToken ct = default)
    {
        var (state, shared) = await ReadVerifiedHeadAsync(binding, ct);
        return EngineBrokerDiagnostics.From(state, shared);
    }
    internal async Task<EngineRealObservation> ReadMarketObservationAsync(Guid binding, CancellationToken ct)
    {
        var (state, _) = await ReadVerifiedHeadAsync(binding, ct);
        return EngineRealObservation.From(state);
    }
    private async Task<(BrokerManagedSession State, bool Shared)> ReadVerifiedHeadAsync(Guid binding, CancellationToken ct)
    {
        if (binding == Guid.Empty) throw new ArgumentException("Select the real account binding for diagnostics.");
        await using var c = await open(ct);
        var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
SELECT TOP(1) BindingId,Revision,RevisionId,PreviousRevisionId,SessionId,BaselineId,SavedAt,ContentHash,SnapshotJson
FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@Binding ORDER BY Revision DESC;
""", new { Binding = binding }, cancellationToken: ct)) ?? throw new InvalidOperationException("No saved real strategy revision is available.");
        var contents = await EngineArchiveStorage.ReadContentsAsync(c, null, row.SnapshotJson, ct);
        return (Decode(row, contents), EngineArchiveCodec.IsPacked(row.SnapshotJson));
    }
    public static async Task<BrokerManagedStorageReadiness> ReadStorageAsync(CancellationToken ct = default)
    {
        await using var c = await Open(ct);
        return await c.QuerySingleAsync<BrokerManagedStorageReadiness>(new CommandDefinition("""
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.BrokerAccountRiskBaseline',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) BaselineInstalled,
 CAST(CASE WHEN OBJECT_ID(N'dbo.BrokerAccountRiskContinuation',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) ContinuationInstalled,
 CAST(CASE WHEN OBJECT_ID(N'dbo.BrokerManagedSessionRevision',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) SessionInstalled,
 CAST(CASE WHEN OBJECTPROPERTYEX(OBJECT_ID(N'dbo.BrokerAccountRiskBaseline_Immutable'),N'ExecIsTriggerDisabled')=0
  AND OBJECTPROPERTYEX(OBJECT_ID(N'dbo.BrokerAccountRiskContinuation_Immutable'),N'ExecIsTriggerDisabled')=0
  AND OBJECTPROPERTYEX(OBJECT_ID(N'dbo.BrokerManagedSessionRevision_Immutable'),N'ExecIsTriggerDisabled')=0
  AND COL_LENGTH(N'dbo.BrokerAccountRiskBaseline',N'BaselineJson')=-1
  AND COL_LENGTH(N'dbo.BrokerAccountRiskContinuation',N'ReceiptJson')=-1
  AND COL_LENGTH(N'dbo.BrokerManagedSessionRevision',N'SnapshotJson')=-1
 THEN 1 ELSE 0 END AS bit) ImmutableStorageVerified;
""", cancellationToken: ct));
    }
    public async Task<IBrokerManagedSessionLease> AcquireAsync(Guid bindingId, CancellationToken ct)
    {
        if (bindingId == Guid.Empty) throw new ArgumentException("A permanent broker binding is required.");
        var c = await open(ct);
        try
        {
            await c.ExecuteAsync(new CommandDefinition("""
IF OBJECT_ID(N'dbo.BrokerManagedSessionRevision',N'U') IS NULL
 THROW 54000,'Install reviewed migration 040 before saving a broker session.',1;
DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource=@Key,@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
IF @r<0 THROW 54001,'Another host owns this broker strategy session.',1;
""", new { Key = "TraderVI.BrokerManaged:" + bindingId.ToString("D") }, cancellationToken: ct));
            var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
SELECT TOP(1) BindingId,Revision,RevisionId,PreviousRevisionId,SessionId,BaselineId,SavedAt,ContentHash,CAST(NULL AS nvarchar(max)) SnapshotJson
FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@Id ORDER BY Revision DESC;
""", new { Id = bindingId }, cancellationToken: ct));
            bool qualified = mayReuse is not null && await mayReuse(bindingId, ct);
            var cached = row is null ? null : currentCache?.Read(Key(row), qualified);
            if (cached is not null)
                return new Lease(c, bindingId, cached.State, calendar, sharedStorage, cached.Contents, currentCache);
            if (row is not null) row = row with { SnapshotJson = await c.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT SnapshotJson FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@Binding AND Revision=@Revision;",
                new { Binding = bindingId, row.Revision }, cancellationToken: ct)) };
            var contents = row is null ? new Dictionary<string, string>() : await EngineArchiveStorage.ReadContentsAsync(c, null, row.SnapshotJson, ct);
            var state = row is null ? null : Decode(row, contents);
            if (qualified && row is not null) currentCache!.Save(Key(row), state!, contents);
            return new Lease(c, bindingId, state, calendar,
                sharedStorage || row is not null && EngineArchiveCodec.IsPacked(row.SnapshotJson), contents, qualified ? currentCache : null);
        }
        catch { await ReleaseAndClose(c, bindingId); throw; }
    }
    internal static void ValidateNext(BrokerManagedSession? prior, BrokerManagedSession next,
        Core.Trader.DelphiLive.ITsxSessionCalendar? calendar = null, BrokerHistoricalStateHash? historical = null)
    {
        next.Strategy.Validate();
        BrokerBuyReference.ValidateAppend(prior, next);
        if (next.BindingId == Guid.Empty || next.SessionId == Guid.Empty || next.RevisionId == Guid.Empty || next.SavedAt == default ||
            next.Approval.Review.BindingId != next.BindingId || next.Account.Risk.BindingId != next.BindingId ||
            next.Account.Risk.BaselineId != next.Approval.Review.BaselineId || next.SessionId != next.Approval.Review.RequestId ||
            !Enum.IsDefined(next.Phase) || string.IsNullOrWhiteSpace(next.Event) || next.Event.Length > 64 ||
            next.Orders.Select(o => o.RequestId).Distinct().Count() != next.Orders.Length ||
            next.Orders.Select(o => o.DecisionId).Distinct().Count() != next.Orders.Length ||
            next.Orders.Any(o => o.Quantity <= 0 || o.RequestId == Guid.Empty || o.DecisionId == Guid.Empty || o.Side is not ("BUY" or "SELL")))
            throw new InvalidOperationException("The durable broker session is inconsistent.");
        if (prior is null)
        {
            if (next.MinuteAdoption is not null || next.OngoingManagement is not null || next.SessionContinuation is not null || next.October22 is not null || next.ImplementationReview is not null || next.SourceSwitch is not null || next.Continuation is not null || next.Approval.Review.NoTransferScope?.Continuation is not null ||
                next.Revision != 0 || next.PreviousRevisionId is not null || !next.Orders.IsEmpty || next.ActivatedAt is not null ||
                next.Phase != BrokerManagedPhase.Transition)
                throw new InvalidOperationException("A new account session starts only from reviewed transition authority.");
            next.Approval.Review.Validate(next.Account, next.SavedAt);
            return;
        }
        foreach (var order in next.Orders.Skip(prior.Orders.Length).Where(o => o.Side == "BUY"))
        {
            if (next.Strategy.Strategy.MinuteEntry is not null && order.SignalExpiresUtc is null ||
                order.SignalExpiresUtc is { } deadline && (deadline.Kind != DateTimeKind.Utc ||
                    deadline <= order.CreatedAt.UtcDateTime || deadline - order.CreatedAt.UtcDateTime >= TimeSpan.FromMinutes(1)))
                throw new InvalidOperationException("A new minute buy must preserve its current original signal deadline.");
        }
        if (prior.NewBuysStopped != next.NewBuysStopped &&
            (next.Event != (next.NewBuysStopped ? "OperatorStoppedNewBuys" : "OperatorResumedBuying") ||
             next.Orders.Length != prior.Orders.Length) ||
            (prior.NewBuysStopped || next.NewBuysStopped) && next.Orders.Skip(prior.Orders.Length).Any(o => o.Side == "BUY"))
            throw new InvalidOperationException("Only an explicit buying-control event may change the saved mode; stopped sessions cannot reserve a purchase.");
        if (prior.TrialOrderReviewRequired && (!next.TrialOrderReviewRequired || next.Orders.Length != prior.Orders.Length) ||
            next.TrialOrderReviewRequired && !next.RequiresFullFillReview)
            throw new InvalidOperationException("A native trial order-review hold cannot be cleared or reserve another order through ordinary recovery or buying controls.");
        if (prior.BindingId != next.BindingId || prior.SessionId != next.SessionId || prior.Approval.Fingerprint != next.Approval.Fingerprint ||
            next.Revision != prior.Revision + 1 || next.PreviousRevisionId != prior.RevisionId || next.SavedAt < prior.SavedAt ||
            prior.ActivatedAt is not null && prior.ActivatedAt != next.ActivatedAt || next.Orders.Length < prior.Orders.Length ||
            prior.ReviewInvalidated && !next.ReviewInvalidated ||
            prior.Memory.CapitalReviewRequired && !next.Memory.CapitalReviewRequired ||
            prior.Memory.TradingDate == next.Memory.TradingDate && prior.Memory.DailyLossHold && !next.Memory.DailyLossHold)
            throw new InvalidOperationException("Broker session history, authority and holds must be preserved.");
        if (prior.OngoingManagement?.Fingerprint != next.OngoingManagement?.Fingerprint)
            BrokerOngoingManagementWorkflow.ValidateAppend(prior, next);
        if (prior.SessionContinuation?.Fingerprint != next.SessionContinuation?.Fingerprint)
            BrokerSessionContinuationWorkflow.ValidateAppend(prior, next, calendar ??
                throw new InvalidOperationException("A reviewed calendar is required for selected-session permission."));
        if (prior.Continuation?.Fingerprint != next.Continuation?.Fingerprint)
            BrokerContinuationWorkflow.ValidateAppend(prior, next, calendar ??
                throw new InvalidOperationException("A reviewed calendar is required for a new continuation."), historical);
        if (prior.October22?.Fingerprint != next.October22?.Fingerprint)
            BrokerOctober22Workflow.ValidateAppend(prior, next, calendar ??
                throw new InvalidOperationException("A reviewed calendar is required for October 22 permission."), historical);
        if (BrokerAccountImport.Hash(prior.SourceSwitch) != BrokerAccountImport.Hash(next.SourceSwitch))
            BrokerLiveSourceSwitchWorkflow.ValidateAppend(prior, next, calendar ??
                throw new InvalidOperationException("A reviewed calendar is required for a source switch."), historical);
        if (BrokerAccountImport.Hash(prior.ImplementationReview) != BrokerAccountImport.Hash(next.ImplementationReview))
            BrokerImplementationReviewWorkflow.ValidateAppend(prior, next, calendar ??
                throw new InvalidOperationException("A reviewed calendar is required for an implementation review."), historical);
        if (BrokerAccountImport.Hash(prior.MinuteAdoption) != BrokerAccountImport.Hash(next.MinuteAdoption))
            BrokerMinuteStrategyAdoptionWorkflow.ValidateAppend(prior, next, calendar ??
                throw new InvalidOperationException("A reviewed calendar is required for minute-strategy adoption."), historical);
        for (int i = 0; i < prior.Orders.Length; i++)
        {
            var old = prior.Orders[i]; var fresh = next.Orders[i];
            if (fresh with { Submission = old.Submission, Latest = old.Latest, Reconciled = old.Reconciled } != old ||
                old.Reconciled && !fresh.Reconciled || old.Submission.HasValue && fresh.Submission != old.Submission)
                throw new InvalidOperationException("A durable order's original intent and terminal state cannot be replaced.");
        }
        if (prior.ActivatedAt.HasValue)
            foreach (var old in prior.Protection)
            {
                var fresh = next.Protection.SingleOrDefault(p => p.PositionId == old.PositionId);
                if (fresh is null || fresh.Owner != old.Owner || fresh.EstablishedAt != old.EstablishedAt ||
                    old.CarriedFloor.HasValue && (fresh.CarriedFloor is null || fresh.CarriedFloor < old.CarriedFloor) ||
                    old.ThroughUtc.HasValue && (fresh.ThroughUtc is null || fresh.ThroughUtc < old.ThroughUtc) ||
                    old.State.ExitReason is not null && fresh.State.ExitReason != old.State.ExitReason)
                    throw new InvalidOperationException("Established protection ownership, floors and exit obligations cannot be reset.");
            }
    }
    internal static async Task<BrokerManagedSession> VerifyRevisionAsync(SqlConnection connection, Guid binding, long revision,
        Core.Trader.DelphiLive.ITsxSessionCalendar? calendar, CancellationToken ct, SqlTransaction? transaction = null)
    {
        var next = await ReadArchiveRevisionAsync(connection, binding, revision, ct, transaction);
        var previous = revision == 0 ? null : await ReadArchiveRevisionAsync(connection, binding, revision - 1, ct, transaction);
        ValidateNext(previous?.State, next.State, calendar,
            await HistoricalEvidenceAsync(connection, previous, next, revision, ct, transaction));
        return next.State;
    }

    internal static async Task<BrokerHistoricalStateHash?> HistoricalEvidenceAsync(SqlConnection connection,
        ArchiveRevision? previous, ArchiveRevision next, long through, CancellationToken ct, SqlTransaction? transaction = null)
    {
        if (previous is null || !BrokerHistoricalStateHash.Required(previous.State, next.State)) return null;
        var evidence = BrokerHistoricalStateHash.Create(previous.State, previous.Json, next.State, next.Json);
        if (previous.State.SourceSwitch is null && next.State.SourceSwitch is { } change &&
            !BrokerHistoricalStateHash.Matches(previous.State, change.PreviousSessionHash, evidence))
        {
            // Compatibility only: at most 32 original broker records inside the fixed audit boundary.
            // The normal sequential audit still verifies each witness transition before completing.
            for (long revision = next.State.Revision + 1; revision <= through && revision - next.State.Revision <= 32; revision++)
            {
                ct.ThrowIfCancellationRequested();
                var witness = await ReadArchiveRevisionAsync(connection, next.State.BindingId, revision, ct, transaction);
                evidence.AddOrderWitness(witness.Json);
                if (BrokerHistoricalStateHash.Matches(previous.State, change.PreviousSessionHash, evidence)) break;
            }
        }
        return evidence;
    }

    internal static async Task<BrokerManagedSession> ReadRevisionAsync(SqlConnection connection, Guid binding, long revision,
        CancellationToken ct, SqlTransaction? transaction = null)
        => (await ReadArchiveRevisionAsync(connection, binding, revision, ct, transaction)).State;
    internal sealed record ArchiveRevision(BrokerManagedSession State, string Json);
    internal static async Task<ArchiveRevision> ReadArchiveRevisionAsync(SqlConnection connection, Guid binding, long revision,
        CancellationToken ct, SqlTransaction? transaction = null)
    {
        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(
            "SELECT * FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@Binding AND Revision=@Revision;",
            new { Binding = binding, Revision = revision }, transaction, cancellationToken: ct));
        var contents = await EngineArchiveStorage.ReadContentsAsync(connection, transaction, row.SnapshotJson, ct);
        var state = Decode(row, contents);
        return new(state, EngineArchiveCodec.Restore(row.SnapshotJson, contents));
    }

    private static BrokerManagedSession Decode(Row row, IReadOnlyDictionary<string, string> contents)
    {
        if (Core.Runtime.EngineStrategySettings.Hash(row.SnapshotJson) != row.ContentHash) throw new InvalidDataException("Broker session content checksum failed.");
        var state = Read<BrokerManagedSession>(EngineArchiveCodec.Restore(row.SnapshotJson, contents));
        if (state.BindingId != row.BindingId || state.RevisionId != row.RevisionId || state.PreviousRevisionId != row.PreviousRevisionId ||
            state.SessionId != row.SessionId || state.Revision != row.Revision || state.Approval.Review.BaselineId != row.BaselineId || state.SavedAt != row.SavedAt)
            throw new InvalidDataException("Broker session identity differs from its durable row.");
        state.Strategy.Validate(); return state;
    }
    private sealed class Lease(SqlConnection connection, Guid binding, BrokerManagedSession? initial,
        Core.Trader.DelphiLive.ITsxSessionCalendar? calendar, bool sharedStorage, IReadOnlyDictionary<string, string> initialContents,
        BrokerCurrentStateCache? currentCache) : IBrokerManagedSessionLease
    {
        private IReadOnlyDictionary<string, string> known = initialContents;
        public BrokerManagedSession? Current { get; private set; } = initial;
        public async Task AppendAsync(BrokerManagedSession next, CancellationToken ct)
        {
            if (next.BindingId != binding) throw new InvalidOperationException("The account lease belongs to another binding.");
            ValidateNext(Current, next, calendar);
            string json = Json(next);
            if (Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024) throw new InvalidOperationException("Broker session exceeds the supported size; preserve state and stop execution.");
            var packed = sharedStorage ? EngineArchiveCodec.Pack(json) : null;
            if (packed is not null) json = packed.Manifest;
            string hash = Core.Runtime.EngineStrategySettings.Hash(json);
            using var t = connection.BeginTransaction(IsolationLevel.Serializable);
            if (packed is not null)
            {
                var added = packed.Contents.Where(p => !known.TryGetValue(p.Key, out var value) || value != p.Value).ToDictionary(p => p.Key, p => p.Value);
                await connection.ExecuteAsync(new CommandDefinition(EngineEvidenceStorage.InsertSql,
                    new { Contents = EngineEvidenceStorage.Serialize(added) }, t, cancellationToken: ct));
            }
            await connection.ExecuteAsync(new CommandDefinition("""
IF ISNULL((SELECT MAX(Revision) FROM dbo.BrokerManagedSessionRevision WITH(UPDLOCK,HOLDLOCK) WHERE BindingId=@Binding),-1)<>@Revision-1
 THROW 54002,'Broker session advanced. Recover the saved state.',1;
INSERT dbo.BrokerManagedSessionRevision(BindingId,Revision,RevisionId,PreviousRevisionId,SessionId,BaselineId,SavedAt,ContentHash,SnapshotJson)
VALUES(@Binding,@Revision,@RevisionId,@PreviousRevisionId,@SessionId,@BaselineId,@SavedAt,@Hash,@Json);
""", new { Binding = binding, next.Revision, next.RevisionId, next.PreviousRevisionId, next.SessionId,
                next.Approval.Review.BaselineId, next.SavedAt, Hash = hash, Json = json }, t, cancellationToken: ct));
            await EngineRealObservationStore.SaveIfInstalledAsync(connection, t, next, hash, ct);
            t.Commit(); Current = next;
            if (packed is not null) known = packed.Contents;
            currentCache?.Save(new(next.BindingId, next.Revision, next.RevisionId, next.PreviousRevisionId,
                next.SessionId, next.Approval.Review.BaselineId, next.SavedAt, hash), next, known);
        }
        public async ValueTask DisposeAsync() => await ReleaseAndClose(connection, binding);
    }
    private static async Task ReleaseAndClose(SqlConnection connection, Guid binding)
    {
        try
        {
            if (connection.State == ConnectionState.Open)
                await connection.ExecuteAsync("EXEC sys.sp_releaseapplock @Resource=@Key,@LockOwner=N'Session';",
                    new { Key = "TraderVI.BrokerManaged:" + binding.ToString("D") });
        }
        catch
        {
            // Do not return a possibly lock-owning physical connection to the pool.
            SqlConnection.ClearPool(connection);
        }
        finally { await connection.DisposeAsync(); }
    }
    private static BrokerCurrentStateKey Key(Row row) => new(row.BindingId, row.Revision, row.RevisionId, row.PreviousRevisionId,
        row.SessionId, row.BaselineId, row.SavedAt, row.ContentHash);
    private sealed record Row(Guid BindingId, long Revision, Guid RevisionId, Guid? PreviousRevisionId,
        Guid SessionId, Guid BaselineId, DateTimeOffset SavedAt, string ContentHash, string SnapshotJson);
}
