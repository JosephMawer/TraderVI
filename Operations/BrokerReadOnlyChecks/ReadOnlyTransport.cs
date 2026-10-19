using System.Text.Json;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;

namespace BrokerReadOnlyChecks;

internal sealed class ReadOnlyTransport(string access, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    internal static readonly Uri InfoUri = new("https://api.production.wealthsimple.com/v1/oauth/v2/token/info");
    internal static readonly Uri QueryUri = new("https://my.wealthsimple.com/graphql");
    private string? identity, account;
    private int requests;
    internal int RequestCount => Math.Min(requests, 20);
    internal void BindIdentity(string id) { if (identity is not null) throw Rejected(); identity = id; }
    internal void BindAccount(string id) { if (identity is null || account is not null) throw Rejected(); account = id; }

    internal static readonly IReadOnlyDictionary<string, string> Queries = new Dictionary<string, string>
    {
        ["TraderVITfsaAccounts"] = WealthsimpleAccountQueries.Accounts,
        ["TraderVITfsaFinancials"] = WealthsimpleAccountQueries.Financials,
        ["TraderVITfsaBalances"] = WealthsimpleAccountQueries.Balances,
        ["TraderVITfsaPositions"] = WealthsimpleAccountQueries.Positions,
        ["TraderVITfsaPendingOrders"] = WealthsimpleAuthClient.ReadOnlyPendingQuery,
        ["TraderVITfsaDailyPerformance"] = WealthsimpleAccountQueries.DailyPerformance
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await ValidateAsync(request, ct);
        if (Interlocked.Increment(ref requests) > 20) throw Rejected();
        return await base.SendAsync(request, ct);
    }

    internal async Task ValidateAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        var uri = request.RequestUri;
        string[] allowedHeaders = ["Authorization", "Accept", "Date", "X-WS-Device-ID", "X-WS-Session-ID",
            "X-Wealthsimple-Client", "x-ws-identity-id", "x-ws-profile", "x-ws-api-version", "x-ws-operation-name"];
        if (request.Headers.Any(h => !allowedHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase))) throw Rejected();
        if (uri is null || uri.Fragment.Length != 0 || uri.Query.Length != 0 || uri.UserInfo.Length != 0
            || request.Headers.Authorization?.Scheme != "Bearer" || request.Headers.Authorization.Parameter != access) throw Rejected();
        if (request.Method == HttpMethod.Get && uri == InfoUri && request.Content is null && identity is null) return;
        if (request.Method != HttpMethod.Post || uri != QueryUri || identity is null || request.Content is null
            || request.Content.Headers.ContentType?.MediaType != "application/json"
            || request.Content.Headers.ContentLength is > 32768) throw Rejected();
        string body = await request.Content.ReadAsStringAsync(ct);
        if (body.Length > 32768) throw Rejected();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Keys(root, "operationName", "query", "variables");
        var operation = root.GetProperty("operationName").GetString();
        if (operation is null || !Queries.TryGetValue(operation, out var canonical)
            || root.GetProperty("query").GetString() != canonical) throw Rejected();
        if (!request.Headers.TryGetValues("x-ws-identity-id", out var owners) || !owners.SequenceEqual([identity])
            || !request.Headers.TryGetValues("x-ws-operation-name", out var names) || !names.SequenceEqual([operation])) throw Rejected();
        var vars = root.GetProperty("variables");
        switch (operation)
        {
            case "TraderVITfsaAccounts":
                Keys(vars, "identityId", "cursor"); Text(vars, "identityId", identity); Cursor(vars); break;
            case "TraderVITfsaFinancials":
                Keys(vars, "ids", "currency"); IdArray(vars, "ids"); Currency(vars); break;
            case "TraderVITfsaBalances":
                Keys(vars, "accountCanonicalId", "currency"); Selected(vars, "accountCanonicalId"); Currency(vars); break;
            case "TraderVITfsaPositions":
                Keys(vars, "identityId", "accountIds", "currency", "cursor");
                Text(vars, "identityId", identity); IdArray(vars, "accountIds"); Currency(vars); Cursor(vars); break;
            case "TraderVITfsaPendingOrders":
                if (account is null) throw Rejected();
                Keys(vars, "identityId", "statuses", "cursor"); Text(vars, "identityId", identity); Cursor(vars);
                if (!vars.GetProperty("statuses").EnumerateArray().Select(v => v.GetString()).SequenceEqual(WealthsimpleAuthClient.ReadOnlyPendingStatuses)) throw Rejected();
                break;
            case "TraderVITfsaDailyPerformance":
                Keys(vars, "id", "currency", "timeRange", "marketSession", "includeSimpleReturns");
                Selected(vars, "id"); Currency(vars); Text(vars, "timeRange", "ONE_DAY"); Text(vars, "marketSession", "REGULAR");
                if (!vars.GetProperty("includeSimpleReturns").GetBoolean()) throw Rejected();
                break;
            default: throw Rejected();
        }
    }

    private void Selected(JsonElement vars, string key) { if (account is null) throw Rejected(); Text(vars, key, account); }
    private void IdArray(JsonElement vars, string key)
    {
        var ids = vars.GetProperty(key);
        if (account is null || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() != 1 || ids[0].GetString() != account) throw Rejected();
    }
    private static void Currency(JsonElement vars) => Text(vars, "currency", "CAD");
    private static void Text(JsonElement vars, string key, string expected) { if (vars.GetProperty(key).GetString() != expected) throw Rejected(); }
    private static void Cursor(JsonElement vars)
    {
        var value = vars.GetProperty("cursor");
        if (value.ValueKind == JsonValueKind.Null) return;
        var text = value.GetString();
        if (string.IsNullOrEmpty(text) || text.Length > 512 || text.Any(char.IsControl)) throw Rejected();
    }
    private static void Keys(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(names.Order())) throw Rejected();
    }
    private static InvalidOperationException Rejected() => new("Read-only broker transport rejected the request.");
}
