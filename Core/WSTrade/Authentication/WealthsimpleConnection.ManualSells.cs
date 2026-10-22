#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleConnection : IManualSellBroker
{
    public Task<WealthsimpleReadResult<ManualSellContext>> ReadManualSellContextAsync(WealthsimpleTfsaAccount account, string symbol, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadManualSellContextAsync(account, symbol, ct), ct);
    public Task<WealthsimpleReadResult<WealthsimpleMarketOrderState>> ReadManualSellOrderAsync(ManualSellIntent intent, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadManualSellOrderAsync(intent, ct), ct);
    public Task<WealthsimpleReadResult<ManualSellInventory>> ReadManualSellInventoryAsync(ManualSellIntent intent, CancellationToken ct) =>
        ReadAccountDataAsync(c => c.ReadManualSellInventoryAsync(intent, ct), ct);
    public async Task<WealthsimpleMarketOrderSubmission> SubmitManualSellAsync(WealthsimpleMarketSell order, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (stopped || client == null || !IsConnected || expires <= clock.GetUtcNow()) return new(WealthsimpleSubmissionStatus.SessionRequired);
            return await client.SubmitMarketSellAsync(order, ct);
        }
        finally { gate.Release(); }
    }
}
