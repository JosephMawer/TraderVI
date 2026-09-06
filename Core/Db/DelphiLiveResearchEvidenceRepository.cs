#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Reads only locally persisted canonical evidence. It never collects or backfills prices.</summary>
public sealed class DelphiLiveResearchEvidenceRepository(ITsxSessionCalendar calendar) : SQLBase, IDelphiLiveResearchEvidenceSource
{
    public async Task<IReadOnlyList<DateOnly>> ReadChangedSessionDatesAsync(DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        if (asOfUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("A research refresh cutoff must be UTC.", nameof(asOfUtc));
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // This small header read does not materialize historical OHLC or outcomes.
        // Calendar ordinals remain explicit even when the corresponding bars are absent.
        var sessions = (await connection.QueryAsync<FrozenSessionRow>(new CommandDefinition("""
SELECT SessionId,TradingDate,SessionOpenUtc,SessionCloseUtc
FROM dbo.DelphiLiveSession WHERE SessionCloseUtc<@AsOf ORDER BY TradingDate;
""", new { AsOf = asOfUtc }, cancellationToken: cancellationToken))).ToArray();
        var horizons = sessions.Select(session =>
        {
            var dates = new List<DateOnly> { DateOnly.FromDateTime(session.TradingDate) };
            for (int index = 1; index < 5; index++)
            {
                try { dates.Add(calendar.GetNextSession(dates[^1])); }
                catch (InvalidOperationException) { break; }
            }
            return new
            {
                session.SessionId, TradingDate = dates[0], ThroughDate = dates[^1],
                ThirdCloseUtc = dates.Count >= 3 ? (DateTime?)calendar.GetSessionBounds(dates[2]).CloseUtc : null,
                FifthCloseUtc = dates.Count >= 5 ? (DateTime?)calendar.GetSessionBounds(dates[4]).CloseUtc : null
            };
        }).ToArray();
        if (horizons.Length == 0) return [];
        return (await connection.QueryAsync<DateTime>(new CommandDefinition("""
WITH horizon AS
(
 SELECT * FROM OPENJSON(@Horizons) WITH
 (SessionId UNIQUEIDENTIFIER '$.SessionId',TradingDate DATE '$.TradingDate',ThroughDate DATE '$.ThroughDate',
  ThirdCloseUtc DATETIME2 '$.ThirdCloseUtc',FifthCloseUtc DATETIME2 '$.FifthCloseUtc')
)
SELECT s.TradingDate
FROM horizon h JOIN dbo.DelphiLiveSession s ON s.SessionId=h.SessionId
OUTER APPLY (SELECT MAX(r.ReviewedUtc) AS ReviewedUtc FROM dbo.DelphiLiveResearchSessionReview r
             WHERE r.SessionId=s.SessionId AND r.ReviewedUtc<=@AsOf) review
WHERE review.ReviewedUtc IS NULL
 OR (review.ReviewedUtc<=h.ThirdCloseUtc AND h.ThirdCloseUtc<@AsOf)
 OR (review.ReviewedUtc<=h.FifthCloseUtc AND h.FifthCloseUtc<@AsOf)
 OR (s.UpdatedUtc>review.ReviewedUtc AND s.UpdatedUtc<=@AsOf)
 OR EXISTS(SELECT 1 FROM dbo.DelphiLiveSessionSymbol m WHERE m.SessionId=s.SessionId
           AND m.CreatedUtc>review.ReviewedUtc AND m.CreatedUtc<=@AsOf)
 OR EXISTS(SELECT 1 FROM dbo.IntradayCollectionSlot sl WHERE sl.SessionId=s.SessionId
           AND sl.UpdatedUtc>review.ReviewedUtc AND sl.UpdatedUtc<=@AsOf)
 OR EXISTS(SELECT 1 FROM dbo.DailyBars b JOIN dbo.DelphiLiveSessionSymbol m ON m.Symbol=b.Symbol AND m.SessionId=s.SessionId
           WHERE b.[Date] BETWEEN h.TradingDate AND h.ThroughDate AND b.CreatedAt>review.ReviewedUtc AND b.CreatedAt<=@AsOf)
 OR EXISTS(SELECT 1 FROM dbo.IntradayEvidenceBar b JOIN dbo.DelphiLiveSessionSymbol m ON m.Symbol=b.Symbol AND m.SessionId=s.SessionId
           WHERE b.IntervalMinutes=5 AND b.EventUtc>=s.SessionOpenUtc AND b.EventUtc<s.SessionCloseUtc
           AND b.CreatedUtc>review.ReviewedUtc AND b.CreatedUtc<=@AsOf)
 OR EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict x JOIN dbo.DelphiLiveSessionSymbol m ON m.Symbol=x.Symbol AND m.SessionId=s.SessionId
           WHERE x.IntervalMinutes=5 AND x.ExistingBarEventUtc>=s.SessionOpenUtc AND x.ExistingBarEventUtc<s.SessionCloseUtc
           AND x.CreatedUtc>review.ReviewedUtc AND x.CreatedUtc<=@AsOf)
 OR EXISTS(SELECT 1 FROM dbo.DelphiLiveCorporateActionAudit d JOIN dbo.DelphiLiveSessionSymbol m ON m.Symbol=d.Symbol AND m.SessionId=s.SessionId
           WHERE d.AffectedFrom<=h.ThroughDate AND d.AffectedThrough>=h.TradingDate
           AND d.RecordedUtc>review.ReviewedUtc AND d.RecordedUtc<=@AsOf)
ORDER BY s.TradingDate;
""", new { Horizons = JsonSerializer.Serialize(horizons), AsOf = asOfUtc }, cancellationToken: cancellationToken)))
            .Select(DateOnly.FromDateTime).ToArray();
    }

    public async Task<IReadOnlyList<DateOnly>> ReadFrozenDatesAsync(DateOnly through, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return (await connection.QueryAsync<DateTime>(new CommandDefinition(
            "SELECT TradingDate FROM dbo.DelphiLiveSession WHERE TradingDate<=@Through ORDER BY TradingDate;",
            new { Through = Date(through) }, cancellationToken: cancellationToken))).Select(DateOnly.FromDateTime).ToArray();
    }

    public async Task<DelphiLiveResearchSessionEvidence> ReadAsync(DelphiLiveSessionContext context,
        DateTime throughBarEndUtc, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        if (throughBarEndUtc.Kind != DateTimeKind.Utc || asOfUtc.Kind != DateTimeKind.Utc ||
            throughBarEndUtc > context.Bounds.CloseUtc || asOfUtc <= throughBarEndUtc)
            throw new ArgumentException("Research reads require a completed canonical checkpoint and receipt cutoff.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var memberships = (await connection.QueryAsync<MemberRow>(new CommandDefinition("""
SELECT Symbol,IsXiuBenchmark,RequiredFromBarEndUtc,RequiredThroughBarEndUtc
FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@Session;
""", new { Session = context.Session.SessionId }, cancellationToken: cancellationToken))).ToArray();
        var operational = (await connection.QueryAsync<SlotRow>(new CommandDefinition("""
SELECT Symbol,ExpectedBarEndUtc,EvidenceBarId,Disposition,OperationallyUsable
FROM dbo.IntradayCollectionSlot WHERE SessionId=@Session AND ExpectedBarEndUtc<=@Through;
""", new { Session = context.Session.SessionId, Through = throughBarEndUtc }, cancellationToken: cancellationToken)))
            .ToDictionary(r => (r.Symbol, Utc(r.ExpectedBarEndUtc)));
        var slots = ImmutableArray.CreateBuilder<DelphiLiveExpectedResearchSlot>();
        foreach (var member in memberships)
        for (DateTime endpoint = Utc(member.RequiredFromBarEndUtc);
            endpoint <= Utc(member.RequiredThroughBarEndUtc) && endpoint <= throughBarEndUtc; endpoint = endpoint.AddMinutes(5))
        {
            operational.TryGetValue((member.Symbol, endpoint), out var source);
            slots.Add(new(DelphiLiveResearchCoordinator.StableId($"slot/{context.Session.SessionId:D}/{member.Symbol}/{endpoint:O}"),
                context.Session.SessionId, context.Session.TradingDate, endpoint, member.Symbol, member.IsXiuBenchmark,
                source?.EvidenceBarId, source?.Disposition ?? "MissingScheduledSlot", source?.OperationallyUsable ?? false));
        }
        var metadata = await connection.QuerySingleAsync<SessionRow>(new CommandDefinition("""
SELECT s.HostGapObserved,COALESCE(r.RunContextJson,N'{}') AS RunContextJson,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.DelphiLiveSessionPolicy sp JOIN dbo.DelphiLivePolicyVersion p
 ON p.DelphiLivePolicyVersionId=sp.DelphiLivePolicyVersionId WHERE sp.SessionId=s.SessionId AND
 (sp.PolicySettingsJson<>p.SettingsJson OR sp.PolicySettingsSha256<>p.SettingsSha256)) THEN 0 ELSE 1 END AS bit) AS StablePolicies,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.IntradayCollectionCycle a JOIN dbo.IntradayCollectionCycle b
 ON a.SessionId=b.SessionId AND a.CycleId<>b.CycleId AND a.StartedUtc<b.CompletedUtc AND b.StartedUtc<a.CompletedUtc
 WHERE a.SessionId=s.SessionId) THEN 1 ELSE 0 END AS bit) AS HasOverlap,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.DelphiLiveCorporateActionAudit d JOIN dbo.DelphiLiveSessionSymbol sy
 ON sy.Symbol=d.Symbol AND sy.SessionId=s.SessionId WHERE d.AffectedFrom<=s.TradingDate
 AND d.AffectedThrough>=s.TradingDate AND d.RecordedUtc<=@AsOf) THEN 1 ELSE 0 END AS bit) AS CorporateActionUnsupported
