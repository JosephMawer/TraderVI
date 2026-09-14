#nullable enable
using Core.WSTrade.Accounts;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    // Exact pending-status set observed in the operator's browser capture. This feed is
    // identity-wide: paginate every page, then retain only the selected owned TFSA.
    private static readonly string[] PendingOrderStatuses = ["CANCEL_PENDING", "CONTINGENT", "NEW",
        "PARTIALLY_FILLED", "PENDING_FUND_TRANSFER", "PENDING_REVIEW", "PENDING_SUBMISSION", "PLACED", "SUBMITTED"];

    public Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(
        WealthsimpleTfsaAccount account, CancellationToken ct = default) => ReadLockedAsync(async () =>
    {
        if (account == null || catalogIdentity != session.IdentityId || !tfsaAccounts.Contains(account))
            throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "account selection");
        var orders = new List<WealthsimplePendingOrder>();
        var seen = new HashSet<string>(); var orderIds = new HashSet<string>(); var cursors = new HashSet<string>(); string? cursor = null;
        do
        {
            using var json = await QueryAsync("TraderVITfsaPendingOrders", PendingOrdersQuery,
                new { identityId = session.IdentityId, statuses = PendingOrderStatuses, cursor }, ct);
            var identity = Required(json.RootElement, "data", "identity"); RequireId(identity, session.IdentityId);
            var page = Required(identity, "orderServiceExtendedOrderFeed");
            foreach (var edge in RequiredArray(page, "edges"))
            {
                var node = Required(edge, "node");
                if (!seen.Add(RequiredText(node, "id"))) throw Invalid();
                if (RequiredText(node, "canonicalAccountId") != account.Id) continue;
                var orderId = RequiredText(node, "orderId");
                if (!orderIds.Add(orderId)) throw Invalid();
                var quantity = Number(Required(node, "submittedQuantity"));
                var price = NullableNumber(Optional(node, "limitPrice"));
                var currency = RequiredText(node, "securityCurrency");
                if (quantity <= 0 || price is <= 0 || currency.Length != 3) throw Invalid();
                // Unknown statuses remain visible and block transition review. Pending cancellation
                // and partial fills are still outstanding; quantity is submitted, never remaining.
                orders.Add(new(CleanLabel(RequiredText(node, "symbol")), RequiredText(node, "side"),
                    RequiredText(node, "status"), RequiredText(node, "executionType"), quantity,
                    price.HasValue ? new(price.Value, currency) : null,
                    Timestamp(node, "createdAtUtc") ?? throw Invalid())
                    { OrderId = orderId, SecurityId = RequiredText(node, "securityId") });
            }
            cursor = NextCursor(page, cursors);
        } while (cursor != null);
        return new WealthsimplePendingOrders(account, clock.GetUtcNow(), orders.AsReadOnly());
    }, ct);

    private const string PendingOrdersQuery = """
        query TraderVITfsaPendingOrders($identityId: ID!, $statuses: [OrderServiceOrderStatus!]!, $cursor: String) {
          identity(id: $identityId) { id orderServiceExtendedOrderFeed(statuses: $statuses, first: 25, after: $cursor) {
            edges { node { id orderId canonicalAccountId createdAtUtc status side executionType
              submittedQuantity limitPrice securityCurrency securityId symbol } }
            pageInfo { hasNextPage endCursor }
          } }
        }
        """;
}
