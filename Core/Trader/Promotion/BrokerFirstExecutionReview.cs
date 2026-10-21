#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>References to independently reviewed native evidence, never UI qualification overrides.</summary>
public sealed record BrokerExecutionQualificationBundle(Guid BindingId, string StrategyFingerprint,
    string ImplementationIdentity, DateOnly Session, DateTimeOffset ReviewedAt, DateTimeOffset ExpiresAt,
    string ReviewedBy, ImmutableArray<BrokerQualification> Qualifications,
    BrokerNoTransferSessionScope? NoTransferScope = null)
{
    public void Validate(DateTimeOffset now)
    {
        if (BindingId == Guid.Empty || !CompleteTradingStrategy.IsHash(StrategyFingerprint) ||
            string.IsNullOrWhiteSpace(ImplementationIdentity) || Session == default || ReviewedAt == default ||
            ReviewedAt > now || ExpiresAt <= now || ExpiresAt <= ReviewedAt || string.IsNullOrWhiteSpace(ReviewedBy) ||
            Qualifications.IsDefault || Qualifications.Length > 32)
            throw new InvalidOperationException("The native qualification packet is incomplete, expired or not yet reviewed.");
        if (Qualifications.Any(q => q.BindingId != BindingId || q.StrategyFingerprint != StrategyFingerprint ||
            q.VerifiedAt == default || q.VerifiedAt > ReviewedAt || !CompleteTradingStrategy.IsHash(q.EvidenceHash) ||
            string.IsNullOrWhiteSpace(q.EvidenceReference)) || Qualifications.Select(q => q.Check).Distinct().Count() != Qualifications.Length)
            throw new InvalidOperationException("Native qualification references conflict with their reviewed account or strategy.");
        if (Qualifications.Any(q => q.FirstSessionTrialOnly) && NoTransferScope is not { LimitedFirstSessionTrial: true, FullFillOnlyNativeTrial: true })
            throw new InvalidOperationException("First-day native evidence cannot qualify production or an older trial contract.");
    }
    public override string ToString() => "Reviewed execution qualification references (account data omitted)";
}

public sealed record BrokerExecutionReviewCheck(string Check, string Label, bool Verified, string Detail);
public sealed record BrokerFirstExecutionReview(BrokerManagedStrategy Strategy, decimal PurchaseCeiling,
    ImmutableArray<BrokerExecutionReviewCheck> Checks, ImmutableArray<string> Blocks, BrokerActivationReview? Activation)
{
    public bool CanApprove => Activation is not null && Blocks.IsEmpty;
    public override string ToString() => "First execution review (financial data omitted; no execution authority)";
}

