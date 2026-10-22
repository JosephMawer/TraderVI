#nullable enable
using Core.Calibration;
using Core.Db;
using Core.Trader.DelphiLive;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Shared collection and deterministic measurements; no paper actions, history reads or research callback.</summary>
public sealed class IndependentMarketService : IDelphiLiveNotifier
{
    private readonly DelphiLiveMonitorWorkflow workflow;
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
    }
    public async Task TickAsync()
    {
        await using var settings = await EngineSettingsLease.AcquireAsync();
        await workflow.TickAsync().ConfigureAwait(false);
    }
    public Task StopAsync() => workflow.StopAsync();
    public Task NotifyAsync(DelphiLiveNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
