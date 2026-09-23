#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class PaperComparisonRepository : SQLBase
{
    public const string Migration = "20260909_033_FixedGatePaperComparisons.sql";
    public const string InitialBatchKey = "ADR-0067-initial-five-pairs";

    internal static Task<bool> InstalledAsync(SqlConnection c, SqlTransaction? t = null) =>
        c.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.PaperComparisonAccount',N'U') IS NOT NULL THEN 1 ELSE 0 END AS bit);", transaction: t);

    public async Task<IReadOnlyList<PaperComparisonAccount>> ReadAccountsAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        return await ReadAccountsAsync(c, null, ct);
    }

    internal static async Task<IReadOnlyList<PaperComparisonAccount>> ReadAccountsAsync(SqlConnection c, SqlTransaction? t, CancellationToken ct)
    {
        if (!await InstalledAsync(c, t)) return [];
        var rows = await c.QueryAsync(new CommandDefinition("""
SELECT a.*,b.EffectiveUtc,b.DailyStrategyVersionId FROM dbo.PaperComparisonAccount a
JOIN dbo.PaperComparisonBatch b ON b.BatchId=a.BatchId ORDER BY b.AuthorizedUtc,a.StrategyCode,a.GateProfile;
""", transaction: t, cancellationToken: ct));
        return rows.Select(a => new PaperComparisonAccount((Guid)a.AccountId, (Guid)a.BatchId, (Guid)a.PairId,
            (string)a.StrategyCode, Enum.Parse<PaperGateProfile>((string)a.GateProfile), Utc((DateTime)a.EffectiveUtc),
            (Guid)a.DailyStrategyVersionId, (Guid?)a.LivePolicyVersionId, (Guid?)a.ShadowStrategyVersionId,
            (string?)a.ShadowSettingsJson)).ToArray();
    }

    public async Task<PaperComparisonSource?> ReadSourceAsync(Guid batch, DateTime date, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        return await ReadSourceAsync(c, null, batch, date, ct);
    }

    internal static async Task<PaperComparisonSource?> ReadSourceAsync(SqlConnection c, SqlTransaction? t, Guid batch, DateTime date, CancellationToken ct)
    {
        if (!await InstalledAsync(c, t)) return null;
        var row = await c.QuerySingleOrDefaultAsync(new CommandDefinition("""
SELECT TOP(1) r.RevisionId,r.CalibrationRunId,r.SnapshotJson,r.SnapshotSha256 FROM dbo.PaperComparisonSourceRevision r
JOIN dbo.DelphiLiveSession s ON s.SessionId=r.SessionId WHERE r.BatchId=@Batch AND s.TradingDate=@Date
ORDER BY r.AdoptedUtc DESC,r.RevisionId;
""", new { Batch = batch, Date = date.Date }, t, cancellationToken: ct));
        if (row is null) return null;
        string json = row.SnapshotJson;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(json)), (byte[])row.SnapshotSha256))
            throw new InvalidOperationException("Comparison source checksum mismatch.");
        var source = DelphiLiveLedgerJson.Deserialize<PaperComparisonSource>(json);
        if (source.BatchId != batch || source.RevisionId != (Guid)row.RevisionId || source.RunId != (Guid)row.CalibrationRunId)
            throw new InvalidOperationException("Comparison source identity mismatch.");
        return source;
    }

    public async Task<IReadOnlyList<SystemShadowPortfolioOverview>> ReadShadowOverviewsAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        if (!await InstalledAsync(c)) return [];
        var generations = await c.QueryAsync<Guid>(new CommandDefinition(
            "SELECT DISTINCT p.GenerationId FROM dbo.PaperComparisonAccount a JOIN dbo.ShadowPortfolio p ON p.PortfolioId=a.ShadowPortfolioId;", cancellationToken: ct));
        var rows = new List<SystemShadowPortfolioOverview>();
        foreach (Guid generation in generations)
            rows.AddRange(await new SystemShadowRepository().GetPortfolioOverviewsAsync(generation, ct));
        return rows;
    }

    /// <summary>Explicit operator activation only. All ten accounts commit together; retries never recapitalize.</summary>
    public async Task<IReadOnlyList<PaperComparisonAccount>> ActivateInitialAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await TradingControlRepository.FenceAsync(c, t);
        if (!await InstalledAsync(c, t)) throw new InvalidOperationException($"Install {Migration} before activation.");
        Guid? existing = await c.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT BatchId FROM dbo.PaperComparisonBatch WITH(UPDLOCK,HOLDLOCK) WHERE BatchKey=@Key;", new { Key = InitialBatchKey }, t, cancellationToken: ct));
        if (existing.HasValue)
        {
            var prior = (await ReadAccountsAsync(c, t, ct)).Where(a => a.BatchId == existing).ToArray();
            if (prior.Length != 10) throw new InvalidOperationException("The existing comparison batch is incomplete; activation cannot replace it.");
            await t.CommitAsync(ct);
            return prior;
        }
        Guid daily = await c.QuerySingleAsync<Guid>(new CommandDefinition(
            "SELECT VersionId FROM dbo.StrategyVersion WHERE IsActive=1;", transaction: t, cancellationToken: ct));
        Guid liveBase = await c.QuerySingleAsync<Guid>(new CommandDefinition("""
SELECT l.DelphiLivePolicyVersionId FROM dbo.DelphiLivePortfolioLedger l JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
WHERE g.PortfolioRole=N'OperationalChampion' AND g.EndExclusiveTradingDate IS NULL;
""", transaction: t, cancellationToken: ct));
        var livePolicy = await DelphiLiveSessionRepository.ReadPolicyAsync(c, liveBase, t, ct);
        var shadowRows = (await c.QueryAsync(new CommandDefinition("""
SELECT p.PortfolioCode,p.PortfolioId,a.VersionId,v.SettingsJson,v.SettingsHash FROM dbo.ShadowPortfolio p
JOIN dbo.ShadowPortfolioGeneration g ON g.GenerationId=p.GenerationId AND g.IsGateComparison=0 AND g.Status<>N'Stopped'
OUTER APPLY(SELECT TOP(1) VersionId FROM dbo.EngineStrategyAssignment WHERE TargetId=p.PortfolioId ORDER BY Sequence DESC) a
LEFT JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId;
""", transaction: t, cancellationToken: ct))).ToArray();
        if (shadowRows.Length != 4) throw new InvalidOperationException("Activation requires the four current Shadow strategy definitions.");
        DateTime now = Utc(await c.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: t, cancellationToken: ct)));
        var calendar = DailyBenchmarkPolicy.LoadCalendar();
        DateOnly date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now));
        if (!calendar.IsRegularSession(date) || now >= calendar.GetSessionBounds(date).CloseUtc.AddMinutes(-5)) date = calendar.GetNextSession(date);
        var bounds = calendar.GetSessionBounds(date);
        DateTime effective = PaperDailySourcePolicy.EffectiveBoundary(now.AddMinutes(2));
        if (effective < bounds.OpenUtc) effective = bounds.OpenUtc;
        Guid batch = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.PaperComparisonBatch(BatchId,BatchKey,DailyStrategyVersionId,StartingCapital,Currency,AuthorizedUtc,EffectiveUtc,TradingDate,AuthorizedBy)
