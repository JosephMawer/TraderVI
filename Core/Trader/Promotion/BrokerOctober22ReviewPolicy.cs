#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
namespace Core.Trader.Promotion;
public static class BrokerOctober22ReviewPolicy
{
    public static BrokerFirstExecutionReview Prepare(BrokerManagedSession prior, BrokerOctober22Continuation continuation,
        BrokerStrategyReview strategyReview, DateTimeOffset sourceUpdatedAt, BrokerAccountProjection account,
        bool storageReady, string implementation, Core.Trader.DelphiLive.ITsxSessionCalendar calendar, DateTimeOffset now,
        bool validPreOpenDailySource = false)
    {
        continuation.RequirePrior(prior, calendar);
        var scope = continuation.Scope(calendar);
        var strategy = strategyReview.Strategy;
        BrokerOctober22Continuation.RequireSameDefinition(prior.Strategy, strategy);
        var blocks = ImmutableArray.CreateBuilder<string>();
        var checks = ImmutableArray.CreateBuilder<BrokerExecutionReviewCheck>();
        void Check(string key, string label, Action validate)
        {
            try { validate(); checks.Add(new(key, label, true, "Current readiness check passed.")); }
            catch (InvalidOperationException error)
            { blocks.Add(error.Message); checks.Add(new(key, label, false, error.Message)); }
        }
        Check("current-account", "Current account, cash and orders", () =>
        {
            BrokerContinuationReviewPolicy.RequireCurrentAccount(account, now);
            BrokerTrialCarryForward.RequireUnchangedBook(scope.CashOnlyAnchor, account.Import.Plan.Observation);
        });
        Check("current-risk", "Preserved risk references and holds", () =>
        {
            if (BrokerAccountImport.Hash(account.Risk) != BrokerAccountImport.Hash(continuation.Apply(continuation.PreviousRisk, calendar)))
                throw new InvalidOperationException("Current risk history differs from the reviewed continuation; preserve references and holds for recovery.");
        });
        Check("current-source", "Current Portfolio rules only strategy, source and build", () =>
        {
            if (!strategyReview.Blocks.IsEmpty || strategy.Strategy.Family != TradingStrategyFamily.DelphiLive ||
                strategy.GateProfile != PaperGateProfile.PortfolioRulesOnlyV1 || strategy.Comparison is null ||
                strategy.Strategy.ImplementationIdentity != implementation ||
                strategyReview.Evidence.PaperAccountId != strategy.PaperAccountId ||
                strategyReview.PreparedUtc.Kind != DateTimeKind.Utc || strategyReview.PreparedUtc > now.UtcDateTime ||
                now.UtcDateTime - strategyReview.PreparedUtc > TimeSpan.FromMinutes(2) ||
                !BrokerOctober22Continuation.SourceReady(continuation, sourceUpdatedAt, calendar, now, validPreOpenDailySource))
                throw new InvalidOperationException("Refresh the exact current Portfolio rules only definition, source and running build before approval.");
        });
        Check("current-storage", "Installed immutable account and permission storage", () =>
        {
            if (!storageReady) throw new InvalidOperationException("Current immutable baseline, continuation and session storage checks must pass before approval.");
        });
        var ceiling = System.Math.Min(System.Math.Min(account.Import.Plan.Observation.Cash ?? 0,
            account.Import.Plan.Observation.BuyingPower ?? 0), prior.EffectiveReview.PurchaseCeiling - prior.CommittedPurchaseCost);
        BrokerActivationReview? activation = null;
        Check("dated-permission", "October 22 scope and purchase cutoff", () =>
        {
            if (DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) != scope.Session ||
                now < calendar.GetSessionBounds(scope.Session).OpenUtc && !continuation.PreOpenApproved || now >= scope.PurchaseExpiresAt || ceiling <= 0)
                throw new InvalidOperationException("Review October 22 permission on its date, with explicit pre-open approval when applicable, remaining allowance and the unchanged close cutoff.");
        });
        if (blocks.Count == 0)
        {
            activation = new(scope.ScopeId, account.Risk.BindingId, strategy, account.Risk.BaselineId, account.Fingerprint,
                scope.Session, now, ceiling, [], [], [], [])
                { ReviewedPositions = account.Positions, Duration = BrokerExecutionDuration.Ongoing, NoTransferScope = scope };
            try { activation.Validate(account, now); }
            catch (InvalidOperationException error) { blocks.Add(error.Message); activation = null; }
        }
        return new(strategy, ceiling, checks.ToImmutable(), blocks.Distinct().ToImmutableArray(), activation);
    }

}
