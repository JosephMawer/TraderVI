#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Globalization;
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
    private static readonly Uri GraphQlUri = new("https://my.wealthsimple.com/graphql");
    private readonly List<WealthsimpleTfsaAccount> tfsaAccounts = [];
    private string? catalogIdentity;

    public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct = default) =>
        ReadLockedAsync<IReadOnlyList<WealthsimpleTfsaAccount>>(async () =>
        {
            var previous = catalogIdentity == session.IdentityId ? tfsaAccounts.ToDictionary(a => a.Id) : new Dictionary<string, WealthsimpleTfsaAccount>();
            tfsaAccounts.Clear();
            catalogIdentity = session.IdentityId;
            var found = new List<WealthsimpleTfsaAccount>();
            var ids = new HashSet<string>(); var cursors = new HashSet<string>(); string? cursor = null;
            do
            {
                using var json = await QueryAsync("TraderVITfsaAccounts", WealthsimpleAccountQueries.Accounts,
                    new { identityId = session.IdentityId, cursor }, ct);
                var identity = Required(json.RootElement, "data", "identity");
                RequireId(identity, session.IdentityId);
                var accounts = Required(identity, "accounts");
                foreach (var edge in RequiredArray(accounts, "edges"))
                {
                    var node = Required(edge, "node"); var id = RequiredText(node, "id");
                    if (!ids.Add(id)) throw Invalid();
                    if (StringValue(node, "type") != "tfsa" || StringValue(node, "branch") != "TR" ||
                        StringValue(node, "unifiedAccountType") != "SELF_DIRECTED_TFSA" ||
                        !Required(node, "isOpen").GetBoolean() || !IsNull(node, "closedAt") || !IsNull(node, "archivedAt")) continue;
                    var name = StringValue(node, "nickname");
                    if (string.IsNullOrWhiteSpace(name)) name = "TFSA";
                    string label = $"{CleanLabel(name)} · Self-directed TFSA ({found.Count + 1})";
                    found.Add(previous.TryGetValue(id, out var existing) && existing.DisplayName == label ? existing : new(id, label));
                }
                cursor = NextCursor(accounts, cursors);
            } while (cursor != null);
            tfsaAccounts.AddRange(found);
            return found.AsReadOnly();
        }, ct);

    public Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct = default) =>
        ReadLockedAsync(async () =>
        {
            // A caller cannot supply an arbitrary account ID or reuse another session's selection.
            if (account == null || catalogIdentity != session.IdentityId || !tfsaAccounts.Contains(account)) throw new ReadFailure(WealthsimpleReadStatus.AccountUnavailable, "account selection");
            using var financials = await QueryAsync("TraderVITfsaFinancials", WealthsimpleAccountQueries.Financials,
                new { ids = new[] { account.Id }, currency = "CAD" }, ct);
            var nodes = RequiredArray(Required(financials.RootElement, "data"), "accounts");
            if (nodes.Length != 1) throw Invalid();
            RequireId(nodes[0], account.Id);
            var combined = Required(nodes[0], "financials", "currentCombined");
            var returns = Optional(combined, "simpleReturns");

            using var balances = await QueryAsync("TraderVITfsaBalances", WealthsimpleAccountQueries.Balances,
                new { accountCanonicalId = account.Id, currency = "CAD" }, ct);
            var balanceAccount = Required(balances.RootElement, "data", "account");
            RequireId(balanceAccount, account.Id);
            var balance = Required(balanceAccount, "financials", "current", "tradingBalanceViewV2");

            var holdings = new List<WealthsimpleHolding>(); var ids = new HashSet<string>();
            var cursors = new HashSet<string>(); string? cursor = null; int? total = null;
            do
            {
                using var positions = await QueryAsync("TraderVITfsaPositions", WealthsimpleAccountQueries.Positions,
                    new { identityId = session.IdentityId, accountIds = new[] { account.Id }, currency = "CAD", cursor }, ct);
                var identity = Required(positions.RootElement, "data", "identity"); RequireId(identity, session.IdentityId);
                var page = Required(identity, "financials", "current", "positions");
                var statuses = RequiredArray(page, "status");
                if (statuses.Length == 0 || statuses.Any(s => s.GetString() != "OK")) throw Invalid();
                var count = Required(page, "totalCount").GetInt32();
                if (count < 0 || (total.HasValue && count != total)) throw Invalid();
                total = count;
                foreach (var edge in RequiredArray(page, "edges"))
                {
                    var node = Required(edge, "node");
                    if (!ids.Add(RequiredText(node, "id"))) throw Invalid();
                    var owners = RequiredArray(node, "accounts");
                    if (owners.Length != 1) throw Invalid();
                    RequireId(owners[0], account.Id);
                    var security = Required(node, "security"); var stock = Optional(security, "stock");
                    var type = RequiredText(security, "securityType");
                    holdings.Add(new(CleanLabel(StringValue(stock, "symbol") ?? type),
                        CleanLabel(StringValue(stock, "name") ?? "Instrument details unavailable"), type,
                        Number(Required(node, "quantity")), Money(node, "totalValue"), Money(node, "bookValue"), Money(node, "unrealizedReturns"))
                        { SecurityId = RequiredText(security, "id") });
                }
                cursor = NextCursor(page, cursors);
            } while (cursor != null);
            if (holdings.Count != total) throw Invalid();
            return new WealthsimpleTfsaSnapshot(account, clock.GetUtcNow(),
                Money(combined, "netLiquidationValueV2", cad: true), Money(balance, "cash", "quantity", true),
                Money(balance, "buyingPower", "quantity", true), Money(combined, "netDepositsV2", cad: true),
                Money(returns, "amount", cad: true), NullableNumber(Optional(returns, "rate")),
                DateText(returns, "referenceDate"), Timestamp(returns, "asOf"), holdings.AsReadOnly());
        }, ct);

    private async Task<WealthsimpleReadResult<T>> ReadLockedAsync<T>(Func<Task<T>> read, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(ct);
        try
        {
            if (session == null || session.ExpiresAt <= clock.GetUtcNow()) return new(WealthsimpleReadStatus.NotAuthenticated);
            if (string.IsNullOrEmpty(session.IdentityId)) return new(WealthsimpleReadStatus.UnsupportedResponse, Stage: "verified identity");
            var data = await read();
            if (session == null || session.ExpiresAt <= clock.GetUtcNow()) return new(WealthsimpleReadStatus.NotAuthenticated);
            return new(WealthsimpleReadStatus.Success, data);
        }
        catch (ReadFailure failure) { return new(failure.Status, Stage: failure.Stage); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException("Account read cancelled.", ct); }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        { return new(WealthsimpleReadStatus.Unavailable); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        { return new(WealthsimpleReadStatus.UnsupportedResponse); }
        finally { gate.Release(); }
    }

    private async Task<JsonDocument> QueryAsync(string operation, string query, object variables, CancellationToken ct)
    {
        if (session.ExpiresAt <= clock.GetUtcNow()) throw new ReadFailure(WealthsimpleReadStatus.NotAuthenticated, operation);
        using var response = await ExchangeAsync(GraphQlUri, new() { ["operationName"] = operation, ["query"] = query, ["variables"] = variables },
            new() { ["x-ws-identity-id"] = session.IdentityId, ["x-ws-profile"] = "trade", ["x-ws-api-version"] = "12", ["x-ws-operation-name"] = operation }, session, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        { session = null; tfsaAccounts.Clear(); throw new ReadFailure(WealthsimpleReadStatus.NotAuthenticated, operation); }
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new ReadFailure(WealthsimpleReadStatus.Forbidden, operation);
        if ((int)response.StatusCode == 429) throw new ReadFailure(WealthsimpleReadStatus.RateLimited, operation);
        if (!response.IsSuccessStatusCode) throw new ReadFailure((int)response.StatusCode >= 500 ? WealthsimpleReadStatus.Unavailable : WealthsimpleReadStatus.UnsupportedResponse, operation);
        var json = ReadJson(response);
        if (json == null) throw new ReadFailure(WealthsimpleReadStatus.UnsupportedResponse, operation);
        var errors = Optional(json.RootElement, "errors");
        if (errors.ValueKind != JsonValueKind.Undefined && errors.ValueKind != JsonValueKind.Null &&
            (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() != 0))
        {
            // Discard partial GraphQL data. Classify only standardized error codes, not arbitrary text.
            var expired = errors.ValueKind == JsonValueKind.Array && errors.EnumerateArray().Any(e =>
                StringValue(Optional(e, "extensions"), "code") is "UNAUTHENTICATED" or "UNAUTHORIZED");
            json.Dispose();
            if (expired) { session = null; tfsaAccounts.Clear(); }
            throw new ReadFailure(expired ? WealthsimpleReadStatus.NotAuthenticated : WealthsimpleReadStatus.UnsupportedResponse, operation);
        }
        return json;
    }

    private static JsonElement Optional(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child) ? child : default;
    private static JsonElement Required(JsonElement value, params string[] path)
    {
        foreach (var part in path) { value = Optional(value, part); if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) throw Invalid(); }
        return value;
    }
    private static JsonElement[] RequiredArray(JsonElement value, string name) => Required(value, name).EnumerateArray().ToArray();
    private static string RequiredText(JsonElement value, string name) => StringValue(value, name) is { Length: > 0 and <= 512 } text ? text : throw Invalid();
    private static bool IsNull(JsonElement value, string name) => Optional(value, name).ValueKind is JsonValueKind.Null; // missing fields are not proof of an open account
    private static void RequireId(JsonElement value, string id) { if (RequiredText(value, "id") != id) throw Invalid(); }
    private static string CleanLabel(string value) => new(value.Take(120).Where(c => !char.IsControl(c)).ToArray());
    private static string? NextCursor(JsonElement page, HashSet<string> seen)
    {
        var info = Required(page, "pageInfo");
        if (!Required(info, "hasNextPage").GetBoolean()) return null;
        var cursor = RequiredText(info, "endCursor");
        if (!seen.Add(cursor) || seen.Count >= 100) throw Invalid();
        return cursor;
    }
    private static decimal Number(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDecimal() :
        value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out var amount) ? amount : throw Invalid();
    private static decimal? NullableNumber(JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : Number(value);
    private static WealthsimpleMoney? Money(JsonElement parent, string name, string amountName = "amount", bool cad = false)
    {
        var value = Optional(parent, name);
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        var amount = NullableNumber(Optional(value, amountName));
        if (amount == null) return null;
        var currency = RequiredText(value, "currency");
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') || (cad && currency != "CAD")) throw Invalid();
        return new(amount.Value, currency);
    }
    private static string? DateText(JsonElement parent, string name)
    {
        var text = StringValue(parent, name); if (text == null) return null;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? text : throw Invalid();
    }
    private static DateTimeOffset? Timestamp(JsonElement parent, string name)
    {
        var text = StringValue(parent, name); if (text == null) return null;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : throw Invalid();
    }
    private static ReadFailure Invalid() => new(WealthsimpleReadStatus.UnsupportedResponse, "account data");
    private sealed class ReadFailure(WealthsimpleReadStatus status, string stage) : Exception("Account data unavailable.")
    { internal WealthsimpleReadStatus Status { get; } = status; internal string Stage { get; } = stage; }
}
