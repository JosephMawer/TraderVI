#nullable enable
using Core.Runtime;
using Core.TMX;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed class CollectorAdoptionReadiness : SQLBase
{
    public async Task<BrokerMinuteSourceQualification> ReadAsync(string runtimeDirectory, string implementation,
        ITsxSessionCalendar calendar, DateTimeOffset now, CancellationToken ct)
    {
        if (!CollectorIntradayRouting.Read(runtimeDirectory) || !TmxClient.UsesIndependentIntradayCollector)
            throw new InvalidOperationException("Restart the reviewed app/engine with the qualified collector routing package before adoption.");
        var qualification = CollectorQualificationPackage.Read(runtimeDirectory, implementation, now);
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (!calendar.IsRegularSession(date)) throw new InvalidOperationException("Current regular-session collector evidence is required.");
        var bounds = calendar.GetSessionBounds(date);
        await using var c = new SqlConnection(ConnectionString);
        bool ready = await c.QuerySingleAsync<bool>(new CommandDefinition("""
SELECT CAST(CASE WHEN
 (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN
 (N'MarketCollectorLease',N'MarketCollectorAttempt',N'MarketCollectorBook',N'MarketCollectorRequest',N'MarketCollectorObservation',N'MarketCollectorConflict'))=6
 AND (SELECT COUNT(*) FROM sys.triggers WHERE is_disabled=0 AND is_instead_of_trigger=1 AND name IN
 (N'TR_MarketCollectorAttempt_Immutable',N'TR_MarketCollectorBook_Immutable',N'TR_MarketCollectorRequest_Immutable',N'TR_MarketCollectorObservation_Immutable',N'TR_MarketCollectorConflict_Immutable'))=5
 THEN 1 ELSE 0 END AS bit);
""", cancellationToken: ct));
        if (!ready) throw new InvalidOperationException("Install and qualify collector storage/immutable guards before adoption.");
        bool held = await c.QuerySingleAsync<bool>(new CommandDefinition("""
SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.MarketCollectorLease WHERE SourceContract=@Source AND ExpiresUtc>SYSUTCDATETIME()) THEN 1 ELSE 0 END AS bit);
""", new { Source = qualification.SourceContract }, cancellationToken: ct));
        var book = await new MarketCollectorRepository().ReadAsync(qualification.SourceContract, "XIU", bounds.OpenUtc, ct);
        var end = new DateTime(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        var latest = book?.Observations.SingleOrDefault(o => o.EndUtc == end);
        if (!held || book is null || !book.ConflictingStartsUtc.IsEmpty || latest is null ||
            latest.DeadlineUtc - latest.EndUtc != qualification.DeadlineOffset || DelphiLiveMinuteEvidence.AvailableAt(book, end, now.UtcDateTime) is null)
            throw new InvalidOperationException("The independent collector needs a current, conflict-free XIU minute and valid writer ownership before strategy review.");
        return qualification;
    }
}
