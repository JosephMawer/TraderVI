#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Core.WSTrade.Accounts;

// Each row represents one resolved cumulative order, not an invented individual execution.
public sealed record BrokerProjectedTrade(string OrderId, Guid TradeId, Guid PositionId, string Symbol,
    string Side, int Quantity, decimal Price, decimal Gross, decimal Fees, decimal Net,
    DateTimeOffset LastFilledAt, decimal? RealizedProfit, double? RealizedProfitFraction, int HoldingDays,
    bool Legacy);
public sealed record BrokerProjectedPosition(Guid PositionId, string Symbol, DateOnly EntryDate,
    int Quantity, decimal Cost, decimal EntryPrice, bool Active, bool Legacy);
public sealed record BrokerTradeProjection(IReadOnlyList<BrokerProjectedTrade> Trades,
    IReadOnlyList<BrokerProjectedPosition> Positions);
public sealed record BrokerTradeSyncReceipt(Guid ImportId, Guid BindingId, Guid? PreviousImportId,
    BrokerImportLegacyLedger Baseline, string LedgerFingerprint, BrokerTradeProjection Projection);

/// <summary>Reconstructs resolved broker order aggregates into distinct holding lifecycles.</summary>
public static class BrokerTradeProjector
{
    public static BrokerTradeProjection Build(BrokerImportPlan plan, BrokerImportLegacyLedger baseline)
    {
        if (!plan.CanImport || plan.LegacyFingerprint != baseline.Fingerprint || baseline.AccountLabel != "TFSA")
            throw new InvalidOperationException("A validated import and its original reviewed ledger are required.");
        // Revalidate even callers outside the import workflow; flags in serialized input are not proof.
        var verified = BrokerAccountImport.Preview(plan.Observation,
            new(true, baseline, null, null), plan.Observation.CheckedAt);
        if (!verified.CanImport || !verified.Rows.Select(r => (r.ExternalId, r.LegacyTradeId, r.LegacyPositionId))
            .SequenceEqual(plan.Rows.Select(r => (r.ExternalId, r.LegacyTradeId, r.LegacyPositionId))))
            throw new InvalidOperationException("The broker history or reviewed trade references cannot be reproduced.");
        var trades = new List<BrokerProjectedTrade>();
        var positions = new List<BrokerProjectedPosition>();
        foreach (var security in plan.Observation.Orders.Where(o => o.FilledQuantity > 0)
            .GroupBy(o => o.SecurityId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var cycle = new List<BrokerOrderFact>();
            decimal quantity = 0;
            foreach (var order in security.OrderBy(o => o.LastFilledAt).ThenBy(o => o.ExternalId, StringComparer.Ordinal))
            {
                cycle.Add(order);
                quantity += order.Side == "BUY" ? order.FilledQuantity : -order.FilledQuantity;
                if (quantity == 0) { AddCycle(cycle); cycle.Clear(); }
            }
            if (cycle.Count > 0) AddCycle(cycle);
        }
        if (positions.Select(p => p.PositionId).Distinct().Count() != positions.Count ||
            positions.Where(p => p.Active).GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            baseline.Positions.Any(p => positions.All(x => x.PositionId != p.PositionId)))
            throw new InvalidOperationException("Broker holding lifecycles cannot be mapped uniquely to the recorded positions.");
        return new(trades.OrderBy(t => t.LastFilledAt).ThenBy(t => t.OrderId, StringComparer.Ordinal).ToArray(),
            positions.OrderBy(p => p.PositionId).ToArray());

        void AddCycle(IReadOnlyList<BrokerOrderFact> orders)
        {
            var first = orders[0];
            if (first.Side != "BUY") throw new InvalidOperationException("Broker purchase history is incomplete.");
            var references = plan.Rows.Where(r => orders.Any(o => o.ExternalId == r.ExternalId) && r.LegacyPositionId.HasValue).ToArray();
            var ids = references.Select(r => r.LegacyPositionId!.Value).Distinct().ToArray();
            if (ids.Length > 1) throw new InvalidOperationException("One broker lifecycle maps to multiple tracked positions.");
            var legacy = ids.Length == 1 ? baseline.Positions.Single(p => p.PositionId == ids[0]) : null;
            Guid id = legacy?.PositionId ?? StableId(plan.BrokerKey, "position", first.ExternalId);
            string symbol = legacy?.Symbol ?? ListingSymbol(first, plan.Observation);
            DateOnly entry = BrokerAccountImport.TorontoDate(first.LastFilledAt!.Value);
            if (legacy is not null && legacy.EntryDate != entry)
                throw new InvalidOperationException("A tracked holding's original purchase date differs from the broker lifecycle.");
            decimal quantity = 0, cost = 0, lastOpenCost = 0;
            int lastOpenQuantity = 0;
            foreach (var order in orders)
            {
                if (order.Tax != 0 || new[] { order.GrossValue, order.Fees, order.NetValue }.Any(v => v is null || v != Money(v.Value)))
                    throw new InvalidOperationException("Tax withholding or sub-cent amounts require explicit accounting support before synchronization.");
                int shares = checked((int)order.FilledQuantity);
                if (order.FilledQuantity != shares) throw new InvalidOperationException("Fractional shares require explicit accounting support.");
                decimal? profit = null; double? fraction = null;
                if (order.Side == "BUY") { quantity += shares; cost += order.NetValue!.Value; }
                else
                {
                    decimal removed = cost * shares / quantity;
                    profit = Money(order.NetValue!.Value - removed);
                    fraction = removed == 0 ? 0 : (double)(profit.Value / removed);
                    lastOpenCost = cost; lastOpenQuantity = checked((int)quantity);
                    cost -= removed; quantity -= shares;
                }
                var reference = references.SingleOrDefault(r => r.ExternalId == order.ExternalId);
                trades.Add(new(order.ExternalId, reference?.LegacyTradeId ?? StableId(plan.BrokerKey, "trade", order.ExternalId),
                    id, symbol, order.Side, shares, decimal.Round(order.GrossValue!.Value / shares, 4, MidpointRounding.AwayFromZero),
                    order.GrossValue.Value, order.Fees!.Value, order.NetValue!.Value, order.LastFilledAt!.Value,
                    profit, fraction, BrokerAccountImport.TorontoDate(order.LastFilledAt.Value).DayNumber - entry.DayNumber,
                    reference?.LegacyTradeId.HasValue == true));
            }
            // Closed legacy rows retain their last owned shares/book, matching existing Trade history semantics.
            decimal book = quantity > 0 ? cost : lastOpenCost;
            int held = quantity > 0 ? checked((int)quantity) : lastOpenQuantity;
            if (held <= 0) throw new InvalidOperationException("A holding lifecycle has no supported purchase quantity.");
            positions.Add(new(id, symbol, entry, held, Money(book), decimal.Round(book / held, 4, MidpointRounding.AwayFromZero),
                quantity > 0, legacy is not null));
        }
    }
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static Guid StableId(string account, string kind, string order) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"BrokerTradeSync.v1:{account}:{kind}:{order}")).AsSpan(0, 16));
    private static string ListingSymbol(BrokerOrderFact order, BrokerAccountReconciliation observation)
    {
        var listing = (observation.Instruments ?? []).SingleOrDefault(i => i.SecurityId == order.SecurityId);
        if (listing is null || listing.Currency != "CAD" || listing.PrimaryMic is not ("XTSE" or "XTSX") ||
            listing.SecurityType != order.SecurityType || !string.Equals(listing.Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A new holding requires a verified Canadian listing identity.");
        string suffix = listing.PrimaryMic == "XTSE" ? ".TO" : ".V";
        string symbol = listing.Symbol.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? listing.Symbol : listing.Symbol + suffix;
        if (symbol.Length > 16) throw new InvalidOperationException("The broker listing exceeds the tracked-symbol limit.");
        return symbol.ToUpperInvariant();
    }
}
