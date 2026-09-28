#nullable enable
using System;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Runtime;
using Core.TMX.Models.Domain;
using Core.Trader;
using Core.Trader.Risk;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Core.Db;

public sealed partial class SystemShadowRepository : ISystemShadowRiskReader
{
    /// <summary>Read-only transaction over the full book, assigned rules and existing saved bars. Never collects prices.</summary>
    public async Task<ShadowPaperRiskObservation?> ReadRiskObservationAsync(Guid portfolioId, DateOnly tradingDate,
        CancellationToken cancellationToken = default)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A saved portfolio identity is required.", nameof(portfolioId));
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // Serializable requires no database snapshot-mode change and prevents torn books/phantom positions.
        // Reads use shared locks only. Runtime lock duration and native SQL behavior still need qualification.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        using var rows = await connection.QueryMultipleAsync(new CommandDefinition(RiskReadSql,
            new { Portfolio = portfolioId, Date = tradingDate.ToDateTime(TimeOnly.MinValue),
                Schema = IntradayEvidenceVersions.Schema, Contract = IntradayEvidenceVersions.SourceContract,
                Provider = IntradayEvidenceVersions.Provider, Policy = IntradayEvidenceVersions.Policy },
            transaction, cancellationToken: cancellationToken));
        var g = await rows.ReadSingleOrDefaultAsync();
        var p = await rows.ReadSingleOrDefaultAsync();
        var s = await rows.ReadSingleOrDefaultAsync();
        var assignment = await rows.ReadSingleOrDefaultAsync();
        var a = await rows.ReadSingleOrDefaultAsync();
        var positions = (await rows.ReadAsync()).Select(h => new SystemShadowPositionInfo(
            (Guid)h.PositionId, (Guid)h.PortfolioId, (string)h.Symbol, (string)h.Status, (int)h.Shares,
            (decimal)h.AverageCost, (decimal)h.CostBasis, (decimal)h.FullPositionTarget, Utc((DateTime)h.EntryUtc),
            (DateTime)h.EntryTradingDate, (byte)h.AddOnCount, (byte)h.SameDayReentryCount,
            (decimal?)h.HighestFifteenClose, UtcOrNull((DateTime?)h.LastFifteenMinuteBarUtc), (decimal?)h.TrailingStopPrice,
            (bool)h.ProfitProtectionArmed, (decimal)h.LastPrice, Utc((DateTime)h.LastPriceEventUtc),
            (decimal)h.RealizedProfitLoss, UtcOrNull((DateTime?)h.ExitUtc), (string?)h.ExitReasonCode)).ToImmutableArray();
        var prices = (await rows.ReadAsync()).Select(b => new ShadowPaperRiskPrice((string)b.Symbol,
            new((Guid)b.EvidenceBarId, Utc((DateTime)b.EventUtc), Utc((DateTime)b.ReceivedUtc), 5,
                (decimal)b.Open, (decimal)b.High, (decimal)b.Low, (decimal)b.Close))).ToImmutableArray();
        var times = await rows.ReadSingleAsync();
        await transaction.CommitAsync(cancellationToken);
        if (g is null || p is null || s is null || g.ActivatedUtc is null ||
            (a is null && (assignment is null || (string)assignment.Family != EngineStrategySettings.Shadow))) return null;
        PaperComparisonAccount? comparison = a is null ? null : new((Guid)a.AccountId, (Guid)a.BatchId, (Guid)a.PairId,
            (string)a.StrategyCode, Enum.Parse<PaperGateProfile>((string)a.GateProfile), Utc((DateTime)a.EffectiveUtc),
            (Guid)a.DailyStrategyVersionId, (Guid?)a.LivePolicyVersionId, (Guid?)a.ShadowStrategyVersionId, (string?)a.ShadowSettingsJson);
        var book = new SystemShadowSavedRiskBook(
            new((Guid)g.GenerationId, (string)g.PolicyVersion, Enum.Parse<SystemShadowGenerationStatus>((string)g.Status),
                (decimal)g.TotalAccountValue, (decimal)g.AvailableAccountCash, Utc((DateTime)g.RealSnapshotUtc),
                Utc((DateTime)g.ActivatedUtc), Utc((DateTime)g.UpdatedUtc)),
            new((Guid)p.PortfolioId, (Guid)p.GenerationId, (string)p.PortfolioCode, (string)p.Lens, (byte)p.MaximumPositions,
                (string)p.Status, (decimal)p.CashBalance, (decimal)p.HighestClosingValue, Utc((DateTime)g.ActivatedUtc)),
            new((Guid)s.SessionId, (Guid)s.PortfolioId, (DateTime)s.TradingDate, (Guid?)s.CalibrationRunId,
                (string)s.Status, UtcOrNull((DateTime?)s.ActivationBaselineUtc), (decimal)s.OpeningValue, (bool)s.DailyLossGuardActive),
            positions, prices, Utc((DateTime)times.UpdatedUtc), UtcOrNull((DateTime?)times.LastFillUtc), (bool)g.IsGateComparison,
            assignment is null ? null : (Guid?)assignment.VersionId, assignment is null ? null : (string?)assignment.SettingsJson,
            assignment is null ? null : (string?)assignment.SettingsHash, assignment is null ? null : UtcOrNull((DateTime?)assignment.AssignedUtc), comparison);
        return book.Build(portfolioId, tradingDate);
    }

    private static DateTime? UtcOrNull(DateTime? value) => value is { } time ? Utc(time) : null;

    internal const string RiskReadSql = """
SELECT g.* FROM dbo.ShadowPortfolioGeneration g
JOIN dbo.ShadowPortfolio p ON p.GenerationId=g.GenerationId WHERE p.PortfolioId=@Portfolio;
SELECT * FROM dbo.ShadowPortfolio WHERE PortfolioId=@Portfolio;
SELECT s.SessionId,s.PortfolioId,s.TradingDate,COALESCE(r.CalibrationRunId,s.CalibrationRunId) CalibrationRunId,
 s.Status,COALESCE(r.EffectiveUtc,s.ActivationBaselineUtc) ActivationBaselineUtc,s.OpeningValue,s.DailyLossGuardActive
FROM dbo.ShadowPortfolioSession s
OUTER APPLY(SELECT TOP(1) CalibrationRunId,EffectiveUtc FROM dbo.ShadowDailySourceRevision
 WHERE SessionId=s.SessionId ORDER BY AdoptedUtc DESC,RevisionId) r
WHERE s.PortfolioId=@Portfolio AND s.TradingDate=@Date;
SELECT TOP(1) a.VersionId,a.AssignedUtc,v.Family,v.SettingsJson,v.SettingsHash
FROM dbo.EngineStrategyAssignment a LEFT JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId
WHERE a.TargetId=@Portfolio ORDER BY a.Sequence DESC;
SELECT a.*,b.EffectiveUtc,b.DailyStrategyVersionId FROM dbo.PaperComparisonAccount a
JOIN dbo.PaperComparisonBatch b ON b.BatchId=a.BatchId WHERE a.ShadowPortfolioId=@Portfolio;
SELECT * FROM dbo.ShadowPosition WHERE PortfolioId=@Portfolio ORDER BY PositionId;
SELECT b.EvidenceBarId,b.Symbol,b.EventUtc,b.[Open],b.High,b.Low,b.[Close],o.ReceivedUtc
FROM dbo.ShadowPosition p JOIN dbo.IntradayEvidenceBar b
 ON b.Symbol=p.Symbol AND b.IntervalMinutes=5 AND b.EventUtc=p.LastPriceEventUtc
JOIN dbo.IntradayPollObservation o ON o.ObservationId=b.FirstObservationId
WHERE p.PortfolioId=@Portfolio AND p.Status=N'Open'
 AND o.ReceivedUtc>=DATEADD(minute,5,b.EventUtc) AND o.AuditState IN(N'Valid',N'Degraded')
 AND o.EvidenceSchemaVersion=@Schema AND o.SourceContractVersion=@Contract AND o.Provider=@Provider
 AND (o.PolicyVersion=@Policy OR (o.PolicyVersion IS NULL AND o.CollectorVersion=N'IntradayEvidenceCollectorV3' AND o.Purpose=N'PaperMonitor'))
 AND NOT EXISTS(SELECT 1 FROM dbo.IntradayPollObservation conflict
   WHERE conflict.Symbol=b.Symbol AND conflict.IntervalMinutes=5 AND conflict.AuditState=N'Invalid'
     AND conflict.ReceivedUtc>=o.ReceivedUtc AND conflict.RequestedStartUtc<=b.EventUtc AND conflict.RequestedEndUtc>=b.EventUtc)
ORDER BY b.Symbol;
SELECT (SELECT MAX(t.UpdatedUtc) FROM (
 SELECT UpdatedUtc FROM dbo.ShadowPortfolio WHERE PortfolioId=@Portfolio
 UNION ALL SELECT UpdatedUtc FROM dbo.ShadowPortfolioSession WHERE PortfolioId=@Portfolio AND TradingDate=@Date
 UNION ALL SELECT UpdatedUtc FROM dbo.ShadowPosition WHERE PortfolioId=@Portfolio
 UNION ALL SELECT UpdatedUtc FROM dbo.ShadowOrder WHERE PortfolioId=@Portfolio
 ) t) UpdatedUtc,
 (SELECT MAX(FillUtc) FROM dbo.ShadowOrder WHERE PortfolioId=@Portfolio AND Status=N'Filled') LastFillUtc;
""";
}
