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

/// <summary>Entry pause is durable and independent of strategy identity or capital-review guards.</summary>
public sealed class TradingControlRepository : SQLBase
{
    public const string Migration = "20260907_028_AddTradingControls.sql";
    internal static async Task<bool> InstalledAsync(SqlConnection c, SqlTransaction? t = null) =>
        await c.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.OperatorExitRequest',N'U') IS NULL THEN 0 ELSE 1 END", transaction:t) == 1;

    internal static Task FenceAsync(SqlConnection c, SqlTransaction t, string mode = "Exclusive") => c.ExecuteAsync("""
        DECLARE @r int;
        EXEC @r=sys.sp_getapplock @Resource=N'TraderVI.EngineSettings',@LockMode=@Mode,@LockOwner=N'Transaction',@LockTimeout=30000;
        IF @r<0 THROW 51413,'A trading cycle is still running. Retry after it completes.',1;
        """, new { Mode=mode }, t, commandTimeout:40);

    internal static async Task<bool> IsPausedAsync(SqlConnection c, SqlTransaction? t, string family)
    {
        TradingSystems.Validate(family);
        if (!await InstalledAsync(c,t)) return false;
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TradingSystemControl WHERE Paused=1 AND SystemKey IN (@Family,N'Daily')",new {Family=family},t)>0;
    }
    public async Task<bool> IsPausedAsync(string family)
    {
        await using var c=new SqlConnection(ConnectionString); await c.OpenAsync();
        return await IsPausedAsync(c,null,family);
    }
    public async Task<IReadOnlyList<TradingSystemState>> ReadAsync()
    {
        await using var c=new SqlConnection(ConnectionString); await c.OpenAsync();
        return await InstalledAsync(c) ? (await c.QueryAsync<TradingSystemState>("SELECT SystemKey,Paused,ChangedUtc,Reason FROM dbo.TradingSystemControl")).ToArray() : [];
    }
    public async Task SetPausedAsync(string family,bool paused,string reason)
    {
        TradingSystems.Validate(family); reason=RequireReason(reason);
        await using var c=new SqlConnection(ConnectionString); await c.OpenAsync();
        using var t=c.BeginTransaction(IsolationLevel.Serializable); await FenceAsync(c,t);
        if (!await InstalledAsync(c,t)) throw new InvalidOperationException($"Install reviewed migration {Migration} first.");
        var prior=await c.QuerySingleOrDefaultAsync<TradingSystemState>("SELECT SystemKey,Paused,ChangedUtc,Reason FROM dbo.TradingSystemControl WHERE SystemKey=@Family",new {Family=family},t);
        if(prior is not null && prior.Paused==paused) { t.Commit(); return; }
        DateTime now=DateTime.UtcNow;
        await c.ExecuteAsync("""
            UPDATE dbo.TradingSystemControl SET Paused=@Paused,ChangedUtc=@Now,Reason=@Reason WHERE SystemKey=@Family;
            IF @@ROWCOUNT=0 INSERT dbo.TradingSystemControl(SystemKey,Paused,ChangedUtc,Reason) VALUES(@Family,@Paused,@Now,@Reason);
            INSERT dbo.TradingControlEvent(EventId,SystemKey,RecordedUtc,RecordedBy,Paused,Reason,PriorStateJson)
            VALUES(@Event,@Family,@Now,@By,@Paused,@Reason,@Prior);
            """, new {Family=family,Paused=paused,Now=now,Reason=reason,Event=Guid.NewGuid(),By=Environment.UserName,Prior=System.Text.Json.JsonSerializer.Serialize(prior ?? new(family,false,DateTime.MinValue,"Initial state"))},t);
        // A pending bid is new risk. Never cancel protective sells when pausing.
        if (paused && family is TradingSystems.Daily or EngineStrategySettings.Shadow)
            await c.ExecuteAsync("""
                UPDATE dbo.ShadowOrder SET Status=N'Cancelled',ReasonCode=N'OperatorPaused',UpdatedUtc=@Now
                WHERE Side=N'Buy' AND Status=N'Pending';
                """,new {Now=now},t);
        t.Commit();
    }
    public async Task<TradingSettingsAccess> AccessAsync(string family)
    {
        await using var c=new SqlConnection(ConnectionString); await c.OpenAsync();
        return await AccessAsync(c,null,family);
    }
    internal static async Task<TradingSettingsAccess> AccessAsync(SqlConnection c,SqlTransaction? t,string family)
    {
        TradingSystems.Validate(family);
        if (!await InstalledAsync(c,t)) return new(false,false,0,0);
        if (family==TradingSystems.Daily)
        {
            var parts=new List<TradingSettingsAccess>();
            foreach(var key in TradingSystems.All.Where(k=>k!=TradingSystems.Daily)) parts.Add(await AccessAsync(c,t,key));
            return new(true,parts.All(p=>p.Paused),parts.Sum(p=>p.Holdings),parts.Sum(p=>p.PendingOrders));
        }
        int held=0,pending=0;
        if (family==EngineStrategySettings.Shadow)
        {
            held=await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ShadowPosition WHERE Status=N'Open'",transaction:t);
            pending=await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ShadowOrder WHERE Status=N'Pending'",transaction:t);
        }
        else if(family==EngineStrategySettings.Tracked)
            held=await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ActivePosition WHERE IsActive=1 AND (OriginalPickId IS NOT NULL OR ExecutionMode=N'Real')",transaction:t);
        else
        {
            foreach(var json in await c.QueryAsync<string>("SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger",transaction:t))
            {
                var state=DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(json);
                held+=state.OpenPositions.Count(); pending+=state.PendingActions.Count();
            }
        }
        return new(true,await IsPausedAsync(c,t,family),held,pending);
    }
    internal static async Task RequireEditableAsync(SqlConnection c,SqlTransaction t,string family)
    {
        var access=await AccessAsync(c,t,family);
        if (!access.CanEdit) throw new InvalidOperationException(access.Description);
    }
    public async Task<OperatorExitRequest> RequestExitAsync(string family,Guid targetId,Guid positionId,string reason)
    {
        TradingSystems.Validate(family); reason=RequireReason(reason);
        await using var c=new SqlConnection(ConnectionString); await c.OpenAsync();
        using var t=c.BeginTransaction(IsolationLevel.Serializable); await FenceAsync(c,t);
        if (!await InstalledAsync(c,t)) throw new InvalidOperationException($"Install reviewed migration {Migration} first.");
        if (!await IsPausedAsync(c,t,family)) throw new InvalidOperationException("Pause new buys before requesting an operator exit.");
        var prior=await c.QuerySingleOrDefaultAsync<OperatorExitRequest>("SELECT * FROM dbo.OperatorExitRequest WHERE Family=@Family AND TargetId=@TargetId AND PositionId=@PositionId",new {Family=family,TargetId=targetId,PositionId=positionId},t);
        if(prior is not null) { t.Commit(); return prior; }
        string json,symbol;
        if(family==EngineStrategySettings.Live)
        {
            var snapshot=await c.QuerySingleAsync<string>("SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger WHERE PortfolioId=@Id",new {Id=targetId},t);
            var position=DelphiLiveLedgerJson.Deserialize<DelphiLivePortfolioSnapshot>(snapshot).OpenPositions.SingleOrDefault(p=>p.PositionId==positionId)
                ?? throw new InvalidOperationException("This holding has already closed. Refresh the account.");
            json=System.Text.Json.JsonSerializer.Serialize(position); symbol=position.Symbol;
        }
        else
        {
            if(family==EngineStrategySettings.Tracked && targetId!=EngineStrategySettings.TrackedTargetId) throw new ArgumentException("Invalid tracked target.");
            string sql=family==EngineStrategySettings.Shadow ?
                "SELECT (SELECT * FROM dbo.ShadowPosition WHERE PositionId=@PositionId AND PortfolioId=@TargetId AND Status=N'Open' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)" :
                "SELECT (SELECT * FROM dbo.ActivePosition WHERE PositionId=@PositionId AND IsActive=1 AND ExecutionMode=N'Ghost' AND OriginalPickId IS NOT NULL FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)";
            if(family==TradingSystems.Daily) throw new ArgumentException("Daily Delphi does not own holdings.");
            json=await c.ExecuteScalarAsync<string>(sql,new {PositionId=positionId,TargetId=targetId},t) ?? "";
            if(string.IsNullOrEmpty(json)) throw new InvalidOperationException("No open simulated holding was found. Real sales must be recorded as actual broker fills.");
            using var document=System.Text.Json.JsonDocument.Parse(json); symbol=document.RootElement.GetProperty("Symbol").GetString()!;
        }
        var request=new OperatorExitRequest(Guid.NewGuid(),family,targetId,positionId,symbol,DateTime.UtcNow,Environment.UserName,reason,json);
        await c.ExecuteAsync("INSERT dbo.OperatorExitRequest(RequestId,Family,TargetId,PositionId,Symbol,RequestedUtc,RequestedBy,Reason,PositionJson) VALUES(@RequestId,@Family,@TargetId,@PositionId,@Symbol,@RequestedUtc,@RequestedBy,@Reason,@PositionJson)",request,t);
        t.Commit(); return request;
    }
    public async Task<IReadOnlyList<OperatorExitRequest>> ExitRequestsAsync(string family)
    {
        await using var c=new SqlConnection(ConnectionString); await c.OpenAsync();
        return await InstalledAsync(c) ? (await c.QueryAsync<OperatorExitRequest>("SELECT * FROM dbo.OperatorExitRequest WHERE Family=@Family ORDER BY RequestedUtc",new {Family=family})).ToArray() : [];
    }
    public async Task<Guid?> CurrentDailyStrategyAsync()
    {
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync();
        return await c.QuerySingleOrDefaultAsync<Guid?>("SELECT VersionId FROM dbo.StrategyVersion WHERE IsActive=1");
    }
    public async Task<bool> IsCurrentDailyRunAsync(Guid? runId)
    {
        if(runId is null)return false;
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync();
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.CalibrationRun r JOIN dbo.StrategyVersion s ON s.VersionId=r.StrategyVersionId AND s.IsActive=1 WHERE r.RunId=@Id",new {Id=runId})==1;
    }
    internal static async Task<bool> ResearchInterventionAsync(SqlConnection c,DateTime asOf)
    {
        if(!await InstalledAsync(c))return false;
        return await c.ExecuteScalarAsync<int>("""
            SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TradingControlEvent WHERE SystemKey IN (N'Daily',N'DelphiLive') AND Paused=1 AND RecordedUtc<=@AsOf)
              OR EXISTS(SELECT 1 FROM dbo.OperatorExitRequest WHERE Family=N'DelphiLive' AND RequestedUtc<=@AsOf) THEN 1 ELSE 0 END
            """,new {AsOf=asOf})==1;
    }
    private static string RequireReason(string reason)
    {
        reason=reason.Trim(); if(reason.Length is <1 or >512) throw new ArgumentException("Enter a reason of 1–512 characters."); return reason;
    }
}
