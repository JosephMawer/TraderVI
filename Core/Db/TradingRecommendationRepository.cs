#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

public sealed class TradingRecommendationRepository : ITradingRecommendationStore
{
    public async Task<TradingRecommendationBatch?> ReadLatestAsync(string accountLabel, CancellationToken token = default)
    { await using var c = await Open(token); return await Installed(c) ? await Batch(c, Account(accountLabel)) : null; }
    public async Task<ImmutableArray<string>> AssignedAccountsAsync(CancellationToken token = default)
    {
        await using var c = await Open(token);
        return await Installed(c) ? (await c.QueryAsync<string>("SELECT DISTINCT AccountLabel FROM dbo.TradingPromotion ORDER BY AccountLabel")).ToImmutableArray() : [];
    }
    public async Task<ITradingRecommendationSession?> BeginAsync(string accountLabel, CancellationToken token)
    {
        string account = Account(accountLabel); var c = await Open(token); SqlTransaction? t = null;
        try
        {
            if (!await Installed(c)) { await c.DisposeAsync(); return null; }
            t = c.BeginTransaction(IsolationLevel.Serializable); await Fence(c, t, account, false);
            var assignment = await Assignment(c, account, t);
            if (assignment is null) { t.Rollback(); t.Dispose(); await c.DisposeAsync(); return null; }
            var previous = await Batch(c, account, t);
            var report = await Report(c, account, t); var financial = await Financial(c, account, t);
            var input = await BuildInput(c, t, account, assignment, report, financial, previous, null, PaperGateProfile.NormalV1);
            return new Session(c, t, input, previous?.BatchId);
        }
        catch { t?.Dispose(); await c.DisposeAsync(); throw; }
    }

