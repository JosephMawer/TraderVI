#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

/// <summary>Immutable initial setup only; no writes to imported history, positions, reports, holds or assignments.</summary>
public sealed class BrokerAccountRiskBaselineRepository : IBrokerRiskBaselineStore
{
    public const string RequiredMigration = "20260917_035_AddBrokerAccountRiskBaseline.sql";

    public async Task<BrokerRiskBaselineState> ReadAsync(CancellationToken ct = default)
    {
        await using var c = await Open(ct);
        if (!await Installed(c)) throw new InvalidOperationException("The reported-account schema is required before risk-baseline setup.");
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, "TFSA", false);
        var state = await State(c, t);
        ct.ThrowIfCancellationRequested(); t.Commit(); return state;
    }

    public async Task<BrokerRiskBaselineReceipt?> FindAsync(Guid baselineId, CancellationToken ct = default)
    {
        await using var c = await Open(ct);
        return await BaselineInstalled(c) ? await Receipt(c, baselineId) : null;
    }

    public async Task<BrokerRiskBaselineReceipt> SaveAsync(BrokerRiskBaselineRequest request, CancellationToken ct = default)
    {
        BrokerAccountRiskBaselineWorkflow.Validate(request);
        await using var c = await Open(ct);
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, "TFSA", true);
        if (!await BaselineInstalled(c, t))
            throw new InvalidOperationException($"Install reviewed migration {RequiredMigration} before saving risk-baseline setup.");
        var saved = await Receipt(c, request.Reviewed.BaselineId, t);
        if (saved is not null)
        {
            BrokerAccountRiskBaselineWorkflow.RequireSameRequest(request, saved);
            ct.ThrowIfCancellationRequested(); t.Commit(); return saved;
        }
        var state = await State(c, t);
        var baseline = BrokerAccountRiskBaselineWorkflow.Recheck(request, state, DateTimeOffset.UtcNow);
        string json = Json(baseline);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024)
            throw new InvalidOperationException("The initial risk state exceeds the supported size. No baseline was saved.");
        ct.ThrowIfCancellationRequested();
        await c.ExecuteAsync("""
            INSERT dbo.BrokerAccountRiskBaseline(BaselineId,BindingId,AccountLabel,ImportRequestId,AccountReportId,RecommendationBatchId,
                TradingDate,EstablishedUtc,ReviewedBy,RequestHash,ContentHash,BaselineJson)
            VALUES(@BaselineId,@BindingId,@AccountLabel,@ImportRequestId,@AccountReportId,@RecommendationBatchId,
                @TradingDate,@EstablishedUtc,@ReviewedBy,@RequestHash,@ContentHash,@BaselineJson)
            """, BrokerAccountImportRepository.TimestampParameters(new
            {
                baseline.BaselineId, baseline.BindingId, baseline.AccountLabel, baseline.ImportRequestId, baseline.AccountReportId,
                baseline.RecommendationBatchId, TradingDate = baseline.TradingDate.ToDateTime(TimeOnly.MinValue),
                request.ReviewedBy, request.RequestHash, ContentHash = BrokerAccountImport.Hash(json), BaselineJson = json
            }, ("EstablishedUtc", baseline.EstablishedAt.UtcDateTime)), t);
        ct.ThrowIfCancellationRequested(); t.Commit();
        return new(baseline, request.ReviewedBy, request.RequestHash);
    }

    private static async Task<bool> BaselineInstalled(SqlConnection c, SqlTransaction? t = null) =>
        await c.ExecuteScalarAsync<int>("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.BrokerAccountRiskBaseline',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.BrokerAccountRiskBaseline_Immutable',N'TR') IS NOT NULL THEN 1 ELSE 0 END
            """, transaction: t) == 1;

    private static async Task<BrokerRiskBaselineState> State(SqlConnection c, SqlTransaction t)
    {
        var imported = await BrokerAccountImportRepository.State(c, t);
        bool installed = await BaselineInstalled(c, t);
        var prior = installed && imported.Binding is not null
            ? Decode(await c.QuerySingleOrDefaultAsync<Row>("SELECT * FROM dbo.BrokerAccountRiskBaseline WHERE BindingId=@Id",
                new { Id = imported.Binding.BindingId }, t)) : null;
        // All three reads share the account fence and transaction. Exceptions propagate; absence is
        // reported only after successful reads, never by substituting defaults after a failed query.
        var report = await Report(c, "TFSA", t);
        var batch = await Batch(c, "TFSA", t);
        return new(installed, imported, new(true, report, batch, prior?.Baseline));
    }

    private static async Task<BrokerRiskBaselineReceipt?> Receipt(SqlConnection c, Guid baselineId, SqlTransaction? t = null) =>
        Decode(await c.QuerySingleOrDefaultAsync<Row>("SELECT * FROM dbo.BrokerAccountRiskBaseline WHERE BaselineId=@Id",
            new { Id = baselineId }, t));

    internal static BrokerRiskBaselineReceipt? Decode(Row? row)
    {
        if (row is null) return null;
        var value = Read<BrokerAccountRiskBaseline>(row.BaselineJson);
        var request = new BrokerRiskBaselineRequest(value, row.ReviewedBy);
        BrokerAccountRiskBaselineWorkflow.Validate(request);
        if (value.BaselineId != row.BaselineId || value.BindingId != row.BindingId || value.AccountLabel != "TFSA" || row.AccountLabel != "TFSA" ||
            value.ImportRequestId != row.ImportRequestId || value.AccountReportId != row.AccountReportId ||
            value.RecommendationBatchId != row.RecommendationBatchId || value.TradingDate != DateOnly.FromDateTime(row.TradingDate) ||
            value.EstablishedAt.UtcDateTime != row.EstablishedUtc || BrokerAccountImport.Hash(row.BaselineJson) != row.ContentHash ||
            request.RequestHash != row.RequestHash)
            throw new InvalidOperationException("Saved initial risk-baseline identity or content is inconsistent. Review before continuing.");
        return new(value, row.ReviewedBy, row.RequestHash);
    }

    internal sealed record Row(Guid BaselineId, Guid BindingId, string AccountLabel, Guid ImportRequestId, Guid? AccountReportId,
        Guid? RecommendationBatchId, DateTime TradingDate, DateTime EstablishedUtc, string ReviewedBy,
        string RequestHash, string ContentHash, string BaselineJson);
}