FROM dbo.DelphiLiveSession s LEFT JOIN dbo.CalibrationRun r ON r.RunId=s.CalibrationRunId WHERE s.SessionId=@Session;
""", new { Session = context.Session.SessionId, AsOf = asOfUtc }, cancellationToken: cancellationToken));
        // The official calendar defines each ordinal even when a daily XIU bar
        // is missing. Missing bars therefore cannot compress the horizon.
        var dates = ImmutableArray.CreateBuilder<DateOnly>();
        DateOnly date = context.Session.TradingDate;
        for (int i = 0; i < 5; i++)
        {
            dates.Add(date);
            if (i == 4) break;
            try { date = calendar.GetNextSession(date); }
            catch (InvalidOperationException) { break; }
        }
        string[] symbols = memberships.Select(m => m.Symbol).Append("XIU").Distinct(StringComparer.Ordinal).ToArray();
        var affected = (await connection.QueryAsync<string>(new CommandDefinition("""
SELECT DISTINCT Symbol FROM dbo.DelphiLiveCorporateActionAudit WHERE Symbol IN @Symbols
AND AffectedFrom<=@Through AND AffectedThrough>=@From AND RecordedUtc<=@AsOf;
""", new { Symbols = symbols, From = Date(context.Session.TradingDate), Through = Date(dates[^1]), AsOf = asOfUtc },
            cancellationToken: cancellationToken))).ToImmutableHashSet(StringComparer.Ordinal);
        var bars = ImmutableArray.CreateBuilder<DelphiLiveFiveMinuteBar>();
        var conflicts = (await connection.QueryAsync<ConflictRow>(new CommandDefinition("""
