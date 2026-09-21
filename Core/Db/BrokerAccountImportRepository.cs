#nullable enable
using Core.WSTrade.Accounts;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

/// <summary>Append-only SQL account observations and reviewed legacy references. Never writes tracked positions, fills, reports or approvals.</summary>
public sealed class BrokerAccountImportRepository : IBrokerImportStore
{
    public const string RequiredMigration = "20260916_034_AddBrokerAccountImport.sql";
    public async Task<BrokerImportState> ReadAsync(CancellationToken ct = default)
    {
        await using var c = await Open(ct);
        if (!await Installed(c)) return new(false, new("TFSA", BrokerAccountImport.Hash("unavailable"), [], []), null, null);
        using var t = c.BeginTransaction(IsolationLevel.Serializable); await Fence(c, t, "TFSA", false);
        var state = await State(c, t); t.Commit(); return state;
    }
    public async Task<BrokerImportReceipt?> FindAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var c = await Open(ct);
        return await ImportInstalled(c) ? await Receipt(c, requestId) : null;
    }
    public async Task<BrokerImportReceipt> SaveAsync(BrokerImportRequest request, CancellationToken ct = default)
    {
        BrokerImportWorkflow.Validate(request.RequestId, request.ReviewedBy);
        if (request.Plan.AccountLabel != "TFSA" || request.ReviewedFingerprint != request.Plan.Fingerprint)
            throw new InvalidOperationException("The submitted import differs from the reviewed TFSA plan.");
        await using var c = await Open(ct); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, "TFSA", true);
        if (!await ImportInstalled(c, t)) throw new InvalidOperationException($"Install reviewed migration {RequiredMigration} before importing history.");
        var saved = await Receipt(c, request.RequestId, t);
        if (saved is not null)
        {
            if (saved.RequestHash != request.RequestHash) throw new InvalidOperationException("This import request already has different saved content.");
            t.Commit(); return saved;
        }
        var state = await State(c, t);
        var plan = BrokerAccountImport.Prepare(request, state, DateTimeOffset.UtcNow);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var binding = state.Binding ?? new BrokerImportBinding(Guid.NewGuid(), "TFSA", plan.BrokerKey, plan.LegacyFingerprint, now, request.ReviewedBy);
        string json = Json(plan);
        if (Encoding.UTF8.GetByteCount(json) > 32 * 1024 * 1024)
            throw new InvalidOperationException("The account history exceeds the supported import size. No partial history was saved.");
        ct.ThrowIfCancellationRequested();
        if (state.Binding is null)
            await c.ExecuteAsync("""
                INSERT dbo.BrokerAccountImportBinding(BindingId,AccountLabel,BrokerKey,LegacyFingerprint,CreatedUtc,ReviewedBy)
                VALUES(@BindingId,@AccountLabel,@BrokerKey,@LegacyFingerprint,@CreatedUtc,@ReviewedBy)
                """, TimestampParameters(new { binding.BindingId, binding.AccountLabel, binding.BrokerKey, binding.LegacyFingerprint,
                    binding.ReviewedBy }, ("CreatedUtc", now.UtcDateTime)), t);
        await c.ExecuteAsync("""
            INSERT dbo.BrokerAccountImport(RequestId,BindingId,PriorRequestId,RequestHash,PlanFingerprint,SnapshotUtc,SavedUtc,ReviewedBy,PlanJson)
            VALUES(@RequestId,@BindingId,@PriorRequestId,@RequestHash,@PlanFingerprint,@SnapshotUtc,@SavedUtc,@ReviewedBy,@PlanJson)
            """, TimestampParameters(new { request.RequestId, binding.BindingId, PriorRequestId = plan.PreviousRequestId, request.RequestHash,
                PlanFingerprint = plan.Fingerprint, request.ReviewedBy, PlanJson = json },
                ("SnapshotUtc", plan.Observation.SnapshotAt.UtcDateTime), ("SavedUtc", now.UtcDateTime)), t);
        // Cancellation before commit rolls back both the binding and observation. An uncertain response can be read by RequestId.
        ct.ThrowIfCancellationRequested(); t.Commit();
        return new(request.RequestId, binding.BindingId, request.RequestHash, now, plan);
    }
    private static async Task<bool> ImportInstalled(SqlConnection c, SqlTransaction? t = null) =>
        await c.ExecuteScalarAsync<int>("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.BrokerAccountImportBinding',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.BrokerAccountImport',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.BrokerAccountImportBinding_Immutable',N'TR') IS NOT NULL
                AND OBJECT_ID(N'dbo.BrokerAccountImport_Immutable',N'TR') IS NOT NULL THEN 1 ELSE 0 END
            """, transaction: t) == 1;
    internal static async Task<BrokerImportState> State(SqlConnection c, SqlTransaction t)
    {
        var positions = (await c.QueryAsync<PositionRow>("""
            SELECT PositionId,Symbol,EntryDate,Shares,CostBasis,IsActive FROM dbo.ActivePosition
            WHERE ExecutionMode=N'Real' AND AccountLabel=N'TFSA' ORDER BY PositionId
            """, transaction: t)).Select(p => new BrokerImportLegacyPosition(p.PositionId, p.Symbol,
                DateOnly.FromDateTime(p.EntryDate), p.Shares, p.CostBasis, p.IsActive)).ToArray();
        var trades = (await c.QueryAsync<TradeRow>("""
            SELECT t.TradeId,t.PositionId,t.Symbol,t.TradeType,t.TradeDate,t.Shares,t.Amount,t.Commission,t.NetAmount,f.FillUtc
            FROM dbo.TradeLog t LEFT JOIN dbo.TradingReportedFill f ON f.TradeId=t.TradeId
            WHERE t.ExecutionMode=N'Real' AND t.AccountLabel=N'TFSA' ORDER BY t.TradeId
            """, transaction: t)).Select(f => new BrokerImportLegacyTrade(f.TradeId, f.PositionId, f.Symbol, f.TradeType,
                DateOnly.FromDateTime(f.TradeDate), f.FillUtc is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
                f.Shares, f.Amount, f.Commission, f.NetAmount)).ToArray();
        // Include the existing engine fingerprint and broker-match fields such as NetAmount that it does not contain.
        var financial = await Financial(c, "TFSA", t);
        var ledger = new BrokerImportLegacyLedger("TFSA", BrokerAccountImport.Hash(new { financial.Fingerprint, positions, trades }), positions, trades);
        if (!await ImportInstalled(c, t)) return new(false, ledger, null, null);
        var b = await c.QuerySingleOrDefaultAsync<BindingRow>("SELECT BindingId,AccountLabel,BrokerKey,LegacyFingerprint,CreatedUtc,ReviewedBy FROM dbo.BrokerAccountImportBinding WHERE AccountLabel=N'TFSA'", transaction: t);
        var binding = b is null ? null : new BrokerImportBinding(b.BindingId, b.AccountLabel, b.BrokerKey, b.LegacyFingerprint, Utc(b.CreatedUtc), b.ReviewedBy);
        var latest = binding is null ? null : await c.QuerySingleOrDefaultAsync<ReceiptRow>("""
            SELECT TOP(1) RequestId,BindingId,RequestHash,PlanFingerprint,SnapshotUtc,SavedUtc,PlanJson FROM dbo.BrokerAccountImport
            WHERE BindingId=@Id ORDER BY Sequence DESC
            """, new { Id = binding.BindingId }, t);
        return new(true, ledger, binding, Decode(latest));
    }
    private static async Task<BrokerImportReceipt?> Receipt(SqlConnection c, Guid id, SqlTransaction? t = null) => Decode(
        await c.QuerySingleOrDefaultAsync<ReceiptRow>("""
            SELECT RequestId,BindingId,RequestHash,PlanFingerprint,SnapshotUtc,SavedUtc,PlanJson FROM dbo.BrokerAccountImport WHERE RequestId=@Id
            """, new { Id = id }, t));
    internal static BrokerImportReceipt? Decode(ReceiptRow? row)
    {
        if (row is null) return null;
        var plan = Read<BrokerImportPlan>(row.PlanJson);
        if (!plan.CanImport || plan.AccountLabel != "TFSA" || plan.Fingerprint != row.PlanFingerprint ||
            plan.BrokerKey != BrokerAccountImport.Key(plan.Observation) || !SnapshotMatches(plan.Observation.SnapshotAt, row.SnapshotUtc))
            throw new InvalidOperationException("Saved broker import identity or content is inconsistent. Review the database before continuing.");
        return new(row.RequestId, row.BindingId, row.RequestHash, Utc(row.SavedUtc), plan);
    }
    internal static DynamicParameters TimestampParameters(object values, params (string Name, DateTime Value)[] timestamps)
    {
        var parameters = new DynamicParameters(values);
        // Dapper otherwise sends DateTime as SQL datetime, losing precision before datetime2 storage.
        foreach (var timestamp in timestamps) parameters.Add(timestamp.Name, timestamp.Value, DbType.DateTime2);
        return parameters;
    }
    internal static bool SnapshotMatches(DateTimeOffset observed, DateTime stored)
    {
        if (observed == Utc(stored)) return true;
        // Existing immutable imports used SQL datetime parameters. Accept only that exact historical
        // encoding (1/300 second, then datetime2(7)), not an arbitrary tolerance or a rewritten receipt.
        // The full-precision JSON remains authoritative; content hashes and account checks still apply.
        try
        {
            var legacy = new SqlDateTime(observed.UtcDateTime);
            long ticks = ((long)legacy.TimeTicks * TimeSpan.TicksPerSecond + 150) / 300;
            return stored == new DateTime(1900, 1, 1).AddDays(legacy.DayTicks).AddTicks(ticks);
        }
        catch (SqlTypeException) { return false; }
    }
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private sealed record PositionRow(Guid PositionId, string Symbol, DateTime EntryDate, int Shares, decimal CostBasis, bool IsActive);
    private sealed record TradeRow(Guid TradeId, Guid? PositionId, string Symbol, string TradeType, DateTime TradeDate,
        int Shares, decimal Amount, decimal? Commission, decimal NetAmount, DateTime? FillUtc);
    private sealed record BindingRow(Guid BindingId, string AccountLabel, string BrokerKey, string LegacyFingerprint, DateTime CreatedUtc, string ReviewedBy);
    internal sealed record ReceiptRow(Guid RequestId, Guid BindingId, string RequestHash, string PlanFingerprint, DateTime SnapshotUtc, DateTime SavedUtc, string PlanJson);
}
