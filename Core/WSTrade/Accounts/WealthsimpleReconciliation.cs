#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Core.WSTrade.Accounts;

/// <summary>One cumulative order, never an incremental fill. Preserved in account observation history.</summary>
public sealed record BrokerOrderFact(string ExternalId, string SecurityId, string Symbol, string Side,
    string Status, string Currency, string SecurityType, decimal SubmittedQuantity, decimal FilledQuantity,
    decimal? GrossValue, decimal? NetValue, decimal? Fees, decimal? Tax, DateTimeOffset CreatedAt,
    DateTimeOffset? LastFilledAt)
{
    // Preserve the broker's null rather than falsely claiming it explicitly reported zero. Only
    // narrowly checked, empty expired/cancelled responses can have zero accounting quantity with this flag.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FillQuantityWasNull { get; init; }
    internal static bool AllowsEmptyExecutionReport(string status) => status is "EXPIRED" or "CANCELLED" or "CANCELED";
    public bool IsTerminal => Status is "FILLED" or "POSTED" or "CANCELLED" or "CANCELED" or "EXPIRED" or "REJECTED";
    // Posting is allowed to advance only the status of an already confirmed fill. All identities,
    // quantities, amounts and timestamps must be exactly unchanged; previous observations stay intact.
    internal bool IsSameOrPostedSuccessor(BrokerOrderFact previous) => this == previous ||
        (previous.Status == "FILLED" && Status == "POSTED" && this with { Status = "FILLED" } == previous);
    public override string ToString() => "Broker order facts (financial data omitted)";
}

