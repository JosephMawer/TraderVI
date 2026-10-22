#nullable enable
using Core.Calibration;
using Core.Db;
using Core.Trader.DelphiLive;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Durable FIFO research; it cannot schedule collection or reconstruct missed trading opportunities.</summary>
public sealed class IndependentResearchService
{
    private readonly EngineMarketCycleRepository cycles = new();
    private readonly DelphiLiveExperimentRepository experiments = new();
    private readonly DelphiLiveExperimentWorkflow protocol;
    private readonly DelphiLiveResearchCoordinator research;
    private readonly DelphiLiveSessionRepository sessions;
    private readonly DelphiLiveLedgerRepository ledger = new();
    private readonly ReviewedTsxSessionCalendar calendar;
    private readonly Func<DelphiLiveLease?> currentLease;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateOnly? settledThrough;
    private readonly IDelphiLiveNotifier notifier;
    public async Task DrainAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        gate.Release();
    }
    public async Task ExecuteOperatorAsync(Func<Task> command)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { await RequireIntegrityAsync().ConfigureAwait(false); await command().ConfigureAwait(false); settledThrough = null; }
        finally { gate.Release(); }
    }

    public IndependentResearchService(ReviewedTsxSessionCalendar calendar, Func<DelphiLiveLease?> currentLease,
        IDelphiLiveNotifier notifier)
    {
        this.calendar = calendar; this.currentLease = currentLease; this.notifier = notifier;
        var code = CalibrationProvenance.ResolveCode();
        using var assembly = File.OpenRead(typeof(IndependentResearchService).Assembly.Location);
        string identity = $"{code.Commit}/{code.WorkingTreeState}/CoreSha256:{Convert.ToHexString(SHA256.HashData(assembly))}";
        sessions = new(calendar, new DelphiLiveHoldingSource(ledger, new EnginePaperObservationStore()), code);
        protocol = new(experiments);
        research = new(experiments, experiments, new DelphiLiveResearchEvidenceRepository(calendar),
            sessions, ledger, calendar, new SystemDelphiLiveClock(), () => identity);
    }

    public async Task ConsumeAsync()
    {
        if (!await gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            await RequireIntegrityAsync().ConfigureAwait(false);
            var lease = currentLease(); if (lease is null) return;
            var frame = await cycles.ReadNextAsync("Research", newest: false).ConfigureAwait(false);
            if (frame is null) return;
            // Applicability comes from the original publication, never today's source/assignment.
            bool applicable = frame.Context.SourceRevision is null && frame.Context.ComparisonAccounts.Count == 0;
            if (applicable) await research.CheckpointAsync(frame.Context, frame.BarEndUtc, frame.Evaluations, lease).ConfigureAwait(false);
            await cycles.CompleteAsync("Research", frame.CycleId, applicable ? "ResearchRecorded" : "ResearchNotApplicable").ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task MaintainAsync()
    {
        if (!await gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            await RequireIntegrityAsync().ConfigureAwait(false);
            var lease = currentLease(); if (lease is null) return;
            DateTime now = DateTime.UtcNow;
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")));
            if (calendar.IsRegularSession(date) && now >= calendar.GetSessionBounds(date).OpenUtc &&
                now < calendar.GetSessionBounds(date).CloseUtc.AddMinutes(10)) return;
            if (await EngineSettingsRepository.HasLiveOverridesAsync()) return;
            if (await experiments.LoadAsync() is null)
            {
                var champion = (await ledger.GetPortfoliosForSessionAsync(date)).SingleOrDefault(p => p.Role == "OperationalChampion");
                if (champion is null) return;
                await protocol.InitializeAsync(champion.PortfolioId, champion.PolicyVersionId, now, lease);
            }
            bool complete = await research.RecoverAndMatureBatchAsync(date, lease, maximumSessions: 1).ConfigureAwait(false);
            settledThrough = complete ? date : null;
        }
        finally { gate.Release(); }
    }

    public async Task ApplyBoundaryAsync(DateOnly date, DateTime open, DelphiLiveLease lease, CancellationToken ct)
    {
        if (await EngineSettingsRepository.HasLiveOverridesAsync(ct)) return;
        // Collection never waits for archive work. An unprepared boundary stays pending; frozen sessions cannot be relabelled later.
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            if (settledThrough != date || await experiments.LoadAsync(ct) is null)
            {
                await notifier.NotifyAsync(new("Warning", "ResearchBoundaryPending",
                    "Research recovery is incomplete. Current assignments remain in effect; the pending experiment change waits for a prepared session."), ct);
                return;
            }
            if (await sessions.GetFrozenSessionAsync(date, ct) is null)
                await protocol.ApplySessionBoundaryAsync(date, open, DateTime.UtcNow, lease, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private static async Task RequireIntegrityAsync()
    {
        if (!(await new EngineIntegrityRepository().ReadAccessAsync(EngineIntegrityScope.Research, null).ConfigureAwait(false)).NewExposureAllowed)
            throw new InvalidOperationException("Dependent research is invalidated or paused for integrity recovery.");
    }
}
