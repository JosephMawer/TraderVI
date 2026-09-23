#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

public sealed record BrokerManagedStorageReadiness(bool BaselineInstalled, bool ContinuationInstalled, bool SessionInstalled);

/// <summary>One session lease per permanent broker binding; durable appends commit before broker transmission.</summary>
public sealed class BrokerManagedSessionRepository : IBrokerManagedSessionStore
{
    public const string RequiredMigration = "20260923_040_AddBrokerManagedSession.sql";
    public static async Task<BrokerManagedStorageReadiness> ReadStorageAsync(CancellationToken ct = default)
    {
        await using var c = await Open(ct);
        return await c.QuerySingleAsync<BrokerManagedStorageReadiness>(new CommandDefinition("""
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.BrokerAccountRiskBaseline',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) BaselineInstalled,
 CAST(CASE WHEN OBJECT_ID(N'dbo.BrokerAccountRiskContinuation',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) ContinuationInstalled,
 CAST(CASE WHEN OBJECT_ID(N'dbo.BrokerManagedSessionRevision',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit) SessionInstalled;
""", cancellationToken: ct));
    }
    public async Task<IBrokerManagedSessionLease> AcquireAsync(Guid bindingId, CancellationToken ct)
    {
        if (bindingId == Guid.Empty) throw new ArgumentException("A permanent broker binding is required.");
        var c = await Open(ct);
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
SELECT TOP(1) BindingId,Revision,RevisionId,PreviousRevisionId,SessionId,BaselineId,SavedAt,ContentHash,SnapshotJson
FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@Id ORDER BY Revision DESC;
""", new { Id = bindingId }, cancellationToken: ct));
            return new Lease(c, bindingId, row is null ? null : Decode(row));
        }
        catch { await ReleaseAndClose(c, bindingId); throw; }
    }
    internal static void ValidateNext(BrokerManagedSession? prior, BrokerManagedSession next)
    {
        next.Strategy.Validate();
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
            if (next.Revision != 0 || next.PreviousRevisionId is not null || !next.Orders.IsEmpty || next.ActivatedAt is not null ||
                next.Phase != BrokerManagedPhase.Transition)
                throw new InvalidOperationException("A new account session starts only from reviewed transition authority.");
            next.Approval.Review.Validate(next.Account, next.SavedAt);
            return;
        }
        if (prior.BindingId != next.BindingId || prior.SessionId != next.SessionId || prior.Approval.Fingerprint != next.Approval.Fingerprint ||
            next.Revision != prior.Revision + 1 || next.PreviousRevisionId != prior.RevisionId || next.SavedAt < prior.SavedAt ||
            prior.ActivatedAt is not null && prior.ActivatedAt != next.ActivatedAt || next.Orders.Length < prior.Orders.Length ||
            prior.ReviewInvalidated && !next.ReviewInvalidated ||
            prior.Memory.CapitalReviewRequired && !next.Memory.CapitalReviewRequired ||
            prior.Memory.TradingDate == next.Memory.TradingDate && prior.Memory.DailyLossHold && !next.Memory.DailyLossHold)
            throw new InvalidOperationException("Broker session history, authority and holds must be preserved.");
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
    private static BrokerManagedSession Decode(Row row)
    {
        if (Core.Runtime.EngineStrategySettings.Hash(row.SnapshotJson) != row.ContentHash) throw new InvalidDataException("Broker session content checksum failed.");
        var state = Read<BrokerManagedSession>(row.SnapshotJson);
        if (state.BindingId != row.BindingId || state.RevisionId != row.RevisionId || state.PreviousRevisionId != row.PreviousRevisionId ||
            state.SessionId != row.SessionId || state.Revision != row.Revision || state.Approval.Review.BaselineId != row.BaselineId || state.SavedAt != row.SavedAt)
            throw new InvalidDataException("Broker session identity differs from its durable row.");
        state.Strategy.Validate(); return state;
    }
    private sealed class Lease(SqlConnection connection, Guid binding, BrokerManagedSession? initial) : IBrokerManagedSessionLease
    {
        public BrokerManagedSession? Current { get; private set; } = initial;
        public async Task AppendAsync(BrokerManagedSession next, CancellationToken ct)
        {
            if (next.BindingId != binding) throw new InvalidOperationException("The account lease belongs to another binding.");
            ValidateNext(Current, next);
            string json = Json(next);
            if (Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024) throw new InvalidOperationException("Broker session exceeds the supported size; preserve state and stop execution.");
            string hash = Core.Runtime.EngineStrategySettings.Hash(json);
            using var t = connection.BeginTransaction(IsolationLevel.Serializable);
            await connection.ExecuteAsync(new CommandDefinition("""
IF ISNULL((SELECT MAX(Revision) FROM dbo.BrokerManagedSessionRevision WITH(UPDLOCK,HOLDLOCK) WHERE BindingId=@Binding),-1)<>@Revision-1
 THROW 54002,'Broker session advanced. Recover the saved state.',1;
INSERT dbo.BrokerManagedSessionRevision(BindingId,Revision,RevisionId,PreviousRevisionId,SessionId,BaselineId,SavedAt,ContentHash,SnapshotJson)
VALUES(@Binding,@Revision,@RevisionId,@PreviousRevisionId,@SessionId,@BaselineId,@SavedAt,@Hash,@Json);
""", new { Binding = binding, next.Revision, next.RevisionId, next.PreviousRevisionId, next.SessionId,
                next.Approval.Review.BaselineId, next.SavedAt, Hash = hash, Json = json }, t, cancellationToken: ct));
            t.Commit(); Current = next;
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
    private sealed record Row(Guid BindingId, long Revision, Guid RevisionId, Guid? PreviousRevisionId,
        Guid SessionId, Guid BaselineId, DateTimeOffset SavedAt, string ContentHash, string SnapshotJson);
}
