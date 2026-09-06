#nullable enable
using Core.Calibration;
using Core.Db;
using Core.Trader.DelphiLive;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>
/// Composition for the initial desktop host. Reading status never starts the
/// monitor; activation is a separate, explicit operator command.
/// </summary>
public sealed class DelphiLiveDesktopService : SQLBase, IDelphiLiveNotifier
{
    public const string CalendarPathEnvironmentVariable = "TRADERVI_TSX_CALENDAR_PATH";
    private readonly DelphiLiveLedgerRepository ledger = new();
    private readonly DelphiLiveEvaluationRepository evaluations = new();
    private readonly DelphiLiveExperimentRepository experiments = new();
    private readonly DelphiLiveExperimentWorkflow experimentWorkflow;
    private readonly ReviewedTsxSessionCalendar? calendar;
    private readonly DelphiLiveSessionRepository? sessions;
    private readonly DelphiLiveMonitorWorkflow? workflow;
    private readonly DelphiLiveActionWorkflow? actions;
    private readonly DelphiLiveResearchCoordinator? research;
    private readonly CodeProvenance code;
    private readonly string codeIdentity;
    private readonly List<string> notifications = [];
    private DateOnly notificationDate;
    private DelphiLiveResearchPresentation? cachedResearch;
    private DateOnly? researchFrom;
    private DateOnly? researchThrough;
    private DateTime? researchReadUtc;
    private long? scoredProtocolRevision;
    private DelphiLivePromotionScore? cachedPromotionScore;
    public bool CalendarAvailable => calendar is not null;
    public string? CalendarWarning { get; }

    public DelphiLiveDesktopService()
    {
        experimentWorkflow = new(experiments);
        code = CalibrationProvenance.ResolveCode();
        using (var assembly = File.OpenRead(typeof(DelphiLiveDesktopService).Assembly.Location))
            codeIdentity = $"{code.Commit}/{code.WorkingTreeState}/CoreSha256:{Convert.ToHexString(SHA256.HashData(assembly))}";
        string? path = Environment.GetEnvironmentVariable(CalendarPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            CalendarWarning = "A reviewed official TSX calendar file is required before activation. Set TRADERVI_TSX_CALENDAR_PATH to its local path.";
            return;
        }
        try { calendar = ReviewedTsxSessionCalendar.Load(path); }
        catch (Exception exception) when (exception is IOException or ArgumentException or System.Text.Json.JsonException)
        {
            CalendarWarning = "The reviewed TSX calendar could not be validated. Activation remains unavailable.";
            return;
        }
        var holdings = new DelphiLiveHoldingSource(ledger);
        sessions = new(calendar, holdings, code);
        var collection = new DelphiLiveCollectionRepository(code);
        var clock = new SystemDelphiLiveClock();
        var source = new TmxDelphiLiveMarketDataSource();
        actions = new(ledger, source, clock);
        workflow = new(clock, calendar, sessions, evaluations, collection,
            ledger, holdings, source, this, hostTickCadence: TimeSpan.FromSeconds(30));
        research = new(experiments, experiments, new DelphiLiveResearchEvidenceRepository(calendar),
            sessions, ledger, calendar, clock, () => codeIdentity);
        workflow.ApplySessionBoundaryAsync = async (date, open, lease, token) =>
        {
            var current = await experiments.LoadAsync(token);
            if (current is null)
            {
                var operational = (await ledger.GetPortfoliosForSessionAsync(date, token))
                    .Single(p => p.Role == "OperationalChampion");
                await experimentWorkflow.InitializeAsync(operational.PortfolioId, operational.PolicyVersionId, clock.UtcNow, lease, token);
            }
            // Settle old sessions under their original phase before applying a
            // new boundary; a restart cannot move discovery evidence into confirmation.
            await research.RecoverAndMatureAsync(date, lease, token);
            if (await sessions.GetFrozenSessionAsync(date, token) is null)
                await experimentWorkflow.ApplySessionBoundaryAsync(date, open, clock.UtcNow, lease, token);
        };
        workflow.PersistResearchCheckpointAsync = research.CheckpointAsync;
        workflow.PersistSessionResearchAsync = research.SessionClosedAsync;
        workflow.GetCorporateActionSymbolsAsync = (date, token) => experiments.ReadAffectedSymbolsAsync(date, date, token);
    }

