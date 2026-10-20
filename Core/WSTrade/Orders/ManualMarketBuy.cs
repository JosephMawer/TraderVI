#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Orders;

public sealed record ManualBuyOrigin(string Symbol, string Context, string Direction)
{
    public string? ExpectedBrokerSecurityId { get; init; }
    public static ManualBuyOrigin? FromHolding(WealthsimpleHolding holding) => holding.SecurityType == "EQUITY" && !string.IsNullOrWhiteSpace(holding.SecurityId)
        ? new(holding.Symbol, "Trading · broker holding", "Manual") { ExpectedBrokerSecurityId = holding.SecurityId } : null;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Symbol) || Symbol.Length > 32 ||
            Symbol.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) ||
            string.IsNullOrWhiteSpace(Context) || Context.Length > 300 || Context.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(Direction) || Direction.Length > 32 || Direction.Any(char.IsControl) ||
            (ExpectedBrokerSecurityId != null && (string.IsNullOrWhiteSpace(ExpectedBrokerSecurityId) || ExpectedBrokerSecurityId.Length > 512 || ExpectedBrokerSecurityId.Any(char.IsControl))))
            throw new ArgumentException("A stock and its source context are required.");
    }
    public override string ToString() => "Manual-buy origin (instrument identity omitted)";
}

public sealed record ManualBuyQuote(decimal Ask, decimal Bid, DateTimeOffset QuotedAt,
    string MarketStatus, DateTimeOffset SessionOpen, DateTimeOffset SessionClose, DateTimeOffset RetrievedAt)
{
    public override string ToString() => "Manual-buy quote (financial data omitted)";
}

public sealed record ManualBuyContext(WealthsimpleTfsaAccount Account, WealthsimpleCanadianSecurity Security,
    WealthsimpleTfsaSnapshot Snapshot, WealthsimplePendingOrders PendingOrders, ManualBuyQuote Quote)
{
    public decimal? AvailableCash => Snapshot.Cash is { Currency: "CAD" } cash && Snapshot.BuyingPower is { Currency: "CAD" } buying
        ? System.Math.Min(cash.Amount, buying.Amount) : null;
    public decimal? HeldQuantity => Snapshot.Holdings.Where(h => h.SecurityId == Security.Id).Select(h => (decimal?)h.Quantity).Sum();
    public override string ToString() => "Manual-buy context (financial data omitted)";
}

// Stored only in the protected journal. IDs are never display labels or diagnostic text.
public sealed record ManualBuyIntent(Guid RequestId, string IdentityId, string AccountId, string SecurityId,
    string AccountName, string Symbol, string Exchange, int Quantity, ManualBuyOrigin Origin,
    decimal ReviewedAsk, ManualBuyQuote CheckedQuote, decimal CheckedCash, DateTimeOffset CreatedAt)
{
    public void Validate()
    {
        Origin.Validate();
        if (RequestId == Guid.Empty || Quantity < 1 || string.IsNullOrWhiteSpace(IdentityId) ||
            string.IsNullOrWhiteSpace(AccountId) || string.IsNullOrWhiteSpace(SecurityId) ||
            string.IsNullOrWhiteSpace(AccountName) || string.IsNullOrWhiteSpace(Symbol) ||
            Exchange is not ("XTSE" or "XTSX") || ReviewedAsk <= 0 || CheckedCash <= 0 ||
            CheckedQuote.Ask <= 0 || CreatedAt == default)
            throw new ArgumentException("The saved manual order is incomplete.");
    }
    public override string ToString() => "Manual-buy intent (financial data omitted)";
}

