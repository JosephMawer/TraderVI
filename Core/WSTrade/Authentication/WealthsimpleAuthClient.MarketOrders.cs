#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    private async Task<WealthsimpleMarketOrderSubmission> SubmitMarketOrderAsync(WealthsimpleMarketOrder order,
        WealthsimpleOrderAttemptJournal journal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order); ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(ct);
        try
        {
            if (session == null || session.ExpiresAt <= clock.GetUtcNow() || string.IsNullOrEmpty(session.IdentityId))
                return new(WealthsimpleSubmissionStatus.SessionRequired);
            if (!OwnsAccount(order.Account)) return new(WealthsimpleSubmissionStatus.AccountUnavailable);
            if (!ReferenceEquals(order.Security.SourceClient, this) || order.Security.SourceIdentity != session.IdentityId)
                return new(WealthsimpleSubmissionStatus.SecurityUnavailable);
            try
            {
                using var security = await QueryAsync(order.OperationPrefix + "Eligibility", MarketOrderEligibilityQuery(order),
                    new { securityId = order.Security.Id }, ct);
                var data = Required(security.RootElement, "data", "security"); RequireId(data, order.Security.Id);
                if (RequiredText(data, "currency") != "CAD" || RequiredText(data, "securityType") != "EQUITY" ||
                    !Required(data, "active").GetBoolean() || !Required(data, order.IsSell ? "sellable" : "buyable").GetBoolean() || !Required(data, "wsTradeEligible").GetBoolean() ||
                    !RequiredArray(data, "allowedOrderSubtypes").Any(v => v.GetString() == "MARKET") ||
                    RequiredText(Required(data, "stock"), "primaryMic") != order.Security.Exchange)
                    return new(WealthsimpleSubmissionStatus.SecurityUnavailable);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is ReadFailure or JsonException or InvalidOperationException or FormatException or HttpRequestException or IOException or OperationCanceledException)
            { return new(WealthsimpleSubmissionStatus.PreflightUnavailable); }

            ct.ThrowIfCancellationRequested();
            if (session == null || session.ExpiresAt <= clock.GetUtcNow()) return new(WealthsimpleSubmissionStatus.SessionRequired);
            try { if (!journal.TryBegin(session.IdentityId, order)) return new(WealthsimpleSubmissionStatus.AlreadyAttempted); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { return new(WealthsimpleSubmissionStatus.JournalUnavailable); }

            // After this point even cancellation, malformed success, HTTP failures or a crash leave
            // the attempt fenced. Query by external ID after reconnecting; do not replace/resubmit it.
            try
            {
                using var response = await ExchangeAsync(GraphQlUri, MarketOrderBody(order), new()
                {
                    ["x-ws-identity-id"] = session.IdentityId, ["x-ws-profile"] = "trade", ["x-ws-api-version"] = "12",
                    ["x-ws-operation-name"] = "SoOrdersOrderCreate",
                    ["x-ws-operation-hash"] = "3b3bbe3e7c87ef7dbe00f8041fcfa911d84a73ce2f19cdc76ffefc76a79c046c"
                }, session, ct, allowNonceRetry: false);
                if (response.StatusCode == HttpStatusCode.Unauthorized) { session = null; tfsaAccounts.Clear(); }
                if (!response.IsSuccessStatusCode) return new(WealthsimpleSubmissionStatus.OutcomeUnknown);
                using var json = ReadJson(response);
                if (json == null || HasErrors(Optional(json.RootElement, "errors"))) return new(WealthsimpleSubmissionStatus.OutcomeUnknown);
                var creation = Required(json.RootElement, "data", "soOrdersCreateOrder");
                var errors = RequiredOrNull(creation, "errors"); var acknowledgement = RequiredOrNull(creation, "order");
                if (HasErrors(errors)) return new(acknowledgement.ValueKind == JsonValueKind.Null && errors.ValueKind == JsonValueKind.Array
                    ? WealthsimpleSubmissionStatus.Rejected : WealthsimpleSubmissionStatus.OutcomeUnknown);
                var id = RequiredText(acknowledgement, "orderId");
                return new(WealthsimpleSubmissionStatus.Acknowledged, Timestamp(acknowledgement, "createdAt") ?? throw Invalid()) { BrokerOrderId = id };
            }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or JsonException or ReadFailure or InvalidOperationException or FormatException or OverflowException)
            { return new(WealthsimpleSubmissionStatus.OutcomeUnknown); }
        }
        finally { gate.Release(); }
    }

    private Task<WealthsimpleReadResult<WealthsimpleMarketOrderState>> ReadMarketOrderAsync(WealthsimpleMarketOrder order, string? expectedIdentity, CancellationToken ct) =>
        ReadLockedAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(order);
            if (expectedIdentity != null && expectedIdentity != session.IdentityId)
                throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "order identity");
            if (!OwnsAccount(order.Account)) throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "order account");
            using var json = await QueryAsync(order.OperationPrefix + "Order", MarketOrderQuery(order), new { branchId = "TR", externalId = order.ExternalId }, ct);
            var data = Required(json.RootElement, "data", "orderServiceExtendedOrderByExternalId");
            RequireId(data, order.ExternalId);
            if (RequiredText(data, "canonicalAccountId") != order.Account.Id || RequiredText(data, "identityId") != session.IdentityId ||
                RequiredText(data, "securityId") != order.Security.Id || RequiredText(data, "side") != order.Side ||
                RequiredText(data, "orderType") != order.OrderType || RequiredText(data, "executionType") != "MARKET" ||
                RequiredText(data, "timeInForce") != "DAY" || RequiredText(data, "securityCurrency") != "CAD" ||
                Number(Required(data, "submittedQuantity")) != order.Quantity) throw Invalid();
            var filled = NullableNumber(RequiredOrNull(data, "fillQuantity"));
            var average = NullableNumber(RequiredOrNull(data, "averageFillPrice"));
            var status = RequiredText(data, "status");
            var lastFill = Timestamp(data, "lastFilledAtUtc");
            if (filled < 0 || filled > order.Quantity || (filled > 0 && (average is null or <= 0 || lastFill == null)) ||
                (status is "FILLED" or "POSTED" && filled != order.Quantity)) throw Invalid();
            WealthsimpleMoney? Cad(string name) => NullableNumber(RequiredOrNull(data, name)) is { } amount ? new(amount, "CAD") : null;
            return new WealthsimpleMarketOrderState(status, order.Quantity, filled, average.HasValue ? new(average.Value, "CAD") : null,
                Cad("filledValue"), Cad("filledNetValue"), Cad("filledTotalFee"), Cad("filledTaxWithholding"),
                Timestamp(data, "createdAtUtc") ?? throw Invalid(), lastFill, Timestamp(data, "expiredAtUtc"),
                RequiredText(data, "tradingSession"), clock.GetUtcNow());
        }, ct);

    private bool OwnsAccount(WealthsimpleTfsaAccount account) => catalogIdentity == session.IdentityId && tfsaAccounts.Contains(account);
    private static bool HasErrors(JsonElement errors) => errors.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
        (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() > 0);
    private static JsonElement RequiredOrNull(JsonElement data, string name) => data.TryGetProperty(name, out var value) ? value : throw Invalid();
    internal static Dictionary<string, object> MarketOrderBody(WealthsimpleMarketOrder order) => new()
    {
        ["operationName"] = "SoOrdersOrderCreate", ["query"] = MarketOrderMutation,
        ["variables"] = new { input = new { canonicalAccountId = order.Account.Id, externalId = order.ExternalId,
            executionType = "MARKET", orderType = order.OrderType, quantity = order.Quantity, securityId = order.Security.Id, timeInForce = "DAY" } }
    };
    internal const string MarketOrderMutation = """
        mutation SoOrdersOrderCreate($input: SoOrders_CreateOrderInput!) {
          soOrdersCreateOrder(input: $input) {
            errors {
              code
              message
              __typename
            }
            order {
              orderId
              createdAt
              __typename
            }
            __typename
          }
        }
        """;
    private static string MarketOrderEligibilityQuery(WealthsimpleMarketOrder order) => $$"""
        query {{order.OperationPrefix}}Eligibility($securityId: ID!) {
          security(id: $securityId) { id currency securityType active {{(order.IsSell ? "sellable" : "buyable")}} wsTradeEligible allowedOrderSubtypes stock { primaryMic } }
        }
        """;
    private static string MarketOrderQuery(WealthsimpleMarketOrder order) => $$"""
        query {{order.OperationPrefix}}Order($branchId: String!, $externalId: String!) {
          orderServiceExtendedOrderByExternalId(branchId: $branchId, externalId: $externalId) {
            id canonicalAccountId identityId securityId side orderType executionType timeInForce securityCurrency tradingSession
            status submittedQuantity fillQuantity averageFillPrice filledValue filledNetValue filledTotalFee filledTaxWithholding
            createdAtUtc lastFilledAtUtc expiredAtUtc
          }
        }
        """;
}
