#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

public sealed record BrokerMinuteSourceQualification(string SourceContract, TimeSpan DeadlineOffset,
    string ImplementationIdentity, string EvidenceHash, DateTimeOffset QualifiedAt)
{
    public void Validate(DateTimeOffset now, string implementation)
    {
        if (SourceContract != TmxCollectorSource.Contract || DeadlineOffset <= TimeSpan.Zero ||
            DeadlineOffset >= TimeSpan.FromMinutes(1) || ImplementationIdentity != implementation ||
            !CompleteTradingStrategy.IsHash(EvidenceHash) || QualifiedAt == default || QualifiedAt > now)
            throw new InvalidOperationException("The exact installed source, deadline and build require qualified collector evidence.");
    }
}

public sealed record BrokerMinuteStrategyAdoption(Guid ReviewId, Guid PreviousRevisionId, string PreviousSessionHash,
    Guid OngoingPermissionId, string PreviousStrategyFingerprint, BrokerManagedStrategy Strategy,
    BrokerMinuteSourceQualification Qualification, DateTimeOffset PreparedAt, DateTimeOffset ApprovedAt, string Reviewer)
{
    public override string ToString() => "Explicit recovered-minute strategy review (financial data omitted)";
}
public sealed record BrokerMinuteAdoptionProposal(Guid BindingId, BrokerMinuteStrategyAdoption Review);

