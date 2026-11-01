#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    // Uses the saved request identity directly. The caller has already loaded the owned account catalogue.
    internal Task<WealthsimpleReadResult<BrokerOrderFact>> ReadOriginalOrderAsync(WealthsimpleTfsaAccount account,
        string identity, Guid request, string securityId, string symbol, string side, int quantity, bool previouslyFilled,
        CancellationToken ct = default) => ReadLockedAsync(async () =>
    {
        if (account == null || !OwnsAccount(account) || session.IdentityId != identity || request == Guid.Empty ||
            side is not ("BUY" or "SELL") || quantity < 1 || string.IsNullOrWhiteSpace(securityId) || string.IsNullOrWhiteSpace(symbol))
            throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "original order account or terms");
        var (fact, listing) = await ReadOrderDetailAsync(account, identity, "order-" + request.ToString("D"),
            securityId, symbol, side, previouslyFilled, ct);
        if (fact.SubmittedQuantity != quantity || listing == null || listing.PrimaryMic is not ("XTSE" or "XTSX") ||
            !MatchesCanadianSymbol(listing.Symbol, symbol, listing.PrimaryMic) ||
            !WealthsimpleReconciliation.Valid(fact, clock.GetUtcNow()))
            throw Invalid("original order terms or listing differ");
        return fact;
    }, ct, "original order");
}
