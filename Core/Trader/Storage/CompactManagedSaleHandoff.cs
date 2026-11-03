#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Storage;

// The account lease spans compact recovery, this handoff and the subsequent protection evaluation.
// A completed receipt leaves the active compact book but remains addressable by its original request.
internal static class CompactManagedSaleHandoff
{
    internal const string Event = "CompactSaleReconciled";
    private const string ReportSource = "Compact original sell reconciliation";

    // A scoped report retains the previous complete import. A build review can consume the next
    // complete import only when all differences are the exact sales already reconciled in that report.
    internal static bool MatchesReviewedAccount(BrokerManagedSession saved, BrokerAccountReconciliation current)
    {
        var prior = saved.Account.Import.Plan.Observation;
        if (prior.Orders.Any(o => !current.Orders.Any(n => n.ExternalId == o.ExternalId && n.IsSameOrPostedSuccessor(o)))) return false;
        if (saved.Account.Account.ReportedBy != ReportSource)
            return current.Orders.Count == prior.Orders.Count && current.Cash == prior.Cash;
        if (!saved.HasReviewedCompactRestart || current.IdentityId != prior.IdentityId ||
            current.AccountId != prior.AccountId || !prior.Cash.HasValue) return false;
        var ids = prior.Orders.Select(o => o.ExternalId).ToHashSet(StringComparer.Ordinal);
        var additions = current.Orders.Where(o => !ids.Contains(o.ExternalId)).ToArray();
        var owned = saved.Orders.Where(o => !ids.Contains(o.ExternalId)).ToDictionary(o => o.ExternalId);
        return additions.Length != 0 && additions.Length == owned.Count && additions.All(f =>
            owned.TryGetValue(f.ExternalId, out var o) && o is { Side: "SELL", Reconciled: true, Latest: { } previous } &&
            (f.IsSameOrPostedSuccessor(previous) || MatchesFirstImport(saved, o, f, current))) &&
            current.Cash == prior.Cash + additions.Sum(o => o.NetValue!.Value);
    }

    internal static bool MatchesFirstImport(BrokerManagedSession saved, BrokerStrategyOrder sale, BrokerOrderFact fact,
        BrokerAccountReconciliation imported)
    {
        // A direct original-ID read uses the intent symbol. A first full import can use the
        // broker activity's exchange-qualified listing. All other fields and posting rules still match.
        return saved.HasReviewedCompactRestart && saved.Account.Account.ReportedBy == ReportSource &&
            sale is { Side: "SELL", Reconciled: true, Latest: { Status: "FILLED" or "POSTED" } prior } &&
            prior.Symbol == sale.Symbol && fact.Symbol != prior.Symbol &&
            !saved.Account.Import.Plan.Observation.Orders.Any(o => o.ExternalId == sale.ExternalId) &&
            BrokerMarketSymbols.Matches(imported, sale.SecurityId, sale.Symbol, fact.Symbol) &&
            (fact with { Symbol = prior.Symbol }).IsSameOrPostedSuccessor(prior);
    }

    internal static async Task ReconcileAsync(IBrokerManagedSessionLease lease, AccountRestartResult recovery,
        TradingCurrentStore store, Func<Guid, ManualSellRecord?> findSell,
        Func<TradingOrder, CancellationToken, Task<BrokerOrderFact?>> readOriginal, ManualMarketSellWorkflow sells,
        string identity, string accountId, Action requireCurrentAssignment, TimeProvider clock, CancellationToken ct)
    {
        var book = recovery.Current;
        var start = lease.Current ?? throw new InvalidDataException("Missing managed account for sale reconciliation.");
        foreach (var sale in start.Orders.Where(o => !o.Reconciled && o.Side == "SELL" && o.PositionId.HasValue &&
            start.Account.Positions.Any(p => p.PositionId == o.PositionId) && !book.Positions.Any(p => p.PositionId == o.PositionId)))
        {
            var saved = lease.Current!;
            var holding = saved.Account.Positions.Single(p => p.PositionId == sale.PositionId);
            var original = await store.ReadOrderAsync(saved.BindingId, sale.RequestId, ct);
            var closed = await store.ReadClosedPositionAsync(saved.BindingId, holding.PositionId, ct);
            if (original is not { Status: "Filled", NeedsWork: false, Side: "SELL" } ||
                book.Settings.FinancialPermissionId != saved.EffectiveReview.RequestId || original.PermissionId != book.Settings.PermissionId ||
                original.DecisionId != sale.DecisionId || original.PositionId != sale.PositionId ||
                original.SecurityId != sale.SecurityId || original.Symbol != sale.Symbol || original.Quantity != sale.Quantity ||
                original.CreatedUtc != sale.CreatedAt.UtcDateTime || original.ReferencePrice != sale.ReferencePrice ||
                closed.SecurityId != sale.SecurityId || closed.Symbol != holding.Symbol || closed.FirstEntryUtc != holding.EntryCompletedAt.UtcDateTime)
                throw new InvalidDataException("The closed compact cycle and original managed sell terms differ.");

            var observation = saved.Account.Import.Plan.Observation;
            var entry = observation.Orders.Single(o => o.ExternalId == holding.FirstBuyOrderId);
            var record = findSell(sale.RequestId);
            var intent = record?.Intent;
            if (observation.IdentityId != identity || observation.AccountId != accountId || entry.Side != "BUY" ||
                entry.SecurityId != sale.SecurityId || !BrokerMarketSymbols.Matches(observation, sale.SecurityId, sale.Symbol, entry.Symbol) ||
                intent == null || intent.IdentityId != identity || intent.AccountId != accountId || intent.RequestId != sale.RequestId ||
                intent.SecurityId != sale.SecurityId || intent.Symbol != sale.Symbol || intent.Quantity != sale.Quantity ||
                intent.ProtectiveSale is { } exit && (exit.PositionId != sale.PositionId || exit.DecisionId != sale.DecisionId))
                throw new InvalidDataException("The original sell journal does not match the approved account and cycle.");

            requireCurrentAssignment();
            var verified = await readOriginal(original, ct) ?? throw new InvalidDataException("The original broker sell is unavailable.");
            var (_, applied) = CompactOrderObservation.Reconcile(original, verified, clock.GetUtcNow());
            if (applied.QuantityDelta != 0 || applied.NetValueDelta != 0 || verified.LastFilledAt is not { } filledAt ||
                verified.SecurityType != entry.SecurityType)
                throw new InvalidDataException("The broker fill differs from the closed compact cycle.");
            CompactOrderObservation.RequireSellFillTime(original, closed, verified, () => record);
            var fact = verified;
            if (sale.Latest is { } prior && (prior.IsTerminal && !fact.IsSameOrPostedSuccessor(prior) ||
                prior.ExternalId != fact.ExternalId || prior.SecurityId != fact.SecurityId || prior.Side != fact.Side ||
                prior.Currency != fact.Currency || prior.SecurityType != fact.SecurityType ||
                prior.SubmittedQuantity != fact.SubmittedQuantity || prior.CreatedAt != fact.CreatedAt ||
                prior.FilledQuantity > fact.FilledQuantity || prior.NetValue > fact.NetValue || prior.LastFilledAt > fact.LastFilledAt))
                throw new InvalidDataException("The original sell has conflicting cumulative fill evidence.");
            var result = await sells.CheckClosedSaleAsync(intent, fact, ct);
            if (result.Order is not { IsResolved: true, Latest: { IsFilled: true } fill })
                throw new InvalidDataException("The original sell journal and broker fill require reconciliation.");
            fact = fact with { Status = fill.Status };
            var now = clock.GetUtcNow();

            var revision = Guid.NewGuid();
            var projection = CloseHolding(saved, sale, holding, fact, recovery, revision, now);
            ct.ThrowIfCancellationRequested();
            requireCurrentAssignment();
            await lease.AppendAsync(saved with { Event = Event, RevisionId = revision, PreviousRevisionId = saved.RevisionId,
                Revision = saved.Revision + 1, SavedAt = now, Account = projection,
                Orders = saved.Orders.Replace(sale, sale with { Reconciled = true, Latest = fact }) }, ct);
        }
    }