public sealed record ManualBuyRecord(ManualBuyIntent Intent, WealthsimpleSubmissionStatus? Submission,
    IReadOnlyList<WealthsimpleMarketBuyState> Observations, DateTimeOffset? LastCheckedAt = null, bool ConflictingObservation = false)
{
    public WealthsimpleMarketBuyState? Latest => Observations.LastOrDefault();
    public bool HasCompleteFillFacts => Latest is { FilledQuantity: { } quantity } state &&
        (quantity == 0 || (state.AverageFillPrice is { Currency: "CAD", Amount: > 0 } &&
            state.FilledValue is { Currency: "CAD", Amount: > 0 } && state.FilledNetValue is { Currency: "CAD", Amount: > 0 } &&
            state.Fees is { Currency: "CAD", Amount: >= 0 } && state.TaxWithholding is { Currency: "CAD", Amount: >= 0 }));
    public bool IsResolved => !ConflictingObservation && ((Latest == null && Submission is WealthsimpleSubmissionStatus.Rejected or
        WealthsimpleSubmissionStatus.SessionRequired or WealthsimpleSubmissionStatus.AccountUnavailable or
        WealthsimpleSubmissionStatus.SecurityUnavailable or WealthsimpleSubmissionStatus.PreflightUnavailable or
        WealthsimpleSubmissionStatus.JournalUnavailable) ||
        (HasCompleteFillFacts && Latest?.Status is "FILLED" or "POSTED" or "CANCELLED" or "CANCELED" or "EXPIRED" or "REJECTED"));
    public string Status => ConflictingObservation ? "Fill conflict — review required" : Latest is { } state
        ? state.Status switch
        {
            "FILLED" or "POSTED" => HasCompleteFillFacts ? "Filled" : "Filled — cost details pending",
            "PARTIALLY_FILLED" => "Partially filled",
            "CANCELLED" or "CANCELED" => HasCompleteFillFacts ? "Cancelled" : "Cancelled — fill details pending",
            "EXPIRED" => HasCompleteFillFacts ? "Expired" : "Expired — fill details pending",
            "REJECTED" => HasCompleteFillFacts ? "Rejected" : "Rejected — fill details pending",
            _ => "Pending — " + state.Status
        }
        : Submission switch
        {
            WealthsimpleSubmissionStatus.Acknowledged => "Submitted — awaiting fill",
            WealthsimpleSubmissionStatus.Rejected => "Rejected",
            WealthsimpleSubmissionStatus.SessionRequired or WealthsimpleSubmissionStatus.AccountUnavailable or
            WealthsimpleSubmissionStatus.SecurityUnavailable or WealthsimpleSubmissionStatus.PreflightUnavailable or
            WealthsimpleSubmissionStatus.JournalUnavailable => "Not submitted",
            _ => "Submission uncertain — check status"
        };
    public override string ToString() => "Manual-buy record (financial data omitted)";
}

public interface IManualBuyJournal : IDisposable
{
    IReadOnlyList<ManualBuyRecord> ReadAll();
    void Save(ManualBuyRecord record);
    bool HasUnresolvedOrders() => ReadAll().Any(o => !o.IsResolved);
}

public interface IManualBuyBroker
{
    bool IsConnected { get; }
    long SessionRevision { get; }
    Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct);
    Task<WealthsimpleReadResult<ManualBuyContext>> ReadManualBuyContextAsync(WealthsimpleTfsaAccount account, string symbol, CancellationToken ct);
    Task<WealthsimpleMarketBuySubmission> SubmitManualBuyAsync(WealthsimpleMarketBuy order, CancellationToken ct);
    Task<WealthsimpleReadResult<WealthsimpleMarketBuyState>> ReadManualBuyOrderAsync(ManualBuyIntent intent, CancellationToken ct);
}

// The production lease retains the shared engine fence and serialized account fence through submission.
public interface IManualBuySafetyLease : IAsyncDisposable { string? BlockReason { get; } }
public interface IManualBuySafetySource { Task<IManualBuySafetyLease> AcquireAsync(CancellationToken ct); }

public sealed record PreparedManualBuy(ManualBuyContext Context, ManualBuyOrigin Origin, long SessionRevision, DateTimeOffset PreparedAt)
{
    public Guid ReviewId { get; init; } = Guid.NewGuid();
    public override string ToString() => "Reviewed manual-buy context (financial data omitted)";
}
public sealed record ManualBuyPreparation(PreparedManualBuy? Prepared, string Message);
public sealed record ManualBuySubmissionResult(ManualBuyRecord? Order, PreparedManualBuy? UpdatedReview, string Message);

public static class ManualBuyChecks
{
    public static readonly TimeSpan QuoteMaximumAge = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ReviewLifetime = TimeSpan.FromSeconds(30);
    public static string? Validate(ManualBuyContext context, DateTimeOffset now, int? quantity = null)
    {
        var q = context.Quote;
        if (context.Account != context.Snapshot.Account || context.Account != context.PendingOrders.Account)
            return "The account checks do not match. Refresh the ticket.";
        if (q.Ask <= 0 || q.Bid <= 0 || q.Ask < q.Bid || q.QuotedAt > now || now - q.QuotedAt > QuoteMaximumAge)
            return "A current two-sided CAD quote is required. Refresh the ticket.";
        if (q.MarketStatus != "OPEN" || q.SessionOpen > now || q.SessionClose <= now || q.SessionClose <= q.SessionOpen)
            return "Manual market buys are available during the broker's regular trading session only.";
        if (new[] { q.RetrievedAt, context.Snapshot.RetrievedAt, context.PendingOrders.RetrievedAt }
            .Any(t => t > now || now - t > ReviewLifetime))
            return "The account or market checks expired. Refresh the ticket.";
        if (context.PendingOrders.Orders.Count != 0)
            return "This TFSA has pending broker orders. Resolve them before starting another manual buy.";
        if (context.AvailableCash is not > 0)
            return "Positive CAD cash and buying power must both be available.";
        if (quantity.HasValue)
        {
            if (quantity < 1) return "Enter a positive whole number of shares.";
            try
            {
                if (decimal.Round(checked(q.Ask * quantity.Value), 2, MidpointRounding.AwayFromZero) > context.AvailableCash)
                    return "The estimated cost exceeds the available CAD cash or buying power.";
            }
            catch (OverflowException) { return "The share quantity is too large."; }
        }
        return null;
    }
}