    public async Task<TradingRecommendationInput> ReadBrokerInputAsync(BrokerManagedSession session, CancellationToken token = default,
        bool independentInputs = false)
    {
        session.Strategy.Validate();
        if (session.ActivatedAt is null) throw new InvalidOperationException("Protection must transfer before strategy evaluation.");
        await using var c = await Open(token); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, "TFSA", false);
        var assignment = new TradingStrategyAssignment(session.SessionId, "TFSA", session.Strategy.Strategy,
            session.Strategy.PaperAccountId, session.SessionId, null, session.StrategyEffectiveUtc!.Value,
            session.Approval.Reviewer, session.Approval.Reason);
        var account = session.Account.Account;
        var marketSymbols = BrokerMarketSymbols.From(session.Account, session.Orders);
        var input = await BuildInput(c, t, "TFSA", assignment, account,
            new(account.LedgerFingerprint, account.Holdings, session.Account.Fills), null, session.Memory,
            session.Strategy.GateProfile, session.Strategy.Comparison, session.ProtectionReplayFromUtc, independentInputs, marketSymbols);
        var mappedBars = input.Bars.ToBuilder();
        foreach (var pair in marketSymbols)
            if (input.Bars.TryGetValue(pair.Value, out var bars)) mappedBars[pair.Key] = bars;
        input = input with { BrokerMarketSymbols = marketSymbols, Bars = mappedBars.ToImmutable() };
        token.ThrowIfCancellationRequested(); t.Commit(); return input;
    }

    private static async Task<TradingRecommendationInput> BuildInput(SqlConnection c, SqlTransaction t, string account,
        TradingStrategyAssignment assignment, ReportedTradingAccount? report, FinancialState financial,
        TradingRecommendationBatch? previous, TradingRecommendationMemory? brokerMemory, PaperGateProfile gateProfile,
        PaperComparisonAccount? comparison = null, DateTime? protectionReplayFromUtc = null, bool independentInputs = false,
        ImmutableDictionary<string, string>? marketSymbols = null)
    {
            var now = DateTime.UtcNow; var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now));
            var warnings = ImmutableArray.CreateBuilder<string>();
            var memory = brokerMemory ?? TradingRecommendationMemory.Reconcile(previous, report);
            // Switching strategy requires an empty account. Retain financial risk holds, discard only derived symbol policy state.
            if (brokerMemory is null && previous is not null && previous.PromotionId != assignment.PromotionId) memory = memory with { Symbols = TradingRecommendationMemory.Empty.Symbols };
            var calendarPath = Environment.GetEnvironmentVariable(DelphiLiveDesktopService.CalendarPathEnvironmentVariable);
            ReviewedTsxSessionCalendar? calendar = null; DelphiLiveSessionBounds? bounds = null;
            try
            {
                if (string.IsNullOrWhiteSpace(calendarPath)) throw new InvalidOperationException();
                calendar = ReviewedTsxSessionCalendar.Load(calendarPath);
                if (calendar.IsRegularSession(date)) bounds = calendar.GetSessionBounds(date);
                else warnings.Add("Today is not a regular TSX session.");
            }
            catch (Exception ex) when (ex is System.IO.IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
            { warnings.Add("A reviewed TSX calendar with coverage for this session is required."); }
            Guid? daily = await c.QuerySingleOrDefaultAsync<Guid?>("SELECT VersionId FROM dbo.StrategyVersion WHERE IsActive=1", transaction: t);
            Guid? run = null;
            DateTime? candidateEffective = null;
            var candidates = ImmutableArray<TradingCandidate>.Empty;
            if (bounds is not null && calendar is not null)
            {
                var adopted = comparison is null ? null : await PaperComparisonRepository.ReadSourceAsync(c, t, comparison.BatchId, date.ToDateTime(TimeOnly.MinValue), default);
                bool sourceMatches = comparison is null || adopted is not null && adopted.DailyStrategyVersionId == assignment.Strategy.Daily.StrategyVersionId &&
                    adopted.PublishedUtc <= now && adopted.AdoptedUtc <= now && adopted.EffectiveUtc <= now;
                if (adopted is not null) candidateEffective = adopted.EffectiveUtc > comparison!.EffectiveUtc ? adopted.EffectiveUtc : comparison.EffectiveUtc;
                if (!sourceMatches) warnings.Add("The comparison strategy needs its compatible, prospectively adopted source for this session.");
                var runRow = !sourceMatches ? null : await c.QuerySingleOrDefaultAsync<RunRow>("""
                    SELECT TOP(1) RunId,StrategyConfigJson,ModelSnapshotJson FROM dbo.CalibrationRun WHERE RunPurpose=N'OfficialPaper' AND AuditState=N'Valid'
                    AND RecommendationDate=@Date AND MarketDataAsOf=@Prior AND StrategyVersionId=@Strategy
                    AND ((@Source IS NULL AND CreatedUtc<=@Open) OR RunId=@Source)
                    ORDER BY CreatedUtc DESC,RunId
                    """, new
                {
                    Date = date.ToDateTime(TimeOnly.MinValue),
                    Prior = calendar.GetImmediatelyPrecedingSession(date).ToDateTime(TimeOnly.MinValue),
                    Strategy = assignment.Strategy.Daily.StrategyVersionId,
                    Open = bounds.OpenUtc, Source = adopted?.RunId
                }, t);
                if (runRow is not null)
                {
                    var config = Read<StrategyConfig>(runRow.StrategyConfigJson);
                    var models = Read<ImmutableArray<Core.Calibration.ModelArtifactProvenance>>(runRow.ModelSnapshotJson);
                    if (!assignment.Strategy.Daily.Matches(config,models.Select(m => new TradingModelIdentity(m.ModelId,m.TaskType,m.ArtifactSha256)).ToImmutableArray()))
                        warnings.Add("Frozen daily configuration or model artifacts differ from the approved snapshot.");
                    else run = runRow.RunId;
                }
                if (run.HasValue && assignment.Strategy.Family == TradingStrategyFamily.DailyShadow)
                    candidates = (await c.QueryAsync<TradingCandidate>("""
                        SELECT c.CandidateId,c.RunId,c.Symbol,l.Rank,CAST(c.ObservationClose AS decimal(19,6)) PreviousClose
                        FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
                        WHERE c.RunId=@Run AND l.Lens=@Lens AND l.IsPublished=1 AND (@Ungated=1 OR (l.IsEligible=1 AND l.Direction=N'Buy')) AND l.Rank BETWEEN 1 AND @Maximum
                        ORDER BY l.Rank,c.Symbol
                        """, new { Ungated = gateProfile == PaperGateProfile.PortfolioRulesOnlyV1, Run = run, Lens = assignment.Strategy.Selection[CompleteTradingStrategy.ShadowSelectionPrefix.Length..], Maximum = assignment.Strategy.MaximumHoldings }, t)).ToImmutableArray();
            }
            var liveInputs = ImmutableArray<TradingLiveObservation>.Empty;
            if (bounds is not null && assignment.Strategy.Family == TradingStrategyFamily.DelphiLive)
            {
                const string liveSql = """
                    SELECT e.InputJson,e.ContinuityEpoch,
                        CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict f JOIN dbo.IntradayCollectionSlot sl ON sl.CollectionSlotId=f.CollectionSlotId
                            WHERE sl.SessionId=e.SessionId AND sl.ExpectedBarEndUtc<=e.BarEndUtc AND sl.Symbol IN(e.Symbol,N'XIU')) THEN 1 ELSE 0 END AS bit) HasConflict
                        FROM dbo.DelphiLiveEvaluation e JOIN dbo.DelphiLiveSession s ON s.SessionId=e.SessionId
                        WHERE s.TradingDate>=@FromDate AND s.TradingDate<=@Date AND e.PolicyVersionId=@Policy
                        AND e.RecordedUtc<=@Now AND e.BarEndUtc>@Boundary
                        -- IndependentProgress
                        ORDER BY e.BarEndUtc,e.Symbol
                    """;
                var saved = (await c.QueryAsync<LiveRow>(independentInputs
                    ? liveSql.Replace("dbo.DelphiLiveEvaluation", "dbo.EngineInputPublication", StringComparison.Ordinal)
                        .Replace("-- IndependentProgress", """
AND s.TradingDate=@Date AND e.BarEndUtc>=COALESCE(
 (SELECT MAX(p.BarUtc) FROM OPENJSON(@Progress) WITH(Symbol nvarchar(20),SessionId uniqueidentifier,BarUtc datetime2) p
  WHERE p.Symbol=e.Symbol AND p.SessionId=e.SessionId),@Boundary)
""", StringComparison.Ordinal) : liveSql,
                    new { Date = date.ToDateTime(TimeOnly.MinValue),
                        FromDate = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(protectionReplayFromUtc ?? assignment.EffectiveUtc)).ToDateTime(TimeOnly.MinValue),
                        Policy = assignment.Strategy.PolicyVersionId, Now = now,
                        Boundary = protectionReplayFromUtc ?? assignment.EffectiveUtc,
                        Progress = System.Text.Json.JsonSerializer.Serialize(memory.Symbols.Select(p => new
                        { Symbol = p.Key, SessionId = p.Value.LiveSessionId, BarUtc = p.Value.LastLiveBarUtc })) }, t)).ToArray();
                var contents = independentInputs ? await EngineEvidenceStorage.ReadAsync(c, t, saved.Select(e => e.InputJson)) : null;
                liveInputs = saved.Select(e => BrokerLiveObservationPolicy.Bind(
                    contents is null ? DelphiLiveLedgerJson.Deserialize<DelphiLiveEvaluationInput>(e.InputJson)
                        : DelphiLiveInputCodec.Read(e.InputJson, contents), e.ContinuityEpoch,
                    assignment.Strategy, comparison, e.HasConflict)).ToImmutableArray();
                if (liveInputs.IsEmpty) warnings.Add(independentInputs
                    ? "Delphi Live needs verified shared market inputs from the independent engine. Paper progress cannot supply them."
                    : "Delphi Live needs current durable collection/evaluation inputs. Keep its existing paper monitor available; promotion does not activate it.");
            }
            string[] symbols = financial.Holdings.Select(h => marketSymbols?.GetValueOrDefault(h.Symbol) ?? h.Symbol).Concat(candidates.Select(p => p.Symbol)).Concat(liveInputs.Select(e => e.Input.Stock.Symbol)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var bars = ImmutableDictionary.CreateBuilder<string, ImmutableArray<TradingPriceBar>>(StringComparer.OrdinalIgnoreCase);
            if (bounds is not null && symbols.Length > 0)
            {
                var source = await c.QueryAsync<BarRow>("""
                    SELECT b.Symbol,b.EvidenceBarId EvidenceId,b.EventUtc StartUtc,o.ReceivedUtc,b.IntervalMinutes Minutes,b.[Open],b.High,b.Low,b.[Close]
                    FROM dbo.IntradayEvidenceBar b JOIN dbo.IntradayPollObservation o ON o.ObservationId=b.FirstObservationId
                    WHERE b.Symbol IN @Symbols AND b.EventUtc>=@ReplayFrom AND b.EventUtc<@Close AND o.ReceivedUtc<=@Now
                        AND o.AuditState IN(N'Valid',N'Degraded') AND o.EvidenceSchemaVersion=1 AND o.Provider=N'TMX'
                        AND b.CreatedUtc<=@Now AND DATEADD(minute,b.IntervalMinutes,b.EventUtc)<=o.ReceivedUtc
                        AND NOT EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict f JOIN dbo.IntradayCollectionSlot sl ON sl.CollectionSlotId=f.CollectionSlotId
                            WHERE sl.Symbol=b.Symbol AND sl.ExpectedBarEndUtc=DATEADD(minute,b.IntervalMinutes,b.EventUtc))
                    ORDER BY b.EventUtc
                    """, new { Symbols = symbols,
                        ReplayFrom = protectionReplayFromUtc is { } replay && replay < bounds.OpenUtc
                            ? replay.AddMinutes(-15) : bounds.OpenUtc,
                        Close = bounds.CloseUtc, Now = now }, t);
                foreach (var group in source.GroupBy(b => b.Symbol, StringComparer.OrdinalIgnoreCase))
                    bars[group.Key] = group.Select(b => new TradingPriceBar(b.EvidenceId, Utc(b.StartUtc), Utc(b.ReceivedUtc), b.Minutes, b.Open, b.High, b.Low, b.Close)).ToImmutableArray();
            }
            var ordinals = ImmutableDictionary.CreateBuilder<Guid, int>();
            if (calendar is not null && bounds is not null)
                foreach (var h in financial.Holdings)
                    try { ordinals[h.PositionId] = calendar.GetSessionOrdinal(date) - calendar.GetSessionOrdinal(h.EntryDate) + 1; }
                    catch (InvalidOperationException) { warnings.Add($"{h.Symbol}: entry-session calendar coverage is unavailable."); }
            bool paused = await TradingControlRepository.IsPausedAsync(c, t, EngineStrategySettings.Tracked);
            var priorProtectionSessions = ImmutableArray.CreateBuilder<TradingProtectionSession>();
            if (protectionReplayFromUtc is { } from && calendar is not null && bounds is not null)
                for (var priorDate = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(from)); priorDate < date; priorDate = priorDate.AddDays(1))
                    if (calendar.IsRegularSession(priorDate))
                    {
                        var close = calendar.GetSessionBounds(priorDate).CloseUtc;
                        if (close > from)
                            priorProtectionSessions.Add(new(close, calendar.GetSessionOrdinal(date) - calendar.GetSessionOrdinal(priorDate)));
                    }
            var input = new TradingRecommendationInput(Guid.NewGuid(), assignment, now, date, bounds?.OpenUtc ?? now.AddDays(1), bounds?.CloseUtc ?? now.AddDays(1).AddHours(6.5),
                daily, run, ImplementationIdentity, report, financial.Fingerprint, paused, financial.Holdings, financial.Fills, candidates, bars.ToImmutable(),
                liveInputs, ordinals.ToImmutable(), memory, warnings.ToImmutable())
                { CandidateEffectiveUtc = candidateEffective, PriorProtectionSessions = priorProtectionSessions.ToImmutable() };
            return input;
    }
    private sealed class Session(SqlConnection c, SqlTransaction t, TradingRecommendationInput input, Guid? prior) : ITradingRecommendationSession
    {
        public TradingRecommendationInput Input => input;
        public async Task<TradingRecommendationBatch> PublishAsync(TradingRecommendationInput evaluated, TradingRecommendationResult result, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (evaluated.BatchId != input.BatchId || evaluated.Assignment != input.Assignment || evaluated.CurrentLedgerFingerprint != input.CurrentLedgerFingerprint)
                throw new InvalidOperationException("Evaluation snapshot changed outside the transaction boundary.");
            string json = Json(evaluated); string hash = EngineStrategySettings.Hash(json);
            var batch = new TradingRecommendationBatch(input.BatchId, input.Assignment.AccountLabel, input.Assignment.PromotionId,
                input.Account?.SnapshotId, prior, input.EvaluatedUtc, hash, result) { LedgerFingerprint = input.CurrentLedgerFingerprint };
            await c.ExecuteAsync("""
                IF (SELECT TOP(1) PromotionId FROM dbo.TradingPromotion WHERE AccountLabel=@Account ORDER BY Sequence DESC)<>@Promotion
                    THROW 51503,'Promotion changed during evaluation. Discard this result and retry.',1;
                INSERT dbo.TradingRecommendationBatch(BatchId,AccountLabel,PromotionId,AccountReportId,PriorBatchId,EvaluatedUtc,InputFingerprint,InputJson,BatchJson)
                    VALUES(@Id,@Account,@Promotion,@Report,@Prior,@Now,@Hash,@Input,@Batch);
                """, new
            {
                Id = batch.BatchId,
                Account = batch.AccountLabel,
                Promotion = batch.PromotionId,
                Report = batch.AccountReportId,
                Prior = prior,
                Now = batch.EvaluatedUtc,
                Hash = hash,
                Input = json,
                Batch = Json(batch)
            }, t);
            t.Commit(); return batch;
        }
        public async ValueTask DisposeAsync() { t.Dispose(); await c.DisposeAsync(); }
    }
    private static DateTime Utc(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Utc);
    private sealed record RunRow(Guid RunId, string StrategyConfigJson, string ModelSnapshotJson);
    private sealed record LiveRow(string InputJson, int ContinuityEpoch, bool HasConflict);
    private sealed record BarRow(string Symbol, Guid EvidenceId, DateTime StartUtc, DateTime ReceivedUtc, short Minutes, decimal Open, decimal High, decimal Low, decimal Close);
}
