#nullable enable

using Core.Calibration;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// Durable shared collection only. Construction performs no I/O or activation.
/// One instance belongs to one host; all writers also serialize through SQL Server.
/// The host retains its acquired lease across cycles and releases it when stopping.
/// </summary>
public sealed partial class DelphiLiveCollectionRepository : SQLBase, IDelphiLiveCollectionRuntimeStore
{
    private readonly CodeProvenance code;
    private readonly Dictionary<long, DelphiLiveLease> ownedLeases = new();

    public DelphiLiveCollectionRepository(CodeProvenance code, string? connectionString = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (string.IsNullOrWhiteSpace(code.Commit) || code.Commit.Trim().Length is < 7 or > 128 ||
            code.WorkingTreeState is not ("Clean" or "Dirty" or "Unknown"))
            throw new ArgumentException("Collection requires explicit code provenance.", nameof(code));
        this.code = code;
        if (connectionString is not null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection configuration cannot be empty.", nameof(connectionString));
            ConnectionString = connectionString;
        }
    }

    public async Task<DelphiLiveLease?> TryAcquireAsync(
        string ownerId, DateTime acquiredUtc, DateTime expiresUtc,
        CancellationToken cancellationToken = default)
    {
        RequireUtc(acquiredUtc, nameof(acquiredUtc));
        RequireUtc(expiresUtc, nameof(expiresUtc));
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > 128 || expiresUtc <= acquiredUtc)
            throw new ArgumentException("Lease requires a bounded owner and a future expiry.");
        DelphiLiveLease? result = await WriteAsync(AcquireLeaseSql,
            [P("@Owner", SqlDbType.NVarChar, ownerId, 128), P("@Expiry", SqlDbType.DateTime2, expiresUtc),
             P("@Code", SqlDbType.NVarChar, code.Commit, 128),
             P("@Tree", SqlDbType.NVarChar, code.WorkingTreeState, 16)],
            async reader => await reader.ReadAsync(cancellationToken)
                ? new DelphiLiveLease(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2),
                    Utc(reader.GetDateTime(3)), Utc(reader.GetDateTime(4))) : null,
            cancellationToken);
        if (result is not null)
            lock (ownedLeases) ownedLeases[result.FencingToken] = result;
        return result;
    }

    public async Task<bool> TryRenewAsync(
        DelphiLiveLease lease, DateTime renewedUtc, DateTime expiresUtc,
        CancellationToken cancellationToken = default)
    {
        RequireOwnedLease(lease);
        RequireUtc(renewedUtc, nameof(renewedUtc));
        RequireUtc(expiresUtc, nameof(expiresUtc));
        if (expiresUtc <= renewedUtc)
            throw new ArgumentException("Renewal expiry must follow renewal time.");
        return await WriteAsync(RenewLeaseSql,
            [.. LeaseParameters(lease), P("@Expiry", SqlDbType.DateTime2, expiresUtc)],
            async reader => { await reader.ReadAsync(cancellationToken); return reader.GetBoolean(0); },
            cancellationToken);
    }

    public async Task ReleaseAsync(
        DelphiLiveLease lease, DateTime releasedUtc, CancellationToken cancellationToken = default)
    {
        RequireOwnedLease(lease);
        RequireUtc(releasedUtc, nameof(releasedUtc));
        await WriteAsync(ReleaseLeaseSql, LeaseParameters(lease), cancellationToken);
        // Keep the identity so already-issued late responses can still be audited.
        // SQL fencing, rather than this cache, controls all operational writes.
    }

    /// <summary>
    /// Creates/reloads the host's epoch and records every elapsed expected slot.
    /// It never replays those slots. Invoke after freezing the complete session
    /// observation membership and before resuming at the next scheduled cycle.
    /// Pending portfolio actions are recovered by their own ledger workflow.
    /// </summary>
    public async Task<DelphiLiveCollectionRecovery> RecoverSessionAsync(
        Guid sessionId, DelphiLiveLease lease, CancellationToken cancellationToken = default,
        bool wasArmedAtSessionOpen = false)
    {
        RequireOwnedLease(lease);
        if (sessionId == Guid.Empty) throw new ArgumentException("Session identity is required.", nameof(sessionId));
        // Finish previously frozen sessions as explicit incomplete grids before
        // opening the current session. A host absent across the close cannot
        // make yesterday's uncollected symbol slots disappear.
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            const string priorSql = """
SELECT old.SessionId FROM dbo.DelphiLiveSession old
JOIN dbo.DelphiLiveSession currentSession ON currentSession.SessionId=@Session
WHERE old.SessionOpenUtc<currentSession.SessionOpenUtc AND old.SessionState<>N'Completed'
  AND (old.CompletedUtc IS NULL OR old.CompletedUtc<old.SessionCloseUtc)
ORDER BY old.SessionOpenUtc;
""";
            await using var command = new SqlCommand(priorSql, connection);
            command.Parameters.Add(P("@Session", SqlDbType.UniqueIdentifier, sessionId));
            var prior = new List<Guid>();
            await using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) prior.Add(reader.GetGuid(0));
            foreach (Guid priorSessionId in prior)
                await FinishSessionAsync(priorSessionId, lease, false, cancellationToken);
        }
        return await RecoverCurrentSessionAsync(sessionId, lease, cancellationToken, wasArmedAtSessionOpen);
    }

    private Task<DelphiLiveCollectionRecovery> RecoverCurrentSessionAsync(
        Guid sessionId, DelphiLiveLease lease, CancellationToken cancellationToken,
        bool wasArmedAtSessionOpen = false) =>
        WriteAsync(RecoverSessionSql,
            [.. LeaseParameters(lease), P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
             P("@WasArmedAtSessionOpen", SqlDbType.Bit, wasArmedAtSessionOpen)],
            async reader =>
            {
                await reader.ReadAsync(cancellationToken);
                return new DelphiLiveCollectionRecovery(reader.GetGuid(0), reader.GetInt32(1),
                    reader.GetBoolean(2), reader.GetInt32(3), Utc(reader.GetDateTime(4)));
            }, cancellationToken);

    public async Task FinishSessionAsync(Guid sessionId, DelphiLiveLease lease, bool hostStopping,
        CancellationToken cancellationToken = default)
    {
        RequireOwnedLease(lease);
        if (sessionId == Guid.Empty) throw new ArgumentException("Session identity is required.", nameof(sessionId));
        await RecoverCurrentSessionAsync(sessionId, lease, cancellationToken);
        await WriteAsync(FinishSessionSql,
            [.. LeaseParameters(lease), P("@SessionId", SqlDbType.UniqueIdentifier, sessionId),
             P("@HostStopping", SqlDbType.Bit, hostStopping)], cancellationToken);
    }

    public async Task BeginCycleAsync(
        DelphiLiveCollectionCycle cycle, IReadOnlyList<DelphiLiveObservationTarget> expectedTargets,
        CancellationToken cancellationToken = default)
    {
        ValidateCycle(cycle);
        DelphiLiveLease lease = GetOwnedLease(cycle.LeaseFencingToken);
        IReadOnlyList<DelphiLiveObservationTarget> targets = ValidateTargets(expectedTargets);
        string json = JsonSerializer.Serialize(targets.Select((target, index) => new
        {
            Symbol = target.Symbol.Trim().ToUpperInvariant(),
            PriorityClass = target.Symbol.Equals("XIU", StringComparison.OrdinalIgnoreCase)
                ? nameof(DelphiLiveCollectionPriorityClass.XiuBenchmark) : target.PriorityClass.ToString(),
            PriorityOrdinal = index + 1
        }));
        await WriteAsync(BeginCycleSql,
            [.. LeaseParameters(lease), .. CycleParameters(cycle),
             P("@Targets", SqlDbType.NVarChar, json, -1)], cancellationToken);
    }

    public async Task<DelphiLiveMarketDataReceipt> RecordReceiptAsync(
        DelphiLiveMarketDataReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        DelphiLiveMarketDataRequest request = receipt.Request;
        ValidateRequest(request);
        RequireUtc(receipt.ReceivedUtc, nameof(receipt.ReceivedUtc));
        if (receipt.ReceivedUtc < request.RequestStartedUtc)
            throw new ArgumentException("Receipt precedes its request.", nameof(receipt));
        if (receipt.ProviderAttemptCount < 0 || receipt.ProviderRequestCount < 0 ||
            receipt.ProviderAttemptCount.HasValue != receipt.ProviderRequestCount.HasValue)
            throw new ArgumentException("Source attempt/request counts must be non-negative or explicitly unavailable.", nameof(receipt));
        if (receipt.ProviderFetchStartedUtc is DateTime fetch)
        {
            RequireUtc(fetch, nameof(receipt.ProviderFetchStartedUtc));
            if (fetch < request.RequestStartedUtc || fetch > receipt.ReceivedUtc)
                throw new ArgumentException("Source fetch timestamps must lie inside the actual request/receipt interval.", nameof(receipt));
        }
        string disposition = ClassifyReceipt(receipt);
        OhlcvBar? bar = receipt.ExactCompletedBar;
        bool usableBar = disposition is "OperationalOnTime" or "LateResearchOnly";
        byte[]? hash = usableBar ? SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(bar))) : null;
        var result = await WriteAsync(RecordReceiptSql,
            [P("@CycleId", SqlDbType.UniqueIdentifier, request.CycleId),
             P("@Symbol", SqlDbType.NVarChar, request.Symbol.Trim().ToUpperInvariant(), 20),
             P("@Start", SqlDbType.DateTime2, request.BarStartUtc),
             P("@End", SqlDbType.DateTime2, request.BarEndUtc),
             P("@Deadline", SqlDbType.DateTime2, request.DeadlineUtc),
             P("@Request", SqlDbType.DateTime2, request.RequestStartedUtc),
             P("@Received", SqlDbType.DateTime2, receipt.ReceivedUtc),
             P("@Ordinal", SqlDbType.Int, request.PriorityOrdinal),
             P("@OwnedLeases", SqlDbType.NVarChar, OwnedLeaseIdsJson(), -1),
             P("@Disposition", SqlDbType.NVarChar, disposition, 32),
             P("@HasBar", SqlDbType.Bit, usableBar),
             P("@ProviderAttempts", SqlDbType.Int, receipt.ProviderAttemptCount),
             P("@ProviderRequests", SqlDbType.Int, receipt.ProviderRequestCount),
             P("@ProviderFetch", SqlDbType.DateTime2, receipt.ProviderFetchStartedUtc),
             P("@SuppliedPoll", SqlDbType.UniqueIdentifier, receipt.PollObservationId),
             P("@SuppliedBar", SqlDbType.UniqueIdentifier, receipt.EvidenceBarId),
             DecimalParameter("@Open", usableBar ? bar!.Open : null),
             DecimalParameter("@High", usableBar ? bar!.High : null),
             DecimalParameter("@Low", usableBar ? bar!.Low : null),
             DecimalParameter("@Close", usableBar ? bar!.Close : null),
             P("@Volume", SqlDbType.BigInt, usableBar ? bar!.Volume : null),
             P("@Hash", SqlDbType.Binary, hash, 32),
             P("@ReceiptJson", SqlDbType.NVarChar, JsonSerializer.Serialize(new
                 { receipt.Request, receipt.ExactCompletedBar, receipt.ReceivedUtc, receipt.Disposition,
                   receipt.ProviderAttemptCount, receipt.ProviderRequestCount, receipt.ProviderFetchStartedUtc }), -1),
             P("@ReceiptHash", SqlDbType.Binary, SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                 { receipt.Request, receipt.ExactCompletedBar, receipt.ReceivedUtc, receipt.Disposition,
                   receipt.ProviderAttemptCount, receipt.ProviderRequestCount, receipt.ProviderFetchStartedUtc }))), 32),
             P("@Provider", SqlDbType.NVarChar, IntradayEvidenceVersions.Provider, 32),
             P("@SourceContract", SqlDbType.NVarChar, IntradayEvidenceVersions.SourceContract, 64)],
            async reader =>
            {
                await reader.ReadAsync(cancellationToken);
                return receipt with
                {
                    Disposition = reader.GetString(0),
                    PollObservationId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    EvidenceBarId = reader.IsDBNull(2) ? null : reader.GetGuid(2)
                };
            }, cancellationToken);
        // The first transaction has now committed its canonical bar and receipt.
        // Only a second server-clock check can prove that this durable evidence
        // existed before the deadline. Until then the slot stays Pending.
        if (result.Disposition is "OperationalOnTime" or "IdenticalDuplicate")
        {
            string verified = await WriteAsync(VerifyDurabilitySql,
                [P("@CycleId", SqlDbType.UniqueIdentifier, request.CycleId),
                 P("@Symbol", SqlDbType.NVarChar, request.Symbol.Trim().ToUpperInvariant(), 20),
                 P("@OwnedLeases", SqlDbType.NVarChar, OwnedLeaseIdsJson(), -1),
                 P("@Disposition", SqlDbType.NVarChar, result.Disposition, 32)],
                async reader => { await reader.ReadAsync(cancellationToken); return reader.GetString(0); },
                cancellationToken);
            result = result with { Disposition = verified };
        }
        return result;
    }

    public Task CompleteCycleAsync(
        Guid cycleId, DateTime completedUtc, string status, CancellationToken cancellationToken = default)
    {
        RequireUtc(completedUtc, nameof(completedUtc));
        if (cycleId == Guid.Empty || status is not ("Completed" or "DeadlineExceeded" or "Failed" or "Cancelled"))
            throw new ArgumentException("A cycle identity and supported completion status are required.");
        return WriteAsync(CompleteCycleSql,
            [P("@CycleId", SqlDbType.UniqueIdentifier, cycleId),
             P("@OwnedLeases", SqlDbType.NVarChar, OwnedLeaseIdsJson(), -1),
             P("@Status", SqlDbType.NVarChar, status, 32)], cancellationToken);
    }

    internal static string ClassifyReceipt(DelphiLiveMarketDataReceipt receipt)
    {
        if (!string.IsNullOrWhiteSpace(receipt.Disposition) &&
            receipt.Disposition is not ("OperationalOnTime" or "LateResearchOnly" or "NoCompletedBar" or
            "StaleNoNewBar" or "FormingBarIgnored" or "StructurallyInvalid" or
            "CycleDeadlineExceeded" or "CollectionFailed"))
            throw new ArgumentException("Unsupported source receipt disposition.", nameof(receipt));
        if (receipt.ExactCompletedBar is null &&
            receipt.Disposition is "OperationalOnTime" or "LateResearchOnly")
            return "NoCompletedBar";
        string disposition = DelphiLiveCollectionWorkflow.NormalizeReceipt(
            receipt.Request, receipt, receipt.Request.DeadlineUtc).Disposition;
        if (disposition is not ("OperationalOnTime" or "LateResearchOnly" or "NoCompletedBar" or
            "StaleNoNewBar" or "FormingBarIgnored" or "StructurallyInvalid" or
            "CycleDeadlineExceeded" or "CollectionFailed"))
            throw new ArgumentException("Unsupported source receipt disposition.", nameof(receipt));
        if (receipt.ExactCompletedBar is OhlcvBar bar)
        {
            if (bar.TimestampUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Bar timestamp must be UTC.", nameof(receipt));
            if (disposition is "OperationalOnTime" or "LateResearchOnly")
            {
                if (receipt.ReceivedUtc <= receipt.Request.BarEndUtc)
                    return "FormingBarIgnored";
                // Never silently round facts when writing the canonical decimal(19,6) ledger.
                decimal[] prices = [bar.Open, bar.High, bar.Low, bar.Close];
                if (prices.Any(p => p >= 10_000_000_000_000m || decimal.Round(p, 6) != p))
                    return "StructurallyInvalid";
            }
        }
        return disposition;
    }

    internal static IReadOnlyList<DelphiLiveObservationTarget> ValidateTargets(
        IReadOnlyList<DelphiLiveObservationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var ordered = DelphiLiveCollectionPriorityPlanner.OrderAndDeduplicate(targets);
        if (ordered.Count != targets.Count || !ordered.Any(t => t.Symbol.Equals("XIU", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Expected targets must be unique and include XIU.", nameof(targets));
        return ordered;
    }

    private static void ValidateCycle(DelphiLiveCollectionCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        if (cycle.CycleId == Guid.Empty || cycle.SessionId == Guid.Empty ||
            cycle.LeaseFencingToken < 1 || cycle.ContinuityEpoch < 1)
            throw new ArgumentException("Cycle requires session, lease, and continuity identities.", nameof(cycle));
        RequireUtc(cycle.BarStartUtc, nameof(cycle.BarStartUtc));
        RequireUtc(cycle.BarEndUtc, nameof(cycle.BarEndUtc));
        RequireUtc(cycle.ScheduledStartUtc, nameof(cycle.ScheduledStartUtc));
        RequireUtc(cycle.DeadlineUtc, nameof(cycle.DeadlineUtc));
        if (cycle.BarEndUtc != cycle.BarStartUtc.AddMinutes(5) ||
            cycle.ScheduledStartUtc != cycle.BarEndUtc.AddMinutes(2) ||
            cycle.DeadlineUtc != cycle.ScheduledStartUtc.AddMinutes(5) ||
            cycle.BarStartUtc.Ticks % TimeSpan.FromMinutes(5).Ticks != 0)
            throw new ArgumentException("Cycle timing must follow the exact five-minute schedule.", nameof(cycle));
    }

    private static void ValidateRequest(DelphiLiveMarketDataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireUtc(request.BarStartUtc, nameof(request.BarStartUtc));
        RequireUtc(request.BarEndUtc, nameof(request.BarEndUtc));
        RequireUtc(request.DeadlineUtc, nameof(request.DeadlineUtc));
        RequireUtc(request.RequestStartedUtc, nameof(request.RequestStartedUtc));
        if (request.CycleId == Guid.Empty || string.IsNullOrWhiteSpace(request.Symbol) ||
            request.Symbol.Length > 20 || request.PriorityOrdinal < 1 ||
            request.BarEndUtc != request.BarStartUtc.AddMinutes(5) ||
            request.DeadlineUtc != request.BarEndUtc.AddMinutes(7) ||
            request.RequestStartedUtc < request.BarEndUtc.AddMinutes(2) ||
            request.RequestStartedUtc >= request.DeadlineUtc)
            throw new ArgumentException("Receipt request does not identify a scheduled primary attempt.", nameof(request));
    }

    private DelphiLiveLease GetOwnedLease(long token)
    {
        lock (ownedLeases)
            return ownedLeases.TryGetValue(token, out DelphiLiveLease? lease) ? lease :
                throw new InvalidOperationException("This repository has not acquired that lease identity.");
    }

    private string OwnedLeaseIdsJson()
    {
        lock (ownedLeases) return JsonSerializer.Serialize(ownedLeases.Values.Select(x => x.LeaseId));
    }

    private void RequireOwnedLease(DelphiLiveLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        DelphiLiveLease owned = GetOwnedLease(lease.FencingToken);
        if (owned.LeaseId != lease.LeaseId || owned.OwnerId != lease.OwnerId)
            throw new InvalidOperationException("Lease identity does not belong to this host.");
    }

    private async Task WriteAsync(string sql, SqlParameter[] parameters, CancellationToken cancellationToken) =>
        await WriteAsync(sql, parameters, _ => Task.FromResult(true), cancellationToken);

    private async Task<T> WriteAsync<T>(string sql, SqlParameter[] parameters,
        Func<SqlDataReader, Task<T>> read, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        // A transaction-owned lock gives every Delphi Live writer the same lock
        // order, including the initially empty lease range and recovery paths.
        const string gate = """
SET XACT_ABORT ON;
DECLARE @LockResult INT;
EXEC @LockResult = sys.sp_getapplock @Resource = N'DelphiLiveCollectionV1',
    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
IF @LockResult < 0 THROW 52200, 'Delphi Live collection store lock unavailable.', 1;
""";
        await using (var command = new SqlCommand(gate, connection, transaction))
            await command.ExecuteNonQueryAsync(cancellationToken);
        T result;
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddRange(parameters);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            result = await read(reader);
            // Finish the batch even for a no-result mutation before committing.
            while (await reader.NextResultAsync(cancellationToken)) { }
        }
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static SqlParameter[] LeaseParameters(DelphiLiveLease lease) =>
        [P("@LeaseId", SqlDbType.UniqueIdentifier, lease.LeaseId),
         P("@Owner", SqlDbType.NVarChar, lease.OwnerId, 128),
         P("@Fence", SqlDbType.BigInt, lease.FencingToken)];

    private static SqlParameter[] CycleParameters(DelphiLiveCollectionCycle cycle) =>
        [P("@CycleId", SqlDbType.UniqueIdentifier, cycle.CycleId),
         P("@SessionId", SqlDbType.UniqueIdentifier, cycle.SessionId),
         P("@Epoch", SqlDbType.Int, cycle.ContinuityEpoch),
         P("@Start", SqlDbType.DateTime2, cycle.BarStartUtc),
         P("@End", SqlDbType.DateTime2, cycle.BarEndUtc),
         P("@Scheduled", SqlDbType.DateTime2, cycle.ScheduledStartUtc),
         P("@Deadline", SqlDbType.DateTime2, cycle.DeadlineUtc),
         P("@Provider", SqlDbType.NVarChar, IntradayEvidenceVersions.Provider, 32)];

    private static SqlParameter P(string name, SqlDbType type, object? value, int? size = null) =>
        new(name, type) { Value = value ?? DBNull.Value, Size = size ?? 0 };

    private static SqlParameter DecimalParameter(string name, decimal? value) =>
        new(name, SqlDbType.Decimal) { Precision = 19, Scale = 6, Value = (object?)value ?? DBNull.Value };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
