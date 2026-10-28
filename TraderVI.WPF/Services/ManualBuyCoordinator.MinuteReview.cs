#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    private sealed record MinuteReviewPreparation(BrokerMinuteAdoptionProposal Proposal,
        BrokerMinuteStrategyAdoptionWorkflow Workflow, string Summary);

    private async Task<MinuteReviewPreparation> PrepareMinuteReviewAsync(WealthsimpleTfsaAccount account,
        CancellationToken ct, Action check, Action<string> stage)
    {
        check(); _ = GetExecutionHost();
        if (HostGrants().Read()?.Enabled == true) throw new InvalidOperationException("Stop the execution host before reviewing the one-minute strategy.");
        var imports = await new BrokerAccountImportRepository().ReadAsync(ct); check();
        var binding = imports.Binding ?? throw new InvalidOperationException("Recover the existing account link.");
        var saved = await executionSessions.ReadAsync(binding.BindingId, ct) ?? throw new InvalidOperationException("Recover the ongoing assignment."); check();
        _ = BrokerManagedSessionComposition.MatchAccount(saved, new[] { account });
        var ongoing = saved.OngoingManagement ?? throw new InvalidOperationException("An explicitly approved ongoing agreement is required.");
        Task<BrokerMinuteSourceQualification> Collector(CancellationToken token) => new CollectorAdoptionReadiness().ReadAsync(
            AppContext.BaseDirectory, BrokerManagedSessionComposition.ImplementationIdentity, executionCalendar!, DateTimeOffset.UtcNow, token);
        async Task<BrokerStrategyReview> Definition(CancellationToken token) =>
            (await new TradingPromotionRepository().ReviewExistingBrokerAsync(saved, executionCalendar!, DateTimeOffset.UtcNow, token)).Review;
        stage("checking qualified collector storage, source, current minute evidence and local routing");
        var qualification = await Collector(ct); check();
        var current = await Definition(ct); check();
        var storage = await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        var risk = new BrokerOngoingRiskEvidenceSource(ongoing, executionCalendar!, new BrokerOngoingBoundaryRepository().ReadAsync);
        var components = CreateManagedComponents(connection, connection, connection, journal, journal, orderGate, account, executionCalendar!, risk);
        stage("checking the unchanged account, original orders, risk references and held protection");
        var actual = await components.AccountSource.SynchronizeAsync(saved.Orders, ct); check();
        var proposal = BrokerMinuteStrategyAdoptionWorkflow.Prepare(saved, current, actual,
            storage.BaselineInstalled && storage.ContinuationInstalled && storage.SessionInstalled && storage.ImmutableStorageVerified,
            qualification, BrokerManagedSessionComposition.ImplementationIdentity, Environment.UserName, executionCalendar!, DateTimeOffset.UtcNow);
        var workflow = new BrokerMinuteStrategyAdoptionWorkflow(new BrokerManagedSessionRepository(executionCalendar!, sharedStorage: true),
            components.AccountSource, HostGrants(), executionCalendar!, Collector, Definition);
        string summary = "Review one-minute Delphi Live strategy\n\n" + proposal.Review.Strategy.Strategy.EntryReviewDescription + "\n\n" +
            "Two consecutive qualifying one-minute observations are required. Validated backfill may satisfy both immediately when the latest signal is still current; a restart adds no waiting period.\n" +
            "Three-of-four, VWAP, longer indicator windows, five-minute closing-high protection and fresh broker-bid protection are retained.\n" +
            "The ongoing no-transfer agreement, original risk references, holdings, pending-order identities and cumulative allowance are preserved.\n" +
            $"Remaining purchase allowance: {saved.EffectiveReview.PurchaseCeiling - saved.CommittedPurchaseCost:N2} CAD. Holdings: {saved.Account.Positions.Length}.\n" +
            $"Buying mode retained: {(saved.NewBuysStopped ? "paused" : "allowed after current checks")}. Source qualified {qualification.QualifiedAt:g}; original signal deadline {qualification.DeadlineOffset.TotalSeconds:N0} seconds after each minute.\n\n" +
            "Approve the reviewed one-minute strategy within two minutes, then resume the stopped execution host separately. Approval submits no order.";
        return new(proposal, workflow, summary);
    }
}