    private static BrokerAccountProjection CloseHolding(BrokerManagedSession saved, BrokerStrategyOrder sale,
        BrokerPositionCycle holding, BrokerOrderFact fact, AccountRestartResult recovery, Guid revision, DateTimeOffset now)
    {
        var projection = saved.Account;
        var tradeId = BrokerManagedStrategy.StableId($"{saved.BindingId:D}:{sale.ExternalId}");
        var priorFill = projection.Fills.SingleOrDefault(f => f.TradeId == tradeId);
        if (holding.SecurityId != sale.SecurityId || holding.Quantity + (priorFill?.Quantity ?? 0) != sale.Quantity ||
            priorFill != null && (priorFill.PositionId != holding.PositionId || priorFill.Side != "SELL") ||
            !projection.Account.Holdings.Any(h => h.PositionId == holding.PositionId && h.Symbol == holding.Symbol &&
                h.Quantity == holding.Quantity && h.CostBasis == holding.CostBasis))
            throw new InvalidDataException("The cumulative sell does not close its original managed holding.");
        var at = fact.LastFilledAt!.Value.UtcDateTime;
        var fill = new TradingActualFill(tradeId, holding.PositionId, fact.Symbol, "SELL", at,
            DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(at)), sale.Quantity, fact.GrossValue!.Value / sale.Quantity,
            sale.Kind, sale.Kind, sale.PositionTarget);
        var positions = projection.Positions.Remove(holding);
        var book = recovery.Current;
        bool currentCash = book.Account.BrokerObservedUtc >= at && book.Account.BrokerObservedUtc <= now.UtcDateTime &&
            now.UtcDateTime - book.Account.BrokerObservedUtc <= TimeSpan.FromSeconds(30) &&
            !recovery.Notices.Any(n => n.Action is "Account observation" or "Holdings") &&
            positions.Length == book.Positions.Length && positions.All(p => book.Positions.Any(c =>
                c.PositionId == p.PositionId && c.SecurityId == p.SecurityId && c.Quantity == p.Quantity && c.CostBasis == p.CostBasis));
        // Keep the original import and risk evidence immutable. This is a protection-only report until
        // the ordinary linked-account sync supplies a fresh complete import and qualifies new exposure.
        var reportedAt = currentCash ? book.Account.BrokerObservedUtc!.Value : projection.Account.ReportedUtc;
        var report = projection.Account with { SnapshotId = revision, ReportedBy = ReportSource,
            ReportedUtc = reportedAt, TradingDate = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(reportedAt)),
            LedgerFingerprint = BrokerAccountImport.Hash(new { Previous = projection.Account.LedgerFingerprint, Sale = fact }),
            Holdings = projection.Account.Holdings.RemoveAll(h => h.PositionId == holding.PositionId),
            ValuationCash = currentCash ? book.Account.ObservedCash : null, AvailableCash = null,
            NetAssetValue = null, PendingOrderCount = null };
        report.Validate();
        const string block = "Refresh the complete broker account before new exposure.";
        return projection with { Positions = positions, Account = report,
            Fills = priorFill == null ? projection.Fills.Add(fill) : projection.Fills.Replace(priorFill, fill),
            NewRiskBlocks = projection.NewRiskBlocks.Contains(block) ? projection.NewRiskBlocks : projection.NewRiskBlocks.Add(block) };
    }
}
