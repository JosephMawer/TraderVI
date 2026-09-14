#nullable enable
using Core.Trader;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

public sealed partial class SystemShadowRepository
{
    private static async Task<SystemShadowRuntimeSession> AdoptSourceAsync(SqlConnection connection, SqlTransaction transaction,
        SystemShadowRuntimePortfolio portfolio, SystemShadowRuntimeSession session, SystemShadowDelphiRun run,
        IReadOnlyList<SystemShadowDelphiCandidate> candidates, CancellationToken ct)
    {
        var currentCreated = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT CreatedUtc FROM dbo.CalibrationRun WHERE RunId=@Run;", new { Run = session.CalibrationRunId }, transaction, cancellationToken: ct));
        DateTime now = Utc(await connection.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: ct)));
        if (!PaperDailySourcePolicy.ShouldAdopt(session.CalibrationRunId, currentCreated, run.RunId, run.CreatedUtc, now)) return session;
        Guid revision = Guid.NewGuid();
        DateTime effective = PaperDailySourcePolicy.EffectiveBoundary(now);
        await connection.ExecuteAsync(new CommandDefinition("""
IF NOT EXISTS(SELECT 1 FROM dbo.CalibrationRun r JOIN dbo.StrategyVersion v ON v.VersionId=r.StrategyVersionId AND v.IsActive=1
 WHERE r.RunId=@Run AND r.RecommendationDate=@Date AND r.MarketDataAsOf=@Prior AND r.RunPurpose=N'OfficialPaper'
 AND r.AuditState=N'Valid' AND r.CreatedUtc<=@Now) THROW 52300,'Shadow source is no longer valid or assigned.',1;
INSERT dbo.ShadowDailySourceRevision(RevisionId,SessionId,CalibrationRunId,PreviousRunId,PublishedUtc,AdoptedUtc,EffectiveUtc)
 VALUES(@Revision,@Session,@Run,@Previous,@Published,@Now,@Effective);
INSERT dbo.ShadowPortfolioEvent(EventId,PortfolioId,SessionId,OrderId,OccurredUtc,EventType,ReasonCode,DetailsJson)
 SELECT NEWID(),PortfolioId,SessionId,OrderId,@Now,N'Order',N'DailySourceSuperseded',N'{}'
 FROM dbo.ShadowOrder WITH(UPDLOCK,HOLDLOCK) WHERE PortfolioId=@Portfolio AND Side=N'Buy' AND Status=N'Pending';
UPDATE dbo.ShadowOrder SET Status=N'Cancelled',ReasonCode=N'DailySourceSuperseded',UpdatedUtc=@Now
 WHERE PortfolioId=@Portfolio AND Side=N'Buy' AND Status=N'Pending';
UPDATE dbo.ShadowPortfolioSession SET Status=CASE WHEN Status=N'NoValidDelphiRun' THEN N'Active' ELSE Status END,UpdatedUtc=@Now
 WHERE SessionId=@Session;
INSERT dbo.ShadowPortfolioEvent(EventId,PortfolioId,SessionId,OccurredUtc,EventType,ReasonCode,DetailsJson)
 VALUES(NEWID(),@Portfolio,@Session,@Now,N'Lifecycle',N'DailySourceAdopted',
 (SELECT @Revision revisionId,@Previous previousRunId,@Run calibrationRunId,@Published publishedUtc,@Now adoptedUtc,
 @Effective effectiveUtc,@Count candidateCount FOR JSON PATH,WITHOUT_ARRAY_WRAPPER));
""", PaperSourceSqlParameters.Create(new { Revision = revision, Session = session.SessionId, Portfolio = portfolio.PortfolioId,
            Run = run.RunId, Previous = session.CalibrationRunId, Published = run.CreatedUtc, Now = now, Effective = effective,
            Date = session.TradingDate.Date, Prior = run.MarketDataAsOf.Date, Count = candidates.Count },
            ("Published", run.CreatedUtc), ("Now", now), ("Effective", effective)), transaction, cancellationToken: ct));
        foreach (var candidate in candidates)
            await connection.ExecuteAsync(new CommandDefinition("""
INSERT dbo.ShadowPortfolioCandidate(CandidateTrackingId,SessionId,SourceRevisionId,CalibrationCandidateId,Symbol,Rank,State,CreatedUtc,UpdatedUtc)
SELECT NEWID(),@Session,@Revision,c.CandidateId,c.Symbol,l.Rank,N'Pending',@Now,@Now
FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
WHERE c.CandidateId=@Candidate AND c.RunId=@Run AND l.Lens=@Lens AND l.IsEligible=1 AND l.IsPublished=1 AND l.Rank<=@Maximum;
IF @@ROWCOUNT<>1 THROW 52301,'Shadow adoption candidate no longer matches the official publication.',1;
""", new { Session = session.SessionId, Revision = revision, Candidate = candidate.CandidateId,
                Run = run.RunId, portfolio.Lens, Maximum = portfolio.MaximumPositions, Now = now }, transaction, cancellationToken: ct));
        return session with { CalibrationRunId = run.RunId, ActivationBaselineUtc = effective,
            Status = session.Status == "NoValidDelphiRun" ? "Active" : session.Status };
    }
}
