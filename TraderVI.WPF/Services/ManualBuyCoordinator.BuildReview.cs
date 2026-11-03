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
        stage("checking the exact saved strategy, installed build and current storage");
        var normal=await new TradingPromotionRepository().ReviewExistingBrokerBuildAsync(saved,executionCalendar!,DateTimeOffset.UtcNow,ct); check();
        var storage=await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        var scope=saved.EffectiveReview.NoTransferScope;
        IBrokerRiskEvidenceSource risk = saved.OngoingManagement is { } ongoing
            ? new BrokerOngoingRiskEvidenceSource(ongoing, executionCalendar!, new BrokerOngoingBoundaryRepository().ReadAsync, preserveRiskHistory: true)
            : new BrokerNoTransferRiskEvidenceSource(scope ?? throw new InvalidOperationException("Recover today's original permission."), executionCalendar!);
        var components=CreateManagedComponents(connection,connection,connection,journal,journal,
            orderGate,account,executionCalendar!,risk);
        if (saved.OngoingManagement is not null)
        {
            stage("verifying existing daily risk receipts and synchronizing the stopped session without changing permission");
            saved = await new BrokerRiskStateSynchronizationWorkflow(
                new BrokerManagedSessionRepository(executionCalendar!, sharedStorage: true),
                new BrokerAccountRiskContinuationRepository(executionCalendar!), components.AccountSource,
                HostGrants(), executionCalendar!).SynchronizeAsync(binding.BindingId, ct); check();
        }
        stage("rechecking the actual account, original orders, protection and preserved risk history");
        var projected=await components.AccountSource.SynchronizeAsync(saved.Orders,ct); check();
        var proposal=BrokerImplementationReviewWorkflow.Prepare(saved,normal.Review,normal.Updated,projected,
            storage.BaselineInstalled && storage.ContinuationInstalled && storage.SessionInstalled && storage.ImmutableStorageVerified,
            BrokerManagedSessionComposition.ImplementationIdentity,Environment.UserName,executionCalendar!,DateTimeOffset.UtcNow,
            reviewScopedRestart: saved.OngoingManagement != null, reinvestSaleProceeds: saved.OngoingManagement != null);
        decimal reusableCapital = saved.EffectiveReview.PurchaseCeiling - saved.CommittedPurchaseCost + saved.ReconciledOngoingSaleProceeds;
        decimal availableForReview = Math.Max(0m, Math.Min(projected.Account.AvailableCash ?? 0m, reusableCapital));
        string buyingTerms = proposal.Review.ReinvestSaleProceeds
            ? $"Starting purchase authority {saved.EffectiveReview.PurchaseCeiling:N2} CAD. Reuse net proceeds, including profits, from verified strategy sales since the ongoing agreement began. " +
              $"After purchases and reconciled sales, this review allows up to {availableForReview:N2} CAD before strategy sizing. " +
              "Each original filled sale counts once after reconciliation. Every buy is limited by available broker cash, buying power, whole-share sizing and risk checks. " +
              "Pending proceeds are unavailable. Outside deposits or unexplained cash changes require review.\n"
            : $"Original cumulative purchase ceiling {saved.EffectiveReview.PurchaseCeiling:N2} CAD.\n";
        string summary=$"Updated build review\nExact strategy: {normal.Review.Strategy.Strategy.Name} · {normal.Review.Strategy.GateProfile}\n" +
            normal.Review.Strategy.Strategy.EntryReviewDescription + "\n" +
            $"Previous build: {BrokerAccountImport.Hash(proposal.Review.PreviousImplementation)[..16]}\nNew build: {BrokerAccountImport.Hash(proposal.Review.ImplementationIdentity)[..16]}\n" +
            buyingTerms + $"Fresh broker cash {projected.Import.Plan.Observation.Cash:N2} CAD.\n" +
            $"Original daily reference {projected.Risk.SessionReference.Value:N2} CAD; drawdown reference {projected.Risk.DrawdownReference.Value:N2} CAD; all source dates and history retained.\n" +
            (saved.OngoingManagement is not null ? $"Ongoing agreement retained with {saved.Account.Positions.Length} holdings and their saved protection. " : $"Original permission still expires {scope!.PurchaseExpiresAt?.ToLocalTime():g}. ") +
            "Existing risk controls, position protection, order history and buying mode are preserved.\n\n" +
            (proposal.Review.RestartPolicy == BrokerImplementationReview.ScopedRestart ?
                "Restart permission for this build. Keep the money for an unfinished buy reserved. Look up its original order and count each confirmed fill once. A saved protective sell on a different holding may proceed without another quote or holdings refresh. A conflicting sale on those same shares still blocks that sale.\n\n" : "") +
            "Review these facts, then approve the reviewed build within two minutes. Resume the reviewed host separately afterward. " +
            (saved.OngoingManagement is not null ? "You can review and resume the host after hours. It waits for a regular TSX session and fresh market, account, order and risk checks before trading. This review does not certify tomorrow's data or advance daily risk references." : "");
        return new(proposal,new(new BrokerManagedSessionRepository(executionCalendar!,sharedStorage:true),components.AccountSource,
            HostGrants(),executionCalendar!),summary);
    }

    private async Task ApproveBuildReviewAsync(BuildReviewPreparation prepared,CancellationToken ct,Action check,Action<string> stage)
    {
        stage("rechecking the exact strategy, build and immutable storage before build approval");
        var saved=await executionSessions.ReadAsync(prepared.Proposal.BindingId,ct)
            ?? throw new InvalidOperationException("Recover the existing real-account assignment."); check();
        var normal=await new TradingPromotionRepository().ReviewExistingBrokerBuildAsync(saved,executionCalendar!,DateTimeOffset.UtcNow,ct); check();
        var storage=await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        if (!storage.BaselineInstalled || !storage.ContinuationInstalled || !storage.SessionInstalled || !storage.ImmutableStorageVerified ||
            !normal.Review.Blocks.IsEmpty || normal.Review.Strategy.Fingerprint != prepared.Proposal.Strategy.Fingerprint ||
            normal.Updated > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow-normal.Updated > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("Current build/source/storage changed or expired. Review the updated build again.");
        await prepared.Workflow.ApproveAsync(prepared.Proposal,ct); check();
    }
}
