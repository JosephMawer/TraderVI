#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    private async Task<(BrokerOngoingManagement Review, BrokerOngoingManagementWorkflow Workflow)> PrepareOngoingAsync(
        WealthsimpleTfsaAccount account, CancellationToken ct, Action check, Action<string> stage)
    {
        check(); _ = GetExecutionHost();
        await StopExecutionCoreAsync(ct); check();
        var binding = (await new BrokerAccountImportRepository().ReadAsync(ct)).Binding
            ?? throw new InvalidOperationException("Recover the existing permanent account link.");
        var saved = await executionSessions.ReadAsync(binding.BindingId, ct)
            ?? throw new InvalidOperationException("Recover the selected real strategy.");
        _ = BrokerManagedSessionComposition.MatchAccount(saved, [account]);
        stage("checking the pinned strategy, models, build, storage and integrity");
        async Task Verify(CancellationToken token)
        {
            check();
            var storage = await BrokerManagedSessionRepository.ReadStorageAsync(token);
            var access = await new EngineIntegrityRepository().ReadAccessAsync(EngineIntegrityScope.Broker, saved.BindingId, token);
            if (!storage.ImmutableStorageVerified || !storage.BaselineInstalled || !storage.ContinuationInstalled ||
                !storage.SessionInstalled || !access.NewExposureAllowed || !access.ProtectionAllowed)
                throw new InvalidOperationException("Resolve current storage/integrity blocks before ongoing approval.");
            var current = await new TradingPromotionRepository().ReviewExistingBrokerAsync(saved, executionCalendar!, DateTimeOffset.UtcNow, token);
            if (!current.Review.Blocks.IsEmpty) throw new InvalidOperationException("The selected strategy definition is not ready.");
            check();
        }
        await Verify(ct);
        var definition = await new TradingPromotionRepository().ReviewExistingBrokerAsync(saved, executionCalendar!, DateTimeOffset.UtcNow, ct);
        var review = BrokerOngoingManagement.Prepare(saved, definition.Review.Strategy, DateTimeOffset.UtcNow,
            Environment.UserName, true, true);
        var risk = new BrokerOngoingRiskEvidenceSource(review, executionCalendar!, new BrokerOngoingBoundaryRepository().ReadAsync, reviewOnly: true);
        var components = CreateManagedComponents(connection, connection, connection, journal, journal, orderGate,
            account, executionCalendar!, risk);
        stage("reconciling original cash and financial history for the ongoing agreement");
        var projected = await components.AccountSource.SynchronizeAsync(saved.Orders, ct); check();
        review.RequireCurrentAccount(projected, DateTimeOffset.UtcNow);
        return (review, new(new BrokerManagedSessionRepository(executionCalendar!, sharedStorage: true),
            new BrokerAccountRiskContinuationRepository(executionCalendar!), components.AccountSource,
            HostGrants(), executionCalendar!, Verify));
    }

    private async Task StartApprovedOngoingAsync(BrokerManagedSession saved, CancellationToken ct)
    {
        if (connection is EngineBrokerClient remote)
        {
            await remote.Rpc.CallAsync<bool>(EngineOperation.EnableExecution,
                new EngineEnableExecution(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, Environment.UserName), ct);
            PresentExecution(await remote.Rpc.CallAsync<BrokerExecutionHostStatus>(EngineOperation.ExecutionStatus, ct: ct));
        }
        else await GetExecutionHost().EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, Environment.UserName, ct);
    }
}
