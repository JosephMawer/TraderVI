#nullable enable
using Core.WSTrade.Orders;
using System;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>The original broker ask is immutable for one confirmation episode, including rejected attempts.</summary>
public sealed record BrokerBuyReference(Guid EpisodeId, string Symbol, string SecurityId,
    ManualBuyQuote Quote, DateTimeOffset SavedAt)
{
    public const string Contract = "BrokerAsk025Spread050V1";
    public const decimal Movement = 0.0025m;
    public const decimal MaximumSpread = 0.005m;
    [System.Text.Json.Serialization.JsonIgnore] public decimal MaximumAsk => Quote.Ask * (1m + Movement);
    public static void RequireQuote(ManualBuyQuote quote, DateTimeOffset now)
    {
        if (quote.Ask <= 0 || quote.Bid <= 0 || quote.Ask < quote.Bid ||
            (quote.Ask - quote.Bid) / quote.Ask > MaximumSpread ||
            quote.QuotedAt > now || now - quote.QuotedAt > ManualBuyChecks.QuoteMaximumAge ||
            quote.RetrievedAt > now || now - quote.RetrievedAt > ManualBuyChecks.ReviewLifetime ||
            quote.MarketStatus != "OPEN" || now < quote.SessionOpen || now >= quote.SessionClose)
            throw BrokerOrderFailureEvidence.Tag("A fresh regular-session broker quote with spread at most 0.50% is required.", BrokerOrderCheck.ExecutionQuoteRejected);
    }
    public void Validate(DateTimeOffset now)
    {
        if (EpisodeId == Guid.Empty || string.IsNullOrWhiteSpace(Symbol) || string.IsNullOrWhiteSpace(SecurityId) || SavedAt > now)
            throw new InvalidOperationException("Recover the fixed broker execution reference.");
        RequireQuote(Quote, SavedAt);
    }
    public int Size(int recommendedQuantity, decimal allocation, decimal cash, decimal allowance) =>
        (int)System.Math.Max(0m, System.Math.Min(recommendedQuantity, decimal.Floor(System.Math.Min(allocation, System.Math.Min(cash, allowance)) / MaximumAsk)));

    internal static void ValidateAppend(BrokerManagedSession? prior, BrokerManagedSession next)
    {
        var old = prior?.BuyReferences ?? []; var current = next.BuyReferences ?? [];
        if (current.Length < old.Length || current.Take(old.Length).Where((r, i) => r != old[i]).Any() ||
            current.Select(r => r.EpisodeId).Distinct().Count() != current.Length ||
            current.Length > old.Length && (next.OngoingManagement?.ExecutionPricePolicy != Contract ||
                next.Event != "BrokerBuyReferenceSaved" || current.Length != old.Length + 1 ||
                next.NewBuysStopped || prior is null || !next.Orders.SequenceEqual(prior.Orders)))
            throw new InvalidOperationException("Fixed broker references cannot be replaced or granted without explicit ongoing price-policy approval.");
        foreach (var added in current.Skip(old.Length)) added.Validate(next.SavedAt);
        foreach (var order in next.Orders.Skip(prior?.Orders.Length ?? 0).Where(o => o.BuyReference is not null))
            if (order.Side != "BUY" || !current.Contains(order.BuyReference!) || order.ReferencePrice != order.BuyReference!.MaximumAsk ||
                order.Symbol != order.BuyReference.Symbol || order.SecurityId != order.BuyReference.SecurityId ||
                next.OngoingManagement?.ExecutionPricePolicy != Contract)
                throw new InvalidOperationException("The reserved buy must retain its approved fixed quote and bounded reservation price.");
    }
    public override string ToString() => "Fixed broker execution reference (private values omitted)";
}
