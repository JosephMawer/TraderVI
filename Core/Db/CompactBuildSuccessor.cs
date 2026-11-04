#nullable enable
using Core.Trader.Promotion;
using Core.Trader.Storage;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

// Called inside the operator's validated ImplementationReviewed append and its SQL transaction.
internal static class CompactBuildSuccessor
{
    internal static async Task<(TradingStateWrite Write, CompactDelphiAssignment Assignment)> PrepareAsync(
        BrokerManagedSession prior, BrokerManagedSession next, TradingCurrentState book, CompactDelphiCheckpoint seal,
        CompactOpeningProtection.ReadIntent? readIntent, CancellationToken ct)
    {
        var review = next.ImplementationReview;
        var old = book.Settings;
        if (review == null || next.Event != "ImplementationReviewed" || !next.HasReviewedCompactRestart ||
            next.RevisionId != review.ReviewId || review.PreviousRevisionId != prior.RevisionId ||
            next.PreviousRevisionId != prior.RevisionId || seal.ManagedRevision != prior.RevisionId ||
            prior.BindingId != book.Account.BindingId || next.BindingId != prior.BindingId ||
            next.SessionId != prior.SessionId || prior.SessionId != book.Account.AssignmentId ||
            old.ReusesSaleProceeds() != prior.ReusesSaleProceeds ||
            old.FinancialPermissionId != review.PermissionRequestId || next.EffectiveReview.RequestId != review.PermissionRequestId ||
            seal.Assignment.Permission != old.FinancialPermissionId || old.StrategyId != prior.Strategy.VersionId ||
            seal.Assignment.Strategy != old.StrategyId || seal.Assignment.Grant.StrategyFingerprint != prior.Strategy.Fingerprint ||
            old.ImplementationIdentity != review.PreviousImplementation || seal.Assignment.Grant.ImplementationIdentity != old.ImplementationIdentity ||
            next.Strategy.Strategy.ImplementationIdentity != review.ImplementationIdentity ||
            next.Strategy.PaperAccountId != old.SourceId || next.EffectiveReview.BaselineId != old.BaselineId ||
            next.EffectiveReview.PurchaseCeiling != old.PurchaseCeiling || prior.Orders.Any(o => !o.Reconciled) ||
            !book.Orders.IsEmpty || !seal.Assignment.Opening.IsEmpty || !book.Restrictions.IsEmpty ||
            book.Account.ReservedPurchaseCost != 0 || book.Account.CommittedPurchaseCost != next.CommittedPurchaseCost)
            throw new InvalidDataException("Build review and the settled compact assignment must agree.");
        BrokerOctober21Continuation.RequireSameDefinition(prior.Strategy, next.Strategy);
        var priorPositions = RestartAccountRehearsal.Positions(prior, current: book.Positions).OrderBy(p => p.PositionId);
        if (!priorPositions.SequenceEqual(book.Positions.OrderBy(p => p.PositionId).Select(p => p with { Version = 0 })) ||
            !priorPositions.SequenceEqual(RestartAccountRehearsal.Positions(next, current: book.Positions).OrderBy(p => p.PositionId)))
            throw new InvalidDataException("Build review requires unchanged compact positions and protection.");

        var verifiedOwners = await CompactOpeningProtection.RestoreAsync(prior, book, readIntent, ct);
        var config = JsonNode.Parse(verifiedOwners.ConfigurationJson)!.AsObject();
        config["StrategyFingerprint"] = next.Strategy.Fingerprint;
        config["FinancialPermissionId"] = old.FinancialPermissionId;
        if (next.ReusesSaleProceeds) config["ReinvestSaleProceeds"] = true;
        foreach (var key in config.Select(p => p.Key).Where(k => k.StartsWith("Held_", StringComparison.Ordinal)).ToArray())
            config.Remove(key);
        foreach (var p in book.Positions.Where(verifiedOwners.OwnsProtection))
            config["Held_" + p.PositionId.ToString("N")] = p.ProtectionOwner;
        var settings = old with { Version = 0, PermissionId = review.ReviewId, StrategyId = next.Strategy.VersionId,
            ImplementationIdentity = review.ImplementationIdentity, ApprovedUtc = review.ApprovedAt.UtcDateTime,
            ConfigurationJson = config.ToJsonString() };
        if (book.Positions.Any(p => settings.OwnsProtection(p) != verifiedOwners.OwnsProtection(p)))
            throw new InvalidDataException("The build review would change an existing holding's protection eligibility.");
        var account = book.Account with { PermissionId = settings.PermissionId, StrategyId = settings.StrategyId };
        var write = new TradingStateWrite(next.RevisionId, "ReviewedBuildSuccessor", next.SavedAt.UtcDateTime, account.Version, account)
        {
            Settings = [settings], Symbols = [.. book.Symbols.Select(s => s with { StrategyId = settings.StrategyId })],
            AdvanceCheckpoint = true
        };
        // This is an expected identity in the checkpoint. Only an independently enabled host grant can run.
        var expected = seal.Assignment.Grant with { Enabled = false, ImplementationIdentity = settings.ImplementationIdentity,
            StrategyFingerprint = next.Strategy.Fingerprint, ReviewedAt = review.ApprovedAt, ReviewedBy = review.ReviewedBy,
            ReinvestmentReviewId = next.ReinvestmentReviewId };
        return (write, seal.Assignment with { Grant = expected, Strategy = settings.StrategyId });
    }
}
