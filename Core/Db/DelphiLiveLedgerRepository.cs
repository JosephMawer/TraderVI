#nullable enable

using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Atomic current snapshots plus immutable revisions/events. Every position,
/// action, quote attempt, fill, floor and exact mark remains reconstructible in
/// its original revision; no existing Shadow or tracked-position row is used.
/// </summary>
public sealed class DelphiLiveLedgerRepository : SQLBase, IDelphiLiveLedgerStore
{
    public const string MigrationFileName = "20260905_023_AddDelphiLivePortfolioLedger.sql";

    public async Task<bool> HasSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.DelphiLivePortfolioGeneration', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLivePortfolioLedger', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLivePortfolioRevision', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveLedgerEvent', N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit);
""", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<DelphiLivePortfolioSnapshot?> LoadPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await Read(connection, null, portfolioId, false, cancellationToken);
    }

    public async Task<IReadOnlyList<DelphiLivePortfolioSnapshot>> GetPortfoliosForSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
SELECT l.[SnapshotJson]
FROM [dbo].[DelphiLivePortfolioLedger] l
JOIN [dbo].[DelphiLivePortfolioGeneration] g ON g.[GenerationId] = l.[GenerationId]
WHERE g.[EffectiveTradingDate] <= @Date
  AND (g.[EndExclusiveTradingDate] IS NULL OR g.[EndExclusiveTradingDate] > @Date)
ORDER BY l.[PortfolioId];
""", connection);
        command.Parameters.Add(P("@Date", SqlDbType.Date, tradingDate.ToDateTime(TimeOnly.MinValue)));
        var rows = new List<DelphiLivePortfolioSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(reader.GetString(0)));
        return rows.AsReadOnly();
    }

    public async Task<DelphiLivePortfolioSnapshot> CreateGenerationAsync(DelphiLiveGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var state = DelphiLiveLedgerIntegrity.Create(request);
        if (request.Role != "OperationalChampion" || request.ExperimentId is not null)
            throw new NotSupportedException("Comparison activation requires its separately validated aligned experiment coordinator.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (var gate = new SqlCommand("""
DECLARE @result int;
EXEC @result = sys.sp_getapplock @Resource = N'DelphiLivePortfolioActivationV1',
    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
IF @result < 0 THROW 51232, 'Another Delphi Live activation is in progress.', 1;
""", connection, transaction))
            await gate.ExecuteNonQueryAsync(cancellationToken);

        var existing = await Read(connection, transaction, request.PortfolioId, true, cancellationToken);
        if (existing is not null)
        {
            await using var verify = new SqlCommand("SELECT [AuthorizationJson] FROM [dbo].[DelphiLivePortfolioGeneration] WHERE [GenerationId] = @Id;", connection, transaction);
            verify.Parameters.Add(P("@Id", SqlDbType.UniqueIdentifier, request.GenerationId));
            var authorization = await verify.ExecuteScalarAsync(cancellationToken) as string;
            if (authorization != DelphiLiveLedgerJson.Serialize(request))
                throw new InvalidOperationException("An existing generation cannot be replaced or recapitalized.");
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }
        Guid assignmentId = Guid.NewGuid();
        string json = DelphiLiveLedgerJson.Serialize(state);
        await using (var command = new SqlCommand("""
IF SYSUTCDATETIME() >= @EffectiveOpen OR @Authorized > SYSUTCDATETIME()
    THROW 51233, 'Initial activation must be authorized before the next session boundary.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[DelphiLivePolicyAssignment] WITH (UPDLOCK, HOLDLOCK)
           WHERE [RoleSlot] = 0 AND [EndExclusiveTradingDate] IS NULL AND [CancelledUtc] IS NULL)
    THROW 51234, 'An Operational Champion already has an assignment; initial activation cannot replace it.', 1;
INSERT INTO [dbo].[DelphiLivePolicyAssignment]
([AssignmentId], [DelphiLivePolicyVersionId], [PolicyRole], [RoleSlot], [ExperimentId], [EffectiveTradingDate],
 [AuthorizedUtc], [AuthorizedBy], [AuthorizationReason], [DecisionRef])
VALUES (@Assignment, @Policy, N'OperationalChampion', 0, NULL, @Date, @Authorized, @By, @Reason, N'ADR-0053');
INSERT INTO [dbo].[DelphiLivePortfolioGeneration]
([GenerationId], [AssignmentId], [DelphiLivePolicyVersionId], [PortfolioRole], [ExperimentId], [StartingCapital],
 [Currency], [EffectiveTradingDate], [EffectiveSessionOpenUtc], [AuthorizedUtc], [AuthorizedBy], [AuthorizationReason], [AuthorizationJson])
VALUES (@Generation, @Assignment, @Policy, N'OperationalChampion', NULL, @Capital, @Currency, @Date, @EffectiveOpen,
 @Authorized, @By, @Reason, @AuthorizationJson);
INSERT INTO [dbo].[DelphiLivePortfolioLedger]
([PortfolioId], [GenerationId], [DelphiLivePolicyVersionId], [Revision], [SnapshotSchemaVersion], [SnapshotJson], [UpdatedUtc])
VALUES (@Portfolio, @Generation, @Policy, 0, 1, @Json, @Authorized);
INSERT INTO [dbo].[DelphiLivePortfolioRevision] ([PortfolioId], [Revision], [SnapshotJson]) VALUES (@Portfolio, 0, @Json);
INSERT INTO [dbo].[DelphiLiveLedgerEvent] ([EventId], [PortfolioId], [Revision], [EventKind], [RecordedUtc], [DataJson])
VALUES (@Event, @Portfolio, 0, N'InitialCapitalActivationAuthorized', @Authorized, @AuthorizationJson);
""", connection, transaction))
        {
            command.Parameters.AddRange(new[]
            {
                P("@Assignment", SqlDbType.UniqueIdentifier, assignmentId), P("@Generation", SqlDbType.UniqueIdentifier, request.GenerationId),
                P("@Portfolio", SqlDbType.UniqueIdentifier, request.PortfolioId), P("@Policy", SqlDbType.UniqueIdentifier, request.PolicyVersionId),
                P("@Capital", SqlDbType.Decimal, request.StartingCapital), P("@Currency", SqlDbType.Char, request.Currency, 3),
                P("@Date", SqlDbType.Date, request.EffectiveSession.ToDateTime(TimeOnly.MinValue)), P("@EffectiveOpen", SqlDbType.DateTime2, request.EffectiveSessionOpenUtc),
                P("@Authorized", SqlDbType.DateTime2, request.AuthorizedUtc), P("@By", SqlDbType.NVarChar, request.AuthorizedBy, 128),
                P("@Reason", SqlDbType.NVarChar, request.Reason, 1024), P("@Json", SqlDbType.NVarChar, json, -1),
                P("@AuthorizationJson", SqlDbType.NVarChar, DelphiLiveLedgerJson.Serialize(request), -1), P("@Event", SqlDbType.UniqueIdentifier, Guid.NewGuid())
            });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return state;
    }

    public async Task<DelphiLivePortfolioSnapshot> CommitAsync(long expectedRevision, DelphiLivePortfolioSnapshot next,
        IReadOnlyList<DelphiLiveLedgerEvent> events, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        if (events.Count == 0 || lease.LeaseId == Guid.Empty || lease.FencingToken < 1)
            throw new ArgumentException("Every portfolio revision requires auditable events and a durable host lease.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await RequireLease(connection, transaction, lease, cancellationToken);
        var prior = await Read(connection, transaction, next.PortfolioId, true, cancellationToken)
            ?? throw new InvalidOperationException("Portfolio does not exist.");
        string json = DelphiLiveLedgerJson.Serialize(next);
        if (prior.Revision == next.Revision && DelphiLiveLedgerJson.Serialize(prior) == json)
        {
            await transaction.CommitAsync(cancellationToken);
            return prior; // Exact retry following an ambiguous commit.
        }
        if (prior.Revision != expectedRevision)
            throw new InvalidOperationException("Portfolio revision changed; reload before taking an action.");
        DelphiLiveLedgerIntegrity.ValidateTransition(prior, next);
        await using (var command = new SqlCommand("""
UPDATE [dbo].[DelphiLivePortfolioLedger]
SET [Revision] = @Revision, [SnapshotJson] = @Json, [UpdatedUtc] = @Updated
WHERE [PortfolioId] = @Portfolio AND [Revision] = @Expected;
IF @@ROWCOUNT <> 1 THROW 51235, 'Portfolio revision conflict.', 1;
INSERT INTO [dbo].[DelphiLivePortfolioRevision]
([PortfolioId], [Revision], [SnapshotJson], [LeaseId], [LeaseFencingToken])
VALUES (@Portfolio, @Revision, @Json, @Lease, @Fence);
""", connection, transaction))
        {
            command.Parameters.AddRange(new[] { P("@Portfolio", SqlDbType.UniqueIdentifier, next.PortfolioId),
                P("@Revision", SqlDbType.BigInt, next.Revision), P("@Expected", SqlDbType.BigInt, expectedRevision),
                P("@Json", SqlDbType.NVarChar, json, -1), P("@Updated", SqlDbType.DateTime2, next.UpdatedUtc),
                P("@Lease", SqlDbType.UniqueIdentifier, lease.LeaseId), P("@Fence", SqlDbType.BigInt, lease.FencingToken) });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var entry in events)
        {
            if (entry.EventId == Guid.Empty || entry.RecordedUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(entry.Kind))
                throw new ArgumentException("Event identity, kind, and UTC time are required.");
            await using var command = new SqlCommand("""
INSERT INTO [dbo].[DelphiLiveLedgerEvent] ([EventId], [PortfolioId], [Revision], [EventKind], [RecordedUtc], [DataJson])
VALUES (@Event, @Portfolio, @Revision, @Kind, @Time, @Json);
""", connection, transaction);
            command.Parameters.AddRange(new[] { P("@Event", SqlDbType.UniqueIdentifier, entry.EventId),
                P("@Portfolio", SqlDbType.UniqueIdentifier, next.PortfolioId), P("@Revision", SqlDbType.BigInt, next.Revision),
                P("@Kind", SqlDbType.NVarChar, entry.Kind, 64), P("@Time", SqlDbType.DateTime2, entry.RecordedUtc),
                P("@Json", SqlDbType.NVarChar, entry.DataJson, -1) });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await RequireLease(connection, transaction, lease, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    private static async Task RequireLease(SqlConnection connection, SqlTransaction transaction, DelphiLiveLease lease, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
IF NOT EXISTS (SELECT 1 FROM [dbo].[DelphiLiveHostLease] WITH (UPDLOCK, HOLDLOCK)
 WHERE [LeaseId] = @Lease AND [OwnerId] = @Owner AND [FencingToken] = @Fence
 AND [IsHeld] = 1 AND [ExpiresUtc] > SYSUTCDATETIME())
 THROW 51236, 'The Delphi Live portfolio writer no longer owns its durable lease.', 1;
""", connection, transaction);
        command.Parameters.AddRange(new[] { P("@Lease", SqlDbType.UniqueIdentifier, lease.LeaseId),
            P("@Owner", SqlDbType.NVarChar, lease.OwnerId, 128), P("@Fence", SqlDbType.BigInt, lease.FencingToken) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DelphiLivePortfolioSnapshot?> Read(SqlConnection connection, SqlTransaction? transaction,
        Guid portfolioId, bool locked, CancellationToken cancellationToken)
    {
        string sql = locked
            ? "SELECT [SnapshotJson] FROM [dbo].[DelphiLivePortfolioLedger] WITH (UPDLOCK, HOLDLOCK) WHERE [PortfolioId] = @Id;"
            : "SELECT [SnapshotJson] FROM [dbo].[DelphiLivePortfolioLedger] WHERE [PortfolioId] = @Id;";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(P("@Id", SqlDbType.UniqueIdentifier, portfolioId));
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(json) : null;
    }

    private static SqlParameter P(string name, SqlDbType type, object value, int size = 0)
    {
        var parameter = size == 0 ? new SqlParameter(name, type) : new SqlParameter(name, type, size);
        parameter.Value = value;
        if (type == SqlDbType.Decimal) { parameter.Precision = 28; parameter.Scale = 6; }
        return parameter;
    }
}
