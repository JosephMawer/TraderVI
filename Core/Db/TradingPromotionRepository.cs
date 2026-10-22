#nullable enable
using Core.ML.Engine.Profit;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

public sealed class TradingPromotionRepository : ITradingPromotionStore
{
    // Definition readiness only: no synthetic current-session Live evaluation is created.
    public async Task<bool> HasPreOpenDailySourceAsync(BrokerManagedStrategy strategy, ITsxSessionCalendar calendar,
        DateTimeOffset now, CancellationToken token = default)
    {
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (!BrokerSessionContinuation.IsPreOpenReview(calendar, now)) return false;
        await using var c = await Open(token);
        var row = await c.QuerySingleOrDefaultAsync<PreOpenDailyRow>(new CommandDefinition("""
SELECT TOP(1) AuditState,MarketDataAsOf,StrategyVersionId,CreatedUtc,StrategyConfigJson,ModelSnapshotJson
FROM dbo.CalibrationRun WHERE RunPurpose=N'OfficialPaper' AND RecommendationDate=@Date
ORDER BY CreatedUtc DESC,RunId;
""", new { Date = date.ToDateTime(TimeOnly.MinValue) }, cancellationToken: token));
        if (row is null || row.AuditState != "Valid" || row.StrategyVersionId != strategy.Strategy.Daily.StrategyVersionId ||
            DateOnly.FromDateTime(row.MarketDataAsOf) != calendar.GetImmediatelyPrecedingSession(date) ||
            row.CreatedUtc > now.UtcDateTime || row.CreatedUtc >= calendar.GetSessionBounds(date).OpenUtc) return false;
        var models = Read<ImmutableArray<Core.Calibration.ModelArtifactProvenance>>(row.ModelSnapshotJson);
        return strategy.Strategy.Daily.Matches(Read<StrategyConfig>(row.StrategyConfigJson),
            models.Select(m => new TradingModelIdentity(m.ModelId,m.TaskType,m.ArtifactSha256)).ToImmutableArray());
    }
    private sealed record PreOpenDailyRow(string AuditState, DateTime MarketDataAsOf, Guid? StrategyVersionId,
        DateTime CreatedUtc, string StrategyConfigJson, string ModelSnapshotJson);

