#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.IO;
using System.Linq;

namespace Core.Trader.Storage;

// Explicit maintenance only: reconcile one already-applied original buy, preserving financial authority.
internal static class SettledBuyTransition
{
    internal const string Event = "FilledBuyReconciled";
    internal static BrokerManagedSession Prepare(BrokerManagedSession saved, TradingCurrentState book,
        TradingOrder receipt, BrokerOrderFact verified, BrokerAccountProjection account,
        Guid revision, Guid request, DateTimeOffset now)
    {
        if (saved.RevisionId != revision || !saved.HasReviewedCompactRestart || !saved.ActivatedAt.HasValue ||
            saved.ReviewInvalidated || saved.TrialOrderReviewRequired || !saved.Blocks.IsEmpty ||
            !book.Orders.IsEmpty || !book.Restrictions.IsEmpty || book.Account.ReservedPurchaseCost != 0 ||
            book.Account.BindingId != saved.BindingId || book.Account.AssignmentId != saved.SessionId ||
            book.Settings.FinancialPermissionId != saved.EffectiveReview.RequestId ||
            book.Settings.ImplementationIdentity != saved.Strategy.Strategy.ImplementationIdentity ||
            book.Account.StrategyId != saved.Strategy.VersionId || saved.Orders.Count(o => !o.Reconciled) != 1)
            throw new InvalidDataException("Require the pinned compact assignment and one unsettled managed buy.");
        var buy = saved.Orders.Single(o => !o.Reconciled);
        if (buy.RequestId != request || buy.Side != "BUY" || buy.Kind is not ("Initial" or "Reentry") || buy.PositionId != null ||
            receipt.RequestId != request || receipt.BindingId != saved.BindingId || receipt.Side != "BUY" ||
            receipt.Status != "Filled" || receipt.NeedsWork || receipt.PermissionId != book.Settings.PermissionId ||
            receipt.DecisionId != buy.DecisionId || receipt.SecurityId != buy.SecurityId || receipt.Symbol != buy.Symbol ||
            receipt.Quantity != buy.Quantity || receipt.ReferencePrice != buy.ReferencePrice || receipt.CreatedUtc != buy.CreatedAt.UtcDateTime ||
            receipt.AppliedFilledQuantity != buy.Quantity || verified.LastFilledAt is not { } filled)
            throw new InvalidDataException("The original buy must already be completely applied.");
        var (_, delta) = CompactOrderObservation.Reconcile(receipt, verified, now);
        if (delta.QuantityDelta != 0 || delta.NetValueDelta != 0 || verified.Status is not ("FILLED" or "POSTED"))
            throw new InvalidDataException("Maintenance cannot apply another fill or charge.");
        var observed = account.Import.Plan.Observation;
        var original = observed.Orders.Single(o => o.ExternalId == buy.ExternalId);
        if (!BrokerMarketSymbols.Matches(observed, buy.SecurityId, buy.Symbol, original.Symbol) ||
            !(original with { Symbol = verified.Symbol }).IsSameOrPostedSuccessor(verified) ||
            account.Import.BindingId != saved.BindingId || !observed.HoldingsMatch ||
            observed.SnapshotAt > now || now - observed.SnapshotAt > TimeSpan.FromSeconds(30) ||
            observed.CheckedAt < observed.SnapshotAt || observed.CheckedAt > now ||
            account.Account.ReportedUtc > now.UtcDateTime || now.UtcDateTime - account.Account.ReportedUtc > TimeSpan.FromSeconds(30) ||
            BrokerAccountImport.Hash(account.Risk) != BrokerAccountImport.Hash(saved.Account.Risk) ||
            account.NewRiskBlocks.Any(b => b != BrokerAccountProjector.RiskSessionContinuationRequired))
            throw new InvalidDataException("Fresh matching account facts and unchanged risk history are required.");
        _ = BrokerOngoingRiskEvidenceSource.Reconcile(saved.OngoingManagement!, observed, saved.Orders);
        var priorFacts = saved.Account.Import.Plan.Observation.Orders;
        if (priorFacts.Any(o => !observed.Orders.Any(n => n.ExternalId == o.ExternalId && n.IsSameOrPostedSuccessor(o))) ||
            observed.Orders.Any(o => o.ExternalId != buy.ExternalId && !priorFacts.Any(p => p.ExternalId == o.ExternalId)))
            throw new InvalidDataException("Only the original buy may extend the saved account history.");
        var orders = saved.Orders.Replace(buy, buy with { Reconciled = true, Latest = original });
        account = account.WithManagedFillAttribution(saved.BindingId, orders);
        var added = account.Positions.Where(p => !saved.Account.Positions.Any(old => old.PositionId == p.PositionId)).ToArray();
        if (added.Length != 1 || account.Positions.Length != book.Positions.Length ||
            saved.Account.Positions.Any(p => !account.Positions.Contains(p)) ||
            added[0].FirstBuyOrderId != buy.ExternalId || added[0].SecurityId != buy.SecurityId ||
            added[0].EntryCompletedAt != filled || added[0].Quantity != buy.Quantity || added[0].CostBasis != receipt.AppliedNetValue ||
            saved.Protection.Any(p => p.PositionId == added[0].PositionId))
            throw new InvalidDataException("The added position must be exactly the original filled buy.");
        var next = saved with { Revision = saved.Revision + 1, PreviousRevisionId = revision,
            RevisionId = BrokerManagedStrategy.StableId($"{revision:D}:{request:D}:{Event}"), Event = Event, SavedAt = now,
            Account = account, Orders = orders, Protection = saved.Protection.Add(
                BrokerLiveProtection.Transfer(saved.Strategy, added[0], null, added[0].EntryCompletedAt)) };
        var projected = RestartAccountRehearsal.Positions(next, current: book.Positions);
        foreach (var position in book.Positions)
        {
            var after = projected.Single(p => p.PositionId == position.PositionId);
            TradingStorageValidation.ValidateTransition(position, after with { Version = position.Version });
            if (position.Quantity != after.Quantity || position.CostBasis != after.CostBasis ||
                position.ProtectionOwner != after.ProtectionOwner || position.ExitDecisionId != after.ExitDecisionId)
                throw new InvalidDataException("Original holdings and protection must remain intact.");
        }
        _ = CompactManagedPublication.Prepare(saved, next, book);
        return next;
    }
}
