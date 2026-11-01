#nullable enable
using Core.Db;
using Core.Runtime;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class MarketCollectorSqlTests
{
    private async Task VerifyRuleHistoryAsync(SqlConnection db, MarketCollectorRepository repository)
    {
        const string source = "SyntheticMinuteRuleDemand";
        var now = Utc(await db.QuerySingleAsync<DateTime>("SELECT SYSUTCDATETIME();"));
        var end = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
        var subscriptions = new CollectorSubscriptionSnapshot(end.AddHours(-1), end.AddHours(1), ["IDLE"], [], []);
        var lease = (await repository.TryAcquireAsync(source, Guid.NewGuid()))!;
        var empty = new DelphiLiveMinuteEvidenceBook("XIU", source, subscriptions.SessionOpenUtc, subscriptions.SessionCloseUtc, [], []);
        var needed = new CollectorRuleHistory(source, empty.SessionOpenUtc, empty.SessionCloseUtc);
        void Missing(DateTime first, DateTime until)
            => needed.ObserveWindow(empty, DelphiLiveMinuteWindows.BuildRecoveredContext(empty, until, until-first, now), now, "MinuteEntry");
        Missing(end.AddMinutes(-30), end.AddMinutes(-26));
        Missing(end.AddMinutes(-9), end.AddMinutes(-4));
        Assert.True(await repository.RequestHistoryAsync(needed, now, default));
        // A bar arrives after the rule reported a gap, before the collector claims it.
        var seed = new CollectorAttempt(Guid.NewGuid(), source, "XIU", empty.SessionOpenUtc, empty.SessionCloseUtc,
            end.AddMinutes(-6), end.AddMinutes(-5));
        await db.ExecuteAsync("""
INSERT dbo.MarketCollectorAttempt(RequestId,SourceContract,Symbol,SessionOpenUtc,SessionCloseUtc,ExpectedEndUtc,ReservedUtc,OwnerId,FencingToken)
VALUES(@RequestId,@SourceContract,@Symbol,@SessionOpenUtc,@SessionCloseUtc,@ExpectedEndUtc,@ReservedUtc,@OwnerId,@FencingToken);
""", new { seed.RequestId, seed.SourceContract, seed.Symbol, seed.SessionOpenUtc, seed.SessionCloseUtc, seed.ExpectedEndUtc,
            seed.ReservedUtc, lease.OwnerId, lease.FencingToken });
        DelphiLiveCollectorBatch Batch(CollectorAttempt a, DateTime time, ImmutableArray<OhlcvBar> bars) =>
            new(a.RequestId,source,a.Symbol,empty.SessionOpenUtc,empty.SessionCloseUtc,a.ExpectedEndUtc,TimeSpan.FromSeconds(45),
                time,time,time,bars,"{}",null) { RequestedStartUtc=a.ExpectedEndUtc.AddMinutes(-1),RequestedHistory=a.RequiredHistory };
        OhlcvBar Bar(DateTime start) => new(start,10,11,9,10,1);
        await repository.AppendAsync(lease,Batch(seed,now,[Bar(end.AddMinutes(-7))]));
        var idle = (await repository.ReserveAsync(lease,subscriptions,"IDLE",end,20,default))!;
        Assert.Null(idle.RequiredHistory);
        var attempt = (await repository.ReserveAsync(lease,subscriptions,"XIU",end,20,default))!;
        Assert.Equal(new CollectorHistoryRange(end.AddMinutes(-6),end.AddMinutes(-4)),attempt.RequiredHistory);
        var time = Utc(await db.QuerySingleAsync<DateTime>("SELECT SYSUTCDATETIME();"));
        var response = Batch(attempt,time,[Bar(end.AddMinutes(-1)),Bar(end.AddMinutes(-6))]);
        var wrong = response with { Bars = [], RequestedHistory = null };
        Assert.Equal(54648,(await Assert.ThrowsAsync<SqlException>(() => repository.AppendAsync(lease,wrong))).Number);
        var saved = await repository.AppendAsync(lease,response);
        Assert.Equal(3,saved.Observations.Length);
        Assert.True(await repository.RequestHistoryAsync(needed,time,default));
        Assert.Equal(2,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.MarketCollectorHistoryNeed WHERE RequestId=@RequestId",attempt));
        Assert.Null(await repository.ReserveAsync(lease,subscriptions,"XIU",end,20,default));
        await repository.ReleaseAsync(lease);
        output.WriteLine("PASS: active rule gap, SQL recheck, exact contiguous claim, ordinary subscriber, fenced receipt, partial recovery and retry suppression.");
    }
}
