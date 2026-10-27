#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Explicit, append-only replacement of dated permission; financial history is retained.</summary>
public sealed record BrokerOngoingManagement(Guid RequestId, Guid SessionId, Guid PreviousRevisionId,
    string PreviousSessionHash, string PreviousRiskHash, BrokerActivationReview Review,
    BrokerAccountReconciliation CashAnchor, DateTimeOffset PreparedAt, string Reviewer,
    bool NoOutsideChangesConfirmed, bool PauseBeforeOutsideChanges, DateTimeOffset? ApprovedAt = null)
{
    public const string Contract = "OngoingNoTransferManagementV1";
    [JsonIgnore] public string Fingerprint => BrokerAccountImport.Hash(this);
    [JsonIgnore] public BrokerOngoingRiskAuthorization RiskAuthorization => new(RequestId, Review.BindingId, Review.BaselineId,
        PreviousRiskHash, PreviousSessionHash, PreparedAt, Reviewer);

    public static BrokerOngoingManagement Prepare(BrokerManagedSession prior, BrokerManagedStrategy strategy,
        DateTimeOffset now, string reviewer, bool noOutsideChanges, bool pauseBeforeChanges)
    {
        var origin = prior.EffectiveReview.NoTransferScope
            ?? throw new InvalidOperationException("Recover the existing dated permission before its explicit ongoing review.");
        var id = Guid.NewGuid();
        var value = new BrokerOngoingManagement(id, prior.SessionId, prior.RevisionId,
            BrokerSessionContinuation.StateHash(prior), BrokerAccountImport.Hash(prior.Account.Risk),
            prior.EffectiveReview with { RequestId = id, Strategy = strategy, PreparedAt = now,
                Duration = BrokerExecutionDuration.Ongoing, NoTransferScope = null },
            origin.CashOnlyAnchor, now, reviewer, noOutsideChanges, pauseBeforeChanges);
        value.RequirePrior(prior);
        return value;
    }

    public void RequirePrior(BrokerManagedSession prior)
    {
        Review.Strategy.Validate();
        BrokerSessionContinuation.RequireSameDefinition(prior.Strategy, Review.Strategy);
        if (RequestId == Guid.Empty || Review.RequestId != RequestId || Review.BindingId != prior.BindingId ||
            Review.BaselineId != prior.Account.Risk.BaselineId || SessionId != prior.SessionId ||
            PreviousRevisionId != prior.RevisionId || PreviousSessionHash != BrokerSessionContinuation.StateHash(prior) ||
            PreviousRiskHash != BrokerAccountImport.Hash(prior.Account.Risk) || prior.OngoingManagement is not null ||
            !NoOutsideChangesConfirmed || !PauseBeforeOutsideChanges || string.IsNullOrWhiteSpace(Reviewer) || Reviewer.Length > 128 ||
            PreparedAt < prior.SavedAt || Review.Duration != BrokerExecutionDuration.Ongoing || Review.NoTransferScope is not null ||
            Review.PurchaseCeiling != prior.EffectiveReview.PurchaseCeiling || Review.PurchaseCeiling <= prior.CommittedPurchaseCost ||
            prior.EffectiveReview.NoTransferScope is not { FullFillOnlyNativeTrial: true } scope ||
            BrokerAccountImport.Hash(CashAnchor) != BrokerAccountImport.Hash(scope.CashOnlyAnchor) ||
            prior.Phase is not (BrokerManagedPhase.Active or BrokerManagedPhase.Paused) || prior.ActivatedAt is null ||
            prior.ReviewInvalidated || prior.TrialOrderReviewRequired || prior.Account.Risk.DailyLossHold ||
            prior.Account.Risk.CapitalReviewRequired || prior.Memory.DailyLossHold || prior.Memory.CapitalReviewRequired ||
            !prior.Orders.IsEmpty || !prior.Account.Positions.IsEmpty || !prior.Protection.IsEmpty)
            throw new InvalidOperationException("Ongoing review requires the exact unchanged cash-only assignment, original allowance and risk history, and explicit agreement to pause before outside activity.");
        BrokerTrialCarryForward.RequireUnchangedBook(CashAnchor, prior.Account.Import.Plan.Observation);
    }

    public void RequireCurrentAccount(BrokerAccountProjection account, DateTimeOffset now)
    {
        var observed = account.Import.Plan.Observation;
        if (PreparedAt > now || now - PreparedAt > TimeSpan.FromMinutes(2) || ApprovedAt is not null ||
            !observed.HoldingsMatch || !account.Positions.IsEmpty || observed.Orders.Any(o => !o.IsTerminal) ||
            observed.SnapshotAt > now || now - observed.SnapshotAt > TimeSpan.FromSeconds(30) ||
            observed.CheckedAt < observed.SnapshotAt || observed.CheckedAt > now ||
            account.Risk.BindingId != Review.BindingId || account.Risk.BaselineId != Review.BaselineId ||
            account.Risk.DailyLossHold || account.Risk.CapitalReviewRequired ||
            account.NewRiskBlocks.Any(b => b != "Continue the saved account risk session before new buys." &&
                b != "Trial-carried references require their exact next-session trial scope; they cannot qualify production or older scopes."))
            throw new InvalidOperationException("Refresh the unchanged account and ongoing review before approving.");
        BrokerTrialCarryForward.RequireUnchangedBook(CashAnchor, observed);
    }

    public override string ToString() => "Explicit ongoing no-transfer agreement (financial data omitted)";
}

