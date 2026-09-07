#nullable enable
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>SELECT-only sources for the inactive watchlist and isolated historical replay.</summary>
public sealed class DelphiLivePreviewRepository : SQLBase
{
    public async Task<DelphiLiveWatchlistPreview> ReadAsync(DateOnly? replayDate = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        DateTime? cutoff = replayDate.HasValue ? ReviewedTsxSessionCalendar.At(replayDate.Value, new(9, 30)) : null;
        var row = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition("""
SELECT TOP (1) RunId,StrategyVersionId,RunPurpose,AuditState,RecommendationDate,MarketDataAsOf,StartedUtc,CreatedUtc
FROM dbo.CalibrationRun
WHERE RunPurpose=N'OfficialPaper' AND AuditState=N'Valid' AND StrategyVersionId IS NOT NULL
 AND (@Date IS NULL OR (RecommendationDate=@Date AND CreatedUtc<=@Cutoff))
ORDER BY CreatedUtc DESC,StartedUtc DESC,RunId;
""", new { Date = replayDate?.ToDateTime(TimeOnly.MinValue), Cutoff = cutoff }, cancellationToken: cancellationToken));
        if (row is null) return DelphiLiveWatchlistPreview.Empty;
        var run = new DelphiLiveOfficialRunSource(row.RunId, row.StrategyVersionId, row.RunPurpose, row.AuditState,
            DateOnly.FromDateTime(row.RecommendationDate), DateOnly.FromDateTime(row.MarketDataAsOf), Utc(row.StartedUtc), Utc(row.CreatedUtc));
        var sources = await connection.QueryAsync<PickRow>(new CommandDefinition("""
SELECT c.Symbol,c.CandidateId,c.CompositeScore,c.ObservationClose,l.Lens,l.Rank,l.IsEligible,l.RankingKey,l.FirstFailedGate,l.GateTraceJson
FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
WHERE c.RunId=@Run AND l.IsPublished=1 AND l.Lens IN (N'Continuation',N'Breakout')
ORDER BY l.Rank,c.Symbol,l.Lens;
""", new { Run = run.RunId }, cancellationToken: cancellationToken));
        var picks = sources.GroupBy(s => s.Symbol, StringComparer.Ordinal).Select(group =>
        {
            var first = group.First();
            return new DelphiLivePreviewPick(first.Symbol, first.CandidateId, Convert.ToDecimal(first.CompositeScore),
                Convert.ToDecimal(first.ObservationClose), group.Select(l => new DelphiLivePreviewLens(l.Lens, l.Rank,
                    l.IsEligible, Convert.ToDecimal(l.RankingKey), l.FirstFailedGate, l.GateTraceJson)).ToArray());
        }).OrderBy(p => p.BestRank).ThenBy(p => p.Symbol, StringComparer.Ordinal).ToArray();
        return new(run, picks);
    }

    public async Task<decimal> ReadShadowStartingCapitalAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        decimal? capital = await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
SELECT TOP (1) s.TotalAccountValue FROM dbo.ShadowPortfolioGeneration g
CROSS APPLY (SELECT TOP (1) TotalAccountValue FROM dbo.ShadowCapitalEvent
 WHERE GenerationId=g.GenerationId AND EventType IN (N'InitialSnapshot',N'AccountSnapshot')
 ORDER BY OccurredUtc DESC,CreatedUtc DESC) s ORDER BY g.CreatedUtc DESC;
""", cancellationToken: cancellationToken));
        return capital is > 0m ? capital.Value : throw new InvalidOperationException("No positive saved Shadow account capital is available.");
    }

    public async Task<IReadOnlyDictionary<string, DelphiLiveFrozenBaseline>> ReadBaselinesAsync(
        DelphiLiveWatchlistPreview preview, ReviewedTsxSessionCalendar calendar, CancellationToken cancellationToken = default)
    {
        var run = preview.Run ?? throw new ArgumentException("A saved run is required.", nameof(preview));
        var prior = calendar.GetImmediatelyPrecedingSession(run.RecommendationDate);
        var open = calendar.GetSessionBounds(run.RecommendationDate).OpenUtc;
        if (run.MarketDataAsOf != prior || run.CreatedUtc > open)
            throw new InvalidOperationException("The saved daily run was not fresh and available before the selected session opened.");
        var dates = new List<DateOnly> { prior };
        while (dates.Count < 21) dates.Add(calendar.GetImmediatelyPrecedingSession(dates[^1]));
        dates.Reverse();
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<BarRow>(new CommandDefinition("""
SELECT Id,Symbol,[Date],[Open],High,Low,[Close],Volume FROM dbo.DailyBars
WHERE Symbol IN @Symbols AND [Date] IN @Dates AND CreatedAt<=@Cutoff ORDER BY Symbol,[Date];
""", new { Symbols = preview.Picks.Select(p => p.Symbol).Append("XIU").Distinct().ToArray(),
            Dates = dates.Select(d => d.ToDateTime(TimeOnly.MinValue)).ToArray(), Cutoff = open }, cancellationToken: cancellationToken));
        var result = new Dictionary<string, DelphiLiveFrozenBaseline>(StringComparer.Ordinal);
        foreach (string symbol in preview.Picks.Select(p => p.Symbol).Append("XIU").Distinct())
        {
            var bars = rows.Where(r => r.Symbol == symbol).Select(r => new DelphiLiveDailyBar(
                new Guid(r.Id, 0, 0, new byte[8]), symbol, DateOnly.FromDateTime(r.Date), Convert.ToDecimal(r.Open),
                Convert.ToDecimal(r.High), Convert.ToDecimal(r.Low), Convert.ToDecimal(r.Close), r.Volume)).ToArray();
            var ruler = DelphiLiveMeasurements.CalculateVolatilityRulers(bars, dates, run.RecommendationDate, DelphiLivePolicyDefinition.Version1);
            result.Add(symbol, new(bars.SingleOrDefault(b => b.SessionDate == prior)?.Close, bars, dates, ruler));
        }
        return result;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private sealed class RunRow
    {
        public Guid RunId { get; set; } public Guid StrategyVersionId { get; set; }
        public string RunPurpose { get; set; } = ""; public string AuditState { get; set; } = "";
        public DateTime RecommendationDate { get; set; } public DateTime MarketDataAsOf { get; set; }
        public DateTime StartedUtc { get; set; } public DateTime CreatedUtc { get; set; }
    }
    private sealed class PickRow
    {
        public string Symbol { get; set; } = ""; public Guid CandidateId { get; set; }
        public double CompositeScore { get; set; } public float ObservationClose { get; set; }
        public string Lens { get; set; } = ""; public int Rank { get; set; } public bool IsEligible { get; set; }
        public double RankingKey { get; set; } public string? FirstFailedGate { get; set; } public string GateTraceJson { get; set; } = "";
    }
    private sealed class BarRow
    {
        public int Id { get; set; } public string Symbol { get; set; } = ""; public DateTime Date { get; set; }
        public float Open { get; set; } public float High { get; set; } public float Low { get; set; }
        public float Close { get; set; } public long Volume { get; set; }
    }
}
