#nullable enable
using System;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.ML.Engine.Profit;
using Core.Oracle;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Core.Db;

/// <summary>Reads maintained portfolio definitions. Never creates a strategy, changes settings or reads model files.</summary>
public sealed class AiPortfolioCatalogRepository : SQLBase, IAiPortfolioCatalogReader
{
    private readonly TimeSpan maximumAge;
    public AiPortfolioCatalogRepository(TimeSpan maximumAge)
    {
        if (maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumAge));
        this.maximumAge = maximumAge;
    }

    public async Task<AiPortfolioCatalog> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(cancellationToken);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        CommandDefinition Query(string sql, object? args = null) => new(sql, args, t, cancellationToken: cancellationToken);
        var dailyRow = await c.QuerySingleOrDefaultAsync<StrategyVersionInfo>(Query("SELECT * FROM dbo.StrategyVersion WHERE IsActive=1;"));
        var comparisonAccounts = await PaperComparisonRepository.ReadAccountsAsync(c, t, cancellationToken);
        TradingDailyInputs? daily = null;
        try
        {
            if (dailyRow is not null)
            {
                var binding = await c.QuerySingleOrDefaultAsync(Query("SELECT ModelSetJson,ModelSetSha256 FROM dbo.StrategyModelBinding WHERE StrategyVersionId=@Id;", new { Id = dailyRow.VersionId }));
                if (binding is not null)
                {
                    var models = StoredProfitModelSet.Read((string)binding.ModelSetJson, (string)binding.ModelSetSha256);
                    models.Validate(dailyRow.VersionId, dailyRow.DecisionRef);
                    daily = new(dailyRow.VersionId, dailyRow.ToConfig(), (string)binding.ModelSetSha256, models.InputContract,
                        models.Models.OrderBy(m => m.Registry.TaskType, StringComparer.Ordinal)
                            .Select(m => new TradingModelIdentity(m.Registry.ModelId, m.Registry.TaskType, m.ArtifactSha256)).ToImmutableArray());
                }
            }
        }
        catch (Exception ex) when (InvalidDefinition(ex)) { /* Preserve all choices as unavailable below. */ }
        var entries = ImmutableArray.CreateBuilder<AiPortfolioCatalogEntry>();
        var shadow = await c.QueryAsync(Query("""
SELECT p.PortfolioId,p.DisplayName,p.Lens,p.MaximumPositions,p.Status,g.Status GenerationStatus,g.IsGateComparison,
 a.VersionId,v.Family,v.SettingsJson,v.SettingsHash
FROM dbo.ShadowPortfolio p JOIN dbo.ShadowPortfolioGeneration g ON g.GenerationId=p.GenerationId
OUTER APPLY(SELECT TOP(1) VersionId FROM dbo.EngineStrategyAssignment WHERE TargetId=p.PortfolioId ORDER BY Sequence DESC) a
LEFT JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId
WHERE g.IsGateComparison=1 OR g.GenerationId=(SELECT TOP(1) GenerationId FROM dbo.ShadowPortfolioGeneration
 WHERE IsGateComparison=0 ORDER BY CreatedUtc DESC,GenerationId);
"""));
        foreach (var row in shadow)
        {
            Guid id = row.PortfolioId;
            var comparison = comparisonAccounts.SingleOrDefault(a => a.AccountId == id);
            entries.Add(Create(id, (string)row.DisplayName, TradingStrategyFamily.DailyShadow, comparison, () =>
            {
                if (daily is null || (bool)row.IsGateComparison != (comparison is not null)) throw new InvalidDataException();
                string json = comparison is null ? (string?)row.SettingsJson ?? throw new InvalidDataException()
                    : comparison.ShadowSettingsJson ?? throw new InvalidDataException();
                Guid policyId = (comparison is null ? (Guid?)row.VersionId : comparison.ShadowStrategyVersionId) ?? throw new InvalidDataException();
                if (comparison is null && ((string?)row.Family != EngineStrategySettings.Shadow || (string?)row.SettingsHash != EngineStrategySettings.Hash(json)))
                    throw new InvalidDataException();
                var policy = (SystemShadowPolicyConfig)EngineStrategySettings.Read(EngineStrategySettings.Shadow, json);
                return Definition(id, (string)row.DisplayName, TradingStrategyFamily.DailyShadow, daily, policyId, policy, null,
                    CompleteTradingStrategy.ShadowSelectionPrefix + (string)row.Lens, (byte)row.MaximumPositions);
            }, (string)row.Status is "Draft" or "Stopped" || (string)row.GenerationStatus is "Draft" or "Stopped"
                ? ["Paper source is not an active strategy account"] : []));
        }
        var live = await c.QueryAsync(Query("""
SELECT l.PortfolioId,l.SnapshotJson,g.EndExclusiveTradingDate,g.EffectiveSessionOpenUtc,a.CancelledUtc
FROM dbo.DelphiLivePortfolioLedger l JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
JOIN dbo.DelphiLivePolicyAssignment a ON a.AssignmentId=g.AssignmentId ORDER BY l.PortfolioId;
"""));
        DateTime readUtc = DateTime.SpecifyKind(await c.ExecuteScalarAsync<DateTime>(Query("SELECT SYSUTCDATETIME();")), DateTimeKind.Utc);
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(readUtc));
        foreach (var row in live)
        {
            Guid id = row.PortfolioId;
            var comparison = comparisonAccounts.SingleOrDefault(a => a.AccountId == id);
            try
            {
                var snapshot = await DelphiLiveSnapshotStorage.ReadAsync(c, t, (string)row.SnapshotJson, cancellationToken);
                var policy = await DelphiLiveSessionRepository.ReadPolicyAsync(c, snapshot.PolicyVersionId, t, cancellationToken);
                var account = new DelphiLivePortfolioAccount(snapshot, DateTime.SpecifyKind((DateTime)row.EffectiveSessionOpenUtc, DateTimeKind.Utc),
                    row.EndExclusiveTradingDate is null ? null : DateOnly.FromDateTime((DateTime)row.EndExclusiveTradingDate), (DateTime?)row.CancelledUtc);
                entries.Add(Create(id, account.DisplayName, TradingStrategyFamily.DelphiLive, comparison, () =>
                {
                    if (daily is null || snapshot.PortfolioId != id || snapshot.ComparisonAccount != comparison) throw new InvalidDataException();
                    return Definition(id, account.DisplayName, TradingStrategyFamily.DelphiLive, daily, policy.PolicyVersionId,
                        null, policy, CompleteTradingStrategy.LiveSelection, policy.MaximumHoldings);
                }, !account.IsCurrentOrQueued(date) || readUtc < account.EffectiveOpenUtc ? ["Paper source is ended, cancelled or not yet effective"] : []));
            }
            catch (Exception ex) when (InvalidDefinition(ex))
            {
                entries.Add(new(id, "Delphi Live · " + id.ToString("N")[..8], nameof(TradingStrategyFamily.DelphiLive), null,
                    comparison, ["Saved strategy definition is missing, inconsistent or unsupported"]));
            }
        }
        await t.CommitAsync(cancellationToken);
        return new(readUtc, new DateTimeOffset(readUtc).Add(maximumAge), entries.OrderBy(e => e.Id, StringComparer.Ordinal).ToImmutableArray());
    }

    internal static AiPortfolioCatalogEntry Create(Guid id, string name, TradingStrategyFamily family,
        PaperComparisonAccount? comparison, Func<CompleteTradingStrategy> definition, ImmutableArray<string> blocks)
    {
        try
        {
            var entry = new AiPortfolioCatalogEntry(id, name, family.ToString(), definition(), comparison, blocks);
            entry.Validate();
            return entry;
        }
        catch (Exception ex) when (InvalidDefinition(ex))
        {
            return new(id, name, family.ToString(), null, comparison, blocks.Add("Saved strategy definition is missing, inconsistent or unsupported"));
        }
    }

    private static bool InvalidDefinition(Exception ex) => ex is InvalidDataException or ArgumentException or InvalidOperationException or JsonException;
    private static CompleteTradingStrategy Definition(Guid id, string name, TradingStrategyFamily family, TradingDailyInputs daily,
        Guid policyId, SystemShadowPolicyConfig? shadow, DelphiLivePolicyDefinition? live, string selection, int maximum)
    {
        var strategy = new CompleteTradingStrategy(id, id, name, family, daily, policyId, shadow, live, selection, maximum,
            TradingRecommendationStorage.ImplementationIdentity, CompleteTradingStrategy.Contract);
        return strategy with { VersionId = TradingRecommendationStorage.Id(strategy.Fingerprint) };
    }
}
