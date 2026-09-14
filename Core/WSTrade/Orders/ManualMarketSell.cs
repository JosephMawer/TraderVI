#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Orders;

/// <summary>The exact holding displayed when the operator clicks Sell all.</summary>
public sealed record ManualSellSelection(WealthsimpleTfsaAccount Account, string SecurityId, string Symbol,
    decimal Quantity, DateTimeOffset SnapshotAt, long SessionRevision)
{
    public Guid SelectionId { get; init; } = Guid.NewGuid();
    public bool IsWholeHolding => Quantity > 0 && Quantity <= int.MaxValue && decimal.Truncate(Quantity) == Quantity;
    public static ManualSellSelection? FromHolding(WealthsimpleHolding holding, WealthsimpleTfsaSnapshot snapshot, long revision) =>
        holding.SecurityType == "EQUITY" && holding.Value?.Currency == "CAD" && holding.Quantity > 0 &&
        !string.IsNullOrWhiteSpace(holding.SecurityId) && snapshot.Holdings.Contains(holding)
            ? new(snapshot.Account, holding.SecurityId, holding.Symbol, holding.Quantity, snapshot.RetrievedAt, revision) : null;
    public override string ToString() => "Manual-sell selection (financial data omitted)";
}

public sealed record ManualSellContext(WealthsimpleTfsaAccount Account, WealthsimpleCanadianSecurity Security,
    WealthsimpleTfsaSnapshot Snapshot, WealthsimplePendingOrders PendingOrders, ManualBuyQuote Quote)
{
    public decimal? HeldQuantity => Snapshot.Holdings.SingleOrDefault(h => h.SecurityId == Security.Id)?.Quantity;
    public override string ToString() => "Manual-sell context (financial data omitted)";
}

public sealed record ManualSellIntent(Guid RequestId, string IdentityId, string AccountId, string SecurityId,
    string AccountName, string Symbol, string Exchange, int Quantity, DateTimeOffset DisplayedSnapshotAt,
    ManualBuyQuote CheckedQuote, DateTimeOffset CreatedAt)
{
    public void Validate()
    {
        if (RequestId == Guid.Empty || Quantity < 1 || string.IsNullOrWhiteSpace(IdentityId) ||
            string.IsNullOrWhiteSpace(AccountId) || string.IsNullOrWhiteSpace(SecurityId) ||
            string.IsNullOrWhiteSpace(AccountName) || string.IsNullOrWhiteSpace(Symbol) ||
            Exchange is not ("XTSE" or "XTSX") || CheckedQuote is null || CheckedQuote.Bid <= 0 ||
            CheckedQuote.Ask < CheckedQuote.Bid || CreatedAt == default || DisplayedSnapshotAt == default)
            throw new ArgumentException("The saved manual sell is incomplete.");
    }
    public override string ToString() => "Manual-sell intent (financial data omitted)";
}

public sealed record ManualSellRecord(ManualSellIntent Intent, WealthsimpleSubmissionStatus? Submission,
    IReadOnlyList<WealthsimpleMarketOrderState> Observations, DateTimeOffset? LastCheckedAt = null,
    bool ConflictingObservation = false, DateTimeOffset? InventoryVerifiedAt = null)
{
    public WealthsimpleMarketOrderState? Latest => Observations.LastOrDefault();
    public bool HasCompleteFillFacts => Latest is { FilledQuantity: { } quantity } state && quantity >= 0 &&
        quantity <= Intent.Quantity && (quantity == 0 || (state.AverageFillPrice is { Currency: "CAD", Amount: > 0 } &&
        state.FilledValue is { Currency: "CAD", Amount: > 0 } && state.FilledNetValue is { Currency: "CAD", Amount: >= 0 } &&
        state.Fees is { Currency: "CAD", Amount: >= 0 } && state.TaxWithholding is { Currency: "CAD", Amount: >= 0 }));
    public bool HasTerminalBrokerResult => HasCompleteFillFacts && (Latest?.IsFilled == true || Latest?.Status is "CANCELLED" or "CANCELED" or "EXPIRED" or "REJECTED");
    public bool IsResolved => !ConflictingObservation && ((Latest == null && Submission is WealthsimpleSubmissionStatus.Rejected or
        WealthsimpleSubmissionStatus.SessionRequired or WealthsimpleSubmissionStatus.AccountUnavailable or
        WealthsimpleSubmissionStatus.SecurityUnavailable or WealthsimpleSubmissionStatus.PreflightUnavailable or WealthsimpleSubmissionStatus.JournalUnavailable) ||
        (HasTerminalBrokerResult && InventoryVerifiedAt != null));
    public string Status => ConflictingObservation ? "Conflicting results — review at Wealthsimple" : Latest is { } state
        ? (state.Status switch { "FILLED" => "Filled", "PARTIALLY_FILLED" => "Partially filled", "CANCELLED" or "CANCELED" => "Cancelled",
            "EXPIRED" => "Expired", "REJECTED" => "Rejected", _ => "Pending" }) +
            (HasTerminalBrokerResult && !IsResolved ? " — awaiting holdings update" : !HasCompleteFillFacts ? " — fill details pending" : "")
        : Submission switch { WealthsimpleSubmissionStatus.Acknowledged => "Submitted — awaiting fill",
            WealthsimpleSubmissionStatus.Rejected => "Rejected", _ => IsResolved ? "Not submitted" : "Submission uncertain — check status" };
    public override string ToString() => "Manual-sell record (financial data omitted)";
}

