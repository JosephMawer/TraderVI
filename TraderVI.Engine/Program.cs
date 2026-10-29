using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using TraderVI.WPF.Services;

namespace TraderVI.Engine;

internal static class Program
{
    // Construction/building never launches this host. Starting it is an explicit operator action.
    public static async Task<int> Main(string[] args)
    {
        if (args.Length is 3 or 4 && args[0] == "--control")
        {
            try { await EngineControlCommand.RunAsync(args[1], args[2], args.Length == 4 ? args[3] : null); return 0; }
            catch { return 1; }
        }
        if (args.Length != 1 || args[0] is not ("--run" or "--verify" or "--recover-real" or "--prepare-observations" or "--prepare-working-state" or "--prepare-real-observations")) return 64;
        bool recoveryMaintenance = args[0] == "--recover-real";
        bool verificationOnly = args[0] == "--verify" || recoveryMaintenance;
        try
        {
            CollectorIntradayRouting.ApplyIfConfigured(AppContext.BaseDirectory);
            using var owner = new EngineOwnershipLease(EnginePaths.OwnershipPath);
            if (args[0] == "--prepare-real-observations")
            {
                await new EngineRealObservationStore().QualifyExistingAsync().ConfigureAwait(false);
                return 0;
            }
            if (args[0] == "--prepare-working-state")
            {
                await new EnginePaperWorkingStore().QualifyExistingAsync().ConfigureAwait(false);
                return 0;
            }
            if (args[0] == "--prepare-observations")
            {
                await new EnginePaperObservationStore().QualifyExistingAsync().ConfigureAwait(false);
                return 0;
            }
            var calendarPath = Environment.GetEnvironmentVariable(DelphiLiveDesktopService.CalendarPathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(calendarPath)) return 65;
            var calendar = ReviewedTsxSessionCalendar.Load(calendarPath);
            using var sessionStore = new WindowsWealthsimpleSessionStore();
            var connection = new WealthsimpleConnection(sessionStore);
            var orderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Wealthsimple", "ManualOrders");
            using var journal = new ManualBuyFileJournal(orderPath, new WindowsManualBuyProtector());
            var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(orderPath, "execution-host.dat"), new WindowsManualBuyProtector());
            var continuity = new EngineContinuity(new EngineContinuityRepository(),
                new EngineContinuityFile(Path.Combine(EnginePaths.DirectoryPath, "continuity.dat"), new WindowsManualBuyProtector()));
            var broker = new EngineBrokerService(connection, journal, sessionStore, grants, calendar,
                async (saved, ct) =>
                {
                    await continuity.VerifyAsync(ct).ConfigureAwait(false);
                    await IndependentExecutionReadiness.VerifyAsync(saved, ct).ConfigureAwait(false);
                }, async (binding, ct) =>
                {
                    await continuity.VerifyAsync(ct).ConfigureAwait(false);
                    return (await new EngineIntegrityRepository().ReadAccessAsync(EngineIntegrityScope.Broker, binding, ct).ConfigureAwait(false)).ProtectionAllowed;
                }, verificationOnly: verificationOnly);
            var market = new IndependentMarketService(calendar);
            var paper = new IndependentPaperService(calendar, () => market.CurrentLease, market);
            var research = new IndependentResearchService(calendar, () => market.CurrentLease, market);
            var auxiliary = new IndependentAuxiliaryPaperService(calendar, market);
            var audits = new IndependentAuditService(calendar, market);
            var liveCommands = new DelphiLiveDesktopService { OperatorExecutor = paper.ExecuteOperatorAsync, DeferResearchRecovery = true };
            bool stopping = false;
            market.ApplySessionBoundaryAsync = research.ApplyBoundaryAsync;
            var engine = new IndependentEngine(recoveryMaintenance
                ? EngineRecoveryMaintenance.Work(continuity.TickAsync, audits.TickRealRecoveryAsync) : [
                new("Engine continuity", TimeSpan.FromSeconds(10), continuity.TickAsync),
                new("Connection", TimeSpan.FromSeconds(10), broker.PollAuthenticationAsync),
                new("Collection ownership", TimeSpan.FromSeconds(30), market.MaintainLeaseAsync),
                new("Market inputs", TimeSpan.FromSeconds(30), market.TickAsync),
                new("Real trading", TimeSpan.FromSeconds(1), broker.PollExecutionAsync),
                new("Paper protection", TimeSpan.FromSeconds(10), paper.ProtectAsync),
                new("Paper inputs", TimeSpan.FromSeconds(10), paper.ConsumeAsync),
                new("Research inputs", TimeSpan.FromSeconds(30), research.ConsumeAsync),
                new("Research history", TimeSpan.FromSeconds(30), research.MaintainAsync),
                new("Tracked paper", TimeSpan.FromSeconds(30), auxiliary.TrackedTickAsync),
                new("Shadow paper", TimeSpan.FromSeconds(30), auxiliary.ShadowTickAsync),
                new("Shadow protection", TimeSpan.FromSeconds(10), auxiliary.ShadowProtectionAsync),
                new("AI research", TimeSpan.FromSeconds(30), auxiliary.CheckAiAsync),
                new("Historical audits", TimeSpan.FromSeconds(1), audits.TickAsync)
            ], async () =>
            {
                if (verificationOnly) await broker.StartAsync().ConfigureAwait(false); // Persist disabled grant before any worker starts.
                await continuity.StartAsync().ConfigureAwait(false);
                if (!verificationOnly) await broker.StartAsync().ConfigureAwait(false);
                if (!recoveryMaintenance) await auxiliary.StartAsync().ConfigureAwait(false);
            }, async () =>
            {
                try
                {
                    await research.DrainAsync().ConfigureAwait(false);
                    await audits.DrainAsync().ConfigureAwait(false);
                    await paper.DrainAsync().ConfigureAwait(false);
                    await auxiliary.CloseAsync().ConfigureAwait(false);
                    await market.StopAsync().ConfigureAwait(false);
                }
                finally { await broker.CloseAsync().ConfigureAwait(false); }
                await continuity.CloseAsync().ConfigureAwait(false);
            });
            using var stopAccepting = new CancellationTokenSource();
            var server = new EngineRpcServer(EnginePaths.PipeName, async request =>
            {
                if (recoveryMaintenance) EngineRecoveryMaintenance.RequireOperation(request.Operation);
                if (request.Operation == EngineOperation.Status) return engine.ReadStatus() with
                    { Notices = market.Notices, VerificationOnly = verificationOnly, RecoveryMaintenance = recoveryMaintenance };
                if (request.Operation == EngineOperation.Stop)
                {
                    stopping = true; broker.BeginStop(); paper.BeginStop(); auxiliary.BeginStop(); audits.BeginStop();
                    await engine.StopAsync().ConfigureAwait(false);
                    return engine.ReadStatus();
                }
                if (request.Operation == EngineOperation.PollTrackedPaper) return await auxiliary.PollTrackedAsync().ConfigureAwait(false);
                if (request.Operation == EngineOperation.PollShadowPaper) return await auxiliary.PollShadowAsync().ConfigureAwait(false);
                if (request.Operation == EngineOperation.IntegrityStatus) return await new EngineIntegrityRepository().ReadStatusAsync().ConfigureAwait(false);
                if (request.Operation == EngineOperation.ResumePaperPendingRecovery)
                {
                    if (stopping || verificationOnly) throw new InvalidOperationException("Temporary Paper continuation requires the normal running engine.");
                    var financial = (Core.Trader.Promotion.BrokerExecutionHostStatus)await broker.DispatchAsync(
                        request with { Operation = EngineOperation.ExecutionStatus }).ConfigureAwait(false);
                    if (financial.State != "Off") throw new InvalidOperationException("Leave Real execution Off before requesting temporary Paper continuation.");
                    await continuity.VerifyAsync().ConfigureAwait(false);
                    var observation = await new EngineContinuityRepository().ReadAsync(CancellationToken.None).ConfigureAwait(false);
                    var status = await new EngineIntegrityRepository().ReadStatusAsync().ConfigureAwait(false);
                    var command = request.Payload.Deserialize<EngineResumePaperPendingRecovery>() ?? throw new ArgumentException("Missing Paper continuation request.");
                    var permission = EnginePaperRecoveryContinuation.Prepare(command, status, observation.Database, DateTime.UtcNow);
                    EnginePaperRecoveryContinuationGate.Activate(permission);
                    await market.NotifyAsync(new("Warning", "PaperRecoveryContinuation",
                        $"Temporary Paper continuation until {permission.ExpiresUtc:O}. Historical integrity remains unverified; Real and research holds remain.")).ConfigureAwait(false);
                    return permission;
                }
                if (request.Operation is EngineOperation.VerifyRecovery or EngineOperation.VerifyRealRecovery or EngineOperation.VerifyPaperRecovery)
                {
                    await continuity.VerifyAsync().ConfigureAwait(false);
                    return await audits.StartRecoveryAsync(request.Operation switch { EngineOperation.VerifyRealRecovery => EngineRecoveryScope.Real,
                        EngineOperation.VerifyPaperRecovery => EngineRecoveryScope.Paper, _ => EngineRecoveryScope.All }).ConfigureAwait(false);
                }
                if (request.Operation == EngineOperation.ClearIntegrityHolds)
                {
                    var clear = request.Payload.Deserialize<EngineClearIntegrityHolds>() ?? throw new ArgumentException("Missing recovery command.");
                    if (recoveryMaintenance) EngineRecoveryMaintenance.RequireClearScope(clear.Scope);
                    await continuity.VerifyAsync().ConfigureAwait(false);
                    await audits.ClearAsync(clear.RecoveryRunId, clear.Incidents, clear.Reason, clear.Scope).ConfigureAwait(false);
                    return true;
                }
                if (request.Operation == EngineOperation.LiveCommand)
                {
                    if (stopping) throw new InvalidOperationException("Engine is stopping.");
                    var command = request.Payload.Deserialize<EngineLiveCommand>() ?? throw new InvalidOperationException("Missing paper command.");
                    await research.ExecuteOperatorAsync(async () =>
                    {
                        if (stopping) throw new InvalidOperationException("Engine is stopping.");
                        switch (command.Kind)
                        {
                            case EngineLiveCommandKind.Activate: await liveCommands.ActivateAsync(command.Capital, command.Currency, command.Reason); break;
                            case EngineLiveCommandKind.Discovery: await liveCommands.ScheduleDiscoveryAsync(command.Family, command.Values ?? [], command.Capital, command.Currency, command.Reason); break;
                            case EngineLiveCommandKind.Untouched: await liveCommands.ScheduleUntouchedAsync(command.Id ?? throw new ArgumentException(), command.Reason); break;
                            case EngineLiveCommandKind.Promotion: await liveCommands.ApprovePromotionAsync(command.Reason); break;
                            case EngineLiveCommandKind.MeasurementDefect: await liveCommands.RecordMeasurementDefectAsync(command.Reason); break;
                            case EngineLiveCommandKind.CapitalReview: await liveCommands.ResumeCapitalReviewAsync(command.Id ?? throw new ArgumentException(), command.Reason); break;
                            case EngineLiveCommandKind.CorporateAction: await liveCommands.RecordCorporateActionAsync(command.Symbol ?? throw new ArgumentException(), command.From ?? throw new ArgumentException(), command.Through ?? throw new ArgumentException(), command.Reason); break;
                            default: throw new ArgumentException("Unknown paper command.");
                        }
                    }).ConfigureAwait(false);
                    return true;
                }
                if (request.Operation is EngineOperation.PrepareBuy or EngineOperation.SubmitBuy or EngineOperation.SellAll or EngineOperation.EnableExecution)
                    await continuity.VerifyAsync().ConfigureAwait(false);
                return await broker.DispatchAsync(request).ConfigureAwait(false);
            });
            var serving = server.RunAsync(stopAccepting.Token);
            try
            {
                await engine.StartAsync().ConfigureAwait(false);
                // Losing the control listener is an engine failure, not an invisible unmanaged lifetime.
                var completed = await Task.WhenAny(engine.Completion, serving).ConfigureAwait(false);
                if (completed == serving)
                {
                    await serving.ConfigureAwait(false);
                    throw new InvalidOperationException("The engine control listener stopped unexpectedly.");
                }
                await engine.Completion.ConfigureAwait(false);
                return 0;
            }
            finally
            {
                stopping = true; broker.BeginStop(); paper.BeginStop(); auxiliary.BeginStop(); audits.BeginStop();
                await engine.StopAsync().ConfigureAwait(false);
                stopAccepting.Cancel();
                await serving.ConfigureAwait(false);
            }
        }
        catch { return 1; } // Never write credentials, account contents or SQL connection details to console/logs.
    }
}