public sealed record BrokerOngoingRiskAuthorization(Guid RequestId, Guid BindingId, Guid BaselineId,
    string PreviousRiskHash, string PreviousSessionHash, DateTimeOffset ReviewedAt, string Reviewer)
{
    public BrokerRiskContinuationRequest Request(Guid? previousRevision) => new(RequestId, BaselineId, previousRevision,
        BrokerRiskContinuationKind.OngoingAuthorization, null, null, null) { OngoingAuthorization = this };
    public BrokerAccountRiskState Apply(BrokerAccountRiskState state, DateTimeOffset now)
    {
        if (RequestId == Guid.Empty || BindingId != state.BindingId || BaselineId != state.BaselineId ||
            PreviousRiskHash != BrokerAccountImport.Hash(state) || !CompleteTradingStrategy.IsHash(PreviousSessionHash) ||
            ReviewedAt > now || now - ReviewedAt > TimeSpan.FromMinutes(2) || string.IsNullOrWhiteSpace(Reviewer) ||
            state.OngoingAuthorization is not null || state.SessionContinuation is null ||
            state.DailyLossHold || state.CapitalReviewRequired)
            throw new InvalidOperationException("Explicit ongoing authority must preserve the exact original risk state.");
        // Old permission objects remain in immutable historical receipts. References and valuation boundaries do not change.
        return BrokerSessionContinuation.Anchor(state) with { RevisionId = RequestId, OngoingAuthorization = this };
    }
}

public sealed class BrokerOngoingManagementWorkflow(IBrokerManagedSessionStore sessions,
    IBrokerAccountRiskContinuationStore risks, IBrokerManagedAccountSource source,
    IBrokerExecutionHostGrantStore grants, ITsxSessionCalendar calendar,
    Func<CancellationToken, Task> verifyReadiness, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public async Task<BrokerManagedSession> ApproveAsync(BrokerOngoingManagement review, CancellationToken ct = default)
    {
        await using var lease = await sessions.AcquireAsync(review.Review.BindingId, ct);
        var prior = lease.Current ?? throw new InvalidOperationException("Recover the existing assignment.");
        if (prior.OngoingManagement is { } recovered)
        {
            if (BrokerAccountImport.Hash(recovered with { ApprovedAt = null }) != BrokerAccountImport.Hash(review))
                throw new InvalidOperationException("Recover the already approved ongoing agreement; do not replace it.");
            return prior;
        }
        RequireHostOff(); review.RequirePrior(prior);
        if (source.ImplementationIdentity != review.Review.Strategy.Strategy.ImplementationIdentity)
            throw new InvalidOperationException("Review the exact running implementation.");
        var account = await source.SynchronizeAsync(prior.Orders, ct);
        review.RequireCurrentAccount(account, clock.GetUtcNow());
        await verifyReadiness(ct);
        var request = review.RiskAuthorization.Request(prior.Account.Risk.RevisionId);
        var receipt = await risks.FindAsync(request.RequestId, ct);
        var actual = await risks.ReadAsync(review.Review.BaselineId, ct);
        var expected = review.RiskAuthorization.Apply(prior.Account.Risk, clock.GetUtcNow());
        if (receipt is not null) BrokerAccountRiskContinuationWorkflow.RequireSameRequest(request, receipt);
        if (BrokerAccountImport.Hash(actual) != (receipt is null ? review.PreviousRiskHash : BrokerAccountImport.Hash(expected)))
            throw new InvalidOperationException("Risk advanced during ongoing review; preserve the saved request for recovery.");
        if (BrokerAccountImport.Hash(account.Risk) != review.PreviousRiskHash &&
            BrokerAccountImport.Hash(account.Risk) != BrokerAccountImport.Hash(expected))
            throw new InvalidOperationException("The reconciled account must carry the exact reviewed risk references.");
        RequireHostOff();
        receipt ??= await new BrokerAccountRiskContinuationWorkflow(risks, calendar, time).ApplyAsync(request, ct);
        var readback = await risks.ReadAsync(review.Review.BaselineId, ct);
        if (BrokerAccountImport.Hash(readback) != BrokerAccountImport.Hash(expected))
            throw new InvalidOperationException("Recover the ongoing risk receipt before enabling execution.");
        var now = clock.GetUtcNow(); review.RequireCurrentAccount(account, now); RequireHostOff();
        var next = prior with { Revision = prior.Revision + 1, PreviousRevisionId = prior.RevisionId,
            RevisionId = review.RequestId, SavedAt = now, Event = "OngoingManagementApproved",
            OngoingManagement = review with { ApprovedAt = now }, Account = account with { Risk = readback }, Blocks = [] };
        ValidateAppend(prior, next); await lease.AppendAsync(next, ct); return next;
    }
    private void RequireHostOff()
    {
        if (grants.Read()?.Enabled == true) throw new InvalidOperationException("Stop execution for the initial ongoing review. Existing dated grants cannot become ongoing authority.");
    }
    public static void ValidateAppend(BrokerManagedSession prior, BrokerManagedSession next)
    {
        var value = next.OngoingManagement ?? throw new InvalidOperationException("Missing ongoing agreement.");
        (value with { ApprovedAt = null }).RequirePrior(prior);
        if (next.Event != "OngoingManagementApproved" || value.ApprovedAt != next.SavedAt ||
            next.Account.Risk.OngoingAuthorization != value.RiskAuthorization ||
            BrokerAccountImport.Hash(next.Account.Risk) != BrokerAccountImport.Hash(value.RiskAuthorization.Apply(prior.Account.Risk, next.SavedAt)) ||
            BrokerAccountImport.Hash(next with { OngoingManagement = prior.OngoingManagement, Account = prior.Account,
                Blocks = prior.Blocks, Revision = prior.Revision, RevisionId = prior.RevisionId,
                PreviousRevisionId = prior.PreviousRevisionId, Event = prior.Event, SavedAt = prior.SavedAt }) != BrokerAccountImport.Hash(prior))
            throw new InvalidOperationException("Ongoing approval must preserve existing financial history, controls, orders and protection.");
    }
}