    public async Task<bool> HasSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition("""
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.DelphiLivePolicyVersion',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveSession',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.IntradayCollectionReceipt',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLivePortfolioLedger',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveEvaluation',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveExpectedResearchSlot',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveRankingCheckpoint',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveResearchOutcomeRevision',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveExperimentProtocol',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveExperimentRevision',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveExperimentEvent',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveCorporateActionAudit',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.DelphiLiveResearchSessionReview',N'U') IS NOT NULL THEN 1 ELSE 0 END AS BIT);
""", cancellationToken: cancellationToken));
    }

    public async Task<DelphiLiveRuntimeSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        DateOnly date = Today();
        ResetNotificationsFor(date);
        var warnings = new List<string>(notifications);
        if (CalendarWarning is not null) warnings.Add(CalendarWarning);
        if (!await HasSchemaAsync(cancellationToken))
            return new("Schema not installed · inactive", date, null, null, [], [], warnings);
        await using var connection = new SqlConnection(ConnectionString);
        var session = await connection.QuerySingleOrDefaultAsync<DesktopSessionRow>(new CommandDefinition(
            "SELECT SessionId,SessionState,CoverageState,HostGapObserved FROM dbo.DelphiLiveSession WHERE TradingDate=@Date;",
            new { Date = date.ToDateTime(TimeOnly.MinValue) }, cancellationToken: cancellationToken));
        Guid? sessionId = session?.SessionId;
        var latest = sessionId is Guid id ? await evaluations.GetLatestSnapshotAsync(id, cancellationToken) : [];
        var portfolios = await ledger.GetPortfoliosForSessionAsync(date, cancellationToken);
        var queued = await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.DelphiLivePolicyAssignment WHERE PolicyRole=N'OperationalChampion' AND CancelledUtc IS NULL AND EndExclusiveTradingDate IS NULL;",
            cancellationToken: cancellationToken));
        string status = portfolios.Count == 0 ? queued > 0 ? "Activation queued for next session" : "Inactive" :
            session is null ? "Awaiting regular session" : $"{session.SessionState} · coverage {session.CoverageState}";
        if (session?.HostGapObserved == true) warnings.Add("This session contains a host coverage gap and cannot count as a clean cohort.");
        return await WithExperimentAsync(new(status, date, sessionId,
            latest.Count == 0 ? null : latest.Max(e => e.Input.BarEndUtc), latest, portfolios, warnings), cancellationToken);
    }

    public async Task<DelphiLiveRuntimeSnapshot> TickAsync(CancellationToken cancellationToken = default)
    {
        ResetNotificationsFor(Today());
        if (workflow is null || !await HasSchemaAsync(cancellationToken)) return await SnapshotAsync(cancellationToken);
        var result = await workflow.TickAsync(cancellationToken);
        if (result.Status == "Inactive") return await SnapshotAsync(cancellationToken);
        return await WithExperimentAsync(result with { Warnings = result.Warnings.Concat(notifications).Distinct().ToArray() }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => workflow?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task ActivateAsync(decimal capital, string currency, string reason, CancellationToken cancellationToken = default)
    {
        if (calendar is null || sessions is null) throw new InvalidOperationException(CalendarWarning);
        if (!await HasSchemaAsync(cancellationToken)) throw new InvalidOperationException("The reviewed Delphi Live migrations are required before activation.");
        if (capital <= 0m || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Positive explicit simulation capital and an activation reason are required.");
        DateTime now = DateTime.UtcNow;
        DateOnly date = Today();
        DateOnly next = calendar.IsRegularSession(date) && now < calendar.GetSessionBounds(date).OpenUtc ? date : calendar.GetNextSession(date);
        var policy = await sessions.GetPolicyAsync(DelphiLivePolicyDefinition.Version1.PolicyVersionId, cancellationToken);
        await ledger.CreateGenerationAsync(new(Guid.NewGuid(), Guid.NewGuid(), policy.PolicyVersionId,
            "OperationalChampion", null, capital, currency, next, calendar.GetSessionBounds(next).OpenUtc,
            now, Environment.UserName, reason), cancellationToken);
    }

    public Task NotifyAsync(DelphiLiveNotification notification, CancellationToken cancellationToken = default)
    {
        ResetNotificationsFor(Today());
        string text = $"{notification.Severity}: {notification.Symbol} {notification.Code} · {notification.Message}";
        if (!notifications.Contains(text)) notifications.Add(text);
        return Task.CompletedTask;
    }

    public Task ScheduleDiscoveryAsync(DelphiLiveHypothesisFamily family, IReadOnlyList<decimal> selectedValues,
        decimal capital, string currency, string reason, CancellationToken cancellationToken = default) =>
        ExecuteOperatorAsync(async (lease, token) =>
        {
            var current = await RequiredExperimentAsync(token);
            var champion = await sessions!.GetPolicyAsync(current.ChampionPolicyVersionId, token);
            if (selectedValues.Count is < 1 or > 2 || selectedValues.Distinct().Count() != selectedValues.Count)
                throw new ArgumentException("Select one or two distinct predeclared values from one hypothesis family.");
            var policies = new Dictionary<Guid, DelphiLivePolicyDefinition> { [champion.PolicyVersionId] = champion };
            foreach (decimal selected in selectedValues)
            {
                var challenger = family switch
                {
                    DelphiLiveHypothesisFamily.RawMoveThreshold => champion with { PolicyVersionId = Guid.NewGuid(), SelectedRawMoveThreshold = selected },
                    DelphiLiveHypothesisFamily.RelativeDeadband => champion with { PolicyVersionId = Guid.NewGuid(), SelectedExcessMoveThreshold = selected },
                    DelphiLiveHypothesisFamily.VolatilityRuler when selected == decimal.Truncate(selected) =>
                        champion with { PolicyVersionId = Guid.NewGuid(), SelectedRulerSessions = checked((int)selected) },
                    _ => throw new ArgumentException("The selected hypothesis family or value is unsupported.")
                };
                DelphiLiveExperimentPolicy.ValidateOneFamily(champion, challenger, family);
                policies.Add(challenger.PolicyVersionId, challenger);
            }
            var definition = new DelphiLiveExperimentDefinition(Guid.NewGuid(), champion.PolicyVersionId,
                policies.Keys.Where(id => id != champion.PolicyVersionId).ToImmutableArray(), family,
                capital, currency.Trim().ToUpperInvariant(), codeIdentity);
            DelphiLiveExperimentPolicy.ValidateDefinition(definition, policies);
            var command = BoundaryCommand("StartDiscovery", definition, null, reason);
            foreach (var contender in policies.Values.Where(p => p.PolicyVersionId != champion.PolicyVersionId))
                await experiments.RegisterPolicyAsync(contender, "ADR-0053", lease, token);
            await experimentWorkflow.ScheduleDiscoveryAsync(command, policies, lease, token);
        }, cancellationToken);

    public Task ScheduleUntouchedAsync(Guid challengerId, string reason, CancellationToken cancellationToken = default) =>
        ExecuteOperatorAsync(async (lease, token) =>
        {
            var current = await RequiredExperimentAsync(token);
            var definition = current.Definition ?? throw new InvalidOperationException("No discovery experiment exists.");
            await experimentWorkflow.ScheduleUntouchedAsync(BoundaryCommand("StartUntouched", definition, challengerId, reason), lease, token);
        }, cancellationToken);

    public Task ApprovePromotionAsync(string reason, CancellationToken cancellationToken = default) =>
        ExecuteOperatorAsync(async (lease, token) =>
        {
            var current = await RequiredExperimentAsync(token);
            var definition = current.Definition ?? throw new InvalidOperationException("No untouched experiment exists.");
            var policy = await sessions!.GetPolicyAsync(current.ChampionPolicyVersionId, token);
            await experimentWorkflow.ApprovePromotionAsync(
                BoundaryCommand("Promote", definition, current.SelectedChallenger, reason), policy, lease, token);
        }, cancellationToken);

    public Task RecordMeasurementDefectAsync(string reason, CancellationToken cancellationToken = default) =>
        ExecuteOperatorAsync(async (lease, token) =>
            await experimentWorkflow.RecordMeasurementDefectAsync(reason, DateTime.UtcNow, lease, token), cancellationToken);

    public Task ResumeCapitalReviewAsync(Guid portfolioId, string reason, CancellationToken cancellationToken = default) =>
        ExecuteOperatorAsync(async (lease, token) =>
        {
            var portfolio = await ledger.LoadPortfolioAsync(portfolioId, token)
                ?? throw new InvalidOperationException("The selected portfolio does not exist.");
            var latest = portfolio.Marks.LastOrDefault();
            if (latest?.Complete != true || latest.Nav is not decimal nav)
                throw new InvalidOperationException("Capital review requires the latest checkpoint to have a complete, exact NAV.");
            await actions!.ResumeCapitalReviewAsync(portfolioId, nav, Environment.UserName, reason, lease, token);
        }, cancellationToken);

    public Task RecordCorporateActionAsync(string symbol, DateOnly affectedFrom, DateOnly affectedThrough,
        string reason, CancellationToken cancellationToken = default) =>
        ExecuteOperatorAsync(async (lease, token) =>
        {
            await experiments.RecordCorporateActionAsync(new(Guid.NewGuid(), symbol.Trim().ToUpperInvariant(),
                affectedFrom, affectedThrough, DateTime.UtcNow, Environment.UserName, reason), lease, token);
            await research!.RecoverAndMatureAsync(Today(), lease, token);
            cachedResearch = null;
            researchReadUtc = null;
        }, cancellationToken);

    public async Task<DelphiLiveResearchPresentation> LoadResearchAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default)
    {
        if (through < from) throw new ArgumentException("The research date range is reversed.");
        if (through.DayNumber - from.DayNumber > 365)
            throw new ArgumentOutOfRangeException(nameof(through), "Choose at most 366 calendar dates per research report.");
        if (research is null) throw new InvalidOperationException(CalendarWarning);
        if (!await HasSchemaAsync(cancellationToken)) throw new InvalidOperationException("Delphi Live research storage is unavailable.");
        // Report aggregation can span many saved sessions. Keep its I/O and
        // CPU continuation off WPF's dispatcher so scheduled monitoring still ticks.
        var result = await Task.Run(() => research.ReadPresentationAsync(from, through, cancellationToken), cancellationToken);
        cachedResearch = result;
        researchFrom = from;
        researchThrough = through;
        researchReadUtc = DateTime.UtcNow;
        return result;
    }

    private async Task ExecuteOperatorAsync(Func<DelphiLiveLease, CancellationToken, Task> command, CancellationToken token)
    {
        if (workflow is null || calendar is null || sessions is null) throw new InvalidOperationException(CalendarWarning);
        if (!await HasSchemaAsync(token)) throw new InvalidOperationException("The reviewed Delphi Live migrations are required before operator commands.");
        await workflow.ExecuteOperatorCommandAsync(command, token);
    }

    private async Task<DelphiLiveExperimentState> RequiredExperimentAsync(CancellationToken token) =>
        await experiments.LoadAsync(token) ?? throw new InvalidOperationException("The activated champion has not started its engineering shakedown.");

    private DelphiLiveExperimentBoundaryPlan BoundaryCommand(string kind, DelphiLiveExperimentDefinition definition,
        Guid? challenger, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An explicit operator reason is required.");
        DateTime now = DateTime.UtcNow;
        DateOnly today = Today();
        DateOnly next = calendar!.IsRegularSession(today) && now < calendar.GetSessionBounds(today).OpenUtc
            ? today : calendar.GetNextSession(today);
        return new(Guid.NewGuid(), kind, definition, next, calendar.GetSessionBounds(next).OpenUtc,
            now, Environment.UserName, reason.Trim(), challenger, null);
    }

    private async Task<DelphiLiveRuntimeSnapshot> WithExperimentAsync(DelphiLiveRuntimeSnapshot snapshot, CancellationToken token)
    {
        var state = await experiments.LoadAsync(token);
        Guid? championId = state?.ChampionPolicyVersionId ?? snapshot.Portfolios.FirstOrDefault(p => p.Role == "OperationalChampion")?.PolicyVersionId;
        DelphiLivePolicyDefinition? champion = championId is Guid id && sessions is not null
            ? await sessions.GetPolicyAsync(id, token) : null;
        if (scoredProtocolRevision != state?.Revision)
        {
            cachedPromotionScore = state?.Definition is { } definition && state.SelectedChallenger is Guid selected && champion is not null
                ? DelphiLiveExperimentPolicy.Score(definition, selected, state.DiscoveryCohorts, state.UntouchedCohorts, champion) : null;
            scoredProtocolRevision = state?.Revision;
        }
        return snapshot with { Experiment = state, PromotionScore = cachedPromotionScore, ChampionPolicy = champion,
            Research = cachedResearch, ResearchFrom = researchFrom, ResearchThrough = researchThrough, ResearchReadUtc = researchReadUtc };
    }

    private static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ReviewedTsxSessionCalendar.Toronto));

    private sealed record DesktopSessionRow(Guid SessionId, string SessionState, string CoverageState, bool HostGapObserved);

    private void ResetNotificationsFor(DateOnly date)
    {
        if (notificationDate == date) return;
        notifications.Clear();
        notificationDate = date;
    }
}