SELECT DISTINCT b.Symbol,DATEADD(MINUTE,5,b.EventUtc) AS BarEndUtc
FROM dbo.IntradayEvidenceConflict x
JOIN dbo.IntradayEvidenceBar b ON b.EvidenceBarId=x.ExistingEvidenceBarId
WHERE b.Symbol IN @Symbols AND b.IntervalMinutes=5 AND b.EventUtc>=@Open AND b.EventUtc<@Close
 AND x.ReceivedUtc<=@AsOf AND x.CreatedUtc<=@AsOf;
""", new { Symbols = symbols, Open = context.Bounds.OpenUtc, Close = context.Bounds.CloseUtc, AsOf = asOfUtc },
            cancellationToken: cancellationToken)))
            .Select(row => $"{row.Symbol}/{Utc(row.BarEndUtc):O}").ToImmutableHashSet(StringComparer.Ordinal);
        var rows = await connection.QueryAsync<BarRow>(new CommandDefinition("""
SELECT b.EvidenceBarId,b.Symbol,b.EventUtc,b.[Open],b.High,b.Low,b.[Close],b.Volume,p.ReceivedUtc,p.Provider
FROM dbo.IntradayEvidenceBar b JOIN dbo.IntradayPollObservation p ON p.ObservationId=b.FirstObservationId
WHERE b.Symbol IN @Symbols AND b.IntervalMinutes=5 AND b.EventUtc>=@Open AND b.EventUtc<@Close
 AND p.ReceivedUtc<=@AsOf AND p.ReceivedUtc>DATEADD(MINUTE,5,b.EventUtc)
 AND b.CreatedUtc<=@AsOf AND p.CreatedUtc<=@AsOf AND p.AuditState IN(N'Valid',N'Degraded')
 AND p.Provider=N'TMXMoney' AND p.SourceContractVersion=N'TmxChartIntradayNoFreqV1'
 AND p.EvidenceSchemaVersion=1 AND p.CollectorVersion IN(N'IntradayEvidenceCollectorV2',N'IntradayEvidenceCollectorV3')
 AND NOT EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict x WHERE x.ExistingEvidenceBarId=b.EvidenceBarId
                AND x.ReceivedUtc<=@AsOf AND x.CreatedUtc<=@AsOf);
