#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    /// <summary>Discover orders through the captured OWN activity feed, then verify each order against the owned account.</summary>
    public Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadTfsaOrderHistoryAsync(WealthsimpleTfsaAccount account,
        CancellationToken ct = default) => ReadLockedAsync(async () =>
    {
        if (account == null || !OwnsAccount(account))
            throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "history account");
        string identity = session.IdentityId;
        var references = new Dictionary<string, (string Security, string Symbol, string Side)>();
        var activityReportedFills = new HashSet<string>();
        var activities = new HashSet<string>(); var cursors = new HashSet<string>();
        var limitations = new List<string>(); string? cursor = null, head = null; int pages = 0;
        do
        {
            // Exhaust the selected account's feed. A cap is a failure, never truncated success.
            if (++pages > 200) throw new ReadFailure(WealthsimpleReadStatus.Unavailable, "history page limit");
            using var json = await QueryAsync("TraderVITfsaActivityHistory", ReconciliationActivityQuery,
                new { first = 50, cursor, condition = new { accountIds = new[] { account.Id } },
                    orderBy = new[] { "OCCURRED_AT_DESC" }, accountScope = "OWN" }, ct);
            var page = Required(json.RootElement, "data", "activityFeedItems");
            if (pages == 1) head = ActivityFingerprint(page, account.Id);
            foreach (var edge in RequiredArray(page, "edges"))
            {
                var node = Required(edge, "node");
                if (RequiredText(node, "accountId") != account.Id) throw Invalid("activity account mismatch");
                if (!activities.Add(RequiredText(node, "canonicalId"))) throw Invalid("duplicate activity across history pages");
                var type = RequiredText(node, "type");
                if (type is not ("DIY_BUY" or "DIY_SELL"))
                {
                    bool cashOnly = type is "DEPOSIT" or "WITHDRAWAL" or "INTEREST" or "INTERNAL_TRANSFER" &&
                        StringValue(node, "securityId") == null && NullableNumber(Optional(node, "assetQuantity")) == null;
                    if (!cashOnly && !(type == "DIVIDEND" && StringValue(node, "subType") == "CASH_DIVIDEND"))
                        limitations.Add("Account activity includes transfers or unsupported events; review their effect on holdings and cost basis.");
                    continue;
                }
                var id = RequiredText(node, "externalCanonicalId");
                if (!id.StartsWith("order-", StringComparison.Ordinal) || id.Length > 512) throw Invalid("unsupported activity order reference");
                if (ActivityStatus(node) is "FILLED" or "POSTED" or "PARTIALLY_FILLED") activityReportedFills.Add(id);
                var reference = (RequiredText(node, "securityId"), CleanLabel(RequiredText(node, "assetSymbol")), type == "DIY_BUY" ? "BUY" : "SELL");
                if (references.TryGetValue(id, out var prior) && prior != reference) throw Invalid("conflicting activity order references");
                references[id] = reference;
            }
            cursor = NextCursor(page, cursors);
        } while (cursor != null);
        var orders = new List<BrokerOrderFact>();
        var instruments = new Dictionary<string, BrokerInstrumentIdentity>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            using var json = await QueryAsync("TraderVITfsaHistoricalOrder", ReconciliationOrderQuery,
                new { branchId = "TR", externalId = reference.Key }, ct);
            var data = Required(json.RootElement, "data", "orderServiceExtendedOrderByExternalId");
            RequireId(data, reference.Key);
            if (RequiredText(data, "canonicalAccountId") != account.Id || RequiredText(data, "identityId") != identity ||
                RequiredText(data, "securityId") != reference.Value.Security || RequiredText(data, "side") != reference.Value.Side)
                throw Invalid("historical order account, identity, security or side mismatch");
            var security = Required(data, "security"); RequireId(security, reference.Value.Security);
            var stock = Optional(security, "stock");
            // Null stock data on old/delisted instruments permits exact-symbol history only.
            // Never infer a listing or alias from the activity label alone.
            if (stock.ValueKind == JsonValueKind.Object && StringValue(stock, "symbol") is { Length: > 0 } listedSymbol &&
                StringValue(stock, "primaryMic") is { Length: > 0 } mic)
            {
                var instrument = new BrokerInstrumentIdentity(reference.Value.Security, listedSymbol, mic,
                    RequiredText(data, "securityCurrency"), RequiredText(security, "securityType"));
                if (instruments.TryGetValue(instrument.SecurityId, out var old) && old != instrument)
                    throw Invalid("historical instrument listing changed during read");
                instruments[instrument.SecurityId] = instrument;
            }
            var status = HistoricalOrderStatus(data);
            decimal? Amount(string name) => NullableNumber(RequiredOrNull(data, name));
            var quantity = Amount("fillQuantity");
            var gross = Amount("filledValue"); var net = Amount("filledNetValue");
            var fees = Amount("filledTotalFee"); var tax = Amount("filledTaxWithholding");
            var lastFilled = Timestamp(data, "lastFilledAtUtc");
            bool nullTerminalQuantity = false;
            if (quantity == null)
            {
                var averagePrice = Amount("averageFillPrice");
                // The operator's live diagnostics identified EXPIRED and CANCELLED with no execution report.
                // Check every selected fill field, including average price, before accounting for
                // it as an empty terminal order. Preserve null provenance in the saved observation.
                nullTerminalQuantity = BrokerOrderFact.AllowsEmptyExecutionReport(status) && !activityReportedFills.Contains(reference.Key) &&
                    gross == null && net == null && fees == null && tax == null &&
                    RequiredOrNull(data, "lastFilledAtUtc").ValueKind == JsonValueKind.Null && averagePrice == null;
                if (!nullTerminalQuantity)
                    throw new ReadFailure(WealthsimpleReadStatus.Unavailable, MissingFillDiagnostic(status, gross, net, fees, tax, lastFilled,
                        averagePrice, activityReportedFills.Contains(reference.Key)));
                quantity = 0;
            }
            // Missing details for an actual execution cannot replace confirmed history.
            if (quantity > 0 && (gross == null || net == null || fees == null || tax == null || lastFilled == null))
                throw new ReadFailure(WealthsimpleReadStatus.Unavailable, "order fill details");
            orders.Add(new(reference.Key, reference.Value.Security, reference.Value.Symbol, reference.Value.Side,
                status, RequiredText(data, "securityCurrency"), RequiredText(security, "securityType"),
                Number(Required(data, "submittedQuantity")), quantity.Value, gross, net, fees, tax,
                Timestamp(data, "createdAtUtc") ?? throw Invalid(), lastFilled) { FillQuantityWasNull = nullTerminalQuantity });
        }
        if (session.IdentityId != identity || !OwnsAccount(account)) throw Invalid();
        return new BrokerAccountHistory(identity, account.Id, clock.GetUtcNow(), orders.ToArray(), limitations.Distinct().ToArray())
            { ActivityFingerprint = head, Instruments = instruments.Count == 0 ? null : instruments.Values.OrderBy(i => i.SecurityId, StringComparer.Ordinal).ToArray() };
    }, ct, "order history");

    public Task<WealthsimpleReadResult<BrokerActivityCheckpoint>> ReadTfsaActivityCheckpointAsync(WealthsimpleTfsaAccount account,
        CancellationToken ct = default) => ReadLockedAsync(async () =>
    {
        if (account == null || !OwnsAccount(account)) throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "history checkpoint");
        using var json = await QueryAsync("TraderVITfsaActivityHistory", ReconciliationActivityQuery,
            new { first = 50, cursor = (string?)null, condition = new { accountIds = new[] { account.Id } },
                orderBy = new[] { "OCCURRED_AT_DESC" }, accountScope = "OWN" }, ct);
        return new BrokerActivityCheckpoint(session.IdentityId, account.Id,
            ActivityFingerprint(Required(json.RootElement, "data", "activityFeedItems"), account.Id), clock.GetUtcNow());
    }, ct, "activity checkpoint");

    private static string ActivityFingerprint(JsonElement page, string account)
    {
        var nodes = RequiredArray(page, "edges").Select(e => Required(e, "node")).ToArray();
        if (nodes.Any(n => RequiredText(n, "accountId") != account) || nodes.Select(n => RequiredText(n, "canonicalId")).Distinct().Count() != nodes.Length)
            throw Invalid("activity checkpoint account mismatch or duplicate activity");
        var facts = nodes.Select(n => new { Id = RequiredText(n, "canonicalId"), External = StringValue(n, "externalCanonicalId"),
            Type = RequiredText(n, "type"), Status = ActivityStatus(n), Quantity = NullableNumber(Optional(n, "assetQuantity")) })
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(facts)));
    }

    private static string? ActivityStatus(JsonElement node)
    {
        // Feed status is change-detection metadata, not evidence that an order filled. Preserve
        // null/empty values in the checkpoint; completion always comes from verified order details.
        if (!node.TryGetProperty("status", out var value)) throw Invalid("activity feed status field missing");
        return value.ValueKind == JsonValueKind.Null ? null :
            value.ValueKind == JsonValueKind.String && value.GetString() is { Length: <= 512 } status
                ? status : throw Invalid("activity feed status has invalid format");
    }

    private static string HistoricalOrderStatus(JsonElement data) =>
        StringValue(data, "status") is { Length: > 0 and <= 512 } status ? status :
            throw Invalid("historical order status missing or invalid");

    private static string MissingFillDiagnostic(string status, decimal? gross, decimal? net, decimal? fees, decimal? tax,
        DateTimeOffset? lastFill, decimal? averagePrice, bool activityReportedFills)
    {
        // Only known enums and field-presence categories enter diagnostics, never raw values or IDs.
        string state = status is "FILLED" or "POSTED" or "CANCELLED" or "CANCELED" or "EXPIRED" or "REJECTED" or
            "SUBMITTED" or "PARTIALLY_FILLED" or "CANCEL_PENDING" ? status : "unrecognized";
        bool anyAmounts = gross != null || net != null || fees != null || tax != null;
        return $"order fill quantity missing / status {state} / fill amounts {(anyAmounts ? "present" : "absent")} / " +
            $"fill timestamp {(lastFill.HasValue ? "present" : "absent")} / average fill price {(averagePrice.HasValue ? "present" : "absent")}" +
            (activityReportedFills ? " / activity reports a fill" : "");
    }

    private const string ReconciliationActivityQuery = """
        query TraderVITfsaActivityHistory($first: Int, $cursor: Cursor, $condition: ActivityCondition,
          $orderBy: [ActivitiesOrderBy!] = OCCURRED_AT_DESC, $accountScope: AccountScope = OWN) {
          activityFeedItems(first: $first, after: $cursor, condition: $condition, orderBy: $orderBy, accountScope: $accountScope) {
            edges { node { accountId canonicalId externalCanonicalId type subType status securityId assetSymbol assetQuantity } }
            pageInfo { hasNextPage endCursor }
          }
        }
        """;
    private const string ReconciliationOrderQuery = """
        query TraderVITfsaHistoricalOrder($branchId: String!, $externalId: String!) {
          orderServiceExtendedOrderByExternalId(branchId: $branchId, externalId: $externalId) {
            id canonicalAccountId identityId securityId side status securityCurrency submittedQuantity fillQuantity
            averageFillPrice filledValue filledNetValue filledTotalFee filledTaxWithholding createdAtUtc lastFilledAtUtc
            security { id securityType stock { symbol primaryMic } }
          }
        }
        """;
}