/// <summary>Current dated readiness for the existing assignment. Historical broker tests are not daily permissions.</summary>
public static class BrokerContinuationReviewPolicy
{
    public static BrokerFirstExecutionReview Prepare(BrokerManagedSession prior, BrokerOctober21Continuation continuation,
        BrokerStrategyReview strategyReview, DateTimeOffset sourceUpdatedAt, BrokerAccountProjection account,
        bool storageReady, string implementation, Core.Trader.DelphiLive.ITsxSessionCalendar calendar, DateTimeOffset now,
        bool validPreOpenDailySource = false)
    {
        continuation.RequirePrior(prior, calendar);
        var scope = continuation.Scope(calendar);
        var strategy = strategyReview.Strategy;
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy, strategy);
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
            RequireCurrentAccount(account, now);
            BrokerTrialCarryForward.RequireUnchangedBook(scope.CashOnlyAnchor, account.Import.Plan.Observation);
        });
        Check("current-risk", "Preserved risk references and holds", () =>
        {
            if (BrokerAccountImport.Hash(account.Risk) != BrokerAccountImport.Hash(continuation.Apply(continuation.PreviousRisk, calendar)))
                throw new InvalidOperationException("Current risk history differs from the reviewed continuation; preserve references and holds for recovery.");
        });
        Check("current-source", "Current Normal strategy, source and build", () =>
        {
            if (!strategyReview.Blocks.IsEmpty || strategy.Strategy.Family != TradingStrategyFamily.DelphiLive ||
                strategy.GateProfile != PaperGateProfile.NormalV1 || strategy.Comparison is null ||
                strategy.Strategy.ImplementationIdentity != implementation ||
                strategyReview.Evidence.PaperAccountId != strategy.PaperAccountId ||
                strategyReview.PreparedUtc.Kind != DateTimeKind.Utc || strategyReview.PreparedUtc > now.UtcDateTime ||
                now.UtcDateTime - strategyReview.PreparedUtc > TimeSpan.FromMinutes(2) ||
                !BrokerOctober21Continuation.SourceReady(continuation, sourceUpdatedAt, calendar, now, validPreOpenDailySource))
                throw new InvalidOperationException("Refresh the exact current Normal definition, source and running build before approval.");
        });
        Check("current-storage", "Installed immutable account and permission storage", () =>
        {
            if (!storageReady) throw new InvalidOperationException("Current immutable baseline, continuation and session storage checks must pass before approval.");
        });
        var ceiling = System.Math.Min(System.Math.Min(account.Import.Plan.Observation.Cash ?? 0,
            account.Import.Plan.Observation.BuyingPower ?? 0), prior.EffectiveReview.PurchaseCeiling - prior.CommittedPurchaseCost);
        BrokerActivationReview? activation = null;
        Check("dated-permission", "October 21 scope and purchase cutoff", () =>
        {
            if (DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime)) != scope.Session ||
                now < calendar.GetSessionBounds(scope.Session).OpenUtc && !continuation.PreOpenApproved || now >= scope.PurchaseExpiresAt || ceiling <= 0)
                throw new InvalidOperationException("Review October 21 permission on its date, with explicit pre-open approval when applicable, remaining allowance and the unchanged close cutoff.");
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

    public static void RequireCurrentAccount(BrokerAccountProjection account, DateTimeOffset now)
    {
        var observation = account.Import.Plan.Observation;
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (now == default || account.Account.ReportedUtc > now.UtcDateTime ||
            now.UtcDateTime - account.Account.ReportedUtc > TimeSpan.FromSeconds(30) ||
            observation.SnapshotAt > now || now - observation.SnapshotAt > TimeSpan.FromSeconds(30) ||
            observation.CheckedAt < observation.SnapshotAt || observation.CheckedAt > now ||
            !observation.HoldingsMatch || observation.Blocks.Count != 0 || observation.HistoryConflicts.Count != 0 ||
            observation.Holdings.Any(h => h.BrokerQuantity != 0) || observation.Orders.Any(o => !o.IsTerminal) ||
            observation.Orders.Select(o => o.ExternalId).Distinct().Count() != observation.Orders.Count ||
            !account.Positions.IsEmpty || !account.Account.Holdings.IsEmpty || !account.NewRiskBlocks.IsEmpty ||
            account.Risk.DailyLossHold || account.Risk.CapitalReviewRequired ||
            account.Account.DailyLossHold || account.Account.CapitalReviewRequired ||
            observation.Cash is not > 0 || observation.BuyingPower is not > 0 || observation.AccountValue != observation.Cash ||
            account.Account.AvailableCash != System.Math.Min(observation.Cash ?? 0, observation.BuyingPower ?? 0) ||
            account.Risk.TradingDate != date || account.Account.TradingDate != date)
            throw new InvalidOperationException("Refresh and reconcile the current cash-only account, cash/buying power and orders; resolve holds or account blockers before approval.");
    }
}