""", new { Symbols = symbols, Open = context.Bounds.OpenUtc, Close = context.Bounds.CloseUtc, AsOf = asOfUtc }, cancellationToken: cancellationToken));
        foreach (var row in rows)
        {
            try
            {
                DateTime start = Utc(row.EventUtc), end = start.AddMinutes(5), received = Utc(row.ReceivedUtc);
                bool onTime = operational.TryGetValue((row.Symbol, end), out var slot) && slot.OperationallyUsable && slot.EvidenceBarId == row.EvidenceBarId;
                bars.Add(new(row.EvidenceBarId, row.Symbol, context.Session.TradingDate, start, end,
                    row.Open, row.High, row.Low, row.Close, row.Volume, received, row.Provider, 1,
                    onTime ? DelphiLiveEvidenceDisposition.OperationalOnTime : DelphiLiveEvidenceDisposition.LateResearchOnly));
            }
            catch (ArgumentException) { /* Structurally invalid canonical source remains a missing metric input. */ }
        }
        var daily = ImmutableArray.CreateBuilder<DelphiLiveDailyBar>();
        var dailyRows = await connection.QueryAsync<DailyRow>(new CommandDefinition("""
SELECT Id,Symbol,[Date],[Open],High,Low,[Close],Volume FROM dbo.DailyBars
WHERE Symbol IN @Symbols AND [Date] IN @Dates AND CreatedAt<=@AsOf ORDER BY [Date];
""", new { Symbols = symbols, Dates = dates.Select(Date).ToArray(), AsOf = asOfUtc }, cancellationToken: cancellationToken));
        foreach (var row in dailyRows)
        {
            try { daily.Add(new(DelphiLiveResearchCoordinator.StableId($"DailyBars/{row.Id}"), row.Symbol,
                DateOnly.FromDateTime(row.Date), Convert.ToDecimal(row.Open), Convert.ToDecimal(row.High), Convert.ToDecimal(row.Low), Convert.ToDecimal(row.Close), row.Volume)); }
            catch (Exception error) when (error is ArgumentException or OverflowException) { }
        }
        return new(slots.ToImmutable(), bars.ToImmutable(), daily.ToImmutable(), dates.ToImmutable(), metadata.RunContextJson,
            metadata.HostGapObserved || slots.Any(s => s.OperationalDisposition == "MissingScheduledSlot"), metadata.HasOverlap,
            metadata.StablePolicies, metadata.CorporateActionUnsupported || affected.Count > 0)
        {
            CorporateActionSymbols = affected,
            HasConflictingEvidence = conflicts.Count > 0,
            ConflictingAnchors = conflicts
        };
    }

    private static DateTime Date(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
    private static DateTime Utc(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Utc);
    private sealed record MemberRow(string Symbol, bool IsXiuBenchmark, DateTime RequiredFromBarEndUtc, DateTime RequiredThroughBarEndUtc);
    private sealed record SlotRow(string Symbol, DateTime ExpectedBarEndUtc, Guid? EvidenceBarId, string Disposition, bool OperationallyUsable);
    private sealed record SessionRow(bool HostGapObserved, string RunContextJson, bool StablePolicies, bool HasOverlap, bool CorporateActionUnsupported);
    private sealed record BarRow(Guid EvidenceBarId, string Symbol, DateTime EventUtc, decimal Open, decimal High, decimal Low, decimal Close, long Volume, DateTime ReceivedUtc, string Provider);
    private sealed record ConflictRow(string Symbol, DateTime BarEndUtc);
    private sealed record FrozenSessionRow(Guid SessionId, DateTime TradingDate, DateTime SessionOpenUtc, DateTime SessionCloseUtc);
    private sealed record DailyRow(int Id, string Symbol, DateTime Date, float Open, float High, float Low, float Close, long Volume);
}