    // Renewal reviews the real account's frozen definition, never reopens a paper portfolio's lifetime history.
    public async Task<(BrokerStrategyReview Review, DateTimeOffset Updated)> ReviewExistingBrokerAsync(
        BrokerManagedSession saved, ITsxSessionCalendar calendar, DateTimeOffset now, CancellationToken token = default)
    {
        saved.Strategy.Validate();
        await using var c = await Open(token); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, "TFSA", false);
        if (!await EngineIntegrityRepository.RequiredGuardsAsync(c, t, token))
            throw new InvalidOperationException("Independent immutable history guards must be verified before review.");
        await EngineRealObservationStore.RequireCurrentAsync(c, t, saved, token);
        var policy = await DelphiLiveSessionRepository.ReadPolicyAsync(c, saved.Strategy.Strategy.PolicyVersionId, t, token);
        if (BrokerAccountImport.Hash(policy) != BrokerAccountImport.Hash(saved.Strategy.Strategy.ReadLivePolicy()))
            throw new InvalidOperationException("The real assignment's saved policy differs from its immutable source.");
        var daily = await c.QuerySingleOrDefaultAsync<StrategyVersionInfo>(
            "SELECT * FROM dbo.StrategyVersion WHERE IsActive=1", transaction: t)
            ?? throw new InvalidOperationException("The current daily assignment is unavailable.");
        var binding = await c.QuerySingleOrDefaultAsync<Binding>(
            "SELECT ModelSetJson,ModelSetSha256 FROM dbo.StrategyModelBinding WHERE StrategyVersionId=@Id",
            new { Id = daily.VersionId }, t) ?? throw new InvalidOperationException("The exact daily model binding is unavailable.");
        var models = StoredProfitModelSet.Read(binding.ModelSetJson, binding.ModelSetSha256); models.Validate(daily.VersionId, daily.DecisionRef);
        if (daily.VersionId != saved.Strategy.Strategy.Daily.StrategyVersionId ||
            binding.ModelSetSha256 != saved.Strategy.Strategy.Daily.ModelSetSha256 ||
            models.InputContract != saved.Strategy.Strategy.Daily.InputContract ||
            !saved.Strategy.Strategy.Daily.Matches(daily.ToConfig(), models.Models.Select(m =>
                new TradingModelIdentity(m.Registry.ModelId, m.Registry.TaskType, m.ArtifactSha256)).ToImmutableArray()))
            throw new InvalidOperationException("Current daily settings/models differ from the real assignment; a renewal cannot change them.");
        var strategy = saved.Strategy with { Strategy = saved.Strategy.Strategy with { ImplementationIdentity = ImplementationIdentity } };
        strategy.Validate(); BrokerSessionContinuation.RequireSameDefinition(saved.Strategy, strategy);
        var evidence = saved.SourceSwitch?.SourceReview.Evidence
            ?? throw new InvalidOperationException("Recover the originally reviewed Portfolio rules only evidence.");
        evidence = evidence with { Limitations = evidence.Limitations.Add("Historical source evidence is preserved; this session renewal does not refresh paper performance or wait for paper research.") };
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        var bounds = calendar.GetSessionBounds(date);
        DateTime? updated = BrokerSessionContinuation.IsPreOpenReview(calendar, now) ? now.UtcDateTime :
            await c.QuerySingleAsync<DateTime?>(new CommandDefinition("""
SELECT MAX(p.RecordedUtc) FROM dbo.DelphiLiveSession s
JOIN dbo.EngineInputPublication p ON p.SessionId=s.SessionId AND p.PolicyVersionId=@Policy
WHERE s.TradingDate=@Date AND p.BarEndUtc>=@Open AND p.BarEndUtc<@Close AND p.RecordedUtc<=@Now;
""", new { Policy = policy.PolicyVersionId, Date = date.ToDateTime(TimeOnly.MinValue), Open = bounds.OpenUtc, Close = bounds.CloseUtc, Now = now.UtcDateTime }, t, cancellationToken: token));
        t.Commit();
        return (new(strategy, evidence, now.UtcDateTime, []),
            new DateTimeOffset(DateTime.SpecifyKind(updated ?? DateTime.MinValue, DateTimeKind.Utc)));
    }

    // Read-only strategy definition for broker setup. This never uses the recommendation-only approval exception.
    public async Task<BrokerStrategyReview> ReviewBrokerAsync(Guid paperAccountId, TradingStrategyFamily family,
        CancellationToken token = default)
    {
        await using var c = await Open(token); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, "TFSA", false);
        var review = await Review(c, t, paperAccountId, family, "TFSA", token, brokerReview: true);
        var comparison = (await PaperComparisonRepository.ReadAccountsAsync(c, t, token)).SingleOrDefault(a => a.AccountId == paperAccountId);
        var strategy = new BrokerManagedStrategy(review.Strategy, paperAccountId, comparison?.Profile ?? PaperGateProfile.NormalV1, comparison);
        if (family == TradingStrategyFamily.DelphiLive && comparison is not null &&
            comparison.LivePolicyVersionId != review.Strategy.PolicyVersionId)
        {
            Guid assignment = await c.QuerySingleOrDefaultAsync<Guid>(new CommandDefinition("""
SELECT a.AssignmentId FROM dbo.EngineStrategyAssignment a
JOIN dbo.ProgressiveProfitProtectionComparisonSuccessor allowed
 ON allowed.AccountId=a.TargetId AND allowed.VersionId=a.VersionId
WHERE a.TargetId=@Paper AND a.VersionId=@Policy
 AND a.Sequence=(SELECT MAX(latest.Sequence) FROM dbo.EngineStrategyAssignment latest WHERE latest.TargetId=@Paper);
""", new { Paper = paperAccountId, Policy = review.Strategy.PolicyVersionId }, t, cancellationToken: token));
            if (assignment == Guid.Empty || comparison.LivePolicyVersionId is not { } originalId)
                throw new InvalidOperationException("The current Normal policy is not its installed, explicitly assigned progressive successor.");
            var original = await DelphiLiveSessionRepository.ReadPolicyAsync(c, originalId, t, token);
            strategy = strategy with { LiveComparisonSuccessor = new(paperAccountId, assignment, original) };
        }
        strategy.Validate();
        t.Commit();
        return new(strategy, review.Evidence, review.PreparedUtc, review.OperationalBlocks);
    }
    public async Task<TradingPromotionReview> ReviewAsync(Guid paperAccountId, TradingStrategyFamily family, string accountLabel, CancellationToken token = default)
    {
        string account = Account(accountLabel);
        await using var c = await Open(token); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, account, false);
        var review = await Review(c, t, paperAccountId, family, account, token);
        t.Commit(); return review;
    }
    public async Task<TradingStrategyAssignment?> ReadAssignmentAsync(string accountLabel, CancellationToken token = default)
    {
        await using var c = await Open(token);
        return await Installed(c) ? await Assignment(c, Account(accountLabel)) : null;
    }
    public async Task<TradingPromotionReview> ReviewPreviousAsync(Guid promotionId, string accountLabel, CancellationToken token = default)
    {
        string account = Account(accountLabel);
        await using var c = await Open(token); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, account, false); await RequireInstalled(c, t);
        var result = await PreviousReview(c, t, promotionId, account, token);
        t.Commit(); return result;
    }
    private static async Task<TradingPromotionReview> PreviousReview(SqlConnection c, SqlTransaction t, Guid promotionId, string account, CancellationToken token)
    {
        string json = await c.QuerySingleOrDefaultAsync<string>("SELECT ReviewJson FROM dbo.TradingPromotion WHERE PromotionId=@Id AND AccountLabel=@Account", new { Id = promotionId, Account = account }, t)
            ?? throw new InvalidOperationException("That historical promotion does not belong to this account.");
        var old = Read<TradingPromotionReview>(json);
        var report = await Report(c, account, t); var financial = await Financial(c, account, t);
        var current = await Assignment(c, account, t);
        var access = await TradingControlRepository.AccessAsync(c, t, EngineStrategySettings.Tracked);
        var blocks = TradingPromotionPolicy.OperationalBlocks(true, access, report, financial.Fingerprint);
        if (old.Strategy.ImplementationIdentity != ImplementationIdentity) blocks = blocks.Add("The saved implementation cannot be run by this build. Review the paper strategy with the current implementation.");
        if (current?.Strategy.Fingerprint == old.Strategy.Fingerprint) blocks = blocks.Add("This complete strategy is already assigned.");
        var limitations = old.Evidence.Limitations.Add("Reapproval uses the exact saved strategy and historical evidence. Its prior performance is not new evidence.");
        Guid? activeDaily = await c.QuerySingleOrDefaultAsync<Guid?>("SELECT VersionId FROM dbo.StrategyVersion WHERE IsActive=1",transaction:t);
        if (activeDaily != old.Strategy.Daily.StrategyVersionId)
            limitations = limitations.Add("The saved daily version is not currently assigned. New entries will wait for that daily assignment and matching frozen evidence. This approval does not change shared daily settings.");
        return old with
        {
            RequestId = Guid.NewGuid(),
            PreviousReviewPromotionId = promotionId,
            PriorPromotionId = current?.PromotionId,
            AccountReportId = report?.SnapshotId,
            PreparedUtc = DateTime.UtcNow,
            OperationalBlocks = blocks,
            Evidence = old.Evidence with { Limitations = limitations }
        };
    }
    public async Task<ImmutableArray<TradingStrategyAssignment>> HistoryAsync(string accountLabel, CancellationToken token = default)
    {
        await using var c = await Open(token);
        if (!await Installed(c)) return [];
        var rows = await c.QueryAsync<string>("SELECT AssignmentJson FROM dbo.TradingPromotion WHERE AccountLabel=@Account ORDER BY Sequence DESC", new { Account = Account(accountLabel) });
        return rows.Select(Read<TradingStrategyAssignment>).ToImmutableArray();
    }
    private static async Task<TradingPromotionReview> Review(SqlConnection c, SqlTransaction t, Guid paper, TradingStrategyFamily family, string account, CancellationToken token, bool brokerReview = false)
    {
        if (!brokerReview && await PaperComparisonRepository.InstalledAsync(c, t) && await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PaperComparisonAccount WHERE AccountId=@Id;", new { Id = paper }, t) > 0)
            throw new InvalidOperationException("Fixed-gate comparison accounts are paper experiments; their admission profile is not a recommendation approval.");
        var comparison = brokerReview
            ? (await PaperComparisonRepository.ReadAccountsAsync(c, t, token)).SingleOrDefault(a => a.AccountId == paper) : null;
        var daily = await c.QuerySingleOrDefaultAsync<StrategyVersionInfo>("SELECT * FROM dbo.StrategyVersion WHERE IsActive=1", transaction: t)
            ?? throw new InvalidOperationException("A preserved daily strategy assignment is required to identify the complete strategy.");
        var binding = await c.QuerySingleOrDefaultAsync<Binding>("SELECT ModelSetJson,ModelSetSha256 FROM dbo.StrategyModelBinding WHERE StrategyVersionId=@Id", new { Id = daily.VersionId }, t)
            ?? throw new InvalidOperationException("The daily strategy has no preserved model-set identity.");
        var models = StoredProfitModelSet.Read(binding.ModelSetJson, binding.ModelSetSha256); models.Validate(daily.VersionId, daily.DecisionRef);
        var inputs = new TradingDailyInputs(daily.VersionId, daily.ToConfig(), binding.ModelSetSha256, models.InputContract,
            models.Models.OrderBy(m => m.Registry.TaskType, StringComparer.Ordinal).Select(m => new TradingModelIdentity(m.Registry.ModelId, m.Registry.TaskType, m.ArtifactSha256)).ToImmutableArray());
        if (comparison is not null && comparison.DailyStrategyVersionId != daily.VersionId)
            throw new InvalidOperationException("The comparison's pinned daily version differs from the active daily version. Do not substitute current settings.");
        var notes = ImmutableArray.CreateBuilder<string>();
        notes.Add("Returns describe this paper account's history. Periods, capital, prior policies, coverage and fill assumptions may differ from other accounts.");
        notes.Add(brokerReview ? "Broker execution needs separate evidence qualification and explicit authority; this definition review grants neither."
            : "Manual approval is your decision. No minimum performance, cohort count or automatic winner test is enforced for recommendation mode.");
        string name, paperName, selection, facts; int maximum; Guid policyId; SystemShadowPolicyConfig? shadow = null; DelphiLivePolicyDefinition? live = null;
        if (family == TradingStrategyFamily.DailyShadow)
        {
            var row = await c.QuerySingleOrDefaultAsync<ShadowSource>("SELECT PortfolioCode,DisplayName,Lens,CAST(MaximumPositions AS int) MaximumPositions FROM dbo.ShadowPortfolio WHERE PortfolioId=@Id", new { Id = paper }, t)
                ?? throw new InvalidOperationException("The selected daily paper account is unavailable.");
            var version = await c.QuerySingleOrDefaultAsync<EngineStrategyVersion>("""
                SELECT TOP(1) v.VersionId,v.Family,v.Name,v.SettingsJson,v.SettingsHash,v.CreatedUtc
                FROM dbo.EngineStrategyAssignment a JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId WHERE a.TargetId=@Id ORDER BY a.Sequence DESC
                """, new { Id = paper }, t);
            if (version is not null && version.Family != EngineStrategySettings.Shadow) throw new InvalidOperationException("Paper account policy family is inconsistent.");
            shadow = version?.Read<SystemShadowPolicyConfig>() ?? SystemShadowPolicyConfig.Version1;
            if (comparison is not null)
                shadow = Read<SystemShadowPolicyConfig>(comparison.ShadowSettingsJson ?? throw new InvalidOperationException("The comparison's pinned policy is missing."));
            policyId = version?.VersionId ?? Id(SystemShadowVersions.Policy); maximum = row.MaximumPositions;
            if (comparison is not null) policyId = comparison.ShadowStrategyVersionId ?? Id(SystemShadowVersions.Policy);
            name = $"{row.Lens} Top {maximum} · {version?.Name ?? SystemShadowVersions.Policy}"; selection = CompleteTradingStrategy.ShadowSelectionPrefix + row.Lens;
            paperName = row.DisplayName;
            facts = await c.QuerySingleAsync<string>("""
                SELECT (SELECT @Id PaperAccountId,
                JSON_QUERY((SELECT PortfolioId,GenerationId,PortfolioCode,DisplayName,Status,CashBalance,HighestClosingValue FROM dbo.ShadowPortfolio WHERE PortfolioId=@Id FOR JSON PATH)) Account,
                JSON_QUERY((SELECT SessionId,TradingDate,CalibrationRunId,Status,OpeningValue,ClosingValue,DailyLossGuardActive FROM dbo.ShadowPortfolioSession WHERE PortfolioId=@Id ORDER BY TradingDate FOR JSON PATH)) Sessions,
                JSON_QUERY((SELECT r.* FROM dbo.ShadowDailySourceRevision r JOIN dbo.ShadowPortfolioSession s ON s.SessionId=r.SessionId WHERE s.PortfolioId=@Id ORDER BY r.AdoptedUtc,r.RevisionId FOR JSON PATH)) DailySourceAdoptions,
                JSON_QUERY((SELECT AssignmentId,VersionId,AssignedUtc FROM dbo.EngineStrategyAssignment WHERE TargetId=@Id ORDER BY Sequence FOR JSON PATH)) PolicyHistory
                FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
                """, new { Id = paper }, t);
            notes.Add("Daily Shadow uses later five-minute simulated fills, whole-share staged sizing and policy friction. Actual manual fills will differ.");
            notes.Add("Early Shadow sessions include engineering shakedown. This review does not certify clean forward performance.");
        }
        else
        {
            string json = await c.QuerySingleOrDefaultAsync<string>("SELECT SnapshotJson FROM dbo.DelphiLivePortfolioLedger WHERE PortfolioId=@Id", new { Id = paper }, t)
                ?? throw new InvalidOperationException("The selected Delphi Live paper account is unavailable.");
            var portfolio = await DelphiLiveSnapshotStorage.ReadAsync(c, t, json, token);
            live = await DelphiLiveSessionRepository.ReadPolicyAsync(c, portfolio.PolicyVersionId, t, token);
            policyId = live.PolicyVersionId; maximum = live.MaximumHoldings;
            name = $"Delphi Live · {policyId.ToString("N")[..8]}"; selection = CompleteTradingStrategy.LiveSelection;
            paperName = comparison?.DisplayName ?? $"Delphi Live {portfolio.Role} · {portfolio.PortfolioId:D}";
            if (comparison is not null)
            {
                name = $"{comparison.ProfileName} · {name}";
                notes.Add($"Exact comparison admission profile: {comparison.ProfileName}. This is definition/transition review only; no recommendation approval or execution qualification is granted.");
            }
            bool researchIntervened = await c.ExecuteScalarAsync<int>("""
                SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TradingControlEvent WHERE SystemKey IN(N'Daily',N'DelphiLive') AND Paused=1)
                    OR EXISTS(SELECT 1 FROM dbo.OperatorExitRequest WHERE Family=N'DelphiLive')
                    OR EXISTS(SELECT 1 FROM dbo.EngineStrategyAssignment a JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId WHERE v.Family=N'DelphiLive')
                    THEN 1 ELSE 0 END
                """, transaction:t)==1;
            facts = Json(new
            {
                portfolio.PortfolioId,
                portfolio.GenerationId,
                portfolio.PolicyVersionId,
                portfolio.Role,
                Comparison = comparison,
                portfolio.Marks,
                FillCount = portfolio.Fills.Length,
                BroadResearchInterventionGuard = researchIntervened,
                ResearchProtocolContexts = await c.QueryAsync("SELECT ProtocolId,OperationalPortfolioId,Revision,SnapshotJson FROM dbo.DelphiLiveExperimentProtocol ORDER BY UpdatedUtc", transaction:t),
                PolicyHistory = await c.QueryAsync("SELECT AssignmentId,VersionId,AssignedUtc FROM dbo.EngineStrategyAssignment WHERE TargetId=@Id ORDER BY Sequence", new { Id = paper }, t)
            });
            notes.Add("Live's champion/control/challenger role applies only inside its paper research family. Live research eligibility is not universal approval.");
            notes.Add(researchIntervened
                ? brokerReview
                    ? "Live's broad research-intervention guard is active. Its internal research promotion remains restricted; this broker definition review grants no approval."
                    : "Live's broad research-intervention guard is active. Its internal research promotion remains restricted. You may still approve this separate manual recommendation assignment."
                : "No broad Live research intervention is recorded. This review does not certify an untouched comparison or calculate a qualifying research score; inspect the saved protocol contexts separately.");
            notes.Add("Live quotes and costs remain paper assumptions; actual reported fills retain their own prices. Its research rules and intervention history remain unchanged.");
        }
        notes.Add($"This snapshot binds current daily inputs {daily.VersionName}; earlier account sessions may use different daily inputs. Model files are not copied or trained.");
        var strategy = new CompleteTradingStrategy(Id("TradingStrategy:" + family + ":" + selection + ":" + maximum), Guid.NewGuid(), name, family, inputs, policyId,
            shadow, live, selection, maximum, ImplementationIdentity, CompleteTradingStrategy.Contract);
        strategy = strategy with { VersionId = Id(strategy.Fingerprint) }; strategy.Validate();
        bool installed = await Installed(c, t);
        if (installed)
        {
            string? canonical = await c.QuerySingleOrDefaultAsync<string>("SELECT DefinitionJson FROM dbo.TradingStrategyVersion WHERE VersionId=@Id", new { Id = strategy.VersionId }, t);
            if (canonical is not null) strategy = Read<CompleteTradingStrategy>(canonical);
        }
        var report = installed ? await Report(c, account, t) : null;
        var financial = installed ? await Financial(c, account, t) : null;
        var access = await TradingControlRepository.AccessAsync(c, t, EngineStrategySettings.Tracked);
        var current = installed ? await Assignment(c, account, t) : null;
        var blocks = brokerReview ? ImmutableArray<string>.Empty
            : TradingPromotionPolicy.OperationalBlocks(installed, access, report, financial?.Fingerprint ?? new string('0', 64));
        if (current?.Strategy.Fingerprint == strategy.Fingerprint) blocks = blocks.Add("This complete strategy is already assigned to this account.");
        return new(Guid.NewGuid(), account, strategy, new(paper, paperName, family.ToString(), facts, notes.ToImmutable()), current?.PromotionId,
            report?.SnapshotId, DateTime.UtcNow, blocks);
    }
    public async Task<TradingStrategyAssignment> ApproveAsync(TradingPromotionApproval approval, CancellationToken token = default)
    {
        approval.Validate(); string account = Account(approval.Review.AccountLabel);
        await using var c = await Open(token); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, account, true); await RequireInstalled(c, t);
        var saved = await c.QuerySingleOrDefaultAsync<StoredApproval>("SELECT RequestHash,AssignmentJson FROM dbo.TradingPromotion WHERE PromotionId=@Id", new { Id = approval.Review.RequestId }, t);
        if (saved is not null) { var retry = TradingPromotionPolicy.Retry(saved.RequestHash, Read<TradingStrategyAssignment>(saved.AssignmentJson), approval); t.Commit(); return retry; }
        var fresh = approval.Review.PreviousReviewPromotionId is Guid old
            ? await PreviousReview(c, t, old, account, token)
            : await Review(c, t, approval.Review.Evidence.PaperAccountId, approval.Review.Strategy.Family, account, token);
        TradingPromotionPolicy.Recheck(approval, fresh);
        DateTime now = DateTime.SpecifyKind(await c.ExecuteScalarAsync<DateTime>("SELECT SYSUTCDATETIME()", transaction: t), DateTimeKind.Utc);
        var assignment = new TradingStrategyAssignment(approval.Review.RequestId, account, approval.Review.Strategy, approval.Review.Evidence.PaperAccountId,
            approval.Review.RequestId, approval.Review.PriorPromotionId, now, approval.Reviewer, approval.Reason);
        string definition = Json(assignment.Strategy);
        await c.ExecuteAsync("""
            IF EXISTS(SELECT 1 FROM dbo.TradingStrategyVersion WHERE VersionId=@Version AND (DefinitionJson<>@Definition OR Fingerprint<>@Fingerprint))
                THROW 51502,'A complete strategy version cannot change in place.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.TradingStrategyVersion WHERE VersionId=@Version)
                INSERT dbo.TradingStrategyVersion(VersionId,StrategyId,DailyStrategyVersionId,Family,Fingerprint,DefinitionJson)
                VALUES(@Version,@Strategy,@Daily,@Family,@Fingerprint,@Definition);
            IF EXISTS(SELECT 1 FROM dbo.TradingPromotion WHERE AccountLabel=@Account AND EffectiveUtc>=@Now)
                THROW 51504,'The approval clock must advance beyond the prior boundary. Retry when the clock is corrected.',1;
            INSERT dbo.TradingPromotion(PromotionId,AccountLabel,StrategyVersionId,PaperAccountId,PriorPromotionId,AccountReportId,
                EffectiveUtc,Reviewer,Reason,RequestHash,ReviewJson,AssignmentJson)
                VALUES(@Id,@Account,@Version,@Paper,@Prior,@Report,@Now,@Reviewer,@Reason,@RequestHash,@Review,@Assignment);
            """, new
        {
            Version = assignment.Strategy.VersionId,
            Strategy = assignment.Strategy.StrategyId,
            Daily = assignment.Strategy.Daily.StrategyVersionId,
            Family = assignment.Strategy.Family.ToString(),
            assignment.Strategy.Fingerprint,
            Definition = definition,
            Id = assignment.PromotionId,
            Account = account,
            Paper = assignment.PaperAccountId,
            Prior = assignment.PriorPromotionId,
            Report = approval.Review.AccountReportId,
            Now = now,
            approval.Reviewer,
            approval.Reason,
            approval.RequestHash,
            Review = Json(approval.Review),
            Assignment = Json(assignment)
        }, t);
        t.Commit(); return assignment;
    }
    private sealed record Binding(string ModelSetJson, string ModelSetSha256);
    private sealed record ShadowSource(string PortfolioCode, string DisplayName, string Lens, int MaximumPositions);
    private sealed record StoredApproval(string RequestHash, string AssignmentJson);
}
