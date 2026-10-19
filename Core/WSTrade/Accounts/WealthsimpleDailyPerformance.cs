#nullable enable
using System;

namespace Core.WSTrade.Accounts;

/// <summary>
/// Broker-calculated ONE_DAY/REGULAR simple return for display only. This is neither a
/// saved risk reference nor proof of complete cash-flow history or permission to trade.
/// Missing latest-point returns never fall back to an earlier point or the overall return.
/// </summary>
public sealed class WealthsimpleDailyPerformance
{
    public WealthsimpleTfsaAccount Account { get; }
    public DateTimeOffset RetrievedAt { get; }
    public DateTimeOffset? AsOf { get; }
    public DateTimeOffset? ReferenceAt { get; }
    public WealthsimpleMoney? Amount { get; }
    public decimal? Rate { get; }

    internal WealthsimpleDailyPerformance(WealthsimpleTfsaAccount account, DateTimeOffset retrievedAt,
        DateTimeOffset? asOf, DateTimeOffset? referenceAt, WealthsimpleMoney? amount, decimal? rate)
    { Account = account; RetrievedAt = retrievedAt; AsOf = asOf; ReferenceAt = referenceAt; Amount = amount; Rate = rate; }

    public override string ToString() => "Broker daily performance (financial data omitted)";
}
