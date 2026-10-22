#nullable enable
using Core.Calibration;
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

public sealed partial class DelphiLiveSessionRepository
{
    public async Task ApplySharedComparisonSourcesToPaperAsync(DateOnly date, CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        if (date != DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now)) || !calendar.IsRegularSession(date)) return;
        var bounds = calendar.GetSessionBounds(date);
        if (now < bounds.OpenUtc || now >= bounds.CloseUtc) return;
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(ct);
        if (!await PaperComparisonRepository.InstalledAsync(c)) return;
        var accounts = await PaperComparisonRepository.ReadAccountsAsync(c, null, ct);
        if (accounts.GroupBy(a => a.BatchId).Any(g => g.Count() != 10))
            throw new InvalidOperationException("Comparison batch is incomplete; paper adoption is unavailable.");
        foreach (var batch in accounts.Select(a => a.BatchId).Distinct())
        {
            using var t = c.BeginTransaction(IsolationLevel.Serializable);
            var source = await PaperComparisonRepository.ReadSourceAsync(c, t, batch, Date(date), ct);
            if (source is not null)
            {
                DateTime effective = PaperDailySourcePolicy.EffectiveBoundary(now);
                await c.ExecuteAsync(new CommandDefinition(AdoptShadowComparisonSql, PaperSourceSqlParameters.Create(new
                {
                    Batch = batch, Run = source.RunId, Date = Date(date), Now = now,
                    Effective = effective, Published = source.PublishedUtc, Revision = source.RevisionId
                }, ("Now", now), ("Effective", effective), ("Published", source.PublishedUtc)), t, cancellationToken: ct));
            }
            t.Commit();
        }
    }

    public async Task RefreshDailySourceAsync(DateOnly date, DelphiLiveLease lease, CancellationToken cancellationToken = default)
    {
        await RefreshOfficialDailySourceAsync(date, lease, cancellationToken);
        await RefreshComparisonSourcesAsync(date, lease, cancellationToken);
    }

    private async Task RefreshComparisonSourcesAsync(DateOnly date, DelphiLiveLease lease, CancellationToken ct)
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync(ct);
        if (!await PaperComparisonRepository.InstalledAsync(c)) return;
        await using var t = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockSessionAsync(c, t, ct);
        await c.ExecuteAsync(new CommandDefinition("""
IF NOT EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WITH(UPDLOCK,HOLDLOCK) WHERE LeaseId=@Lease AND OwnerId=@Owner
 AND FencingToken=@Fence AND IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME()) THROW 52420,'Comparison source refresh lost its host lease.',1;
""", new { Lease = lease.LeaseId, Owner = lease.OwnerId, Fence = lease.FencingToken }, t, cancellationToken: ct));
        var session = await c.QuerySingleOrDefaultAsync(new CommandDefinition("""
SELECT SessionId,SessionOpenUtc,SessionCloseUtc,ExpectedPriorCanonicalSessionDate FROM dbo.DelphiLiveSession WHERE TradingDate=@Date;
""", new { Date = Date(date) }, t, cancellationToken: ct));
        if (session is null) { await t.CommitAsync(ct); return; }
        DateTime now = Utc(await c.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: t, cancellationToken: ct)));
        DateTime open = Utc((DateTime)session.SessionOpenUtc), close = Utc((DateTime)session.SessionCloseUtc);
        if (now < open || now >= close) { await t.CommitAsync(ct); return; }
        var accounts = (await PaperComparisonRepository.ReadAccountsAsync(c, t, ct)).ToList();
        var realBatches = new HashSet<Guid>();
        if (realObservations is not null)
            foreach (var real in await realObservations.ReadAsync(ct))
                if (real.Strategy.Comparison is { } approved)
                {
                    realBatches.Add(approved.BatchId);
                    if (accounts.All(a => a.AccountId != approved.AccountId)) accounts.Add(approved);
                }
        var run = await c.QuerySingleOrDefaultAsync(new CommandDefinition(CalibrationEvidenceRepository.LatestOperationalRunSql,
            PaperSourceSqlParameters.Create(new { Date = Date(date), Prior = (DateTime)session.ExpectedPriorCanonicalSessionDate, Now = now }, ("Now", now)), t, cancellationToken: ct));
        if (run is null) { await t.CommitAsync(ct); return; }
        Guid runId = run.RunId, daily = run.StrategyVersionId, sessionId = session.SessionId;
        foreach (var batch in accounts.GroupBy(a => a.BatchId))
        {
            if (realObservations is null && batch.Count() != 10) throw new InvalidOperationException("Comparison batch is incomplete.");
            if (batch.Count() != 10)
            {
                await new EngineIntegrityRepository().RaiseAsync(EngineIntegrityScope.Paper, null,
                    "ComparisonBatch/" + batch.Key.ToString("D"), "PaperComparisonIncomplete", true, ct);
                if (!realBatches.Contains(batch.Key)) continue;
            }
            var first = batch.First();
            if (first.DailyStrategyVersionId != daily || first.EffectiveUtc > close || now < first.EffectiveUtc) continue;
            var prior = await PaperComparisonRepository.ReadSourceAsync(c, t, batch.Key, Date(date), ct);
            if (!PaperDailySourcePolicy.ShouldAdopt(prior?.RunId, prior?.PublishedUtc, runId, Utc((DateTime)run.CreatedUtc), now)) continue;
            await c.ExecuteAsync(new CommandDefinition("""
IF EXISTS(SELECT l.Lens,l.Rank FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25 GROUP BY l.Lens,l.Rank HAVING COUNT(*)>1)
 THROW 52421,'Comparison publication has duplicate lens ranks.',1;
IF EXISTS(SELECT 1 FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25 AND
 (c.ObservationDate<>@Prior OR c.ObservationClose<=0 OR c.Symbol=N'XIU' OR l.RankingKey IS NULL))
 THROW 52422,'Comparison publication has invalid input evidence.',1;
""", new { Run = runId, Prior = (DateTime)session.ExpectedPriorCanonicalSessionDate }, t, cancellationToken: ct));
            var rows = await c.QueryAsync(new CommandDefinition("""
SELECT c.CandidateId,c.Symbol,c.CompositeScore,c.SnapshotJson,l.LensEvaluationId,l.Lens,l.IsEligible,l.IsPublished,l.Rank,l.RankingKey,l.FirstFailedGate,l.GateTraceJson
FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
WHERE c.RunId=@Run AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25 AND l.Lens IN(N'Continuation',N'Breakout') ORDER BY c.Symbol,l.Lens;
""", new { Run = runId }, t, cancellationToken: ct));
            var candidates = rows.GroupBy(r => (string)r.Symbol, StringComparer.Ordinal).Select(g =>
            {
                var r = g.First();
                return new DelphiLiveFrozenCandidate((Guid)r.CandidateId, g.Key, Convert.ToDecimal(r.CompositeScore), (string)r.SnapshotJson,
                    g.Select(l => new DelphiLiveLensSource((Guid)l.LensEvaluationId, (Guid)l.CandidateId, (string)l.Lens,
                        (bool)l.IsEligible, (bool)l.IsPublished, (int)l.Rank, Convert.ToDecimal(l.RankingKey),
                        (string?)l.FirstFailedGate, (string)l.GateTraceJson)).ToArray());
            }).ToArray();
            var existingSymbols = await c.QueryAsync<string>(new CommandDefinition(
                "SELECT Symbol FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@Session;", new { Session = sessionId }, t, cancellationToken: ct));
            var symbols = existingSymbols.Concat(candidates.Select(x => x.Symbol)).Distinct(StringComparer.Ordinal).ToArray();
            var baselines = new Dictionary<string, DelphiLiveFrozenBaseline>(StringComparer.Ordinal);
            await FreezeBaselinesAsync(c, t, sessionId, date, DateOnly.FromDateTime((DateTime)session.ExpectedPriorCanonicalSessionDate), now, now, symbols, ct, baselines);
            now = Utc(await c.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: t, cancellationToken: ct)));
            if (now >= close) { await t.RollbackAsync(ct); return; }
            DateTime effective = PaperDailySourcePolicy.EffectiveBoundary(now);
            var source = new PaperComparisonSource(Guid.NewGuid(), batch.Key, sessionId, runId, daily,
                Utc((DateTime)run.CreatedUtc), now, effective, candidates, baselines);
            _ = PaperGateComparison.SelectLive(source, PaperGateProfile.PortfolioRulesOnlyV1);
            string json = DelphiLiveLedgerJson.Serialize(source);
            await c.ExecuteAsync(new CommandDefinition("""
INSERT dbo.PaperComparisonSourceRevision(RevisionId,BatchId,SessionId,CalibrationRunId,PublishedUtc,AdoptedUtc,EffectiveUtc,SnapshotJson,SnapshotSha256)
VALUES(@Revision,@Batch,@Session,@Run,@Published,@Now,@Effective,@Json,@Hash);
INSERT dbo.DelphiLiveSessionSymbol(SessionSymbolId,SessionId,Symbol,IsFrozenDailyCandidate,IsAdoptedDailyCandidate,IsXiuBenchmark,
 IsTrackedHolding,IsDelphiLiveHolding,HasPendingProtectiveSell,IsSessionCarryCandidate,FrozenSourceLensCount,BestFrozenSourceLensRank,
 RequiredFromBarEndUtc,RequiredThroughBarEndUtc,SourceIdentityJson,AddedUtc)
SELECT NEWID(),@Session,s.Symbol,0,1,0,0,0,0,0,0,NULL,CASE WHEN DATEADD(MINUTE,5,@Effective)>@Close THEN @Close ELSE DATEADD(MINUTE,5,@Effective) END,
 @Close,(SELECT @Batch comparisonBatchId,@Revision comparisonSourceRevisionId,@Run calibrationRunId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),@Now
FROM OPENJSON(@Symbols) WITH(Symbol NVARCHAR(20) '$') s WHERE NOT EXISTS
 (SELECT 1 FROM dbo.DelphiLiveSessionSymbol m WHERE m.SessionId=@Session AND m.Symbol=s.Symbol);
""", PaperSourceSqlParameters.Create(new { Revision = source.RevisionId, Batch = batch.Key, Session = sessionId, Run = runId,
                Published = source.PublishedUtc, Now = now, Effective = effective, Json = json, Hash = SHA256.HashData(Encoding.UTF8.GetBytes(json)),
                Close = close, Symbols = DelphiLiveLedgerJson.Serialize(candidates.Select(x => x.Symbol)) }, ("Published", source.PublishedUtc),
                ("Now", now), ("Effective", effective), ("Close", close)), t, cancellationToken: ct));
            if (realObservations is null) await c.ExecuteAsync(new CommandDefinition(AdoptShadowComparisonSql, PaperSourceSqlParameters.Create(new
            {
                Batch = batch.Key, Run = runId, Date = Date(date), Now = now, Effective = effective, Published = source.PublishedUtc, Revision = source.RevisionId
            }, ("Now", now), ("Effective", effective), ("Published", source.PublishedUtc)), t, cancellationToken: ct));
        }
        await t.CommitAsync(ct);
    }

    private static async Task<DelphiLiveSessionContext> WithComparisonsAsync(SqlConnection c, DelphiLiveSessionContext context, DateOnly date, CancellationToken ct)
    {
        var accounts = (await PaperComparisonRepository.ReadAccountsAsync(c, null, ct))
            .Where(a => a.LivePolicyVersionId.HasValue && DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(a.EffectiveUtc)) <= date).ToArray();
        if (accounts.Length == 0) return context;
        var assignments = context.Assignments.ToList(); var policies = context.Policies.ToDictionary(x => x.Key, x => x.Value);
        var sources = new Dictionary<Guid, PaperComparisonSource>(); var baselines = context.Baselines.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        for (int index = 0; index < accounts.Length; index++)
        {
            var account = accounts[index];
            Guid policy = account.LivePolicyVersionId!.Value;
            Guid? activePolicy = await c.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT DelphiLivePolicyVersionId FROM dbo.DelphiLivePortfolioLedger WHERE PortfolioId=@Id", new { Id = account.AccountId }, cancellationToken: ct));
            if (activePolicy.HasValue && activePolicy != policy)
            {
                var original = await ReadPolicyAsync(c, policy, null, ct);
                var successor = await ReadPolicyAsync(c, activePolicy.Value, null, ct);
                ProgressiveProfitProtectionRollout.ValidateChange(original, successor);
                policy = activePolicy.Value;
                accounts[index] = account = account with { LivePolicyVersionId = policy };
            }
            assignments.Add(new(account.AccountId, policy, DelphiLivePolicyRole.GateComparison, date));
            policies[policy] = await ReadPolicyAsync(c, policy, null, ct);
            var source = await PaperComparisonRepository.ReadSourceAsync(c, null, account.BatchId, Date(date), ct);
            if (source is not null)
            {
                sources[account.BatchId] = source;
                foreach (var baseline in source.Baselines) baselines.TryAdd(baseline.Key, baseline.Value);
            }
        }
        return context with { Assignments = assignments, Policies = policies, Baselines = baselines,
            ComparisonAccounts = accounts.ToDictionary(a => a.LivePolicyVersionId!.Value), ComparisonSources = sources };
    }

    internal const string AdoptShadowComparisonSql = """
INSERT dbo.ShadowPortfolioSession(SessionId,PortfolioId,TradingDate,CalibrationRunId,Status,ActivationBaselineUtc,OpeningValue,StartedUtc,CreatedUtc,UpdatedUtc)
SELECT NEWID(),p.PortfolioId,@Date,NULL,N'Active',@Effective,p.CashBalance+COALESCE(v.Held,0),@Now,@Now,@Now
FROM dbo.PaperComparisonAccount a JOIN dbo.ShadowPortfolio p ON p.PortfolioId=a.ShadowPortfolioId
OUTER APPLY(SELECT SUM(Shares*LastPrice) Held FROM dbo.ShadowPosition WHERE PortfolioId=p.PortfolioId AND Status=N'Open') v
WHERE a.BatchId=@Batch AND NOT EXISTS(SELECT 1 FROM dbo.ShadowPortfolioSession s WHERE s.PortfolioId=p.PortfolioId AND s.TradingDate=@Date);
DECLARE @Revisions TABLE(SessionId UNIQUEIDENTIFIER,RevisionId UNIQUEIDENTIFIER,PortfolioId UNIQUEIDENTIFIER);
INSERT dbo.ShadowDailySourceRevision(RevisionId,SessionId,CalibrationRunId,PreviousRunId,PublishedUtc,AdoptedUtc,EffectiveUtc)
OUTPUT inserted.SessionId,inserted.RevisionId,NULL INTO @Revisions
SELECT NEWID(),s.SessionId,@Run,old.CalibrationRunId,@Published,@Now,@Effective FROM dbo.PaperComparisonAccount a
JOIN dbo.ShadowPortfolioSession s ON s.PortfolioId=a.ShadowPortfolioId AND s.TradingDate=@Date
OUTER APPLY(SELECT TOP(1) CalibrationRunId FROM dbo.ShadowDailySourceRevision WHERE SessionId=s.SessionId ORDER BY AdoptedUtc DESC,RevisionId) old
WHERE a.BatchId=@Batch AND NOT EXISTS(SELECT 1 FROM dbo.ShadowDailySourceRevision WHERE SessionId=s.SessionId AND CalibrationRunId=@Run);
UPDATE r SET PortfolioId=s.PortfolioId FROM @Revisions r JOIN dbo.ShadowPortfolioSession s ON s.SessionId=r.SessionId;
INSERT dbo.ShadowPortfolioEvent(EventId,PortfolioId,SessionId,OrderId,OccurredUtc,EventType,ReasonCode,DetailsJson)
SELECT NEWID(),o.PortfolioId,o.SessionId,o.OrderId,@Now,N'Order',N'DailySourceSuperseded',N'{}' FROM dbo.ShadowOrder o
JOIN @Revisions r ON r.PortfolioId=o.PortfolioId WHERE o.Side=N'Buy' AND o.Status=N'Pending';
UPDATE o SET Status=N'Cancelled',ReasonCode=N'DailySourceSuperseded',UpdatedUtc=@Now FROM dbo.ShadowOrder o
JOIN @Revisions r ON r.PortfolioId=o.PortfolioId WHERE o.Side=N'Buy' AND o.Status=N'Pending';
UPDATE s SET Status=CASE WHEN s.Status=N'NoValidDelphiRun' THEN N'Active' ELSE s.Status END,UpdatedUtc=@Now
FROM dbo.ShadowPortfolioSession s JOIN @Revisions r ON r.SessionId=s.SessionId;
INSERT dbo.ShadowPortfolioCandidate(CandidateTrackingId,SessionId,SourceRevisionId,CalibrationCandidateId,Symbol,Rank,State,CreatedUtc,UpdatedUtc)
SELECT NEWID(),r.SessionId,r.RevisionId,c.CandidateId,c.Symbol,l.Rank,N'Pending',@Now,@Now FROM @Revisions r
JOIN dbo.PaperComparisonAccount a ON a.ShadowPortfolioId=r.PortfolioId JOIN dbo.ShadowPortfolio p ON p.PortfolioId=r.PortfolioId
JOIN dbo.CalibrationCandidate c ON c.RunId=@Run JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
WHERE l.Lens=p.Lens AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND p.MaximumPositions AND (l.IsEligible=1 OR a.GateProfile=N'PortfolioRulesOnlyV1');
INSERT dbo.ShadowPortfolioEvent(EventId,PortfolioId,SessionId,OccurredUtc,EventType,ReasonCode,DetailsJson)
SELECT NEWID(),r.PortfolioId,r.SessionId,@Now,N'Lifecycle',N'ComparisonDailySourceAdopted',
 (SELECT @Revision comparisonSourceRevisionId,@Run calibrationRunId,@Effective effectiveUtc,a.GateProfile gateProfile,
  (SELECT COUNT(*) FROM dbo.ShadowPortfolioCandidate WHERE SourceRevisionId=r.RevisionId) candidateCount FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
FROM @Revisions r JOIN dbo.PaperComparisonAccount a ON a.AccountId=r.PortfolioId;
""";
}
