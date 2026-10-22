#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public sealed record EngineOrders(ManualBuyRecord[] Buys, ManualSellRecord[] Sells);
public sealed record EnginePrepareBuy(WealthsimpleTfsaAccount Account, ManualBuyOrigin Origin);
public sealed record EngineSubmitBuy(Guid ReviewId, int Quantity);
public sealed record EngineCheckBuy(ManualBuyRecord Order, string Message);
public sealed record EngineSell(ManualSellSelection Selection, Guid RequestId);
public sealed record EngineReconcile(WealthsimpleTfsaAccount Account, long Revision);

/// <summary>Explicitly refreshed display evidence; only the engine can change the durable order journal.</summary>
public sealed class EngineOrderReadModel(IEngineRpc rpc) : IBrokerOrderJournal
{
    private EngineOrders? snapshot;
    public async Task RefreshAsync(CancellationToken ct = default) =>
        snapshot = await rpc.CallAsync<EngineOrders>(EngineOperation.Orders, ct: ct).ConfigureAwait(false);
    private EngineOrders Current => snapshot ?? throw new InvalidOperationException("Refresh engine order history first.");
    public IReadOnlyList<ManualBuyRecord> ReadAll() => Current.Buys;
    public IReadOnlyList<ManualSellRecord> ReadSells() => Current.Sells;
    public bool HasUnresolvedOrders() => Current.Buys.Any(b => !b.IsResolved) || Current.Sells.Any(s => !s.IsResolved);
    public void Save(ManualBuyRecord record) => throw new InvalidOperationException("The engine owns order writes.");
    public void SaveSell(ManualSellRecord record) => throw new InvalidOperationException("The engine owns order writes.");
    public BrokerAccountReconciliation? ReadReconciliation(string identityId, string accountId) =>
        throw new InvalidOperationException("Read reconciliation through the engine workflow.");
    public void SaveReconciliation(BrokerAccountReconciliation reconciliation) =>
        throw new InvalidOperationException("The engine owns reconciliation writes.");
    public void Dispose() => snapshot = null;
}

public sealed class EngineBuyWorkflow(EngineBrokerClient connection, EngineOrderReadModel orders) : IManualBuyWorkflow
{
    private readonly HashSet<Guid> submitted = [];
    public bool IsConnected => connection.IsConnected;
    public long SessionRevision => connection.SessionRevision;
    public IReadOnlyList<ManualBuyRecord> ReadOrders() => orders.ReadAll();
    public async Task<IReadOnlyList<ManualBuyRecord>> ReadOrdersAsync(CancellationToken ct = default)
    { await orders.RefreshAsync(ct); return orders.ReadAll(); }
    public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadAccountsAsync(CancellationToken ct) => connection.ReadTfsaAccountsAsync(ct);
    public Task<ManualBuyPreparation> PrepareAsync(WealthsimpleTfsaAccount account, ManualBuyOrigin origin, CancellationToken ct = default) =>
        connection.Rpc.CallAsync<ManualBuyPreparation>(EngineOperation.PrepareBuy, new EnginePrepareBuy(account, origin), ct);
    public string? ValidateReview(PreparedManualBuy? review, int? quantity)
    {
        if (review is null) return "Refresh the ticket to check the account and market.";
        if (!IsConnected || review.SessionRevision != SessionRevision) return "The engine connection changed. Refresh the ticket.";
        lock (submitted) if (submitted.Contains(review.ReviewId)) return "This review was submitted. Check its saved order before starting another.";
        var now = DateTimeOffset.UtcNow;
        if (review.PreparedAt > now || now - review.PreparedAt > ManualBuyChecks.ReviewLifetime) return "The review expired. Refresh the ticket.";
        if (quantity is not > 0) return "Enter a positive whole number of shares.";
        return ManualBuyChecks.Validate(review.Context, now, quantity);
    }
    public async Task<ManualBuySubmissionResult> SubmitAsync(PreparedManualBuy review, int quantity, CancellationToken ct = default)
    {
        var error = ValidateReview(review, quantity);
        if (error is not null) return new(null, null, error);
        lock (submitted) if (!submitted.Add(review.ReviewId)) throw new InvalidOperationException("The review is already submitted.");
        // Never retry on transport failure: the server may already have saved/submitted the original order.
        return await connection.Rpc.CallAsync<ManualBuySubmissionResult>(EngineOperation.SubmitBuy,
            new EngineSubmitBuy(review.ReviewId, quantity), ct);
    }
    public async Task<(ManualBuyRecord Order, string Message)> CheckOrderAsync(Guid requestId, CancellationToken ct = default)
    {
        var result = await connection.Rpc.CallAsync<EngineCheckBuy>(EngineOperation.CheckBuy, requestId, ct);
        await orders.RefreshAsync(ct); return (result.Order, result.Message);
    }
}

public sealed class EngineSellWorkflow(EngineBrokerClient connection, EngineOrderReadModel orders) : IManualSellWorkflow
{
    public bool IsConnected => connection.IsConnected;
    public IReadOnlyList<ManualSellRecord> ReadOrders() => orders.ReadSells();
    public async Task<IReadOnlyList<ManualSellRecord>> ReadOrdersAsync(CancellationToken ct = default)
    { await orders.RefreshAsync(ct); return orders.ReadSells(); }
    public Task<ManualSellResult> SellAllAsync(ManualSellSelection selection, CancellationToken ct = default) =>
        SellReviewedAsync(selection, Guid.NewGuid(), ct);
    public Task<ManualSellResult> SellReviewedAsync(ManualSellSelection selection, Guid requestId, CancellationToken ct = default) =>
        connection.Rpc.CallAsync<ManualSellResult>(EngineOperation.SellAll, new EngineSell(selection, requestId), ct);
    public async Task<ManualSellResult> CheckOrderAsync(Guid requestId, CancellationToken ct = default)
    {
        var result = await connection.Rpc.CallAsync<ManualSellResult>(EngineOperation.CheckSell, requestId, ct);
        await orders.RefreshAsync(ct); return result;
    }
}

public sealed class EngineReconciliationWorkflow(IEngineRpc rpc, EngineOrderReadModel orders) : IBrokerReconciliationWorkflow
{
    public async Task<BrokerAccountRead> ReadAsync(WealthsimpleTfsaAccount account, long revision, CancellationToken ct = default)
    {
        var result = await rpc.CallAsync<BrokerAccountRead>(EngineOperation.Reconcile, new EngineReconcile(account, revision), ct);
        await orders.RefreshAsync(ct); return result;
    }
    public async Task<BrokerAccountReconciliation> RefreshAsync(WealthsimpleTfsaAccount account, long revision, CancellationToken ct = default) =>
        (await ReadAsync(account, revision, ct)).Reconciliation;
}