/// <summary>Prepare the accepted cash-start Normal review. It does not save approval, enable a host or send orders.</summary>
public static class BrokerFirstExecutionReviewPolicy
{
    public static BrokerFirstExecutionReview Prepare(BrokerStrategyReview strategyReview, BrokerAccountProjection account,
        BrokerExecutionQualificationBundle? evidence, string implementation, DateTimeOffset now)
    {
        if (now == default) throw new ArgumentException("Execution review requires its current evaluation time.", nameof(now));
        var strategy = strategyReview.Strategy; strategy.Validate();
        var blocks = ImmutableArray.CreateBuilder<string>();
        blocks.AddRange(strategyReview.Blocks); blocks.AddRange(account.NewRiskBlocks);
        if (strategy.Strategy.Family != TradingStrategyFamily.DelphiLive || strategy.GateProfile != PaperGateProfile.NormalV1 || strategy.Comparison is null)
            blocks.Add("Select the exact current Delphi Live Normal comparison for this first execution review.");
        if (strategy.Strategy.ImplementationIdentity != implementation)
            blocks.Add("The complete strategy must identify this running implementation.");
        if (strategyReview.Evidence.PaperAccountId != strategy.PaperAccountId || strategyReview.PreparedUtc.Kind != DateTimeKind.Utc ||
            strategyReview.PreparedUtc > now.UtcDateTime || now.UtcDateTime - strategyReview.PreparedUtc > TimeSpan.FromMinutes(2))
            blocks.Add("Refresh the complete definition and evidence for this exact Normal source before approval.");
        if (account.Account.ReportedUtc > now.UtcDateTime || now.UtcDateTime - account.Account.ReportedUtc > TimeSpan.FromSeconds(30) ||
            account.Import.Plan.Observation.SnapshotAt > now || now - account.Import.Plan.Observation.SnapshotAt > TimeSpan.FromSeconds(30))
            blocks.Add("Refresh the actual account immediately before the first execution review.");
        if (!account.Positions.IsEmpty || !account.Account.Holdings.IsEmpty)
            blocks.Add("Both reviewed opening holdings must sell completely and their inventories must reconcile before approval.");
        if (account.Risk.DailyLossHold || account.Risk.CapitalReviewRequired || account.Account.DailyLossHold || account.Account.CapitalReviewRequired)
            blocks.Add("Existing account loss or capital-review holds remain in force; execution review cannot release them.");
        decimal ceiling = System.Math.Min(account.Import.Plan.Observation.Cash ?? 0, account.Import.Plan.Observation.BuyingPower ?? 0);
        if (ceiling <= 0 || account.Account.AvailableCash != ceiling)
            blocks.Add("Positive actual CAD cash and buying power must reconcile before approving new purchases.");
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        if (account.Risk.TradingDate != date || account.Account.TradingDate != date)
            blocks.Add("The saved risk references and actual account must belong to this reviewed session.");
        bool compatible = false;
        if (evidence is null) blocks.Add("No independently reviewed native qualification packet is installed for this account and strategy.");
        else
        {
            try
            {
                evidence.Validate(now);
                compatible = evidence.BindingId == account.Risk.BindingId && evidence.StrategyFingerprint == strategy.Fingerprint &&
                    evidence.ImplementationIdentity == implementation && evidence.Session == date;
                if (!compatible) blocks.Add("The qualification packet belongs to another account, strategy, build or session.");
            }
            catch (InvalidOperationException error) { blocks.Add(error.Message); }
        }
        var scope = compatible ? evidence!.NoTransferScope : null;
        var required = BrokerActivationReview.ChecksFor(scope);
        var checks = required.Select(check =>
        {
            var records = compatible ? evidence!.Qualifications.Where(q => q.Check == check).ToArray() : [];
            bool verified = records.Length == 1;
            return new BrokerExecutionReviewCheck(check, Label(check), verified,
                verified ? "Reviewed evidence reference is present; final account checks still apply." : "Native qualification is missing or incompatible.");
        }).ToImmutableArray();
        blocks.AddRange(checks.Where(c => !c.Verified).Select(c => c.Label + ": native qualification required."));
        BrokerActivationReview? activation = null;
        if (blocks.Count == 0)
        {
            activation = new(Guid.NewGuid(), account.Risk.BindingId, strategy, account.Risk.BaselineId, account.Fingerprint,
                date, now, ceiling, [], [], [], evidence!.Qualifications)
                { ReviewedPositions = account.Positions, Duration = BrokerExecutionDuration.Ongoing, NoTransferScope = scope };
            try { activation.Validate(account, now); }
            catch (InvalidOperationException error) { blocks.Add(error.Message); activation = null; }
        }
        return new(strategy, ceiling, checks, blocks.Distinct().ToImmutableArray(), activation);
    }

    public static string Label(string check) => check switch
    {
        "cash-events" => "Native cash-event coverage",
        "opening-values" => "Native opening and risk-reference evidence",
        "native-order-lifecycle" => "Native broker order lifecycle",
        "strategy-evidence" => "Exact Normal strategy and current source evidence",
        "restart-recovery" => "Durable restart and original-order recovery",
        "installed-storage" => "Installed storage and recovery verification",
        "supervised-no-transfer-cash" => "Reviewed first-day cash reconciliation",
        "scoped-initial-reference" => "Actual initial scoped risk reference",
        "opening-native-sale-fills" => "Both native opening sales and inventories",
        "operator-supervised-trial" => "Explicit supervised-session scope",
        "scoped-durable-recovery" => "Scoped permission and recovery verification",
        _ => "Unrecognized qualification"
    };
}
