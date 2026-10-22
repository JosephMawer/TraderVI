#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>An explicit prospective source replacement, retaining the original dated financial permission.</summary>
public sealed record BrokerLiveSourceSwitch(Guid RequestId, Guid PreviousRevisionId, string PreviousSessionHash,
    BrokerStrategyReview SourceReview, DateTimeOffset SourceUpdatedAt, DateTimeOffset PreparedAt,
    DateTimeOffset ApprovedAt, string Reviewer)
{
    public override string ToString() => "Reviewed Live source switch (financial data omitted)";
}
public sealed record BrokerLiveSourceSwitchProposal(Guid BindingId, BrokerLiveSourceSwitch Review);

public sealed class BrokerLiveSourceSwitchWorkflow(IBrokerManagedSessionStore sessions, IBrokerManagedAccountSource source,
    IBrokerExecutionHostGrantStore grants, ITsxSessionCalendar calendar, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public static BrokerLiveSourceSwitchProposal Prepare(BrokerManagedSession prior, BrokerStrategyReview target,
        DateTimeOffset updated, BrokerAccountProjection account, bool storageReady, string implementation,
        string reviewer, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        RequireSession(prior, calendar, now);
        RequireTarget(prior.Strategy, target.Strategy);
        RequireAccount(prior, account, now);
        if (!storageReady || !target.Blocks.IsEmpty || target.Strategy.Strategy.ImplementationIdentity != implementation ||
            string.IsNullOrWhiteSpace(reviewer) || reviewer.Length > 128 || target.Evidence.PaperAccountId != target.Strategy.PaperAccountId)
            throw new InvalidOperationException("Review the exact Portfolio rules only definition, disclosed evidence and installed immutable storage.");
        var id = BrokerManagedStrategy.StableId($"{prior.RevisionId:D}:PortfolioRulesOnly:{target.Strategy.Fingerprint}");
        var review = new BrokerLiveSourceSwitch(id, prior.RevisionId, BrokerAccountImport.Hash(prior), target,
            updated, now, default, reviewer);
        RequireFresh(review, now);
        return new(prior.BindingId, review);
    }

    public async Task<BrokerManagedSession> ApproveAsync(BrokerLiveSourceSwitchProposal proposal, CancellationToken ct = default)
    {
        RequireHostOff();
        await using var lease = await sessions.AcquireAsync(proposal.BindingId, ct);
        var prior = lease.Current ?? throw new InvalidOperationException("Recover the existing account assignment before switching source.");
        var review = proposal.Review;
        if (prior.SourceSwitch?.RequestId == review.RequestId)
        {
            if (BrokerAccountImport.Hash(prior.SourceSwitch with { ApprovedAt = default }) != BrokerAccountImport.Hash(review) ||
                source.ImplementationIdentity != review.SourceReview.Strategy.Strategy.ImplementationIdentity)
                throw new InvalidOperationException("Recover the original source-switch request; its content or build changed.");
            return prior; // A lost acknowledgement returns current state, never enables execution or creates another approval.
        }
        RequireSession(prior, calendar, clock.GetUtcNow());
        RequireFresh(review, clock.GetUtcNow());
        if (review.ApprovedAt != default || prior.RevisionId != review.PreviousRevisionId ||
            BrokerAccountImport.Hash(prior) != review.PreviousSessionHash ||
            source.ImplementationIdentity != review.SourceReview.Strategy.Strategy.ImplementationIdentity)
            throw new InvalidOperationException("The saved assignment or running build changed. Review the switch again.");
        RequireTarget(prior.Strategy, review.SourceReview.Strategy);
        var account = await source.SynchronizeAsync(prior.Orders, ct);
        var now = clock.GetUtcNow();
        RequireHostOff(); RequireSession(prior, calendar, now); RequireFresh(review, now); RequireAccount(prior, account, now);
        if (source.ImplementationIdentity != review.SourceReview.Strategy.Strategy.ImplementationIdentity)
            throw new InvalidOperationException("The running build changed during source review.");
        var next = prior with { Revision = prior.Revision + 1, RevisionId = review.RequestId,
            PreviousRevisionId = prior.RevisionId, Event = "PortfolioRulesOnlyApproved", SavedAt = now,
            SourceSwitch = review with { ApprovedAt = now }, Account = account, Memory = FreshSignals(prior.Memory, now) };
        ValidateAppend(prior, next, calendar);
        await lease.AppendAsync(next, ct);
        return next; // No risk write, order, paper mutation or host enable.
    }

    private void RequireHostOff()
    {
        if (grants.Read()?.Enabled == true)
            throw new InvalidOperationException("Stop the execution host normally before reviewing or approving a source switch. Enable the reviewed host separately afterwards.");
    }
    private static void RequireFresh(BrokerLiveSourceSwitch review, DateTimeOffset now)
    {
        if (review.PreparedAt > now || now - review.PreparedAt > TimeSpan.FromMinutes(2) ||
            review.SourceUpdatedAt > now || now - review.SourceUpdatedAt > TimeSpan.FromMinutes(2) ||
            review.SourceReview.PreparedUtc.Kind != DateTimeKind.Utc || review.SourceReview.PreparedUtc > now.UtcDateTime ||
            now.UtcDateTime - review.SourceReview.PreparedUtc > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("The source-switch review or source expired. Review the current source again.");
    }
    private static void RequireSession(BrokerManagedSession prior, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        var scope = prior.EffectiveReview.NoTransferScope;
        scope?.Validate(calendar);
        var bounds = calendar.GetSessionBounds(BrokerOctober21Continuation.ExecutionDate);
        if (prior.SourceSwitch is not null || prior.Continuation is null || prior.ActivatedAt is null ||
            scope?.Session != BrokerOctober21Continuation.ExecutionDate || scope.FullFillOnlyNativeTrial != true ||
            now < bounds.OpenUtc || now >= bounds.CloseUtc || scope.PurchaseExpiresAt != bounds.CloseUtc ||
            prior.Strategy.GateProfile != PaperGateProfile.NormalV1 || prior.ReviewInvalidated || prior.TrialOrderReviewRequired ||
            prior.Memory.DailyLossHold || prior.Memory.CapitalReviewRequired || !prior.Orders.IsEmpty ||
            !prior.Account.Positions.IsEmpty || !prior.Protection.IsEmpty)
            throw new InvalidOperationException("This switch requires today's existing cash-only Normal continuation before 4 PM, without automatic orders, positions or holds. Other states require separate recovery.");
    }
    private static void RequireAccount(BrokerManagedSession prior, BrokerAccountProjection account, DateTimeOffset now)
    {
        BrokerContinuationReviewPolicy.RequireCurrentAccount(account, now);
        BrokerTrialCarryForward.RequireUnchangedBook(prior.EffectiveReview.NoTransferScope!.CashOnlyAnchor, account.Import.Plan.Observation);
        if (account.Risk.BindingId != prior.BindingId || BrokerAccountImport.Hash(account.Risk) != BrokerAccountImport.Hash(prior.Account.Risk))
            throw new InvalidOperationException("A source switch cannot reset or replace financial risk history, references or holds.");
    }
    public static void RequireTarget(BrokerManagedStrategy previous, BrokerManagedStrategy target)
    {
        previous.Validate(); target.Validate();
        var original = previous.Strategy.ReadLivePolicy(); var incoming = target.Strategy.ReadLivePolicy();
        if (previous.GateProfile != PaperGateProfile.NormalV1 || target.GateProfile != PaperGateProfile.PortfolioRulesOnlyV1 ||
            previous.Comparison is null || target.Comparison is null || previous.PaperAccountId == target.PaperAccountId ||
            previous.Comparison.BatchId != target.Comparison.BatchId || previous.Comparison.PairId != target.Comparison.PairId ||
            BrokerAccountImport.Hash(original with { PolicyVersionId = incoming.PolicyVersionId,
                PolicyDefinitionName = incoming.PolicyDefinitionName }) != BrokerAccountImport.Hash(incoming) ||
            (previous.Strategy with { PolicyVersionId = target.Strategy.PolicyVersionId, LivePolicy = incoming,
                ImplementationIdentity = target.Strategy.ImplementationIdentity }).Fingerprint != target.Strategy.Fingerprint)
            throw new InvalidOperationException("Switch only to the existing matched Portfolio rules only source with the same daily/model, intraday, sizing and risk settings. No other policy change is approved.");
    }
    internal static TradingRecommendationMemory FreshSignals(TradingRecommendationMemory prior, DateTimeOffset now) =>
        prior with { Symbols = prior.Symbols.ToImmutableDictionary(p => p.Key, p => p.Value with
        { LiveEvaluation = null, LiveSessionId = null, LiveEpoch = null, LastLiveResult = null, LastLiveBarUtc = now.UtcDateTime },
            StringComparer.OrdinalIgnoreCase) };

    public static void ValidateAppend(BrokerManagedSession prior, BrokerManagedSession next, ITsxSessionCalendar calendar)
        => ValidateAppend(prior, next, calendar, null);
    internal static void ValidateAppend(BrokerManagedSession prior, BrokerManagedSession next, ITsxSessionCalendar calendar, BrokerHistoricalStateHash? historical)
    {
        RequireSession(prior, calendar, next.SavedAt);
        var review = next.SourceSwitch ?? throw new InvalidOperationException("The exact source-switch review is required.");
        RequireFresh(review, next.SavedAt); RequireTarget(prior.Strategy, review.SourceReview.Strategy);
        RequireAccount(prior, next.Account, next.SavedAt);
        if (review.RequestId != BrokerManagedStrategy.StableId($"{prior.RevisionId:D}:PortfolioRulesOnly:{review.SourceReview.Strategy.Fingerprint}") ||
            next.Event != "PortfolioRulesOnlyApproved" || next.RevisionId != review.RequestId ||
            review.PreviousRevisionId != prior.RevisionId || !BrokerHistoricalStateHash.Matches(prior, review.PreviousSessionHash, historical) ||
            review.ApprovedAt != next.SavedAt || string.IsNullOrWhiteSpace(review.Reviewer) || review.Reviewer.Length > 128 ||
            !review.SourceReview.Blocks.IsEmpty || review.SourceReview.Evidence.PaperAccountId != next.Strategy.PaperAccountId ||
            BrokerAccountImport.Hash(next.Memory) != BrokerAccountImport.Hash(FreshSignals(prior.Memory, next.SavedAt)) ||
            BrokerAccountImport.Hash(next with { Revision = prior.Revision, RevisionId = prior.RevisionId,
                PreviousRevisionId = prior.PreviousRevisionId, Event = prior.Event, SavedAt = prior.SavedAt,
                SourceSwitch = null, Account = prior.Account, Memory = prior.Memory }) != BrokerAccountImport.Hash(prior))
            throw new InvalidOperationException("Source switch must preserve original approvals, financial history/risk, orders, protection and buying/paused modes. Only new-source signal history starts afresh.");
    }
}
