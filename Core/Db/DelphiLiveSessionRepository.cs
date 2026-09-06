#nullable enable
using Core.Calibration;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Atomic, immutable daily-source freeze. No constructor opens a connection.</summary>
public sealed partial class DelphiLiveSessionRepository : SQLBase,
    IDelphiLiveSessionContextStore, ICanonicalXiuSessionSource
{
    private readonly ReviewedTsxSessionCalendar calendar;
    private readonly IDelphiLiveHoldingSource holdings;
    private readonly CodeProvenance code;

    public DelphiLiveSessionRepository(ReviewedTsxSessionCalendar calendar,
        IDelphiLiveHoldingSource holdings, CodeProvenance code)
    {
        this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        this.holdings = holdings ?? throw new ArgumentNullException(nameof(holdings));
        this.code = code ?? throw new ArgumentNullException(nameof(code));
        if (string.IsNullOrWhiteSpace(code.Commit) || code.Commit.Length is < 7 or > 128 ||
            code.WorkingTreeState is not ("Clean" or "Dirty" or "Unknown"))
            throw new ArgumentException("Session freezing requires explicit code provenance.", nameof(code));
    }

    public async Task<DelphiLiveFrozenSession?> GetFrozenSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await ReadFrozenAsync(connection, tradingDate, null, cancellationToken);
    }

    public async Task<DateOnly?> GetImmediatelyPrecedingCompletedSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        DateTime? date = await connection.QuerySingleAsync<DateTime?>(new CommandDefinition(
            "SELECT MAX([Date]) FROM dbo.DailyBars WHERE Symbol='XIU' AND [Date]<@Date;",
            new { Date = Date(tradingDate) }, cancellationToken: cancellationToken));
        return date.HasValue ? DateOnly.FromDateTime(date.Value) : null;
    }

    public async Task<IReadOnlyList<DelphiLivePolicyAssignment>> GetAssignmentsForSessionAsync(DateOnly tradingDate, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await ReadAssignmentsAsync(connection, tradingDate, null, cancellationToken);
    }

    public async Task<DelphiLivePolicyDefinition> GetPolicyAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await ReadPolicyAsync(connection, policyId, null, cancellationToken);
    }

    public async Task<DelphiLiveFrozenSession> FreezeSessionAsync(DelphiLiveSessionFreezeRequest request, CancellationToken cancellationToken = default)
    {
        DelphiLiveSessionBounds bounds = calendar.GetSessionBounds(request.TradingDate);
        if (request.FreezeBoundaryUtc != bounds.OpenUtc || request.ExpectedMarketDataAsOf != calendar.GetImmediatelyPrecedingSession(request.TradingDate))
            throw new ArgumentException("The freeze must use the official session boundary and immediately preceding session.");
        DateTime now = DateTime.UtcNow;
        if (now < bounds.OpenUtc || now > bounds.CloseUtc.AddMinutes(7))
            throw new InvalidOperationException("Session initialization requires the current regular-session monitoring window.");
        var observed = NormalizeObservedHoldings(await holdings.GetObservedHoldingsAsync(cancellationToken));
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockSessionAsync(connection, transaction, cancellationToken);
        now = Utc(await connection.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME();", transaction: transaction, cancellationToken: cancellationToken)));
        DelphiLiveFrozenSession? existing = await ReadFrozenAsync(connection, request.TradingDate, transaction, cancellationToken);
        if (existing is not null) { await transaction.CommitAsync(cancellationToken); return existing; }
        var assignments = await ReadAssignmentsAsync(connection, request.TradingDate, transaction, cancellationToken);
        if (assignments.Count(a => a.Role == DelphiLivePolicyRole.OperationalChampion) != 1 || assignments.Count > 3 ||
            !assignments.Select(a => a.AssignmentId).Order().SequenceEqual(request.Assignments.Select(a => a.AssignmentId).Order()))
            throw new InvalidOperationException("Frozen policy assignments require one champion and at most two explicitly assigned Shadow policies.");
        foreach (var assignment in assignments) await ReadPolicyAsync(connection, assignment.PolicyVersionId, transaction, cancellationToken);
        Guid sessionId = Guid.NewGuid();
        string holdingJson = JsonSerializer.Serialize(observed.Select(h => new { symbol = h.Symbol, owner = h.Owner, id = h.OwnerRecordId, mayAct = h.DelphiLiveMayAct }));
        await connection.ExecuteAsync(new CommandDefinition(FreezeSql, new
        {
            Session = sessionId, TradingDate = Date(request.TradingDate), Open = bounds.OpenUtc, Close = bounds.CloseUtc,
            Prior = Date(request.ExpectedMarketDataAsOf), Now = now, Calendar = calendar.Version,
            Code = code.Commit, Tree = code.WorkingTreeState, Holdings = holdingJson
        }, transaction, cancellationToken: cancellationToken));
        var symbols = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT Symbol FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@Session ORDER BY Symbol;",
            new { Session = sessionId }, transaction, cancellationToken: cancellationToken))).ToArray();
        await FreezeBaselinesAsync(connection, transaction, sessionId, request.TradingDate,
            request.ExpectedMarketDataAsOf, bounds.OpenUtc, now, symbols, cancellationToken);
        var frozen = await ReadFrozenAsync(connection, request.TradingDate, transaction, cancellationToken)
            ?? throw new InvalidOperationException("Session freeze did not persist.");
        await transaction.CommitAsync(cancellationToken);
        return frozen;
    }

    private async Task FreezeBaselinesAsync(SqlConnection connection, SqlTransaction transaction,
        Guid sessionId, DateOnly tradingDate, DateOnly priorDate, DateTime cutoffUtc, DateTime now,
        IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        var canonicalDates = (await connection.QueryAsync<DateTime>(new CommandDefinition(
            "SELECT TOP (21) [Date] FROM dbo.DailyBars WHERE Symbol='XIU' AND [Date]<=@Prior AND CreatedAt<=@Cutoff ORDER BY [Date] DESC;",
            new { Prior = Date(priorDate), Cutoff = cutoffUtc }, transaction, cancellationToken: cancellationToken))).Reverse().Select(DateOnly.FromDateTime).ToArray();
        // A missing official XIU date cannot be compressed into an older ruler.
        bool canonicalContiguous = canonicalDates.Length > 0 && canonicalDates[^1] == priorDate;
        try
        {
            for (int i = 1; i < canonicalDates.Length; i++)
                canonicalContiguous &= calendar.GetImmediatelyPrecedingSession(canonicalDates[i]) == canonicalDates[i - 1];
        }
        catch (InvalidOperationException)
        {
            // Missing reviewed calendar coverage makes the ruler unavailable;
            // it must not prevent ongoing quote-based holding protection.
            canonicalContiguous = false;
        }
        foreach (string symbol in symbols)
        {
            var rows = (await connection.QueryAsync(new CommandDefinition(
                "SELECT Id,[Date],[Open],High,Low,[Close],Volume FROM dbo.DailyBars WHERE Symbol=@Symbol AND [Date] IN @Dates AND CreatedAt<=@Cutoff ORDER BY [Date];",
                new { Symbol = symbol, Dates = canonicalDates.Select(Date).ToArray(), Cutoff = cutoffUtc }, transaction, cancellationToken: cancellationToken))).ToArray();
            var bars = new List<DelphiLiveDailyBar>();
            foreach (var row in rows)
            {
                try { bars.Add(new(StableDailyId((int)row.Id), symbol, DateOnly.FromDateTime((DateTime)row.Date),
                    Convert.ToDecimal(row.Open), Convert.ToDecimal(row.High), Convert.ToDecimal(row.Low), Convert.ToDecimal(row.Close), (long)row.Volume)); }
                catch (Exception exception) when (exception is ArgumentException or OverflowException)
                { /* Invalid source remains unavailable; no price substitution. */ }
            }
            var rulers = DelphiLiveMeasurements.CalculateVolatilityRulers(bars,
                canonicalContiguous ? canonicalDates : Array.Empty<DateOnly>(), tradingDate, DelphiLivePolicyDefinition.Version1);
            decimal? previous = bars.SingleOrDefault(b => b.SessionDate == priorDate)?.Close;
            var volumes = bars.Where(b => canonicalDates.TakeLast(20).Contains(b.SessionDate)).Select(b => (decimal)b.Volume).Order().ToArray();
            decimal? medianVolume = canonicalContiguous && volumes.Length == 20 ? (volumes[9] + volumes[10]) / 2m : null;
            var baseline = new DelphiLiveFrozenBaseline(previous, bars, canonicalContiguous ? canonicalDates : [], rulers);
            bool complete = bars.Count == 21 && medianVolume > 0 && new[] { rulers.FiveSession, rulers.TenSession, rulers.FourteenSession, rulers.TwentySession }.All(r => r.MedianTrueRangePct.Value > 0);
            await connection.ExecuteAsync(new CommandDefinition(FreezeBaselineSql, new
            {
                Id = Guid.NewGuid(), Session = sessionId, Symbol = symbol, Prior = Date(priorDate), Count = bars.Count,
                Previous = previous, R5 = rulers.FiveSession.MedianTrueRangePct.Value, R10 = rulers.TenSession.MedianTrueRangePct.Value,
                R14 = rulers.FourteenSession.MedianTrueRangePct.Value, R20 = rulers.TwentySession.MedianTrueRangePct.Value, Volume = medianVolume,
                Json = DelphiLiveLedgerJson.Serialize(baseline), Audit = complete ? "Valid" : "Unavailable", Reason = complete ? null : "BaselineCoverageIncomplete", Now = now
            }, transaction, cancellationToken: cancellationToken));
        }
    }

    public async Task<DelphiLiveSessionContext?> ReadContextAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var session = await ReadFrozenAsync(connection, date, null, cancellationToken);
        if (session is null) return null;
        var assignments = (await connection.QueryAsync(new CommandDefinition(
            "SELECT AssignmentId,DelphiLivePolicyVersionId,PolicyRole,ExperimentId FROM dbo.DelphiLiveSessionPolicy WHERE SessionId=@Session ORDER BY RoleSlot;",
            new { Session = session.SessionId }, cancellationToken: cancellationToken))).Select(row => new DelphiLivePolicyAssignment(
                (Guid)row.AssignmentId, (Guid)row.DelphiLivePolicyVersionId, Enum.Parse<DelphiLivePolicyRole>((string)row.PolicyRole), date, (Guid?)row.ExperimentId)).ToArray();
        var policies = new Dictionary<Guid, DelphiLivePolicyDefinition>();
        foreach (var assignment in assignments) policies.Add(assignment.PolicyVersionId, await ReadPolicyAsync(connection, assignment.PolicyVersionId, null, cancellationToken));
        var candidates = new Dictionary<string, DelphiLiveFrozenCandidate>(StringComparer.Ordinal);
        var rows = await connection.QueryAsync(new CommandDefinition(
            "SELECT c.CalibrationCandidateId,s.Symbol,c.CommonCompositeScore,c.CandidateSnapshotJson,c.FrozenCandidateId FROM dbo.DelphiLiveFrozenCandidate c JOIN dbo.DelphiLiveSessionSymbol s ON s.SessionSymbolId=c.SessionSymbolId WHERE c.SessionId=@Session;",
            new { Session = session.SessionId }, cancellationToken: cancellationToken));
        foreach (var row in rows)
        {
            var lenses = (await connection.QueryAsync(new CommandDefinition(
                "SELECT * FROM dbo.DelphiLiveFrozenCandidateLens WHERE FrozenCandidateId=@Id ORDER BY Lens;", new { Id = (Guid)row.FrozenCandidateId }, cancellationToken: cancellationToken)))
                .Select(l => new DelphiLiveLensSource((Guid)l.CalibrationLensEvaluationId, (Guid)l.CalibrationCandidateId,
                    (string)l.Lens, (bool)l.IsEligible, (bool)l.IsPublished, (int)l.FrozenRank, Convert.ToDecimal(l.FrozenRankingKey), (string?)l.FirstFailedGate, (string)l.GateTraceJson)).ToArray();
            candidates.Add((string)row.Symbol, new((Guid)row.CalibrationCandidateId, (string)row.Symbol,
                Convert.ToDecimal(row.CommonCompositeScore), (string)row.CandidateSnapshotJson, lenses));
        }
        var baselines = (await connection.QueryAsync(new CommandDefinition(
            "SELECT s.Symbol,b.AlignedDailyBarsJson FROM dbo.DelphiLiveDailyBaseline b JOIN dbo.DelphiLiveSessionSymbol s ON s.SessionSymbolId=b.SessionSymbolId WHERE b.SessionId=@Session;",
            new { Session = session.SessionId }, cancellationToken: cancellationToken))).ToDictionary(row => (string)row.Symbol,
                row => DelphiLiveLedgerJson.Deserialize<DelphiLiveFrozenBaseline>((string)row.AlignedDailyBarsJson), StringComparer.Ordinal);
        var membership = (await connection.QueryAsync(new CommandDefinition(
            "SELECT Symbol,IsFrozenDailyCandidate,IsXiuBenchmark,IsTrackedHolding,IsDelphiLiveHolding,HasPendingProtectiveSell,IsSessionCarryCandidate,RequiredFromBarEndUtc,RequiredThroughBarEndUtc FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@Session;",
            new { Session = session.SessionId }, cancellationToken: cancellationToken))).ToDictionary(row => (string)row.Symbol,
            row => new DelphiLiveObservationMembership((string)row.Symbol,(bool)row.IsFrozenDailyCandidate,(bool)row.IsXiuBenchmark,
                (bool)row.IsTrackedHolding,(bool)row.IsDelphiLiveHolding,(bool)row.HasPendingProtectiveSell,(bool)row.IsSessionCarryCandidate,
                Utc((DateTime)row.RequiredFromBarEndUtc),Utc((DateTime)row.RequiredThroughBarEndUtc)),StringComparer.Ordinal);
        return new(session, calendar.GetSessionBounds(date), assignments, policies, candidates, baselines)
            { ObservationMembership = membership };
    }

    private static async Task<DelphiLiveFrozenSession?> ReadFrozenAsync(SqlConnection connection, DateOnly date, SqlTransaction? transaction, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT SessionId,TradingDate,CalibrationRunId,DailyStrategyVersionId,FreezeStatus,FrozenUtc FROM dbo.DelphiLiveSession WITH (UPDLOCK,HOLDLOCK) WHERE TradingDate=@Date;",
            new { Date = Date(date) }, transaction, cancellationToken: ct));
        if (row is null) return null;
        var symbols = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT Symbol FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@Id ORDER BY Symbol;", new { Id = (Guid)row.SessionId }, transaction, cancellationToken: ct))).ToArray();
        return new((Guid)row.SessionId, date, (Guid?)row.CalibrationRunId, (Guid?)row.DailyStrategyVersionId,
            (string)row.FreezeStatus, Utc((DateTime)row.FrozenUtc), symbols);
    }

    private static async Task<IReadOnlyList<DelphiLivePolicyAssignment>> ReadAssignmentsAsync(SqlConnection connection, DateOnly date, SqlTransaction? transaction, CancellationToken ct)
    {
        var rows = await connection.QueryAsync(new CommandDefinition("""
SELECT AssignmentId,DelphiLivePolicyVersionId,PolicyRole,EffectiveTradingDate,ExperimentId
FROM dbo.DelphiLivePolicyAssignment
WHERE EffectiveTradingDate<=@Date AND (EndExclusiveTradingDate IS NULL OR EndExclusiveTradingDate>@Date)
 AND CancelledUtc IS NULL AND RoleSlot<3 ORDER BY RoleSlot;
""", new { Date = Date(date) }, transaction, cancellationToken: ct));
        return rows.Select(row => new DelphiLivePolicyAssignment((Guid)row.AssignmentId, (Guid)row.DelphiLivePolicyVersionId,
            Enum.Parse<DelphiLivePolicyRole>((string)row.PolicyRole), DateOnly.FromDateTime((DateTime)row.EffectiveTradingDate), (Guid?)row.ExperimentId)).ToArray();
    }

    internal static async Task<DelphiLivePolicyDefinition> ReadPolicyAsync(SqlConnection connection, Guid id, SqlTransaction? transaction, CancellationToken ct)
    {
        var row = await connection.QuerySingleAsync(new CommandDefinition("SELECT * FROM dbo.DelphiLivePolicyVersion WHERE DelphiLivePolicyVersionId=@Id;", new { Id = id }, transaction, cancellationToken: ct));
        if ((string)row.SettingsEncoding != "UTF-8") throw new InvalidOperationException("Unsupported policy settings encoding.");
        return DelphiLivePolicyStorage.Read(new(id, (string)row.PolicyDefinitionName, (int)row.PolicyDefinitionSchemaVersion,
            (string)row.EvaluatorVersion, (string)row.CollectorVersion, (int)row.CollectorSourceContractVersion,
            (string)row.DecisionDossierVersion, (int)row.DecisionDossierSchemaVersion, (string)row.QuoteFillVersion,
            (string)row.ShadowPortfolioVersion, (string)row.ResearchOutcomeVersion, (string)row.RankingDiagnosticVersion,
            (string)row.PromotionProtocolVersion), (string)row.SettingsJson, (byte[])row.SettingsSha256);
    }

    private static Guid StableDailyId(int id) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"TraderVI.DailyBars/{id}")).AsSpan(0, 16));
    private static DateTime Date(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal const string FreezeBaselineSql = """
INSERT dbo.DelphiLiveDailyBaseline
(DailyBaselineId,SessionId,SessionSymbolId,BaselineDefinition,BaselineSchemaVersion,SourceThroughTradingDate,
 AlignedDailyBarCount,PreviousCanonicalClose,MedianTrueRangePct5,MedianTrueRangePct10,MedianTrueRangePct14,
 MedianTrueRangePct20,MedianFullDayVolume20,AlignedDailyBarsJson,AuditState,AuditCode,FrozenUtc)
SELECT @Id,@Session,SessionSymbolId,N'DelphiLiveDailyBaselineV1',1,@Prior,@Count,@Previous,@R5,@R10,@R14,@R20,@Volume,@Json,@Audit,@Reason,@Now
FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@Session AND Symbol=@Symbol;
""";

    internal const string FreezeSql = """
SET XACT_ABORT ON;
DECLARE @Run UNIQUEIDENTIFIER, @Strategy UNIQUEIDENTIFIER;
SELECT TOP (1) @Run=RunId,@Strategy=StrategyVersionId FROM dbo.CalibrationRun WITH(HOLDLOCK)
WHERE RunPurpose=N'OfficialPaper' AND AuditState=N'Valid' AND RecommendationDate=@TradingDate
 AND MarketDataAsOf=@Prior AND CreatedUtc<=@Open AND StrategyVersionId IS NOT NULL
ORDER BY CreatedUtc DESC,StartedUtc DESC,RunId;
INSERT dbo.DelphiLiveSession
(SessionId,TradingDate,SessionOpenUtc,SessionCloseUtc,FreezeBoundaryUtc,FrozenUtc,ExpectedPriorCanonicalSessionDate,
 FreezeStatus,CalibrationRunId,DailyStrategyVersionId,CalibrationRunPurpose,CalibrationRunAuditState,
 CalibrationRecommendationDate,CalibrationMarketDataAsOf,CalibrationRunStartedUtc,CalibrationRunCreatedUtc,
 CollectorVersion,CollectorSourceContractVersion,CalendarVersion,CodeCommit,WorkingTreeState,SessionState,CoverageState,HostGapObserved)
SELECT @Session,@TradingDate,@Open,@Close,@Open,@Now,@Prior,
 CASE WHEN @Run IS NULL THEN N'NoValidDelphiRun' ELSE N'FrozenOfficialRun' END,
 @Run,@Strategy,r.RunPurpose,r.AuditState,r.RecommendationDate,r.MarketDataAsOf,r.StartedUtc,r.CreatedUtc,
 N'IntradayEvidenceCollectorV3',1,@Calendar,@Code,@Tree,N'Frozen',N'Pending',0
FROM (SELECT 1 AS Dummy) d LEFT JOIN dbo.CalibrationRun r ON r.RunId=@Run;
INSERT dbo.DelphiLiveSessionPolicy
(SessionPolicyId,SessionId,AssignmentId,DelphiLivePolicyVersionId,DailyStrategyVersionId,PolicyRole,RoleSlot,
 ExperimentId,IsOperationallyEnabled,PolicySettingsJson,PolicySettingsSha256,FrozenUtc)
SELECT NEWID(),@Session,a.AssignmentId,a.DelphiLivePolicyVersionId,@Strategy,a.PolicyRole,a.RoleSlot,a.ExperimentId,1,p.SettingsJson,p.SettingsSha256,@Now
FROM dbo.DelphiLivePolicyAssignment a JOIN dbo.DelphiLivePolicyVersion p ON p.DelphiLivePolicyVersionId=a.DelphiLivePolicyVersionId
WHERE a.EffectiveTradingDate<=@TradingDate AND (a.EndExclusiveTradingDate IS NULL OR a.EndExclusiveTradingDate>@TradingDate)
 AND a.CancelledUtc IS NULL AND a.RoleSlot<3;
IF EXISTS (SELECT l.Lens,l.Rank FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsPublished=1 AND l.IsEligible=1 AND l.Rank BETWEEN 1 AND 25 GROUP BY l.Lens,l.Rank HAVING COUNT(*)>1)
 THROW 51201,'Frozen daily source has duplicate published ranks.',1;
;WITH selected AS
(
 SELECT c.Symbol,COUNT(*) AS Lenses,MIN(l.Rank) AS BestRank FROM dbo.CalibrationCandidate c
 JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsEligible=1 AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25
 AND l.Lens IN(N'Continuation',N'Breakout') GROUP BY c.Symbol
), held AS
(
 SELECT symbol,MAX(CAST(mayAct AS INT)) AS MayAct FROM OPENJSON(@Holdings)
 WITH(symbol NVARCHAR(20) '$.symbol',mayAct BIT '$.mayAct') GROUP BY symbol
), required AS
(
 SELECT Symbol FROM selected UNION SELECT symbol FROM held UNION SELECT N'XIU'
)
INSERT dbo.DelphiLiveSessionSymbol
(SessionSymbolId,SessionId,Symbol,IsFrozenDailyCandidate,IsXiuBenchmark,IsTrackedHolding,IsDelphiLiveHolding,
 HasPendingProtectiveSell,IsSessionCarryCandidate,FrozenSourceLensCount,BestFrozenSourceLensRank,
 RequiredFromBarEndUtc,RequiredThroughBarEndUtc,SourceIdentityJson,AddedUtc)
SELECT NEWID(),@Session,r.Symbol,CASE WHEN s.Symbol IS NULL THEN 0 ELSE 1 END,CASE WHEN r.Symbol=N'XIU' THEN 1 ELSE 0 END,
 CASE WHEN h.symbol IS NULL THEN 0 ELSE 1 END,COALESCE(h.MayAct,0),0,0,COALESCE(s.Lenses,0),s.BestRank,
 DATEADD(MINUTE,5,@Open),@Close,@Holdings,CASE WHEN @Now>@Close THEN @Close ELSE @Now END
FROM required r LEFT JOIN selected s ON s.Symbol=r.Symbol LEFT JOIN held h ON h.symbol=r.Symbol;
INSERT dbo.DelphiLiveFrozenCandidate
(FrozenCandidateId,SessionId,SessionSymbolId,CalibrationRunId,CalibrationCandidateId,DailyStrategyVersionId,
 ObservationDate,ObservationOpen,ObservationHigh,ObservationLow,ObservationClose,ObservationVolume,DirectionEdge,
 CommonCompositeScore,CandidateSnapshotSchemaVersion,CandidateSnapshotJson,CalibrationCandidateCreatedUtc,FrozenUtc)
SELECT NEWID(),@Session,s.SessionSymbolId,@Run,c.CandidateId,@Strategy,c.ObservationDate,c.ObservationOpen,c.ObservationHigh,
 c.ObservationLow,c.ObservationClose,c.ObservationVolume,c.DirectionEdge,c.CompositeScore,c.SnapshotSchemaVersion,c.SnapshotJson,c.CreatedUtc,@Now
FROM dbo.DelphiLiveSessionSymbol s JOIN dbo.CalibrationCandidate c ON c.RunId=@Run AND c.Symbol=s.Symbol
WHERE s.SessionId=@Session AND s.IsFrozenDailyCandidate=1;
INSERT dbo.DelphiLiveFrozenCandidateLens
(FrozenCandidateLensId,FrozenCandidateId,CalibrationCandidateId,CalibrationLensEvaluationId,Lens,Direction,IsEligible,
 FrozenRank,FrozenRankingKey,IsPublished,FirstFailedGate,TraceSchemaVersion,GateTraceJson,CalibrationLensCreatedUtc,FrozenUtc)
SELECT NEWID(),f.FrozenCandidateId,f.CalibrationCandidateId,l.LensEvaluationId,l.Lens,l.Direction,l.IsEligible,l.Rank,
 l.RankingKey,l.IsPublished,l.FirstFailedGate,l.TraceSchemaVersion,l.GateTraceJson,l.CreatedUtc,@Now
FROM dbo.DelphiLiveFrozenCandidate f JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=f.CalibrationCandidateId
WHERE f.SessionId=@Session AND l.IsEligible=1 AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25
 AND l.Lens IN(N'Continuation',N'Breakout');
""";
}
