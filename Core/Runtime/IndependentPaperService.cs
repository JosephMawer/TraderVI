#nullable enable
using Core.Calibration;
using Core.Db;
using Core.Trader.DelphiLive;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Paper execution consumes durable shared inputs. Historical research has no place in this lane.</summary>
public sealed class IndependentPaperService
{
    private readonly DelphiLiveLedgerRepository ledger = new(workingState: true);
    private readonly EngineMarketCycleRepository cycles = new();
    private readonly DelphiLiveMonitorWorkflow workflow;
    private readonly DelphiLiveActionWorkflow actions;
    private readonly Func<DelphiLiveLease?> currentLease;
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile bool closing;
    public void BeginStop() => closing = true;
    public async Task DrainAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        gate.Release();
    }
    public async Task ExecuteOperatorAsync(Func<DelphiLiveLease, CancellationToken, Task> command, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (closing) throw new InvalidOperationException("Engine is stopping; paper commands are no longer admitted.");
            if (!(await new EngineIntegrityRepository().ReadAccessAsync(EngineIntegrityScope.Paper, null, ct).ConfigureAwait(false)).NewExposureAllowed)
                throw new InvalidOperationException("Paper commands are paused for integrity recovery.");
            await command(currentLease() ?? throw new InvalidOperationException("The engine does not own the collection lease."), ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public IndependentPaperService(ReviewedTsxSessionCalendar calendar, Func<DelphiLiveLease?> currentLease,
        IDelphiLiveNotifier notifier)
    {
        this.currentLease = currentLease;
        var code = CalibrationProvenance.ResolveCode();
        var holdings = new DelphiLiveHoldingSource(ledger, new EnginePaperObservationStore());
        var sessions = new DelphiLiveSessionRepository(calendar, holdings, code);
        var clock = new SystemDelphiLiveClock();
        var source = new TmxDelphiLiveMarketDataSource();
        actions = new(ledger, source, clock);
        workflow = new(clock, calendar, sessions, new DelphiLiveEvaluationRepository(independentInputs: true),
            new DelphiLiveCollectionRepository(code), ledger, holdings, source, notifier);
        workflow.GetCorporateActionSymbolsAsync = (date, ct) => new DelphiLiveExperimentRepository().ReadAffectedSymbolsAsync(date, date, ct);
        workflow.IsDailyStrategyCurrentAsync = async (strategy, _) => strategy is Guid id &&
            id == await new TradingControlRepository().CurrentDailyStrategyAsync();
    }

    public Task ProtectAsync() => WithLeaseAsync(async lease =>
    {
        await ApplyControlsAsync(lease).ConfigureAwait(false);
        await workflow.ProtectCurrentPaperAsync(lease).ConfigureAwait(false);
    });

    public Task ConsumeAsync() => WithLeaseAsync(async lease =>
    {
        await ApplyControlsAsync(lease).ConfigureAwait(false);
        var frame = await cycles.ReadNextAsync("Paper", newest: true).ConfigureAwait(false);
        if (frame is null) return;
        string outcome = await cycles.HasConflictAsync(frame).ConfigureAwait(false) ? "EvidenceConflict" :
            await workflow.ConsumeSharedCheckpointAsync(frame, lease).ConfigureAwait(false);
        await cycles.CompleteAsync("Paper", frame.CycleId, outcome).ConfigureAwait(false);
    });

    private async Task ApplyControlsAsync(DelphiLiveLease lease)
    {
        var controls = new TradingControlRepository();
        bool paused = await controls.IsPausedAsync(EngineStrategySettings.Live).ConfigureAwait(false) ||
            !(await new EngineIntegrityRepository().ReadAccessAsync(EngineIntegrityScope.Paper, null).ConfigureAwait(false)).NewExposureAllowed;
        workflow.NewBuysPaused = paused;
        if (paused)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")));
            foreach (var portfolio in await ledger.GetPortfoliosForSessionAsync(today).ConfigureAwait(false))
                await actions.CancelOperatorPausedBuysAsync(portfolio.PortfolioId, lease).ConfigureAwait(false);
        }
        foreach (var request in await controls.ExitRequestsAsync(EngineStrategySettings.Live).ConfigureAwait(false))
            await actions.QueueOperatorExitAsync(request, lease).ConfigureAwait(false);
    }

    private async Task WithLeaseAsync(Func<DelphiLiveLease, Task> action)
    {
        // Paper actions serialize against other paper actions, never against real execution or research.
        if (!await gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            var integrity = await new EngineIntegrityRepository().ReadAccessAsync(EngineIntegrityScope.Paper, null).ConfigureAwait(false);
            if (!integrity.ProtectionAllowed) throw new InvalidOperationException("Paper execution is unavailable under the integrity hold.");
            await using var settings = await EngineSettingsLease.AcquireAsync().ConfigureAwait(false);
            var lease = currentLease();
            if (!closing && lease is not null) await action(lease).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
