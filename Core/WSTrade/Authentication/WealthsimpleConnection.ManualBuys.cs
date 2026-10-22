#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleConnection : IManualBuyBroker
{
    public Task<WealthsimpleReadResult<ManualBuyContext>> ReadManualBuyContextAsync(WealthsimpleTfsaAccount account, string symbol, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadManualBuyContextAsync(account, symbol, ct), ct);
    public Task<WealthsimpleReadResult<WealthsimpleMarketBuyState>> ReadManualBuyOrderAsync(ManualBuyIntent intent, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadManualBuyOrderAsync(intent, ct), ct);

    public async Task<WealthsimpleMarketBuySubmission> SubmitManualBuyAsync(WealthsimpleMarketBuy order, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            // Readiness was checked before the explicit operator action. Do not refresh/retry
            // here: changed sessions require a fresh review with newly scoped account objects.
            if (stopped || client == null || !IsConnected || expires <= clock.GetUtcNow())
                return new(WealthsimpleSubmissionStatus.SessionRequired);
            return await client.SubmitMarketBuyAsync(order, ct);
        }
        finally { gate.Release(); }
    }
}
