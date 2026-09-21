#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

/// <summary>Append-only account reference history; never updates positions, baseline, paper or legacy risk records.</summary>
public sealed class BrokerAccountRiskContinuationRepository(ITsxSessionCalendar calendar) : IBrokerAccountRiskContinuationStore
{
    public const string RequiredMigration = "20260921_039_AddBrokerAccountRiskContinuation.sql";

    public async Task<BrokerRiskContinuationReceipt?> FindAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var c = await Open(ct);
        return await IsInstalled(c) ? await Find(c, requestId) : null;
    }

    public async Task<BrokerAccountRiskState> ReadAsync(Guid baselineId, CancellationToken ct = default)
    {
        await using var c = await Open(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, "TFSA", false);
        await RequireSchema(c, t);
        var state = await State(c, t, baselineId);
        ct.ThrowIfCancellationRequested();
        t.Commit();
        return state;
    }

    public async Task<BrokerRiskContinuationReceipt> AppendAsync(BrokerRiskContinuationRequest request, CancellationToken ct = default)
    {
        BrokerAccountRiskContinuation.Validate(request);
        await using var c = await Open(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, "TFSA", true);
        await RequireSchema(c, t);
        var saved = await Find(c, request.RequestId, t);
        if (saved is not null)
        {
            BrokerAccountRiskContinuationWorkflow.RequireSameRequest(request, saved);
            ct.ThrowIfCancellationRequested(); t.Commit(); return saved;
        }
        var previous = await State(c, t, request.BaselineId);
        var now = DateTimeOffset.UtcNow;
        var state = BrokerAccountRiskContinuation.Apply(previous, request, calendar, now);
        var receipt = new BrokerRiskContinuationReceipt(request, state, now);
        string json = Json(receipt);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 256 * 1024)
            throw new InvalidOperationException("Account risk event exceeds the supported size; no event was saved.");
        ct.ThrowIfCancellationRequested();
        await c.ExecuteAsync("""
            INSERT dbo.BrokerAccountRiskContinuation(RequestId,BaselineId,PreviousRevisionId,TradingDate,SavedUtc,RequestHash,ContentHash,ReceiptJson)
            VALUES(@RequestId,@BaselineId,@PreviousRevisionId,@TradingDate,@SavedUtc,@RequestHash,@ContentHash,@ReceiptJson)
            """, BrokerAccountImportRepository.TimestampParameters(new
            {
                request.RequestId, request.BaselineId, request.PreviousRevisionId,
                TradingDate = state.TradingDate.ToDateTime(TimeOnly.MinValue), request.RequestHash,
                ContentHash = EngineStrategySettings.Hash(json), ReceiptJson = json
            }, ("SavedUtc", now.UtcDateTime)), t);
        ct.ThrowIfCancellationRequested(); t.Commit();
        return receipt;
    }

    private static async Task<bool> IsInstalled(SqlConnection c, SqlTransaction? t = null) =>
        await c.ExecuteScalarAsync<int>("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.BrokerAccountRiskContinuation',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.BrokerAccountRiskContinuation_Immutable',N'TR') IS NOT NULL
              AND OBJECT_ID(N'dbo.BrokerAccountRiskBaseline',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.BrokerAccountRiskBaseline_Immutable',N'TR') IS NOT NULL THEN 1 ELSE 0 END
            """, transaction: t) == 1;

    private static async Task RequireSchema(SqlConnection c, SqlTransaction t)
    {
        if (!await IsInstalled(c, t))
            throw new InvalidOperationException($"Install separately reviewed migrations 035 and {RequiredMigration} before continuing account risk history.");
    }

    private static async Task<BrokerRiskContinuationReceipt?> Find(SqlConnection c, Guid id, SqlTransaction? t = null) =>
        Decode(await c.QuerySingleOrDefaultAsync<Row>("SELECT * FROM dbo.BrokerAccountRiskContinuation WHERE RequestId=@Id", new { Id = id }, t));

    private static async Task<BrokerAccountRiskState> State(SqlConnection c, SqlTransaction t, Guid baselineId)
    {
        var row = await c.QuerySingleOrDefaultAsync<BrokerAccountRiskBaselineRepository.Row>(
            "SELECT * FROM dbo.BrokerAccountRiskBaseline WHERE BaselineId=@Id", new { Id = baselineId }, t);
        var baseline = BrokerAccountRiskBaselineRepository.Decode(row)?.Baseline
            ?? throw new InvalidOperationException("Establish and verify the initial baseline before continuing it.");
        var latest = Decode(await c.QuerySingleOrDefaultAsync<Row>("""
            SELECT TOP(1) * FROM dbo.BrokerAccountRiskContinuation WHERE BaselineId=@Id ORDER BY Sequence DESC
            """, new { Id = baselineId }, t));
        if (latest is null) return BrokerAccountRiskContinuation.Start(baseline);
        if (latest.State.BaselineId != baseline.BaselineId || latest.State.BindingId != baseline.BindingId ||
            latest.State.AccountLabel != baseline.AccountLabel)
            throw new InvalidOperationException("Continuing risk history does not match its initial account binding.");
        return latest.State;
    }

    internal static BrokerRiskContinuationReceipt? Decode(Row? row)
    {
        if (row is null) return null;
        var receipt = Read<BrokerRiskContinuationReceipt>(row.ReceiptJson);
        BrokerAccountRiskContinuation.Validate(receipt.Request);
        var state = receipt.State;
        if (receipt.Request.RequestId != row.RequestId || receipt.Request.BaselineId != row.BaselineId ||
            receipt.Request.PreviousRevisionId != row.PreviousRevisionId || state.RevisionId != row.RequestId ||
            state.BaselineId != row.BaselineId || state.BindingId == Guid.Empty || state.AccountLabel != "TFSA" ||
            state.TradingDate != DateOnly.FromDateTime(row.TradingDate) || receipt.SavedAt.UtcDateTime != row.SavedUtc ||
            state.Through > receipt.SavedAt || state.SessionReference.Value <= 0m || state.DrawdownReference.Value <= 0m ||
            receipt.Request.RequestHash != row.RequestHash || EngineStrategySettings.Hash(row.ReceiptJson) != row.ContentHash)
            throw new InvalidOperationException("Saved account risk event identity or content is inconsistent.");
        return receipt;
    }

    internal sealed record Row(Guid RequestId, Guid BaselineId, Guid? PreviousRevisionId, DateTime TradingDate,
        DateTime SavedUtc, string RequestHash, string ContentHash, string ReceiptJson);
}
