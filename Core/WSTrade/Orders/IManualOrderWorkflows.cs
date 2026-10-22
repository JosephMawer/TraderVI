#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Orders;

public interface IManualBuyWorkflow
{
    bool IsConnected { get; }
    long SessionRevision { get; }
    IReadOnlyList<ManualBuyRecord> ReadOrders();
    Task<IReadOnlyList<ManualBuyRecord>> ReadOrdersAsync(CancellationToken ct = default);
    Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadAccountsAsync(CancellationToken ct);
    Task<ManualBuyPreparation> PrepareAsync(WealthsimpleTfsaAccount account, ManualBuyOrigin origin, CancellationToken ct = default);
    string? ValidateReview(PreparedManualBuy? review, int? quantity);
    Task<ManualBuySubmissionResult> SubmitAsync(PreparedManualBuy review, int quantity, CancellationToken ct = default);
    Task<(ManualBuyRecord Order, string Message)> CheckOrderAsync(Guid requestId, CancellationToken ct = default);
}
public interface IManualSellWorkflow
{
    bool IsConnected { get; }
    IReadOnlyList<ManualSellRecord> ReadOrders();
    Task<IReadOnlyList<ManualSellRecord>> ReadOrdersAsync(CancellationToken ct = default);
    Task<ManualSellResult> SellAllAsync(ManualSellSelection selection, CancellationToken ct = default);
    Task<ManualSellResult> SellReviewedAsync(ManualSellSelection selection, Guid requestId, CancellationToken ct = default);
    Task<ManualSellResult> CheckOrderAsync(Guid requestId, CancellationToken ct = default);
}
public interface IBrokerOrderJournal : IManualSellJournal, IBrokerReconciliationJournal, IDisposable { }