public sealed record ManualSellInventory(decimal Quantity, bool HasPendingOrders, DateTimeOffset RetrievedAt)
{
    public override string ToString() => "Manual-sell inventory (financial data omitted)";
}
public interface IManualSellJournal : IManualBuyJournal
{
    IReadOnlyList<ManualSellRecord> ReadSells();
    void SaveSell(ManualSellRecord record);
    bool IManualBuyJournal.HasUnresolvedOrders() => ReadAll().Any(o => !o.IsResolved) || ReadSells().Any(o => !o.IsResolved);
}
public interface IManualSellBroker
{
    bool IsConnected { get; }
    long SessionRevision { get; }
    Task<WealthsimpleReadResult<ManualSellContext>> ReadManualSellContextAsync(WealthsimpleTfsaAccount account, string symbol, CancellationToken ct);
    Task<WealthsimpleMarketOrderSubmission> SubmitManualSellAsync(WealthsimpleMarketSell order, CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimpleMarketOrderState>> ReadManualSellOrderAsync(ManualSellIntent intent, CancellationToken ct);
    Task<WealthsimpleReadResult<ManualSellInventory>> ReadManualSellInventoryAsync(ManualSellIntent intent, CancellationToken ct);
}
public sealed record ManualSellResult(ManualSellRecord? Order, string Message);

public static class ManualSellChecks
{
    public static string? Validate(ManualSellSelection selection, ManualSellContext context, DateTimeOffset now)
    {
        if (!selection.IsWholeHolding) return "Sell all requires a whole-share holding. Use Wealthsimple to sell fractional shares.";
        if (selection.Account != context.Account || context.Account != context.Snapshot.Account || context.Account != context.PendingOrders.Account ||
            selection.SecurityId != context.Security.Id || context.Security.SourceIdentity == null)
            return "The account or instrument changed. Refresh holdings before selling.";
        if (context.HeldQuantity != selection.Quantity)
            return "The holding quantity changed. Refresh holdings and click Market sell all again.";
        var q = context.Quote;
        if (q.Bid <= 0 || q.Ask < q.Bid || q.QuotedAt > now || now - q.QuotedAt > ManualBuyChecks.QuoteMaximumAge)
            return "A current two-sided CAD quote is required. No sell was submitted.";
        if (q.MarketStatus != "OPEN" || q.SessionOpen > now || q.SessionClose <= now || q.SessionClose <= q.SessionOpen)
            return "Market sell is available during the regular trading session. No order was queued.";
        if (new[] { q.RetrievedAt, context.Snapshot.RetrievedAt, context.PendingOrders.RetrievedAt }
            .Any(t => t > now || now - t > ManualBuyChecks.ReviewLifetime))
            return "The market or account checks expired. No sell was submitted.";
        if (context.PendingOrders.Orders.Count != 0)
            return "This TFSA has pending broker orders. Resolve them before selling.";
        return null;
    }
}
