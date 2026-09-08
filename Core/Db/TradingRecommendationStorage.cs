#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

internal static class TradingRecommendationStorage
{
    internal const string Migration = "20260907_029_AddTradingPromotion.sql";
    private static readonly Lazy<string> implementation = new(() =>
        "CoreSha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(TradingRecommendationEngine).Assembly.Location))));
    internal static string ImplementationIdentity => implementation.Value;
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value);
    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Stored promotion data is empty.");
    internal static Guid Id(string identity) => new(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
    internal static string Account(string label) => TrackedExecutionModeContract.NormalizeAccountLabel(TrackedExecutionMode.Real, label)!.ToUpperInvariant();
    internal static async Task<SqlConnection> Open(CancellationToken token)
    {
        var c = new SqlConnection(new SQLBase().ConnectionString);
        try { await c.OpenAsync(token); return c; } catch { await c.DisposeAsync(); throw; }
    }
    internal static async Task<bool> Installed(SqlConnection c, SqlTransaction? t = null) =>
        await c.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.TradingRecommendationBatch',N'U') IS NOT NULL THEN 1 ELSE 0 END", transaction: t) == 1;
    internal static async Task RequireInstalled(SqlConnection c, SqlTransaction t)
    { if (!await Installed(c, t)) throw new InvalidOperationException($"Install reviewed migration {Migration} first."); }
    internal static async Task Fence(SqlConnection c, SqlTransaction t, string account, bool exclusive)
    {
        // Match the daily settings writer's lock order. Never hold EngineSettings while waiting for selection.
        await c.ExecuteAsync("""
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=N'TraderVI.StrategyModelSelection',@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=30000;
            IF @r<0 THROW 51500,'Daily strategy selection is busy. Retry.',1;
            """, transaction: t, commandTimeout: 40);
        await TradingControlRepository.FenceAsync(c, t, exclusive ? "Exclusive" : "Shared");
        await c.ExecuteAsync("""
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=30000;
            IF @r<0 THROW 51501,'This Trading account is being evaluated or changed. Retry.',1;
            """, new { Resource = "TraderVI.TradingRecommendations:" + account }, t, commandTimeout: 40);
    }
    internal static async Task<TradingStrategyAssignment?> Assignment(SqlConnection c, string account, SqlTransaction? t = null)
    {
        var row = await c.QuerySingleOrDefaultAsync<AssignmentRow>("""
            SELECT TOP(1) p.PromotionId,p.AccountLabel,p.PaperAccountId,p.PriorPromotionId,p.EffectiveUtc,p.Reviewer,p.Reason,v.Fingerprint,v.DefinitionJson
            FROM dbo.TradingPromotion p JOIN dbo.TradingStrategyVersion v ON v.VersionId=p.StrategyVersionId
            WHERE p.AccountLabel=@Account ORDER BY p.Sequence DESC
            """, new { Account = account }, t);
        if (row is null) return null;
        var strategy = Read<CompleteTradingStrategy>(row.DefinitionJson); strategy.Validate();
        if (strategy.Fingerprint != row.Fingerprint) throw new InvalidOperationException("Saved complete strategy content does not match its immutable identity.");
        return new(row.PromotionId, row.AccountLabel, strategy, row.PaperAccountId, row.PromotionId, row.PriorPromotionId,
            DateTime.SpecifyKind(row.EffectiveUtc, DateTimeKind.Utc), row.Reviewer, row.Reason);
    }
    private sealed record AssignmentRow(Guid PromotionId, string AccountLabel, Guid PaperAccountId, Guid? PriorPromotionId,
        DateTime EffectiveUtc, string Reviewer, string Reason, string Fingerprint, string DefinitionJson);
    internal static async Task<ReportedTradingAccount?> Report(SqlConnection c, string account, SqlTransaction? t = null)
    {
        string? json = await c.QuerySingleOrDefaultAsync<string>("SELECT TOP(1) ReportJson FROM dbo.TradingAccountReport WHERE AccountLabel=@Account ORDER BY Sequence DESC", new { Account = account }, t);
        return json is null ? null : Read<ReportedTradingAccount>(json);
    }
    internal static async Task<TradingRecommendationBatch?> Batch(SqlConnection c, string account, SqlTransaction? t = null)
    {
        string? json = await c.QuerySingleOrDefaultAsync<string>("SELECT TOP(1) BatchJson FROM dbo.TradingRecommendationBatch WHERE AccountLabel=@Account ORDER BY Sequence DESC", new { Account = account }, t);
        return json is null ? null : Read<TradingRecommendationBatch>(json);
    }
    internal sealed record PositionFact(Guid PositionId, string Symbol, DateTime EntryDate, decimal EntryPrice, int Shares, decimal CostBasis, bool IsActive);
    internal sealed record FillFact(Guid TradeId, Guid? PositionId, string Symbol, string TradeType, DateTime TradeDate, int Shares, decimal Price,
        decimal Amount, decimal? Commission, DateTime CreatedUtc, string? Reason, DateTime? ReportedFillUtc, string? RecommendationKind, decimal? PositionTarget);
    internal sealed record FinancialState(string Fingerprint, ImmutableArray<ReportedTradingHolding> Holdings, ImmutableArray<TradingActualFill> Fills);
    internal static async Task<FinancialState> Financial(SqlConnection c, string account, SqlTransaction? t = null)
    {
        var positions = (await c.QueryAsync<PositionFact>("""
            SELECT PositionId,Symbol,EntryDate,EntryPrice,Shares,CostBasis,IsActive FROM dbo.ActivePosition
            WHERE ExecutionMode=N'Real' AND AccountLabel=@Account ORDER BY PositionId
            """, new { Account = account }, t)).ToArray();
        var fills = (await c.QueryAsync<FillFact>("""
            SELECT t.TradeId,t.PositionId,t.Symbol,t.TradeType,t.TradeDate,t.Shares,t.Price,t.Amount,t.Commission,t.CreatedUtc,t.Reason,
                f.FillUtc ReportedFillUtc,f.RecommendationKind,f.PositionTarget
            FROM dbo.TradeLog t LEFT JOIN dbo.TradingReportedFill f ON f.TradeId=t.TradeId
            WHERE t.ExecutionMode=N'Real' AND t.AccountLabel=@Account ORDER BY t.TradeId
            """, new { Account = account }, t)).ToArray();
        // Market marks and monitor timestamps are intentionally excluded; financial facts and fill attribution are included.
        string fingerprint = EngineStrategySettings.Hash(Json(new { Positions = positions, Fills = fills }));
        var holdings = positions.Where(p => p.IsActive).Select(p => new ReportedTradingHolding(p.PositionId, p.Symbol, p.Shares, p.CostBasis, DateOnly.FromDateTime(p.EntryDate))).ToImmutableArray();
        var actual = fills.Where(f => f.PositionId.HasValue).Select(f => new TradingActualFill(f.TradeId, f.PositionId!.Value, f.Symbol, f.TradeType,
            f.ReportedFillUtc.HasValue ? DateTime.SpecifyKind(f.ReportedFillUtc.Value, DateTimeKind.Utc) : PaperTradingMonitor.ToUtc(f.TradeDate),
            DateOnly.FromDateTime(f.TradeDate), f.Shares, f.Price, f.Reason, f.RecommendationKind, f.PositionTarget)).ToImmutableArray();
        return new(fingerprint, holdings, actual);
    }
}
