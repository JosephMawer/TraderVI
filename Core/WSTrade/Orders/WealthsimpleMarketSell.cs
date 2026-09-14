#nullable enable
using Core.WSTrade.Accounts;
using System;

namespace Core.WSTrade.Orders;

/// <summary>A caller-reviewed whole-share market sell. It has no guaranteed minimum proceeds.</summary>
public sealed class WealthsimpleMarketSell
{
    public WealthsimpleTfsaAccount Account { get; }
    public WealthsimpleCanadianSecurity Security { get; }
    public int Quantity { get; }
    // Retain this ID and the reviewed intent durably; never replace it to retry a timeout.
    public Guid RequestId { get; }
    internal string ExternalId => "order-" + RequestId.ToString("D");

    public WealthsimpleMarketSell(WealthsimpleTfsaAccount account, WealthsimpleCanadianSecurity security, decimal quantity, Guid requestId)
    {
        ArgumentNullException.ThrowIfNull(account); ArgumentNullException.ThrowIfNull(security);
        if (quantity <= 0 || quantity > int.MaxValue || decimal.Truncate(quantity) != quantity || requestId == Guid.Empty)
            throw new ArgumentException("A market sell requires positive whole shares and a persistent nonempty request ID.");
        Account = account; Security = security; Quantity = (int)quantity; RequestId = requestId;
    }

    public override string ToString() => "Wealthsimple market-sell intent (financial data omitted)";
}
