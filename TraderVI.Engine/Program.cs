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
#if COMPACT_RESTART_CANDIDATE
        // This binary can only run on its marked rehearsal LocalDB. No live host fallback or RPC.
        if (args is ["--candidate-capabilities"]) { Console.WriteLine("compact-localdb-only-v1"); return 0; }
        return args is ["--run"] ? await IsolatedCompactHost.RunAsync() : 64;
#else
        if (args is ["--reconcile-closed-sale", var packet])
            return await SettledSaleMaintenance.RunAsync(packet);
        if (args is ["--sign-in"])
        {
            try { await RunSignInAsync(); return 0; }
            catch { return 1; }
        }
        if (args is ["--run"] && Environment.GetEnvironmentVariable("TRADERVI_CANDIDATE_REHEARSAL") != null) return 64;
        if (args.Length == 2 && args[0] is ("--stage-compact-rehearsal" or "--verify-compact-restart" or "--compare-compact-rehearsal"))
            return Guid.TryParseExact(args[1], "N", out var token) && token != Guid.Empty
                ? await CompactRestartCommand.RunAsync(args[0], token) : 64;
        if (args.Length is 3 or 4 && args[0] == "--control")
        {
            try { await EngineControlCommand.RunAsync(args[1], args[2], args.Length == 4 ? args[3] : null); return 0; }
            catch { return 1; }
        }
        bool prepareInstalled = args is ["--prepare-compact-book", _];
        if ((!prepareInstalled && args.Length != 1) || args[0] is not ("--run" or "--review-build" or "--verify" or "--recover-real" or "--prepare-observations" or "--prepare-working-state" or "--prepare-real-observations" or "--prepare-compact-book")) return 64;
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
            var continuityStore = new EngineContinuityRepository();
            var protector = new WindowsManualBuyProtector();
            var witness = new EngineContinuityFile(Path.Combine(EnginePaths.DirectoryPath, "continuity.dat"), protector);
            var continuity = new EngineContinuity(continuityStore, witness);
            if (args[0] == "--prepare-compact-book")
            {
                var preparation = new CompactAccountPreparation(new SqlTradingStorageDatabase(EngineIntegrityRepository.OpenAsync),
                    continuityStore, witness, new BrokerManagedSessionRepository(calendar, sharedStorage: true), grants,
                    journal, new CompactReceiptProtector(), CompactManagedWriter.DirectoryPath);
                // Maintenance can run separately from the approved trading package. Its target
                // is the actual installed binary, never a caller-supplied hash or edited grant.
                string reviewedBuild = BrokerManagedSessionComposition.ImplementationIdentity;
                if (prepareInstalled)
                {
                    if (!Path.IsPathFullyQualified(args[1]) || Path.GetFileName(args[1]) != "Core.dll" ||
                        !File.Exists(Path.Combine(Path.GetDirectoryName(args[1])!, "TraderVI.Engine.exe"))) return 64;
                    using var installedCore = File.OpenRead(args[1]);
                    reviewedBuild = "CoreSha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(installedCore));
                }
                await preparation.PrepareAsync(reviewedBuild, CancellationToken.None);
                return 0;
            }
            var sessions = new BrokerManagedSessionRepository(calendar, sharedStorage: true, mayReuse: async (binding, ct) =>
            {
                await continuity.VerifyAsync(ct).ConfigureAwait(false);
                return (await new EngineIntegrityRepository().ReadAccessAsync(EngineIntegrityScope.Broker, binding, ct)).ProtectionAllowed;
            });
            var build = BrokerManagedSessionComposition.ImplementationIdentity;
            var compact = new CompactEngineRestart(new SqlTradingStorageDatabase(EngineIntegrityRepository.OpenAsync).CreateStore(),
                journal, grants, connection, sessions, calendar, new CompactReceiptProtector(), CompactManagedWriter.DirectoryPath, build, TimeProvider.System);
            var broker = new EngineBrokerService(connection, journal, sessionStore, grants, calendar,
                async (saved, ct) =>
                {
                    await continuity.VerifyAsync(ct).ConfigureAwait(false);
                    await IndependentExecutionReadiness.VerifyAsync(saved, ct).ConfigureAwait(false);
                }, args[0] == "--review-build" ? new BrokerManagedSessionRepository(calendar, sharedStorage: true) : sessions,
                build, verificationOnly, cycle: args[0] == "--run" && (grants.Read() is { RestoreAfterRestart: true } ||
                    Directory.Exists(CompactManagedWriter.DirectoryPath)) ? compact.CycleAsync : null);
            if (args[0] == "--review-build")
            {
                await RunReviewAsync(broker);
                return 0;
            }
            async Task StartContinuity()
            {
                if (args[0] == "--run" && (grants.Read() is { RestoreAfterRestart: true } || Directory.Exists(CompactManagedWriter.DirectoryPath)))
                {
                    if (!Directory.Exists(CompactManagedWriter.DirectoryPath))
                        throw new InvalidOperationException("Transfer the reviewed account before starting real trading.");
                    await continuity.StartAsync(compact.QualifyAsync, CancellationToken.None);
                }
                else await continuity.StartAsync();
            }
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
                new("Restart account check", TimeSpan.FromMinutes(1),
                    args[0] == "--run" ? compact.CheckAccountOnceAsync : () => Task.CompletedTask),
                new("Paper protection", TimeSpan.FromSeconds(10), paper.ProtectAsync),
                new("Paper inputs", TimeSpan.FromSeconds(10), paper.ConsumeAsync),
                new("Research inputs", TimeSpan.FromSeconds(30), research.ConsumeAsync),
                new("Research history", TimeSpan.FromSeconds(30), research.MaintainAsync),
                new("Tracked paper", TimeSpan.FromSeconds(30), auxiliary.TrackedTickAsync),
                new("Shadow paper", TimeSpan.FromSeconds(30), auxiliary.ShadowTickAsync),
                new("Shadow protection", TimeSpan.FromSeconds(10), auxiliary.ShadowProtectionAsync),
                new("AI research", TimeSpan.FromSeconds(30), auxiliary.CheckAiAsync),
                new("Historical audits", TimeSpan.FromSeconds(1), audits.TickAsync)
            ], () => IndependentEngine.InitializeHostAsync(StartContinuity, broker.StartAsync, auxiliary.StartAsync, verificationOnly, recoveryMaintenance), async () =>
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
        catch (Exception error) when (args[0] == "--prepare-compact-book")
        {
            var method = new System.Diagnostics.StackTrace(error).GetFrame(0)?.GetMethod();
            Console.Error.WriteLine($"Compact transfer refused. {error.GetType().Name} in {method?.DeclaringType?.Name}.{method?.Name}.");
            return 1;
        }
        catch { return 1; } // Never write credentials, account contents or SQL connection details to console/logs.
