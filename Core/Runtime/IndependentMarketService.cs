#nullable enable
using Core.Calibration;
using Core.Db;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Shared collection and deterministic measurements; no paper actions, history reads or research callback.</summary>
public sealed class IndependentMarketService : IDelphiLiveNotifier
{
    private readonly DelphiLiveMonitorWorkflow workflow;
    private readonly object noticeGate = new();
    private ImmutableArray<EngineNotice> notices = [];
    public ImmutableArray<EngineNotice> Notices { get { lock (noticeGate) return notices; } }
    public IndependentMarketService(ReviewedTsxSessionCalendar calendar)
    {
        var code = CalibrationProvenance.ResolveCode();
        var observations = new EnginePaperObservationStore();
        var ledger = new DelphiLiveLedgerRepository();
        var holdings = new DelphiLiveHoldingSource(ledger, observations);
        var sessions = new DelphiLiveSessionRepository(calendar, holdings, code);
        workflow = new(new SystemDelphiLiveClock(), calendar, sessions, new DelphiLiveEvaluationRepository(independentInputs: true),
            new DelphiLiveCollectionRepository(code), ledger, holdings, new TmxDelphiLiveMarketDataSource(), this,
            hostTickCadence: TimeSpan.FromSeconds(30), observationPortfolios: observations.ReadAsync);
        workflow.PublishSharedCheckpointAsync = new EngineMarketCycleRepository().PublishAsync;
    }
    public DelphiLiveLease? CurrentLease => workflow.CurrentSharedLease;
    public Task MaintainLeaseAsync() => workflow.MaintainSharedLeaseAsync();
    public Func<DateOnly, DateTime, DelphiLiveLease, CancellationToken, Task>? ApplySessionBoundaryAsync
    { set => workflow.ApplySessionBoundaryAsync = value; }
    public async Task TickAsync()
    {
        if (!(await new EngineIntegrityRepository().ReadAccessAsync(EngineIntegrityScope.Global, null).ConfigureAwait(false)).ProtectionAllowed)
            throw new InvalidOperationException("Shared input work is paused for integrity recovery.");
        await using var settings = await EngineSettingsLease.AcquireAsync();
        await workflow.TickAsync().ConfigureAwait(false);
    }
    public Task StopAsync() => workflow.StopAsync();
    public Task NotifyAsync(DelphiLiveNotification notification, CancellationToken cancellationToken = default)
    {
        lock (noticeGate)
        {
            var now = DateTimeOffset.UtcNow;
            notices = notices.Where(n => n.ObservedAt >= now.AddDays(-1) && n.Code != notification.Code)
                .TakeLast(63).Append(new(now, notification.Severity, notification.Code, notification.Message)).ToImmutableArray();
        }
        return Task.CompletedTask;
    }
}
