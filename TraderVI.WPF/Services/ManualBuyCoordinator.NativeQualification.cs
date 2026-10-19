#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Services;

public sealed partial class ManualBuyCoordinator
{
    private BrokerNativeTrialEvidenceFileStore NativeTrialEvidence() => new(ExecutionQualificationPath, new WindowsManualBuyProtector());
    private static async Task<(BrokerStrategyReview Review, DateTimeOffset Updated)> ReadNativeNormalAsync(CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(DateTime.UtcNow));
        var portfolios = await new DelphiLiveLedgerRepository().GetPortfoliosForSessionAsync(date, ct);
        var normal = TfsaLiveStrategySelection.Rank(portfolios).Where(c => c.SupportsTransitionReview &&
            c.Snapshot.ComparisonAccount?.Profile == PaperGateProfile.NormalV1).ToArray();
        if (normal.Length != 1) throw new InvalidOperationException("The exact current Normal source is unavailable or ambiguous.");
        var review = await new TradingPromotionRepository().ReviewBrokerAsync(normal[0].Snapshot.PortfolioId,
            TradingStrategyFamily.DelphiLive, ct);
        return (review, new DateTimeOffset(DateTime.SpecifyKind(normal[0].Snapshot.UpdatedUtc, DateTimeKind.Utc)));
    }

    private async Task<BrokerNativeTrialObservation> ReadNativeTrialObservationAsync(WealthsimpleTfsaAccount account,
        long revision, BrokerNativeTrialObservation? recover, CancellationToken ct)
    {
        void Check()
        {
            if (!connection.IsConnected || connection.SessionRevision != revision)
                throw new InvalidOperationException("Sign-in changed; recover the original checkpoint using the connected account.");
            if (HostGrants().Read()?.Enabled == true)
                throw new InvalidOperationException("Stop the execution host before native qualification. Qualification never changes its permission.");
        }
        Check(); _ = GetExecutionHost();
        if (recover is not null)
        {
            if (!BrokerNativeTrialQualification.MatchesAccount(account, recover.Account) ||
                recover.Strategy.Strategy.Strategy.ImplementationIdentity != BrokerManagedSessionComposition.ImplementationIdentity)
                throw new InvalidOperationException("Recover the original checkpoint in the same account and executable; a different build cannot inherit it.");
            var sales = new ManualMarketSellWorkflow(connection, journal, orderGate: orderGate);
            foreach (var buy in recover.Buys.Where(b => b.Intent.AccountId == recover.Account.AccountId &&
                DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(b.Intent.CreatedAt.UtcDateTime)) == recover.Baseline.TradingDate))
            { await workflow.CheckOrderAsync(buy.Intent.RequestId, ct); Check(); }
            foreach (var sale in recover.Sales.Where(s => s.Intent.AccountId == recover.Account.AccountId &&
                DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(s.Intent.CreatedAt.UtcDateTime)) == recover.Baseline.TradingDate))
            { await sales.CheckOrderAsync(sale.Intent.RequestId, ct); Check(); }
        }
        var synchronized = await new BrokerAccountSynchronizationWorkflow(new BrokerImportWorkflow(new BrokerAccountImportRepository()))
            .RefreshAsync(token => reconciliation.ReadAsync(account, revision, token), ct); Check();
        var saved = synchronized.Saved ?? throw new InvalidOperationException("Reconcile the linked account and all original order statuses before native qualification.");
        var repository = new BrokerAccountRiskBaselineRepository();
        var read = await repository.ReadAsync(ct); Check();
        var baseline = read.RiskHistory.ExistingBaseline ?? throw new InvalidOperationException("Complete the manual trades, reconcile back to cash and explicitly save initial Account risk setup before capturing the native checkpoint.");
        if (read.Import.Binding?.BindingId != saved.BindingId || baseline.BindingId != saved.BindingId ||
            await executionSessions.ReadAsync(saved.BindingId, ct) is not null)
            throw new InvalidOperationException("Native first-session qualification requires this linked account and no existing managed assignment.");
        var found = await repository.FindAsync(baseline.BaselineId, ct) ?? throw new InvalidOperationException("The original saved baseline could not be read back."); Check();
        var risk = await new BrokerAccountRiskContinuationRepository(executionCalendar!).ReadAsync(baseline.BaselineId, ct); Check();
        var storage = await BrokerManagedSessionRepository.ReadStorageAsync(ct); Check();
        var normal = await ReadNativeNormalAsync(ct); Check();
        using var process = Process.GetCurrentProcess();
        var observation = new BrokerNativeTrialObservation(Guid.NewGuid(), Environment.ProcessId,
            new DateTimeOffset(process.StartTime.ToUniversalTime()), DateTimeOffset.UtcNow, normal.Review, normal.Updated,
            saved.Plan.Observation, baseline, found, risk, storage.BaselineInstalled, storage.ContinuationInstalled, storage.SessionInstalled, storage.ImmutableStorageVerified,
            journal.ReadAll().Where(b => b.Intent.AccountId == saved.Plan.Observation.AccountId && b.Intent.IdentityId == saved.Plan.Observation.IdentityId &&
                DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(b.Intent.CreatedAt.UtcDateTime)) == baseline.TradingDate).ToImmutableArray(),
            journal.ReadSells().Where(s => s.Intent.AccountId == saved.Plan.Observation.AccountId && s.Intent.IdentityId == saved.Plan.Observation.IdentityId &&
                DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(s.Intent.CreatedAt.UtcDateTime)) == baseline.TradingDate).ToImmutableArray());
        BrokerNativeTrialQualification.ValidateObservation(observation, DateTimeOffset.UtcNow);
        return observation;
    }
}