#endif
    }

#if !COMPACT_RESTART_CANDIDATE
    private static async Task RunReviewAsync(EngineBrokerService broker)
    {
        using var stop = new CancellationTokenSource();
        var instance = Guid.NewGuid();
        IndependentEngineStatus Status(string state) => new(instance, state, DateTimeOffset.UtcNow, []);
        var server = new EngineRpcServer(EnginePaths.PipeName, async request =>
        {
            if (request.Operation == EngineOperation.Status) return Status("Build review only");
            if (request.Operation == EngineOperation.Stop) { broker.BeginStop(); stop.Cancel(); return Status("Stopping"); }
            if (request.Operation == EngineOperation.IntegrityStatus) return await new EngineIntegrityRepository().ReadStatusAsync();
            if (request.Operation == EngineOperation.ExecutionStatus)
            {
                var status = (BrokerExecutionHostStatus)await broker.DispatchAsync(request);
                return status with { Message = "Build review only. The trading worker is stopped. " + status.Message };
            }
            if (!EngineAuthenticationCommands.Accepts(request.Operation) && request.Operation is not
                (EngineOperation.Connection or EngineOperation.ExecutionStatus or EngineOperation.ExecutionDiagnostics or
                 EngineOperation.Accounts or EngineOperation.AccountSnapshot or EngineOperation.PendingOrders or EngineOperation.DailyPerformance or
                 EngineOperation.Orders or EngineOperation.Reconcile or EngineOperation.OrderHistory or EngineOperation.ActivityCheckpoint or
                 EngineOperation.DisableExecution or EngineOperation.EnableExecution))
                throw new InvalidOperationException("Trading is unavailable during build review.");
            return await broker.DispatchAsync(request);
        });
        try { await broker.StartAsync(); await server.RunAsync(stop.Token); }
        finally { broker.BeginStop(); await broker.CloseAsync(); }
    }

    private static async Task RunSignInAsync()
    {
        using var owner = new EngineOwnershipLease(EnginePaths.OwnershipPath);
        using var sessions = new WindowsWealthsimpleSessionStore();
        using var stop = new CancellationTokenSource();
        var connection = new WealthsimpleConnection(sessions);
        var instance = Guid.NewGuid();
        IndependentEngineStatus Status(string state) => new(instance, state, DateTimeOffset.UtcNow, []);
        // The operator signs in explicitly. Do not restore or rotate an expired token at startup.
        var server = new EngineRpcServer(EnginePaths.PipeName, async request =>
        {
            if (request.Operation == EngineOperation.Status) return Status("Sign-in only");
            if (request.Operation == EngineOperation.ExecutionStatus)
                return new BrokerExecutionHostStatus("Off", "Broker sign-in only. Trading is off.", DateTimeOffset.UtcNow);
            if (request.Operation == EngineOperation.Stop) { stop.Cancel(); return Status("Stopping"); }
            if (request.Operation == EngineOperation.Connection) return EngineConnectionStatus.Read(connection);
            return await EngineAuthenticationCommands.DispatchAsync(request, connection, sessions);
        });
        try { await server.RunAsync(stop.Token); }
        finally { await connection.StopAsync(); }
    }
#endif
}
