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
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class EngineSettingsRepository : SQLBase
{
    public const string Migration = "20260907_027_AddCentralSettings.sql";
    public async Task<IReadOnlyDictionary<Guid, string>> ReadAssignedNamesAsync()
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync();
        if (!await Installed(c)) return new Dictionary<Guid, string>();
        var rows = await c.QueryAsync("""
            WITH currentAssignments AS (SELECT TargetId,VersionId,ROW_NUMBER() OVER(PARTITION BY TargetId ORDER BY Sequence DESC) n FROM dbo.EngineStrategyAssignment)
            SELECT a.TargetId,v.Name FROM currentAssignments a JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId WHERE a.n=1
            """);
        return rows.ToDictionary(r => (Guid)r.TargetId, r => (string)r.Name);
    }
    internal static async Task<bool> Installed(SqlConnection c, SqlTransaction? t = null) =>
        await c.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.EngineStrategyAssignment',N'U') IS NULL THEN 0 ELSE 1 END", transaction: t) == 1;

    public async Task<EngineSettingsCatalog> LoadAsync()
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync();
        bool installed = await Installed(c);
        var versions = installed ? (await c.QueryAsync<EngineStrategyVersion>("SELECT VersionId,Family,Name,SettingsJson,SettingsHash,CreatedUtc FROM dbo.EngineStrategyVersion ORDER BY CreatedUtc DESC")).ToList() : [];
        var targets = new List<EngineSettingsTarget>();
        if (await c.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.ShadowPortfolio',N'U') IS NULL THEN 0 ELSE 1 END") == 1)
            targets.AddRange(await c.QueryAsync<EngineSettingsTarget>("""
                SELECT p.PortfolioId TargetId,N'SystemShadow' Family,p.DisplayName Name,p.Status State,
                CAST(NULL AS uniqueidentifier) VersionId,CAST(NULL AS uniqueidentifier) AssignmentId,
                CONCAT(N'Version 1 · ',p.Lens,N' · ',p.MaximumPositions,N' slots') ActiveName
                FROM dbo.ShadowPortfolio p JOIN dbo.ShadowPortfolioGeneration g ON g.GenerationId=p.GenerationId
                WHERE g.Status<>N'Stopped' AND p.Status<>N'Stopped'
                """));
        if (await c.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.DelphiLivePortfolioLedger',N'U') IS NULL THEN 0 ELSE 1 END") == 1)
        {
            foreach (var row in await c.QueryAsync("""
                SELECT l.SnapshotJson FROM dbo.DelphiLivePortfolioLedger l
                JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId WHERE g.EndExclusiveTradingDate IS NULL
                """))
            {
                var state = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>((string)row.SnapshotJson);
                targets.Add(new(state.PortfolioId, EngineStrategySettings.Live, $"Delphi Live · {state.Role} · {state.PortfolioId.ToString("N")[..8]}",
                    $"{state.OpenPositions.Count()} holdings", state.PolicyVersionId, null, $"Policy {state.PolicyVersionId.ToString("N")[..8]}"));
                if (versions.All(v => v.VersionId != state.PolicyVersionId))
                {
                    var policy = await DelphiLiveSessionRepository.ReadPolicyAsync(c, state.PolicyVersionId, null, default);
                    string json = EngineStrategySettings.Serialize(policy);
                    versions.Add(new(policy.PolicyVersionId, EngineStrategySettings.Live, $"Current policy {policy.PolicyVersionId.ToString("N")[..8]}", json, EngineStrategySettings.Hash(json), DateTime.MinValue));
                }
            }
        }
        targets.Add(new(EngineStrategySettings.TrackedTargetId, EngineStrategySettings.Tracked, "Trading monitor · all tracked positions", "Real exits remain manual", null, null, "Delayed intraday swing V1"));
        if (installed)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var current = await ReadAssignment(c, targets[i].TargetId);
                if (current is not null) targets[i] = targets[i] with { VersionId = current.VersionId, AssignmentId = current.AssignmentId, ActiveName = current.Name };
            }
        }
        return new(installed, versions, targets);
    }

    public async Task<EngineStrategyVersion> SaveAsync(string family, string name, object settings, string reason)
    {
        name = name.Trim(); reason = reason.Trim();
        if (name.Length is < 1 or > 80 || reason.Length is < 1 or > 512) throw new ArgumentException("Enter a name (1–80 characters) and reason (1–512 characters).");
        Guid id = Guid.NewGuid();
        if (settings is DelphiLivePolicyDefinition policy) settings = policy with { PolicyVersionId = id };
        EngineStrategySettings.Validate(family, settings);
        string json = EngineStrategySettings.Serialize(settings);
        var result = new EngineStrategyVersion(id, family, name, json, EngineStrategySettings.Hash(json), DateTime.UtcNow);
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync();
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        if (!await Installed(c, t)) throw new InvalidOperationException($"Install reviewed migration {Migration} before saving versions.");
        if (settings is DelphiLivePolicyDefinition live) await DelphiLiveExperimentRepository.RegisterSettingsPolicyAsync(c, t, live, "ADR-0060", default);
        await c.ExecuteAsync("""
            INSERT dbo.EngineStrategyVersion(VersionId,Family,Name,SettingsJson,SettingsHash,CreatedUtc,CreatedBy,Reason)
            VALUES(@VersionId,@Family,@Name,@SettingsJson,@SettingsHash,@CreatedUtc,@By,@Reason)
            """, new { result.VersionId, result.Family, result.Name, result.SettingsJson, result.SettingsHash, result.CreatedUtc, By = Environment.UserName, Reason = reason }, t);
        t.Commit();
        return result;
    }

    public async Task<Guid> AssignAsync(EngineSettingsTarget target, EngineStrategyVersion version, string reason)
    {
        if (target.Family != version.Family) throw new ArgumentException("A strategy can only be assigned within its own system family.");
        reason = reason.Trim();
        if (reason.Length is < 1 or > 512) throw new ArgumentException("Enter an assignment reason of 1–512 characters.");
        var settings = EngineStrategySettings.Read(version.Family, version.SettingsJson);
        EngineStrategySettings.Validate(version.Family, settings);
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync();
        using var t = c.BeginTransaction(IsolationLevel.Serializable);
        if (!await Installed(c, t)) throw new InvalidOperationException($"Install reviewed migration {Migration} first.");
        await c.ExecuteAsync("""
            SET XACT_ABORT ON;
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=N'TraderVI.EngineSettings',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=30000;
            IF @r<0 THROW 51313,'An evaluation is still running. Retry the assignment after it completes.',1;
            """, transaction: t, commandTimeout: 40);
        var saved = await c.QuerySingleOrDefaultAsync<EngineStrategyVersion>("SELECT VersionId,Family,Name,SettingsJson,SettingsHash,CreatedUtc FROM dbo.EngineStrategyVersion WHERE VersionId=@Id", new { Id = version.VersionId }, t);
        if (saved is null || saved.Family != version.Family || saved.SettingsHash != version.SettingsHash || saved.SettingsJson != version.SettingsJson)
            throw new InvalidOperationException("Save this version before assigning it, then reload the catalog.");
        if (saved.SettingsHash != EngineStrategySettings.Hash(saved.SettingsJson)) throw new InvalidOperationException("Saved strategy checksum failed.");
        var current = await ReadAssignment(c, target.TargetId, t);
        if (current?.AssignmentId != target.AssignmentId || current?.VersionId == version.VersionId)
            throw new InvalidOperationException("The assignment changed or this version is already assigned. Reload settings.");
        Guid assignment = Guid.NewGuid(); DateTime now = DateTime.UtcNow;
        string prior = "{}";
        DelphiLivePortfolioSnapshot? live = null;
        if (target.Family == EngineStrategySettings.Live)
        {
            prior = await c.QuerySingleAsync<string>("SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger WITH(UPDLOCK,HOLDLOCK) WHERE PortfolioId=@Id", new { Id = target.TargetId }, t);
            live = DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(prior);
            if (live.PolicyVersionId != target.VersionId) throw new InvalidOperationException("The portfolio policy changed. Reload settings.");
            if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.DelphiLivePortfolioGeneration WHERE GenerationId=@Id AND EndExclusiveTradingDate IS NULL", new { Id = live.GenerationId }, t) != 1)
                throw new InvalidOperationException("This portfolio generation has ended.");
        }
        else if (target.Family == EngineStrategySettings.Shadow)
        {
            prior = await c.QuerySingleAsync<string>("SELECT (SELECT p.* FROM dbo.ShadowPortfolio p JOIN dbo.ShadowPortfolioGeneration g ON g.GenerationId=p.GenerationId WHERE p.PortfolioId=@Id AND p.Status<>N'Stopped' AND g.Status<>N'Stopped' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)", new { Id = target.TargetId }, t);
            if (string.IsNullOrEmpty(prior)) throw new InvalidOperationException("The Shadow portfolio is no longer available.");
            prior = await c.QuerySingleAsync<string>("""
                SELECT (SELECT JSON_QUERY(@Portfolio) Portfolio,
                JSON_QUERY((SELECT * FROM dbo.ShadowPosition WHERE PortfolioId=@Id FOR JSON PATH)) Positions,
                JSON_QUERY((SELECT * FROM dbo.ShadowOrder WHERE PortfolioId=@Id AND Status=N'Pending' FOR JSON PATH)) PendingOrders FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
                """, new { Id = target.TargetId, Portfolio = prior }, t);
        }
        else if (target.Family == EngineStrategySettings.Tracked)
        {
            if (target.TargetId != EngineStrategySettings.TrackedTargetId) throw new ArgumentException("Unknown tracked-position target.");
            prior = await c.QuerySingleAsync<string>("SELECT (SELECT PositionId,HighWaterMark FROM dbo.ActivePosition WHERE IsActive=1 FOR JSON PATH)", transaction: t);
        }
        await c.ExecuteAsync("""
            INSERT dbo.EngineStrategyAssignment(AssignmentId,TargetId,VersionId,AssignedUtc,AssignedBy,Reason,PriorStateJson)
            VALUES(@Assignment,@Target,@Version,@Now,@By,@Reason,@Prior)
            """, new { Assignment = assignment, Target = target.TargetId, Version = version.VersionId, Now = now, By = Environment.UserName, Reason = reason, Prior = prior }, t);
        if (live is not null)
        {
            var next = EngineStrategyReassignment.Rebase(live, (DelphiLivePolicyDefinition)settings, now);
            string json = DelphiLiveLedgerJson.Serialize(next);
            await c.ExecuteAsync("""
                UPDATE dbo.DelphiLivePortfolioLedger SET DelphiLivePolicyVersionId=@Version,Revision=@Revision,SnapshotJson=@Json,UpdatedUtc=@Now WHERE PortfolioId=@Target;
                INSERT dbo.DelphiLivePortfolioRevision(PortfolioId,Revision,SnapshotJson,SettingsAssignmentId) VALUES(@Target,@Revision,@Json,@Assignment);
                INSERT dbo.DelphiLiveLedgerEvent(EventId,PortfolioId,Revision,EventKind,RecordedUtc,DataJson)
                VALUES(@Assignment,@Target,@Revision,N'StrategyReassigned',@Now,@Audit);
                -- Invalidate cached research reads; the assignment audit determines policy stability.
                UPDATE dbo.DelphiLiveSession SET UpdatedUtc=@Now WHERE TradingDate=@Date;
                """, new { Target = target.TargetId, Version = version.VersionId, next.Revision, Json = json, Now = now, Assignment = assignment,
                    Audit = EngineStrategySettings.Serialize(new { PreviousPolicy = live.PolicyVersionId, version.VersionId, Reason = reason }), Date = PaperTradingMonitor.ToToronto(now).Date }, t);
        }
        else if (settings is SystemShadowPolicyConfig shadow)
        {
            await c.ExecuteAsync("""
                UPDATE dbo.ShadowOrder SET Status=N'Cancelled',ReasonCode=N'StrategyReassigned',UpdatedUtc=@Now
                 WHERE PortfolioId=@Target AND Status=N'Pending';
                UPDATE dbo.ShadowPosition SET
                 ProfitProtectionArmed=CASE WHEN COALESCE(HighestFifteenClose,AverageCost)>=AverageCost/(1-@Friction) THEN 1 ELSE 0 END,
                 TrailingStopPrice=CASE WHEN COALESCE(HighestFifteenClose,AverageCost)<AverageCost/(1-@Friction) THEN NULL
                 WHEN COALESCE(HighestFifteenClose,AverageCost)*(1-@Trail)>AverageCost/(1-@Friction) THEN COALESCE(HighestFifteenClose,AverageCost)*(1-@Trail)
                 ELSE AverageCost/(1-@Friction) END
                 WHERE PortfolioId=@Target AND Status=N'Open';
                INSERT dbo.ShadowPortfolioEvent(EventId,PortfolioId,OccurredUtc,EventType,ReasonCode,DetailsJson)
                 VALUES(@Assignment,@Target,@Now,N'Lifecycle',N'StrategyReassigned',@Audit);
                """, new { Target = target.TargetId, Now = now, Friction = shadow.ExitFrictionRate, Trail = shadow.TrailingLossFraction,
                    Assignment = assignment, Audit = EngineStrategySettings.Serialize(new { version.VersionId, Reason = reason }) }, t);
        }
        t.Commit(); return assignment;
    }

    internal sealed record AssignmentRow(Guid AssignmentId, Guid VersionId, string Name, string Family, string SettingsJson, string SettingsHash, DateTime AssignedUtc);

    public async Task<IReadOnlyDictionary<Guid, decimal?>> ReadTrackedHighsAsync()
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync();
        if (!await Installed(c)) return new Dictionary<Guid, decimal?>();
        return (await c.QueryAsync<TrackedHigh>("""
            SELECT h.PositionId,h.HighWaterMark FROM
             (SELECT TOP(1) PriorStateJson FROM dbo.EngineStrategyAssignment WHERE TargetId=@Target ORDER BY Sequence DESC) a
            CROSS APPLY OPENJSON(a.PriorStateJson) WITH(PositionId uniqueidentifier '$.PositionId',HighWaterMark decimal(19,6) '$.HighWaterMark') h
            """, new { Target = EngineStrategySettings.TrackedTargetId })).ToDictionary(x => x.PositionId, x => x.HighWaterMark);
    }
    private sealed record TrackedHigh(Guid PositionId, decimal? HighWaterMark);
    private static Task<AssignmentRow?> ReadAssignment(SqlConnection c, Guid target, SqlTransaction? t = null) => c.QuerySingleOrDefaultAsync<AssignmentRow>("""
        SELECT TOP(1) a.AssignmentId,a.VersionId,v.Name,v.Family,v.SettingsJson,v.SettingsHash,a.AssignedUtc
        FROM dbo.EngineStrategyAssignment a JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId
        WHERE a.TargetId=@Target ORDER BY a.Sequence DESC
        """, new { Target = target }, t);

    public async Task<(T Settings, Guid? VersionId, DateTime? AssignedUtc)> ReadAsync<T>(Guid target, string family, T fallback)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync();
        if (!await Installed(c)) return (fallback, null, null);
        var row = await ReadAssignment(c, target);
        if (row is null) return (fallback, null, null);
        if (row.Family != family || row.SettingsHash != EngineStrategySettings.Hash(row.SettingsJson)) throw new InvalidOperationException("Invalid strategy assignment; evaluation stopped.");
        var value = (T)EngineStrategySettings.Read(family, row.SettingsJson);
        EngineStrategySettings.Validate(family, value!);
        return (value, row.VersionId, DateTime.SpecifyKind(row.AssignedUtc, DateTimeKind.Utc));
    }

    public static async Task<bool> HasLiveOverridesAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(new SQLBase().ConnectionString); await c.OpenAsync(ct);
        return await Installed(c) && await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.EngineStrategyAssignment a JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId WHERE v.Family=N'DelphiLive'") > 0;
    }

    internal static async Task<DelphiLivePolicyAssignment[]> OverlayLiveAsync(SqlConnection c, DateOnly date, DelphiLivePolicyAssignment[] assignments, CancellationToken ct)
    {
        if (!await Installed(c)) return assignments;
        var rows = await c.QueryAsync("""
            SELECT g.AssignmentId OriginalAssignment,a.AssignmentId,a.VersionId,g.PortfolioRole FROM dbo.DelphiLivePortfolioGeneration g
            JOIN dbo.DelphiLivePortfolioLedger l ON l.GenerationId=g.GenerationId
            CROSS APPLY(SELECT TOP(1) a.AssignmentId,a.VersionId FROM dbo.EngineStrategyAssignment a
              WHERE a.TargetId=l.PortfolioId AND a.AssignedUtc<DATEADD(day,1,@Date) ORDER BY a.Sequence DESC) a
            WHERE g.EffectiveTradingDate<=@Date AND (g.EndExclusiveTradingDate IS NULL OR g.EndExclusiveTradingDate>@Date)
            """, new { Date = date.ToDateTime(TimeOnly.MinValue) });
        var updated = assignments.ToList();
        foreach (var row in rows)
        {
            int i = updated.FindIndex(a => a.Role.ToString() == (string)row.PortfolioRole);
            if (i >= 0) updated[i] = updated[i] with { AssignmentId = (Guid)row.AssignmentId, PolicyVersionId = (Guid)row.VersionId };
            else if ((string)row.PortfolioRole == "ChampionControl")
                updated.Add(new((Guid)row.AssignmentId, (Guid)row.VersionId, DelphiLivePolicyRole.ChampionControl, date, null));
        }
        return updated.ToArray();
    }

    internal static async Task<bool> IsLivePolicyAssignedAsync(SqlConnection c, Guid session, Guid policy)
    {
        if (!await Installed(c)) return false;
        return await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.DelphiLivePortfolioLedger l
            JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
            JOIN dbo.DelphiLiveSession s ON s.SessionId=@Session AND g.EffectiveTradingDate<=s.TradingDate
              AND (g.EndExclusiveTradingDate IS NULL OR g.EndExclusiveTradingDate>s.TradingDate)
            CROSS APPLY(SELECT TOP(1) a.VersionId FROM dbo.EngineStrategyAssignment a WHERE a.TargetId=l.PortfolioId ORDER BY a.Sequence DESC) a
            WHERE a.VersionId=@Policy AND l.DelphiLivePolicyVersionId=@Policy
            """, new { Session = session, Policy = policy }) > 0;
    }

    internal static async Task<bool> HasLiveOverrideForSessionAsync(SqlConnection c, Guid session, DateTime asOf)
    {
        if (!await Installed(c)) return false;
        return await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.EngineStrategyAssignment a
            JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId AND v.Family=N'DelphiLive'
            JOIN dbo.DelphiLivePortfolioLedger l ON l.PortfolioId=a.TargetId
            JOIN dbo.DelphiLivePortfolioGeneration g ON g.GenerationId=l.GenerationId
            JOIN dbo.DelphiLiveSession s ON s.SessionId=@Session
            WHERE a.AssignedUtc<=@AsOf AND a.AssignedUtc<=s.SessionCloseUtc
             AND g.EffectiveTradingDate<=s.TradingDate AND (g.EndExclusiveTradingDate IS NULL OR g.EndExclusiveTradingDate>s.TradingDate)
            """, new { Session = session, AsOf = asOf }) > 0;
    }
}
