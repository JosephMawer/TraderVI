#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    private sealed record SourceSwitchPreparation(BrokerLiveSourceSwitchProposal Proposal,
        BrokerLiveSourceSwitchWorkflow Workflow, string Summary);

    private static async Task<(BrokerStrategyReview Review, DateTimeOffset Updated)> ReadUngatedLiveAsync(
        CancellationToken ct, Action<string> stage)
    {
        stage("reading the existing Portfolio rules only source");
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
        var portfolios = await new DelphiLiveLedgerRepository().GetPortfoliosForSessionAsync(date, ct);
        var target = TfsaLiveStrategySelection.Rank(portfolios).Where(p => p.HasPortfolioRulesOnlyLiveComparisonIdentity).ToArray();
        if (target.Length != 1) throw new InvalidOperationException("The exact Portfolio rules only Live source is missing or ambiguous.");
        var review = await new TradingPromotionRepository().ReviewBrokerAsync(target[0].Snapshot.PortfolioId,
            TradingStrategyFamily.DelphiLive, ct);
        return (review, new DateTimeOffset(DateTime.SpecifyKind(target[0].Snapshot.UpdatedUtc, DateTimeKind.Utc)));
    }
    private async Task<SourceSwitchPreparation> PrepareSourceSwitchAsync(WealthsimpleTfsaAccount account,
        CancellationToken ct, Action check, Action<string> stage)
    {
        check(); _ = GetExecutionHost();
        if (HostGrants().Read()?.Enabled == true) throw new InvalidOperationException("Stop execution host before reviewing a source switch.");
        stage("recovering the existing account assignment and risk scope");
        var imports = await new BrokerAccountImportRepository().ReadAsync(ct); check();
        var saved = imports.Binding is null ? null : await executionSessions.ReadAsync(imports.Binding.BindingId, ct);
        if (saved is null) throw new InvalidOperationException("Recover the existing October 21 continuation first.");
        _ = BrokerManagedSessionComposition.MatchAccount(saved, new[] { account });
        var target = await ReadUngatedLiveAsync(ct, stage); check();
        var storage = await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        var scope = saved.EffectiveReview.NoTransferScope ?? throw new InvalidOperationException("Recover the original dated risk scope.");
        var components = BrokerManagedSessionComposition.CreateComponents(connection, connection, connection, journal, journal,
            orderGate, account, executionCalendar!, new BrokerNoTransferRiskEvidenceSource(scope, executionCalendar!));
        stage("checking fresh account, orders and preserved financial risk");
        var projected = await components.AccountSource.SynchronizeAsync(saved.Orders, ct); check();
        var proposal = BrokerLiveSourceSwitchWorkflow.Prepare(saved, target.Review, target.Updated, projected,
            storage.BaselineInstalled && storage.ContinuationInstalled && storage.SessionInstalled && storage.ImmutableStorageVerified,
            BrokerManagedSessionComposition.ImplementationIdentity, Environment.UserName, executionCalendar!, DateTimeOffset.UtcNow);
        var summary = $"Review source switch: Normal → Delphi Live · Portfolio rules only\n" +
            $"Exact target: {target.Review.Strategy.Strategy.Name}\nProfile: Portfolio rules only\n" +
            $"Policy: {target.Review.Strategy.Strategy.PolicyVersionId:D}\nDaily definition: {target.Review.Strategy.Strategy.Daily.StrategyVersionId:D}\n" +
            $"Running build: {BrokerAccountImport.Hash(BrokerManagedSessionComposition.ImplementationIdentity)[..16]}\n" +
            $"Preserved purchase ceiling remaining: {saved.EffectiveReview.PurchaseCeiling - saved.CommittedPurchaseCost:N2} CAD\n" +
            $"Fresh cash: {projected.Import.Plan.Observation.Cash:N2} CAD · buying power: {projected.Import.Plan.Observation.BuyingPower:N2} CAD\n" +
            $"Original daily reference: {projected.Risk.SessionReference.Value:N2} CAD · evidence {projected.Risk.SessionReference.EvidenceAt.ToLocalTime():g}\n" +
            $"Original drawdown reference: {projected.Risk.DrawdownReference.Value:N2} CAD · evidence {projected.Risk.DrawdownReference.EvidenceAt.ToLocalTime():g}\n\n" +
            "Daily Regime, Breadth, Granville, DownProbability, setup/trend, Direction and Composite admission gates are bypassed by this existing profile.\n" +
            "Intraday confirmation, fresh valid data, sizing/cash limits, stops, risk holds, order reconciliation and full-fill-only restrictions remain.\n" +
            "Account is cash-only with no automatic orders. Original approval, financial history and references remain. New-source signals start after approval; no paper positions, funds or earlier confirmation are transferred.\n" +
            $"New purchases end {scope.PurchaseExpiresAt?.ToLocalTime():g}. No next-day permission.\n\n" +
            "Review and approve within two minutes. Approval submits no order and leaves the host off; Enable reviewed host is separate.\n\n" +
            string.Join("\n", target.Review.Evidence.Limitations);
        return new(proposal, new(new BrokerManagedSessionRepository(executionCalendar!), components.AccountSource,
            HostGrants(), executionCalendar!), summary);
    }
    private async Task ApproveSourceSwitchAsync(SourceSwitchPreparation prepared, CancellationToken ct, Action check, Action<string> stage)
    {
        stage("rechecking the exact current ungated source and immutable storage");
        var fresh = await ReadUngatedLiveAsync(ct, stage); check();
        var storage = await BrokerManagedSessionRepository.ReadStorageAsync(ct); check();
        if (!storage.BaselineInstalled || !storage.ContinuationInstalled || !storage.SessionInstalled || !storage.ImmutableStorageVerified ||
            !fresh.Review.Blocks.IsEmpty || fresh.Review.Strategy.Fingerprint != prepared.Proposal.Review.SourceReview.Strategy.Fingerprint ||
            fresh.Updated > DateTimeOffset.UtcNow || DateTimeOffset.UtcNow - fresh.Updated > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("Source or storage changed. Review the source switch again.");
        stage("saving or recovering the explicitly reviewed source switch");
        await prepared.Workflow.ApproveAsync(prepared.Proposal, ct); check();
    }
}
