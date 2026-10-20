#nullable enable
using Core.WSTrade.Accounts;
using System;

namespace Core.WSTrade.Orders;

/// <summary>A caller-reviewed whole-share market buy. It has no limit price or guaranteed maximum cost.</summary>
public sealed class WealthsimpleMarketBuy
{
    public WealthsimpleTfsaAccount Account { get; }
    public WealthsimpleCanadianSecurity Security { get; }
    public int Quantity { get; }
    // The caller must retain this ID across restart/recovery; never generate a new ID to retry a timeout.
    public Guid RequestId { get; }
    internal string ExternalId => "order-" + RequestId.ToString("D");
    public WealthsimpleMarketBuy(WealthsimpleTfsaAccount account, WealthsimpleCanadianSecurity security, decimal quantity, Guid requestId)
    {
        ArgumentNullException.ThrowIfNull(account); ArgumentNullException.ThrowIfNull(security);
        if (quantity <= 0 || quantity > int.MaxValue || decimal.Truncate(quantity) != quantity || requestId == Guid.Empty)
            throw new ArgumentException("A market buy requires positive whole shares and a persistent nonempty request ID.");
        Account = account; Security = security; Quantity = (int)quantity; RequestId = requestId;
    }
    public override string ToString() => "Wealthsimple market-buy intent (financial data omitted)";
}

public enum WealthsimpleSubmissionStatus
{
    Acknowledged, Rejected, OutcomeUnknown, AlreadyAttempted, SessionRequired, AccountUnavailable,
    SecurityUnavailable, PreflightUnavailable, JournalUnavailable
}

public sealed record WealthsimpleMarketBuySubmission(WealthsimpleSubmissionStatus Status, DateTimeOffset? CreatedAt = null)
{
    // This is an acknowledgement ID, not the external correlation ID or evidence of a fill.
    internal string? BrokerOrderId { get; init; }
    public override string ToString() => $"Wealthsimple market-buy submission: {Status}";
}

/// <summary>Cumulative observation. Repeated reads are not incremental fill events.</summary>
public sealed record WealthsimpleMarketBuyState(string Status, int SubmittedQuantity, decimal? FilledQuantity,
    WealthsimpleMoney? AverageFillPrice, WealthsimpleMoney? FilledValue, WealthsimpleMoney? FilledNetValue,
    WealthsimpleMoney? Fees, WealthsimpleMoney? TaxWithholding, DateTimeOffset CreatedAt,
    DateTimeOffset? LastFilledAt, DateTimeOffset? ExpiresAt, string TradingSession, DateTimeOffset RetrievedAt)
{
    public bool IsFilled => Status is "FILLED" or "POSTED" && FilledQuantity == SubmittedQuantity;
    internal bool IsSameCompletedFillAs(WealthsimpleMarketBuyState previous) => IsFilled && previous.IsFilled &&
        (Status == previous.Status || previous.Status == "FILLED" && Status == "POSTED") &&
        this with { Status = previous.Status, RetrievedAt = previous.RetrievedAt } == previous;
    public override string ToString() => "Wealthsimple market-buy state (financial data omitted)";
}