VALUES(@Batch,@Key,@Daily,1000,'CAD',@Now,@Effective,@Date,@By);
""", PaperSourceSqlParameters.Create(new { Batch = batch, Key = InitialBatchKey, Daily = daily, Now = now, Effective = effective,
            Date = date.ToDateTime(TimeOnly.MinValue), By = Environment.UserName }, ("Now", now), ("Effective", effective)), t, cancellationToken: ct));
        var shadowGenerations = new Dictionary<PaperGateProfile, Guid>();
        foreach (var profile in Enum.GetValues<PaperGateProfile>())
        {
            Guid generation = Guid.NewGuid(); shadowGenerations.Add(profile, generation);
            await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.ShadowPortfolioGeneration(GenerationId,IsGateComparison,PolicyVersion,Status,TotalAccountValue,AvailableAccountCash,RealSnapshotUtc,ActivatedUtc,CreatedUtc,UpdatedUtc)
VALUES(@Generation,1,@Policy,N'Active',1000,1000,@Now,@Effective,@Now,@Now);
INSERT dbo.ShadowCapitalEvent(CapitalEventId,GenerationId,OccurredUtc,EventType,TotalAccountValue,AvailableAccountCash,Notes)
VALUES(NEWID(),@Generation,@Now,N'InitialSnapshot',1000,1000,N'ADR-0067 independent virtual comparison capital; not a real-account snapshot');
""", PaperSourceSqlParameters.Create(new { Generation = generation, Policy = SystemShadowVersions.Policy, Now = now, Effective = effective },
                ("Now", now), ("Effective", effective)), t, cancellationToken: ct));
        }
        foreach (string code in PaperGateComparison.StrategyCodes)
        {
            Guid pair = Guid.NewGuid();
            foreach (var profile in Enum.GetValues<PaperGateProfile>())
            {
                Guid accountId = Guid.NewGuid(); Guid? liveId = null, shadowId = null, shadowVersion = null;
                string? shadowSettings = null;
                if (code != "DelphiLive")
                {
                    var original = shadowRows.Single(r => (string)r.PortfolioCode == code);
                    shadowVersion = (Guid?)original.VersionId;
                    shadowSettings = (string?)original.SettingsJson ?? EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Version1);
                    if (shadowVersion.HasValue && (string?)original.SettingsHash != EngineStrategySettings.Hash(shadowSettings))
                        throw new InvalidOperationException("The source Shadow strategy checksum failed; comparison activation stopped.");
                    EngineStrategySettings.Read(EngineStrategySettings.Shadow, shadowSettings);
                    shadowId = accountId;
                    var definition = SystemShadowPortfolioDefinition.Version1.Single(d => d.Code == code);
                    await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.ShadowPortfolio(PortfolioId,GenerationId,PortfolioCode,DisplayName,Lens,MaximumPositions,SelectionActor,ExecutionMode,Status,CashBalance,HighestClosingValue,CreatedUtc,UpdatedUtc)
VALUES(@Account,@Generation,@Code,@Name,@Lens,@Maximum,N'System',N'Ghost',N'Active',1000,1000,@Now,@Now);
INSERT dbo.ShadowPortfolioEvent(EventId,PortfolioId,OccurredUtc,EventType,ReasonCode,DetailsJson)
VALUES(NEWID(),@Account,@Now,N'Lifecycle',N'ComparisonActivated',@Json);
""", PaperSourceSqlParameters.Create(new { Account = accountId, Generation = shadowGenerations[profile], Code = code,
                        Name = $"{code} · {PaperGateComparison.ProfileName(profile)} · {pair.ToString("N")[..8]}", definition.Lens,
                        Maximum = definition.MaximumPositions, Now = now,
                        Json = DelphiLiveLedgerJson.Serialize(new { batch, pair, profile, startingCapital = 1000, effective }) }, ("Now", now)), t, cancellationToken: ct));
                }
                else
                {
                    liveId = Guid.NewGuid();
                    var policy = livePolicy with { PolicyVersionId = liveId.Value };
                    await DelphiLiveExperimentRepository.RegisterSettingsPolicyAsync(c, t, policy, "ADR-0067", ct);
                    var account = new PaperComparisonAccount(accountId, batch, pair, code, profile, effective, daily, liveId, null, null);
                    Guid generation = Guid.NewGuid(), assignment = Guid.NewGuid();
                    var request = new DelphiLiveGenerationRequest(generation, accountId, liveId.Value, PaperGateComparison.LiveRole,
                        null, 1000, "CAD", date, effective, now, Environment.UserName, "ADR-0067 fixed-gate paper comparison") { ComparisonAccount = account };
                    var snapshot = DelphiLiveLedgerIntegrity.Create(request);
                    string stored = await DelphiLiveSnapshotStorage.SaveAsync(c, t, snapshot, ct);
                    await c.ExecuteAsync(new CommandDefinition("""
DECLARE @Slot tinyint=(SELECT MIN(v.n) FROM (SELECT TOP(97) ROW_NUMBER() OVER(ORDER BY object_id)+2 n FROM sys.all_objects) v
 WHERE NOT EXISTS(SELECT 1 FROM dbo.DelphiLivePolicyAssignment a WHERE a.RoleSlot=v.n AND a.EndExclusiveTradingDate IS NULL AND a.CancelledUtc IS NULL));
IF @Slot IS NULL THROW 52410,'No comparison policy slot is available.',1;
INSERT dbo.DelphiLivePolicyAssignment(AssignmentId,DelphiLivePolicyVersionId,PolicyRole,RoleSlot,EffectiveTradingDate,AuthorizedUtc,AuthorizedBy,AuthorizationReason,DecisionRef)
VALUES(@Assignment,@Policy,N'GateComparison',@Slot,@Date,@Now,@By,N'Fixed-gate comparison',N'ADR-0067');
INSERT dbo.DelphiLivePortfolioGeneration(GenerationId,AssignmentId,DelphiLivePolicyVersionId,PortfolioRole,StartingCapital,Currency,EffectiveTradingDate,EffectiveSessionOpenUtc,AuthorizedUtc,AuthorizedBy,AuthorizationReason,AuthorizationJson,ComparisonBatchId)
VALUES(@Generation,@Assignment,@Policy,N'GateComparison',1000,'CAD',@Date,@Effective,@Now,@By,N'Fixed-gate comparison',@Authorization,@Batch);
INSERT dbo.DelphiLivePortfolioLedger(PortfolioId,GenerationId,DelphiLivePolicyVersionId,Revision,SnapshotSchemaVersion,SnapshotJson,UpdatedUtc)
VALUES(@Account,@Generation,@Policy,0,1,@Json,@Now);
INSERT dbo.DelphiLivePortfolioRevision(PortfolioId,Revision,SnapshotJson) VALUES(@Account,0,@Json);
INSERT dbo.DelphiLiveLedgerEvent(EventId,PortfolioId,Revision,EventKind,RecordedUtc,DataJson)
VALUES(NEWID(),@Account,0,N'ComparisonActivated',@Now,@Authorization);
""", PaperSourceSqlParameters.Create(new { Assignment = assignment, Policy = liveId, Date = date.ToDateTime(TimeOnly.MinValue), Now = now,
                        By = Environment.UserName, Generation = generation, Effective = effective, Authorization = DelphiLiveLedgerJson.Serialize(request), Batch = batch,
                        Account = accountId, Json = stored }, ("Now", now), ("Effective", effective)), t, cancellationToken: ct));
                }
                await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.PaperComparisonAccount(AccountId,BatchId,PairId,StrategyCode,GateProfile,ShadowPortfolioId,LivePortfolioId,LivePolicyVersionId,ShadowStrategyVersionId,ShadowSettingsJson)
VALUES(@Account,@Batch,@Pair,@Code,@Profile,@Shadow,@Live,@Policy,@ShadowVersion,@Settings);
""", new { Account = accountId, Batch = batch, Pair = pair, Code = code, Profile = profile.ToString(), Shadow = shadowId,
                    Live = code == "DelphiLive" ? (Guid?)accountId : null, Policy = liveId, ShadowVersion = shadowVersion, Settings = shadowSettings }, t, cancellationToken: ct));
            }
        }
        var accounts = (await ReadAccountsAsync(c, t, ct)).Where(a => a.BatchId == batch).ToArray();
        if (accounts.Length != 10 || accounts.GroupBy(a => a.PairId).Any(p => p.Count() != 2)) throw new InvalidOperationException("Comparison activation did not produce five complete pairs.");
        await t.CommitAsync(ct);
        return accounts;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
