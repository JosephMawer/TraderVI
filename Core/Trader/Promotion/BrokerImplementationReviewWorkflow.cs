#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Append-only approval of a changed implementation, retaining the same dated financial permission.</summary>
public sealed record BrokerImplementationReview(Guid ReviewId, Guid PreviousRevisionId, string PreviousSessionHash,
    string PreviousImplementation, string ImplementationIdentity, DateTimeOffset PreparedAt, DateTimeOffset ApprovedAt, string ReviewedBy)
{
    public override string ToString() => "Reviewed implementation update (financial data omitted)";
}
public sealed record BrokerImplementationReviewProposal(Guid BindingId, BrokerImplementationReview Review,
    BrokerManagedStrategy Strategy, DateTimeOffset SourceUpdatedAt, DateTimeOffset StrategyPreparedAt);

public sealed class BrokerImplementationReviewWorkflow(IBrokerManagedSessionStore sessions, IBrokerManagedAccountSource source,
    IBrokerExecutionHostGrantStore grants, ITsxSessionCalendar calendar, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public static BrokerImplementationReviewProposal Prepare(BrokerManagedSession prior, BrokerStrategyReview current,
        DateTimeOffset sourceUpdatedAt, BrokerAccountProjection account, bool storageReady, string implementation,
        string reviewer, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        RequireSession(prior, calendar, now);
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy, current.Strategy);
        if (!storageReady || !current.Blocks.IsEmpty || current.Strategy.Strategy.ImplementationIdentity != implementation ||
            implementation == prior.Strategy.Strategy.ImplementationIdentity || string.IsNullOrWhiteSpace(reviewer) || reviewer.Length > 128 ||
            current.Evidence.PaperAccountId != current.Strategy.PaperAccountId ||
            current.PreparedUtc.Kind != DateTimeKind.Utc || current.PreparedUtc > now.UtcDateTime ||
            now.UtcDateTime - current.PreparedUtc > TimeSpan.FromMinutes(2) ||
            sourceUpdatedAt > now || now - sourceUpdatedAt > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("Review the fresh matching source, immutable storage and changed running build before approving its implementation.");
        RequireAccount(prior, account, now);
        var id = BrokerManagedStrategy.StableId($"{prior.RevisionId:D}:ImplementationReview:{implementation}");
        return new(prior.BindingId, new(id, prior.RevisionId, BrokerAccountImport.Hash(prior),
            prior.Strategy.Strategy.ImplementationIdentity, implementation, now, default, reviewer), current.Strategy,
            sourceUpdatedAt, new DateTimeOffset(current.PreparedUtc));
    }

    public async Task<BrokerManagedSession> ApproveAsync(BrokerImplementationReviewProposal proposal, CancellationToken ct = default)
    {
        RequireHostOff();
        await using var lease = await sessions.AcquireAsync(proposal.BindingId, ct);
        var prior = lease.Current ?? throw new InvalidOperationException("Recover the existing assignment before reviewing a build.");
        if (prior.ImplementationReview?.ReviewId == proposal.Review.ReviewId)
        {
            if (prior.ImplementationReview with { ApprovedAt = default } != proposal.Review ||
                prior.Strategy.Fingerprint != proposal.Strategy.Fingerprint || source.ImplementationIdentity != proposal.Review.ImplementationIdentity)
                throw new InvalidOperationException("The recovered build review differs from its original request.");
            return prior;
        }
        var now = clock.GetUtcNow();
        RequireSession(prior, calendar, now);
        RequireFresh(proposal, now);
        if (prior.BindingId != proposal.BindingId || prior.RevisionId != proposal.Review.PreviousRevisionId || BrokerAccountImport.Hash(prior) != proposal.Review.PreviousSessionHash ||
            proposal.Review.PreviousImplementation != prior.Strategy.Strategy.ImplementationIdentity ||
            source.ImplementationIdentity != proposal.Review.ImplementationIdentity ||
            proposal.Strategy.Strategy.ImplementationIdentity != source.ImplementationIdentity ||
            proposal.Review.ApprovedAt != default)
            throw new InvalidOperationException("Build review is stale or the saved assignment changed. Review the exact current build again.");
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy, proposal.Strategy);
        var account = await source.SynchronizeAsync(prior.Orders, ct);
        RequireAccount(prior, account, clock.GetUtcNow());
        RequireHostOff(); now = clock.GetUtcNow(); RequireSession(prior, calendar, now);
        RequireFresh(proposal, now);
        if (source.ImplementationIdentity != proposal.Review.ImplementationIdentity)
            throw new InvalidOperationException("The running build changed during account review.");
        var next = prior with { Revision = prior.Revision + 1, RevisionId = proposal.Review.ReviewId,
            PreviousRevisionId = prior.RevisionId, Event = "ImplementationReviewed", SavedAt = now,
            ImplementationReview = proposal.Review with { ApprovedAt = now }, Account = account };
        ValidateAppend(prior,next,calendar);
        await lease.AppendAsync(next,ct);
        return next; // No risk event, host enable or order is created.
    }

    private void RequireHostOff()
    {
        if (grants.Read()?.Enabled == true) throw new InvalidOperationException("Stop the execution host before approving a changed build. Old host grants cannot transfer.");
    }
    private static void RequireFresh(BrokerImplementationReviewProposal proposal, DateTimeOffset now)
    {
        if (proposal.Review.PreparedAt > now || now - proposal.Review.PreparedAt > TimeSpan.FromMinutes(2) ||
            proposal.SourceUpdatedAt > now || now - proposal.SourceUpdatedAt > TimeSpan.FromMinutes(2) ||
            proposal.StrategyPreparedAt > now || now - proposal.StrategyPreparedAt > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("The updated-build review or source expired. Review the exact current build again.");
    }
    private static void RequireSession(BrokerManagedSession prior, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        var scope=prior.EffectiveReview.NoTransferScope;
        scope?.Validate(calendar);
        var bounds=calendar.GetSessionBounds(BrokerOctober21Continuation.ExecutionDate);
        if (prior.Continuation is null || prior.SourceSwitch is null || prior.ActivatedAt is null ||
            scope?.Session != BrokerOctober21Continuation.ExecutionDate || scope.FullFillOnlyNativeTrial != true ||
            prior.Strategy.GateProfile != PaperGateProfile.PortfolioRulesOnlyV1 ||
            now < bounds.OpenUtc || now >= bounds.CloseUtc || scope.PurchaseExpiresAt != bounds.CloseUtc ||
            prior.ReviewInvalidated || prior.TrialOrderReviewRequired || prior.Memory.DailyLossHold || prior.Memory.CapitalReviewRequired ||
            !prior.Orders.IsEmpty || !prior.Account.Positions.IsEmpty || !prior.Protection.IsEmpty)
            throw new InvalidOperationException("Build-only review requires today's unchanged cash-only continuation before 4 PM, without orders, positions or holds. Other states need separate recovery.");
    }
    private static void RequireAccount(BrokerManagedSession prior, BrokerAccountProjection account, DateTimeOffset now)
    {
        BrokerContinuationReviewPolicy.RequireCurrentAccount(account,now);
        BrokerTrialCarryForward.RequireUnchangedBook(prior.EffectiveReview.NoTransferScope!.CashOnlyAnchor,account.Import.Plan.Observation);
        if (BrokerAccountImport.Hash(account.Risk) != BrokerAccountImport.Hash(prior.Account.Risk))
            throw new InvalidOperationException("Build review cannot change risk history, references or holds.");
    }
    public static void ValidateAppend(BrokerManagedSession prior, BrokerManagedSession next, ITsxSessionCalendar calendar)
        => ValidateAppend(prior, next, calendar, null);
    internal static void ValidateAppend(BrokerManagedSession prior, BrokerManagedSession next, ITsxSessionCalendar calendar, BrokerHistoricalStateHash? historical)
    {
        RequireSession(prior,calendar,next.SavedAt);
        var review=next.ImplementationReview ?? throw new InvalidOperationException("A build review is required.");
        if (review.ReviewId != BrokerManagedStrategy.StableId($"{prior.RevisionId:D}:ImplementationReview:{review.ImplementationIdentity}") ||
            next.Event != "ImplementationReviewed" || next.RevisionId != review.ReviewId ||
            review.PreviousRevisionId != prior.RevisionId || !BrokerHistoricalStateHash.Matches(prior, review.PreviousSessionHash, historical) ||
            review.PreviousImplementation != prior.Strategy.Strategy.ImplementationIdentity ||
            string.IsNullOrWhiteSpace(review.ImplementationIdentity) || review.ImplementationIdentity == review.PreviousImplementation ||
            string.IsNullOrWhiteSpace(review.ReviewedBy) || review.ReviewedBy.Length > 128 ||
            review.PreparedAt > review.ApprovedAt || review.ApprovedAt != next.SavedAt || review.ApprovedAt-review.PreparedAt > TimeSpan.FromMinutes(2) ||
            BrokerAccountImport.Hash(next with { Revision=prior.Revision,RevisionId=prior.RevisionId,PreviousRevisionId=prior.PreviousRevisionId,
                Event=prior.Event,SavedAt=prior.SavedAt,ImplementationReview=prior.ImplementationReview,Account=prior.Account }) != BrokerAccountImport.Hash(prior))
            throw new InvalidOperationException("Build review must preserve all original financial permission/history, risk memory, protection and buying/paused controls.");
        RequireAccount(prior,next.Account,next.SavedAt);
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy,next.Strategy);
    }
}
