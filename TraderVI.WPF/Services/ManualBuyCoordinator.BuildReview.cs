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
    private sealed record BuildReviewPreparation(BrokerImplementationReviewProposal Proposal,
        BrokerImplementationReviewWorkflow Workflow, string Summary);

    private async Task<BuildReviewPreparation> PrepareBuildReviewAsync(WealthsimpleTfsaAccount account,
        CancellationToken ct, Action check, Action<string> stage)
    {
        check(); _=GetExecutionHost();
        if (HostGrants().Read()?.Enabled == true) throw new InvalidOperationException("Stop the execution host before reviewing the updated build.");
        var imports=await new BrokerAccountImportRepository().ReadAsync(ct); check();
        var binding=imports.Binding ?? throw new InvalidOperationException("Recover the existing account link.");
        var saved=await executionSessions.ReadAsync(binding.BindingId,ct) ?? throw new InvalidOperationException("Recover the existing assignment."); check();
        _=BrokerManagedSessionComposition.MatchAccount(saved,new[] { account });
        stage("checking the saved real strategy, direct shared market inputs and current storage for the updated build");
        var normal=await new TradingPromotionRepository().ReviewExistingBrokerAsync(saved,executionCalendar!,DateTimeOffset.UtcNow,ct); check();
        var storage=await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        var scope=saved.EffectiveReview.NoTransferScope;
        IBrokerRiskEvidenceSource risk = saved.OngoingManagement is { } ongoing
            ? new BrokerOngoingRiskEvidenceSource(ongoing, executionCalendar!, new BrokerOngoingBoundaryRepository().ReadAsync)
            : new BrokerNoTransferRiskEvidenceSource(scope ?? throw new InvalidOperationException("Recover today's original permission."), executionCalendar!);
        var components=CreateManagedComponents(connection,connection,connection,journal,journal,
            orderGate,account,executionCalendar!,risk);
        stage("rechecking the actual account, original orders, protection and preserved risk history");
        var projected=await components.AccountSource.SynchronizeAsync(saved.Orders,ct); check();
        var proposal=BrokerImplementationReviewWorkflow.Prepare(saved,normal.Review,normal.Updated,projected,
            storage.BaselineInstalled && storage.ContinuationInstalled && storage.SessionInstalled && storage.ImmutableStorageVerified,
            BrokerManagedSessionComposition.ImplementationIdentity,Environment.UserName,executionCalendar!,DateTimeOffset.UtcNow);
        string summary=$"Updated build review\nExact strategy: {normal.Review.Strategy.Strategy.Name} · {normal.Review.Strategy.GateProfile}\n" +
            normal.Review.Strategy.Strategy.EntryReviewDescription + "\n" +
            $"Previous build: {BrokerAccountImport.Hash(proposal.Review.PreviousImplementation)[..16]}\nNew build: {BrokerAccountImport.Hash(proposal.Review.ImplementationIdentity)[..16]}\n" +
            $"Original purchase ceiling remains {saved.EffectiveReview.PurchaseCeiling:N2} CAD. Fresh cash {projected.Import.Plan.Observation.Cash:N2} CAD.\n" +
            $"Original daily reference {projected.Risk.SessionReference.Value:N2} CAD; drawdown reference {projected.Risk.DrawdownReference.Value:N2} CAD; all source dates and history retained.\n" +
            (saved.OngoingManagement is not null ? $"Ongoing agreement retained with {saved.Account.Positions.Length} holdings and their saved protection. " : $"Original permission still expires {scope!.PurchaseExpiresAt?.ToLocalTime():g}. ") +
            "Gates, risk controls, memory, buying/paused mode and cumulative allowance are unchanged.\n\n" +
            "Real holding protection now uses a fresh Wealthsimple bid matched to the account, security and quantity, with a broker timestamp no more than 60 seconds old. Missing or stale bids do not trigger an exit. Existing stop levels and broker sell checks remain unchanged.\n\n" +
            "Review these facts, then Approve reviewed build within two minutes. This saves only the explicit build review; no order, baseline reset, risk event or host enable. Enable reviewed host separately afterward.";
        return new(proposal,new(new BrokerManagedSessionRepository(executionCalendar!,sharedStorage:true),components.AccountSource,
            HostGrants(),executionCalendar!),summary);
    }

    private async Task ApproveBuildReviewAsync(BuildReviewPreparation prepared,CancellationToken ct,Action check,Action<string> stage)
    {
        stage("rechecking the saved real strategy, direct shared inputs and immutable storage before build approval");
        var saved=await executionSessions.ReadAsync(prepared.Proposal.BindingId,ct)
            ?? throw new InvalidOperationException("Recover the existing real-account assignment."); check();
        var normal=await new TradingPromotionRepository().ReviewExistingBrokerAsync(saved,executionCalendar!,DateTimeOffset.UtcNow,ct); check();
        var storage=await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        if (!storage.BaselineInstalled || !storage.ContinuationInstalled || !storage.SessionInstalled || !storage.ImmutableStorageVerified ||
            !normal.Review.Blocks.IsEmpty || normal.Review.Strategy.Fingerprint != prepared.Proposal.Strategy.Fingerprint ||
            normal.Updated > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow-normal.Updated > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("Current build/source/storage changed or expired. Review the updated build again.");
        await prepared.Workflow.ApproveAsync(prepared.Proposal,ct); check();
    }
}