public sealed record BrokerAccountHistory(string IdentityId, string AccountId, DateTimeOffset RetrievedAt,
    IReadOnlyList<BrokerOrderFact> Orders, IReadOnlyList<string> Limitations)
{
    public string? ActivityFingerprint { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BrokerInstrumentIdentity>? Instruments { get; init; }
    public override string ToString() => "Broker account history (financial data omitted)";
}

/// <summary>Listing evidence read from the security nested in an owned historical order.</summary>
public sealed record BrokerInstrumentIdentity(string SecurityId, string Symbol, string PrimaryMic,
    string Currency, string SecurityType)
{
    public override string ToString() => "Broker instrument identity (identifiers omitted)";
}

public sealed record BrokerActivityCheckpoint(string IdentityId, string AccountId, string Fingerprint, DateTimeOffset RetrievedAt)
{
    public override string ToString() => "Broker activity checkpoint (identity omitted)";
}

public sealed record BrokerReconciledHolding(string SecurityId, string Symbol, decimal BrokerQuantity,
    decimal ReconstructedQuantity, decimal? BrokerBookValue, decimal ReconstructedBookValue,
    DateTimeOffset? FirstBuyCompletedAt, decimal RealizedProceedsLessCost)
{
    public bool Matches => BrokerQuantity == ReconstructedQuantity && BrokerBookValue.HasValue &&
        decimal.Abs(BrokerBookValue.Value - ReconstructedBookValue) <= 0.01m;
    public override string ToString() => "Reconciled holding (financial data omitted)";
}

/// <summary>A reconciliation observation, not strategy approval or permission to submit an order.</summary>
public sealed record BrokerAccountReconciliation(string IdentityId, string AccountId, DateTimeOffset CheckedAt,
    DateTimeOffset SnapshotAt, decimal? Cash, decimal? BuyingPower, decimal? AccountValue,
    IReadOnlyList<BrokerOrderFact> Orders, IReadOnlyList<BrokerReconciledHolding> Holdings,
    IReadOnlyList<string> Blocks, IReadOnlyList<string> HistoryConflicts)
{
    public string? ActivityFingerprint { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BrokerInstrumentIdentity>? Instruments { get; init; }
    public bool HoldingsMatch => Blocks.Count == 0 && HistoryConflicts.Count == 0 && Holdings.All(h => h.Matches);
    public override string ToString() => "Broker reconciliation (financial data omitted)";
}

public static class WealthsimpleReconciliation
{
    public static BrokerAccountReconciliation Compare(BrokerAccountHistory history, WealthsimpleTfsaSnapshot snapshot,
        WealthsimplePendingOrders pending, DateTimeOffset now, bool unresolvedLocalOrders,
        BrokerAccountReconciliation? previous = null)
    {
        ArgumentNullException.ThrowIfNull(history); ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(pending);
        if (string.IsNullOrWhiteSpace(history.IdentityId) || history.AccountId != snapshot.Account.Id ||
            !ReferenceEquals(snapshot.Account, pending.Account) ||
            (previous != null && (previous.IdentityId != history.IdentityId || previous.AccountId != history.AccountId)))
            throw new InvalidOperationException("The reconciliation belongs to another account or login.");
        var blocks = history.Limitations.ToList();
        var conflicts = previous?.HistoryConflicts.ToList() ?? [];
        if (new[] { history.RetrievedAt, snapshot.RetrievedAt, pending.RetrievedAt }.Any(t => t > now || now - t > TimeSpan.FromMinutes(2)))
            blocks.Add("Refresh history, holdings and pending orders together; an observation is stale.");
        if (snapshot.RetrievedAt < history.RetrievedAt)
            blocks.Add("Read holdings again after the order history so confirmed fills are reflected.");
        if (pending.RetrievedAt < snapshot.RetrievedAt)
            blocks.Add("Read pending orders again after the holdings observation.");
        if (pending.Orders.Count != 0) blocks.Add("Outstanding broker orders must be resolved before handover.");
        if (unresolvedLocalOrders) blocks.Add("A saved local order still needs status recovery.");
        decimal? Cad(WealthsimpleMoney? money) => money is { Currency: "CAD", Amount: >= 0 } ? money.Amount : null;
        if (Cad(snapshot.Cash) == null || Cad(snapshot.BuyingPower) == null || Cad(snapshot.Value) == null)
            blocks.Add("Current CAD cash, buying power and account value are required.");
        if (history.Orders.Any(o => string.IsNullOrWhiteSpace(o.ExternalId) || string.IsNullOrWhiteSpace(o.SecurityId)) ||
            history.Orders.Select(o => o.ExternalId).Distinct().Count() != history.Orders.Count)
            throw new InvalidOperationException("Order history contains missing or repeated identities.");
        foreach (var old in previous?.Orders ?? [])
        {
            var next = history.Orders.SingleOrDefault(o => o.ExternalId == old.ExternalId);
            if (next == null || ChangedIdentity(old, next) || next.FilledQuantity < old.FilledQuantity ||
                (old.IsTerminal && !next.IsSameOrPostedSuccessor(old)) ||
                (next.FilledQuantity == old.FilledQuantity && (next.GrossValue != old.GrossValue || next.NetValue != old.NetValue ||
                    next.Fees != old.Fees || next.Tax != old.Tax || next.LastFilledAt != old.LastFilledAt)))
                conflicts.Add("Previously observed order history disappeared or changed. Review at Wealthsimple before handover.");
        }
        var positions = new Dictionary<string, Position>();
        foreach (var order in history.Orders.OrderBy(o => o.LastFilledAt ?? o.CreatedAt).ThenBy(o => o.ExternalId, StringComparer.Ordinal))
        {
            if (!order.IsTerminal) blocks.Add("Order history still contains an unfinished order.");
            if (!Valid(order, history.RetrievedAt)) { blocks.Add("An order has incomplete or unsupported fill facts."); continue; }
            if (order.FilledQuantity == 0) continue;
            if (!positions.TryGetValue(order.SecurityId, out var p)) positions[order.SecurityId] = p = new(order.Symbol, order.SecurityType);
            if (p.SecurityType != order.SecurityType) blocks.Add("An instrument's type changed within order history. Review its identity before handover.");
            // Cumulative order totals cannot establish individual fill sequencing for overlapping orders.
            if (p.LastFill is { } last && order.CreatedAt <= last)
                blocks.Add("Overlapping orders in one security require individual fill history before handover.");
            p.LastFill = order.LastFilledAt;
            if (order.Side == "BUY")
            {
                if (p.Quantity == 0) p.EntryAt = order.LastFilledAt;
                p.Quantity += order.FilledQuantity;
                p.Cost += order.NetValue!.Value;
            }
            else
            {
                if (p.Quantity < order.FilledQuantity) { blocks.Add("A sale has no matching purchase history. Resolve transferred or missing holdings."); continue; }
                decimal cost = order.FilledQuantity == p.Quantity ? p.Cost : p.Cost * order.FilledQuantity / p.Quantity;
                p.Quantity -= order.FilledQuantity; p.Cost -= cost;
                p.Realized += order.NetValue!.Value - cost;
                if (p.Quantity == 0) p.EntryAt = null;
            }
        }
        var actual = snapshot.Holdings.Where(h => h.Quantity != 0).ToArray();
        if (actual.Any(h => string.IsNullOrWhiteSpace(h.SecurityId)) || actual.Select(h => h.SecurityId).Distinct().Count() != actual.Length)
            blocks.Add("Broker holdings have missing or repeated instrument identities.");
        if (actual.Any(h => h.SecurityType != "EQUITY" || h.Value?.Currency != "CAD" || h.BookValue?.Currency != "CAD" ||
            h.Quantity < 0 || h.Quantity > int.MaxValue || decimal.Truncate(h.Quantity) != h.Quantity))
            blocks.Add("A holding needs review for currency, instrument type or fractional quantity. No quantity was rounded.");
        var rows = new List<BrokerReconciledHolding>();
        foreach (var id in positions.Keys.Union(actual.Where(h => h.SecurityId != null).Select(h => h.SecurityId!)).OrderBy(s => s, StringComparer.Ordinal))
        {
            var holding = actual.FirstOrDefault(h => h.SecurityId == id);
            positions.TryGetValue(id, out var p);
            rows.Add(new(id, holding?.Symbol ?? p!.Symbol, holding?.Quantity ?? 0, p?.Quantity ?? 0,
                holding == null ? 0 : Cad(holding.BookValue), p?.Cost ?? 0, p?.EntryAt, p?.Realized ?? 0));
        }
        if (rows.Any(r => !r.Matches)) blocks.Add("Reconstructed shares or book values differ from Wealthsimple. Review the differences before handover.");
        return new(history.IdentityId, history.AccountId, now, snapshot.RetrievedAt, Cad(snapshot.Cash), Cad(snapshot.BuyingPower), Cad(snapshot.Value),
            history.Orders.ToArray(), rows.ToArray(), blocks.Distinct().ToArray(), conflicts.Distinct().ToArray())
            { ActivityFingerprint = history.ActivityFingerprint, Instruments = history.Instruments };
    }

    private static bool ChangedIdentity(BrokerOrderFact a, BrokerOrderFact b) => a.SecurityId != b.SecurityId || a.Side != b.Side ||
        a.Currency != b.Currency || a.SecurityType != b.SecurityType || a.SubmittedQuantity != b.SubmittedQuantity || a.CreatedAt != b.CreatedAt;
    internal static bool Valid(BrokerOrderFact o, DateTimeOffset now)
    {
        if (o.Side is not ("BUY" or "SELL") || o.Currency != "CAD" || o.SecurityType is not ("EQUITY" or "EXCHANGE_TRADED_FUND") ||
            o.SubmittedQuantity <= 0 || o.SubmittedQuantity > int.MaxValue || decimal.Truncate(o.SubmittedQuantity) != o.SubmittedQuantity ||
            o.FilledQuantity < 0 || o.FilledQuantity > o.SubmittedQuantity || decimal.Truncate(o.FilledQuantity) != o.FilledQuantity ||
            (o.Status is "FILLED" or "POSTED" && o.FilledQuantity != o.SubmittedQuantity) || o.CreatedAt == default || o.CreatedAt > now)
            return false;
        if (o.FillQuantityWasNull)
            return BrokerOrderFact.AllowsEmptyExecutionReport(o.Status) && o.FilledQuantity == 0 && o.GrossValue == null && o.NetValue == null &&
                o.Fees == null && o.Tax == null && o.LastFilledAt == null;
        if (o.FilledQuantity == 0) return o.LastFilledAt == null && o.GrossValue is null or 0 && o.NetValue is null or 0 && o.Fees is null or 0 && o.Tax is null or 0;
        if (o.LastFilledAt == null || o.LastFilledAt < o.CreatedAt || o.LastFilledAt > now ||
            o.GrossValue is not > 0 || o.NetValue is not >= 0 || o.Fees is not >= 0 || o.Tax is not >= 0) return false;
        var expected = o.Side == "BUY" ? o.GrossValue + o.Fees + o.Tax : o.GrossValue - o.Fees - o.Tax;
        return decimal.Abs(expected!.Value - o.NetValue.Value) <= 0.01m;
    }
    private sealed class Position(string symbol, string securityType)
    {
        public string Symbol = symbol;
        public string SecurityType = securityType;
        public decimal Quantity, Cost, Realized;
        public DateTimeOffset? EntryAt, LastFill;
    }
}
