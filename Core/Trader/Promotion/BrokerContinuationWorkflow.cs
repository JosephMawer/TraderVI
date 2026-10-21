#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Explicit dated approval. Risk is appended first; an interrupted pair grants no execution.
/// Retrying recovers the original request, never invents a replacement financial event.</summary>
public sealed class BrokerContinuationWorkflow(IBrokerManagedSessionStore sessions,
    IBrokerAccountRiskContinuationStore risks, IBrokerManagedAccountSource source,
    IBrokerExecutionHostGrantStore grants, ITsxSessionCalendar calendar, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public async Task<BrokerManagedSession> ApproveAsync(BrokerExecutionApproval approval, CancellationToken ct = default)
    {
        var scope = approval.Review.NoTransferScope ?? throw new InvalidOperationException("Review the dated continuation first.");
        var continuation = scope.Continuation ?? throw new InvalidOperationException("A first-session review cannot renew buying permission.");
        scope.Validate(calendar);
        if (string.IsNullOrWhiteSpace(approval.Reviewer) || approval.Reviewer.Length > 128 ||
            string.IsNullOrWhiteSpace(approval.Reason) || approval.Reason.Length > 1024 ||
            approval.Review.RequestId != scope.ScopeId)
            throw new InvalidOperationException("An identified explicit October 21 continuation approval is required.");
        await using var lease = await sessions.AcquireAsync(scope.BindingId, ct);
        var prior = lease.Current ?? throw new InvalidOperationException("Recover the existing assignment; do not repeat initial setup.");
        RequireHostOff();
        var request = continuation.RiskRequest();
        if (prior.Continuation is { } recovered)
        {
            if (recovered.RiskRequest.RequestHash != request.RequestHash)
                throw new InvalidOperationException("Recover the already saved continuation; no replacement permission is allowed.");
            var risk = await risks.ReadAsync(scope.BaselineId, ct);
            if (risk.Continuation?.Fingerprint != continuation.Fingerprint)
                throw new InvalidOperationException("The saved permission and risk association disagree. Keep execution off for recovery.");
            return prior;
        }
        continuation.RequirePrior(prior, calendar);
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy, approval.Review.Strategy);
        if (approval.Review.PurchaseCeiling <= 0 ||
            approval.Review.PurchaseCeiling > prior.EffectiveReview.PurchaseCeiling - prior.CommittedPurchaseCost)
            throw new InvalidOperationException("Continuation cannot refill or enlarge the original cumulative purchase allowance.");
        var expected = continuation.Apply(continuation.PreviousRisk, calendar);
        var riskState = await risks.ReadAsync(scope.BaselineId, ct);
        var savedRisk = await risks.FindAsync(request.RequestId, ct);
        if (savedRisk is not null)
        {
            BrokerAccountRiskContinuationWorkflow.RequireSameRequest(request, savedRisk);
            if (BrokerAccountImport.Hash(riskState) != BrokerAccountImport.Hash(savedRisk.State))
                throw new InvalidOperationException("Risk history advanced after a partial continuation save. Preserve the original request for recovery.");
        }
        else if (BrokerAccountImport.Hash(riskState) != BrokerAccountImport.Hash(continuation.PreviousRisk))
            throw new InvalidOperationException("Risk history changed since review. No continuation was approved.");
        var account = await source.SynchronizeAsync(prior.Orders, ct);
        BrokerTrialCarryForward.RequireUnchangedBook(scope.CashOnlyAnchor, account.Import.Plan.Observation);
        approval.Review.Validate(account, clock.GetUtcNow());
        if (source.ImplementationIdentity != approval.Review.Strategy.Strategy.ImplementationIdentity ||
            BrokerAccountImport.Hash(account.Risk) != BrokerAccountImport.Hash(expected))
            throw new InvalidOperationException("The reviewed account, risk association or running build changed.");
        RequireHostOff();
        savedRisk ??= await new BrokerAccountRiskContinuationWorkflow(risks, calendar, time).ApplyAsync(request, ct);
        var readback = await risks.ReadAsync(scope.BaselineId, ct);
        if (BrokerAccountImport.Hash(readback) != BrokerAccountImport.Hash(expected) ||
            BrokerAccountImport.Hash(savedRisk.State) != BrokerAccountImport.Hash(expected))
            throw new InvalidOperationException("Continuation risk save needs original-request recovery. Execution remains off.");
        RequireHostOff();
        var now = clock.GetUtcNow(); approval.Review.Validate(account, now);
        var next = prior with { Revision = prior.Revision + 1, PreviousRevisionId = prior.RevisionId,
            RevisionId = request.RequestId, SavedAt = now, Event = "October21ContinuationApproved",
            Continuation = new(approval, request), Account = account, Blocks = [] };
        ValidateAppend(prior, next, calendar);
        await lease.AppendAsync(next, ct);
        return next;
    }
    private void RequireHostOff()
    {
        if (grants.Read()?.Enabled == true)
            throw new InvalidOperationException("Stop the execution host normally before reviewing or saving a continuation. Approval never inherits an old host grant.");
    }

    public static void ValidateAppend(BrokerManagedSession prior, BrokerManagedSession next, ITsxSessionCalendar calendar)
    {
        if (prior.Continuation is not null || next.Continuation is not { } value || next.Event != "October21ContinuationApproved")
            throw new InvalidOperationException("Only one explicitly approved October 21 continuation may be appended.");
        var continuation = value.RiskRequest.Continuation ?? throw new InvalidOperationException("Missing continuation risk association.");
        continuation.RequirePrior(prior, calendar);
        var scope = value.Approval.Review.NoTransferScope;
        scope?.Validate(calendar);
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy, next.Strategy);
        if (value.RiskRequest.RequestHash != continuation.RiskRequest().RequestHash ||
            scope?.Continuation?.Fingerprint != continuation.Fingerprint ||
            value.Approval.Review.RequestId != value.RiskRequest.RequestId ||
            value.Approval.Review.Session != BrokerOctober21Continuation.ExecutionDate ||
            value.Approval.Review.PurchaseCeiling > prior.EffectiveReview.PurchaseCeiling - prior.CommittedPurchaseCost ||
            BrokerAccountImport.Hash(next.Account.Risk) != BrokerAccountImport.Hash(continuation.Apply(continuation.PreviousRisk, calendar)) ||
            BrokerAccountImport.Hash(prior.Memory) != BrokerAccountImport.Hash(next.Memory) ||
            BrokerAccountImport.Hash(prior.Protection) != BrokerAccountImport.Hash(next.Protection) ||
            BrokerAccountImport.Hash(prior.Orders) != BrokerAccountImport.Hash(next.Orders) ||
            next.NewBuysStopped != prior.NewBuysStopped || next.Phase != prior.Phase ||
            next.Approval.Fingerprint != prior.Approval.Fingerprint || next.ActivatedAt != prior.ActivatedAt)
            throw new InvalidOperationException("Continuation must preserve original authority/history, orders, risk memory, protection and buying controls.");
        value.Approval.Review.Validate(next.Account, next.SavedAt);
    }
}

/// <summary>Read-only prospective risk view used for review, never by execution. Appends are prohibited.</summary>
public sealed class BrokerContinuationPreviewRiskStore(IBrokerAccountRiskContinuationStore original,
    BrokerOctober21Continuation continuation, ITsxSessionCalendar calendar) : IBrokerAccountRiskContinuationStore
{
    public Task<BrokerRiskContinuationReceipt?> FindAsync(Guid id, CancellationToken ct) => original.FindAsync(id, ct);
    public Task<BrokerRiskContinuationReceipt> AppendAsync(BrokerRiskContinuationRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("Continuation review cannot save financial risk events.");
    public async Task<BrokerAccountRiskState> ReadAsync(Guid baseline, CancellationToken ct)
    {
        var actual = await original.ReadAsync(baseline, ct);
        var expected = continuation.Apply(continuation.PreviousRisk, calendar);
        if (BrokerAccountImport.Hash(actual) != BrokerAccountImport.Hash(continuation.PreviousRisk) &&
            BrokerAccountImport.Hash(actual) != BrokerAccountImport.Hash(expected))
            throw new InvalidOperationException("Risk changed during continuation review. Preserve all original records.");
        return expected;
    }
}