/// <summary>Explicit successor approval on the same ongoing account. No order, host enable, new risk
/// reference, new spending allowance or protection handover is created.</summary>
public sealed class BrokerMinuteStrategyAdoptionWorkflow(IBrokerManagedSessionStore sessions,
    IBrokerManagedAccountSource source, IBrokerExecutionHostGrantStore grants, ITsxSessionCalendar calendar,
    Func<CancellationToken, Task<BrokerMinuteSourceQualification>> verifyCollector,
    Func<CancellationToken, Task<BrokerStrategyReview>> verifyStrategy, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public static BrokerMinuteAdoptionProposal Prepare(BrokerManagedSession prior, BrokerStrategyReview current,
        BrokerAccountProjection account, bool storageReady, BrokerMinuteSourceQualification qualification,
        string implementation, string reviewer, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        RequirePrior(prior, calendar, now);
        BrokerImplementationReviewWorkflow.RequireAccount(prior, account, now);
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy, current.Strategy);
        qualification.Validate(now, implementation);
        if (!storageReady || !current.Blocks.IsEmpty || current.Strategy.Strategy.ImplementationIdentity != implementation ||
            current.PreparedUtc.Kind != DateTimeKind.Utc || current.PreparedUtc > now.UtcDateTime ||
            now.UtcDateTime - current.PreparedUtc > TimeSpan.FromMinutes(2) || string.IsNullOrWhiteSpace(reviewer) || reviewer.Length > 128)
            throw new InvalidOperationException("Review the exact current account, daily source, collector qualification and build before adopting minute confirmation.");
        var strategy = current.Strategy with { Strategy = current.Strategy.Strategy with
        {
            Selection = CompleteTradingStrategy.MinuteSelection,
            MinuteEntry = new(current.Strategy.Strategy.ReadLivePolicy(), qualification.SourceContract, qualification.DeadlineOffset)
        } };
        RequireOnlyMinuteChange(prior.Strategy, strategy);
        var id = BrokerManagedStrategy.StableId($"{prior.RevisionId:D}:MinuteStrategy:{strategy.Fingerprint}:{qualification.EvidenceHash}");
        return new(prior.BindingId, new(id, prior.RevisionId, BrokerAccountImport.Hash(prior),
            prior.OngoingManagement!.RequestId, prior.Strategy.Fingerprint, strategy, qualification, now, default, reviewer));
    }

    public async Task<BrokerManagedSession> ApproveAsync(BrokerMinuteAdoptionProposal proposal, CancellationToken ct = default)
    {
        RequireHostOff();
        await using var lease = await sessions.AcquireAsync(proposal.BindingId, ct);
        var prior = lease.Current ?? throw new InvalidOperationException("Recover the existing ongoing assignment.");
        if (prior.MinuteAdoption is { } recovered)
        {
            if (BrokerAccountImport.Hash(recovered with { ApprovedAt = default }) != BrokerAccountImport.Hash(proposal.Review) ||
                source.ImplementationIdentity != recovered.Strategy.Strategy.ImplementationIdentity)
                throw new InvalidOperationException("Recover the original minute-strategy approval; this proposal differs.");
            return prior;
        }
        var now = clock.GetUtcNow(); RequirePrior(prior, calendar, now); RequireFresh(proposal.Review, now);
        if (proposal.BindingId != prior.BindingId || proposal.Review.PreviousRevisionId != prior.RevisionId ||
            proposal.Review.PreviousSessionHash != BrokerAccountImport.Hash(prior) || proposal.Review.ApprovedAt != default)
            throw new InvalidOperationException("The account advanced during strategy review. Review the current state again.");
        var qualified = await verifyCollector(ct);
        if (qualified != proposal.Review.Qualification) throw new InvalidOperationException("Collector qualification changed during review.");
        qualified.Validate(clock.GetUtcNow(), source.ImplementationIdentity);
        var current = await verifyStrategy(ct);
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy, current.Strategy);
        if (!current.Blocks.IsEmpty || current.Strategy.Strategy.ImplementationIdentity != source.ImplementationIdentity ||
            current.PreparedUtc.Kind != DateTimeKind.Utc || current.PreparedUtc > clock.GetUtcNow().UtcDateTime ||
            clock.GetUtcNow().UtcDateTime - current.PreparedUtc > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("Current daily strategy/model evidence changed or expired during minute-strategy review.");
        var account = await source.SynchronizeAsync(prior.Orders, ct);
        RequireHostOff(); now = clock.GetUtcNow(); RequireFresh(proposal.Review, now);
        if (source.ImplementationIdentity != proposal.Review.Strategy.Strategy.ImplementationIdentity)
            throw new InvalidOperationException("The implementation changed during strategy review.");
        var next = prior with { Revision = prior.Revision + 1, RevisionId = proposal.Review.ReviewId,
            PreviousRevisionId = prior.RevisionId, Event = "MinuteStrategyApproved", SavedAt = now,
            MinuteAdoption = proposal.Review with { ApprovedAt = now }, Account = account };
        ValidateAppend(prior, next, calendar);
        await lease.AppendAsync(next, ct);
        return next;
    }

    internal static void ValidateAppend(BrokerManagedSession prior, BrokerManagedSession next,
        ITsxSessionCalendar calendar, BrokerHistoricalStateHash? historical = null)
    {
        RequirePrior(prior, calendar, next.SavedAt);
        var review = next.MinuteAdoption ?? throw new InvalidOperationException("Explicit minute strategy approval is required.");
        RequireFresh(review, next.SavedAt); review.Qualification.Validate(next.SavedAt, review.Strategy.Strategy.ImplementationIdentity);
        RequireOnlyMinuteChange(prior.Strategy, review.Strategy);
        if (review.ReviewId != BrokerManagedStrategy.StableId($"{prior.RevisionId:D}:MinuteStrategy:{review.Strategy.Fingerprint}:{review.Qualification.EvidenceHash}") ||
            review.PreviousRevisionId != prior.RevisionId || !BrokerHistoricalStateHash.Matches(prior, review.PreviousSessionHash, historical) ||
            review.OngoingPermissionId != prior.OngoingManagement!.RequestId || review.PreviousStrategyFingerprint != prior.Strategy.Fingerprint ||
            next.Event != "MinuteStrategyApproved" || next.RevisionId != review.ReviewId || review.ApprovedAt != next.SavedAt ||
            string.IsNullOrWhiteSpace(review.Reviewer) || review.Reviewer.Length > 128 ||
            review.Strategy.Strategy.MinuteEntry!.SourceContract != review.Qualification.SourceContract ||
            review.Strategy.Strategy.MinuteEntry.DeadlineOffset != review.Qualification.DeadlineOffset ||
            BrokerAccountImport.Hash(next with { Revision = prior.Revision, RevisionId = prior.RevisionId,
                PreviousRevisionId = prior.PreviousRevisionId, Event = prior.Event, SavedAt = prior.SavedAt,
                MinuteAdoption = prior.MinuteAdoption, Account = prior.Account }) != BrokerAccountImport.Hash(prior))
            throw new InvalidOperationException("Minute adoption must preserve the ongoing permission, risk references, allowance, original orders, memory, protection and paused mode exactly.");
        BrokerImplementationReviewWorkflow.RequireAccount(prior, next.Account, next.SavedAt);
    }
    private static void RequirePrior(BrokerManagedSession prior, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        if (prior.OngoingManagement?.ApprovedAt is null || prior.MinuteAdoption is not null || prior.Strategy.Strategy.MinuteEntry is not null)
            throw new InvalidOperationException("Minute adoption requires the existing explicitly approved ongoing five-minute assignment.");
        BrokerImplementationReviewWorkflow.RequireSession(prior, calendar, now);
    }
    private static void RequireOnlyMinuteChange(BrokerManagedStrategy prior, BrokerManagedStrategy next)
    {
        next.Validate(); var minute = next.Strategy.MinuteEntry;
        if (prior.Strategy.MinuteEntry is not null || minute is null || next.Strategy.Selection != CompleteTradingStrategy.MinuteSelection ||
            (next with { Strategy = next.Strategy with { Name = prior.Strategy.Name, Selection = prior.Strategy.Selection,
                MinuteEntry = null, ImplementationIdentity = prior.Strategy.ImplementationIdentity } }).Fingerprint != prior.Fingerprint)
            throw new InvalidOperationException("Only the reviewed minute cadence/recovery and build may change; daily models, gates, horizons and all protection rules must remain identical.");
    }
    private static void RequireFresh(BrokerMinuteStrategyAdoption review, DateTimeOffset now)
    {
        if (review.PreparedAt > now || now - review.PreparedAt > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("The minute-strategy review expired. Refresh the review.");
    }
    private void RequireHostOff()
    {
        if (grants.Read()?.Enabled == true) throw new InvalidOperationException("Stop the execution host before explicitly reviewing a changed strategy.");
    }
}
