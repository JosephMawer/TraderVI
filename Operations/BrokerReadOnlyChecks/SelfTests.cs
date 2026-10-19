using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BrokerReadOnlyChecks;

// All credentials, identities and responses below are synthetic. No DPAPI file or network access.
internal static class SelfTests
{
    private const string Token = "synthetic-read-only-token";
    private const string Identity = "synthetic-identity";
    private const string Account = "synthetic-tfsa";
    private static int checks;
    internal static async Task<int> RunAsync()
    {
        checks = 0;
        foreach (var operation in ReadOnlyTransport.Queries.Keys)
        {
            if (operation == "TraderVITfsaHistoricalOrder") continue; // No order ID is trusted before owned-feed discovery.
            using var guard = BoundGuard();
            using var request = Request(operation);
            await guard.ValidateAsync(request); checks++;
        }
        using (var guard = new ReadOnlyTransport(Token, new FakeHandler()))
        using (var request = Info()) { await guard.ValidateAsync(request); checks++; }
        foreach (var uri in new[]
        {
            "https://example.invalid/graphql", "http://my.wealthsimple.com/graphql",
            "https://my.wealthsimple.com/graphql?query=mutation", "https://my.wealthsimple.com/graphql#extra",
            "https://my.wealthsimple.com/graphql/extra", "https://my.wealthsimple.com:444/graphql",
            "https://name@my.wealthsimple.com/graphql", "https://api.production.wealthsimple.com/v1/oauth/v2/token"
        }) await Denied(request => request.RequestUri = new(uri));
        await Denied(request => request.Method = HttpMethod.Delete);
        await Denied(request => request.Method = HttpMethod.Get);
        await Denied(request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-synthetic-token"));
        await Denied(request => request.Headers.Remove("x-ws-identity-id"));
        await Denied(request => request.Headers.Remove("x-ws-operation-name"));
        await Denied(request => request.Headers.Add("X-HTTP-Method-Override", "DELETE"));
        await Denied(request => request.Headers.Add("Cookie", "synthetic-cookie"));
        await Denied(request => request.Content = Body("TraderVITfsaAccounts", "mutation { submitOrder { id } }", Variables("TraderVITfsaAccounts")));
        await Denied(request => request.Content = Body("UnknownRead", ReadOnlyTransport.Queries["TraderVITfsaAccounts"], Variables("TraderVITfsaAccounts")));
        await Denied(request => request.Content = Body("TraderVITfsaAccounts", ReadOnlyTransport.Queries["TraderVITfsaAccounts"] + "\nquery Extra { identity { id } }", Variables("TraderVITfsaAccounts")));
        await Denied(request => request.Content = Body("TraderVITfsaAccounts", ReadOnlyTransport.Queries["TraderVITfsaAccounts"], new { identityId = "other-identity", cursor = (string?)null }));
        await Denied(request => request.Content = Body("TraderVITfsaAccounts", ReadOnlyTransport.Queries["TraderVITfsaAccounts"], new { identityId = Identity, cursor = new string('x', 513) }));
        await Denied(request => request.Content = new StringContent("{}", Encoding.UTF8, "text/plain"));
        await Denied(request => request.Content = new StringContent(new string('x', 32769), Encoding.UTF8, "application/json"));
        await Denied(request => request.Content = Body("TraderVITfsaFinancials", ReadOnlyTransport.Queries["TraderVITfsaFinancials"], new { ids = new[] { "other-account" }, currency = "CAD" }), "TraderVITfsaFinancials");
        await Denied(request => request.Content = Body("TraderVITfsaBalances", ReadOnlyTransport.Queries["TraderVITfsaBalances"], new { accountCanonicalId = Account, currency = "USD" }), "TraderVITfsaBalances");
        await Denied(request => request.Content = Body("TraderVITfsaDailyPerformance", ReadOnlyTransport.Queries["TraderVITfsaDailyPerformance"], new { id = Account, currency = "CAD", timeRange = "ALL", marketSession = "REGULAR", includeSimpleReturns = true }), "TraderVITfsaDailyPerformance");
        await Denied(request => request.Content = Body("TraderVITfsaPendingOrders", ReadOnlyTransport.Queries["TraderVITfsaPendingOrders"], new { identityId = Identity, statuses = new[] { "FILLED" }, cursor = (string?)null }), "TraderVITfsaPendingOrders");
        await Denied(request => request.Content = Body("TraderVITfsaAccounts", ReadOnlyTransport.Queries["TraderVITfsaAccounts"], new { identityId = Identity, cursor = (string?)null, extra = "not-allowed" }));
        using (var guard = new ReadOnlyTransport(Token, new FakeHandler()))
        using (var request = Request("TraderVITfsaAccounts")) { await MustReject(() => guard.ValidateAsync(request)); }
        using (var guard = new ReadOnlyTransport(Token, new FakeHandler()))
        {
            guard.BindIdentity(Identity);
            using var request = Request("TraderVITfsaFinancials");
            await MustReject(() => guard.ValidateAsync(request));
        }
        var inner = new FakeHandler();
        using (var guard = new ReadOnlyTransport(Token, inner))
        using (var client = new HttpClient(guard))
        {
            for (int i = 0; i < ReadOnlyTransport.MaximumRequests; i++) { using var request = Info(); using var response = await client.SendAsync(request); }
            using var over = Info(); await MustReject(async () => { using var response = await client.SendAsync(over); });
            Require(inner.Forwarded == ReadOnlyTransport.MaximumRequests);
        }
        await Pipeline();
        await Pipeline(foreignPosition: true);
        await Pipeline(graphQlError: true);
        await HistoryBoundary();
        var expiredHandler = new FakeHandler();
        using (var expired = WealthsimpleAuthClient.FromReadOnlySession(Session(DateTimeOffset.UtcNow.AddMinutes(-1)), expiredHandler))
            Require(!await expired.VerifyReadOnlyAsync(default) && expiredHandler.Forwarded == 0);
        var rejectedHandler = new FakeHandler { RejectAuth = true };
        using (var rejected = WealthsimpleAuthClient.FromReadOnlySession(Session(), rejectedHandler))
            Require(!await rejected.VerifyReadOnlyAsync(default) && rejectedHandler.Forwarded == 1);
        Require(!typeof(WealthsimpleAuthClient).GetMethods().Any(m => m.Name is "RefreshAsync" or "BeginLoginAsync" or "ProtectSessionAsync" or "FromBrowserHar"));
        Require(!typeof(WealthsimpleAuthClient).Assembly.GetReferencedAssemblies().Any(a => a.Name is "Core" or "Microsoft.Data.SqlClient"));
        Console.WriteLine(JsonSerializer.Serialize(new { HelperVersion = 2, OfflineChecksPassed = checks, NetworkRequests = 0, SavedSessionAccess = false }));
        return 0;
    }
    private static async Task Pipeline(bool foreignPosition = false, bool graphQlError = false)
    {
        var fake = new FakeHandler { ForeignPosition = foreignPosition, GraphQlError = graphQlError };
        using var client = WealthsimpleAuthClient.FromReadOnlySession(Session(), fake);
        Require(await client.VerifyReadOnlyAsync(default));
        var catalog = await client.ReadTfsaAccountsAsync();
        Require(catalog.Status == WealthsimpleReadStatus.Success && catalog.Data?.Count == 1);
        var account = catalog.Data![0]; client.SelectReadOnlyAccount(account);
        var snapshot = await client.ReadTfsaSnapshotAsync(account);
        if (foreignPosition || graphQlError)
        {
            Require(snapshot.Status == WealthsimpleReadStatus.UnsupportedResponse && snapshot.Data is null);
            return;
        }
        Require(snapshot.Status == WealthsimpleReadStatus.Success && snapshot.Data?.Holdings.Count == 1);
        var pending = await client.ReadTfsaPendingOrdersAsync(account);
        Require(pending.Status == WealthsimpleReadStatus.Success && pending.Data?.Orders.Count == 1);
        var daily = await client.ReadTfsaDailyPerformanceAsync(account);
        Require(daily.Status == WealthsimpleReadStatus.Success && daily.Data?.Amount is not null);
        Require(fake.Forwarded == 7 && client.RequestCount == 7);
        var history = await client.ReadTfsaOrderHistoryAsync(account);
        Require(history.Status == WealthsimpleReadStatus.Success && history.Data?.Orders.Count == 1);
        var checkpoint = await client.ReadTfsaActivityCheckpointAsync(account);
        Require(checkpoint.Status == WealthsimpleReadStatus.Success && checkpoint.Data?.Fingerprint == history.Data!.ActivityFingerprint);
        Require(fake.Forwarded == 10 && client.RequestCount == 10);
    }
    private static async Task HistoryBoundary()
    {
        await Denied(r => { }, "TraderVITfsaHistoricalOrder");
        await Denied(r => r.Content = Body("TraderVITfsaActivityHistory", ReadOnlyTransport.Queries["TraderVITfsaActivityHistory"],
            new { first = 50, cursor = (string?)null, condition = new { accountIds = new[] { "foreign-account" } },
                orderBy = new[] { "OCCURRED_AT_DESC" }, accountScope = "OWN" }), "TraderVITfsaActivityHistory");
        using var guard = BoundGuard(); using var http = new HttpClient(guard);
        using (var read = Request("TraderVITfsaActivityHistory")) using (await http.SendAsync(read)) { }
        using (var order = Request("TraderVITfsaHistoricalOrder")) { await guard.ValidateAsync(order); checks++; }
        using (var unknown = Request("TraderVITfsaHistoricalOrder"))
        {
            unknown.Content = Body("TraderVITfsaHistoricalOrder", ReadOnlyTransport.Queries["TraderVITfsaHistoricalOrder"],
                new { branchId = "TR", externalId = "order-not-discovered" });
            await MustReject(() => guard.ValidateAsync(unknown));
        }
        foreach (bool foreignHistory in new[] { true, false })
        {
            using var client = WealthsimpleAuthClient.FromReadOnlySession(Session(),
                new FakeHandler { ForeignHistory = foreignHistory, ForeignHistoricalOrder = !foreignHistory });
            Require(await client.VerifyReadOnlyAsync(default));
            var accounts = await client.ReadTfsaAccountsAsync(); client.SelectReadOnlyAccount(accounts.Data![0]);
            var rejected = await client.ReadTfsaOrderHistoryAsync(accounts.Data[0]);
            Require(rejected.Status != WealthsimpleReadStatus.Success && rejected.Data is null);
        }
    }
    private static byte[] Session(DateTimeOffset? expires = null) => JsonSerializer.SerializeToUtf8Bytes(new
    { version = 1, access = Token, refresh = "synthetic-refresh-must-not-be-used", expires = expires ?? DateTimeOffset.UtcNow.AddHours(1) });
    private static ReadOnlyTransport BoundGuard()
    { var guard = new ReadOnlyTransport(Token, new FakeHandler()); guard.BindIdentity(Identity); guard.BindAccount(Account); return guard; }
    private static async Task Denied(Action<HttpRequestMessage> change, string operation = "TraderVITfsaAccounts")
    { using var guard = BoundGuard(); using var request = Request(operation); change(request); await MustReject(() => guard.ValidateAsync(request)); }
    private static async Task MustReject(Func<Task> action)
    {
        bool rejected = false;
        try { await action(); } catch (Exception error) when (error is InvalidOperationException or JsonException) { rejected = true; }
        Require(rejected);
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Offline broker check failed."); checks++; }
    private static HttpRequestMessage Info()
    { var request = new HttpRequestMessage(HttpMethod.Get, ReadOnlyTransport.InfoUri); request.Headers.Authorization = new("Bearer", Token); return request; }
    private static HttpRequestMessage Request(string operation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ReadOnlyTransport.QueryUri) { Content = Body(operation, ReadOnlyTransport.Queries[operation], Variables(operation)) };
        request.Headers.Authorization = new("Bearer", Token);
        request.Headers.Add("x-ws-identity-id", Identity); request.Headers.Add("x-ws-operation-name", operation);
        return request;
    }
    private static HttpContent Body(string operation, string query, object variables) => new StringContent(
        JsonSerializer.Serialize(new { operationName = operation, query, variables }), Encoding.UTF8, "application/json");
    private static object Variables(string operation) => operation switch
    {
        "TraderVITfsaAccounts" => new { identityId = Identity, cursor = (string?)null },
        "TraderVITfsaFinancials" => new { ids = new[] { Account }, currency = "CAD" },
        "TraderVITfsaBalances" => new { accountCanonicalId = Account, currency = "CAD" },
        "TraderVITfsaPositions" => new { identityId = Identity, accountIds = new[] { Account }, currency = "CAD", cursor = (string?)null },
        "TraderVITfsaPendingOrders" => new { identityId = Identity, statuses = WealthsimpleAuthClient.ReadOnlyPendingStatuses, cursor = (string?)null },
        "TraderVITfsaDailyPerformance" => new { id = Account, currency = "CAD", timeRange = "ONE_DAY", marketSession = "REGULAR", includeSimpleReturns = true },
        "TraderVITfsaActivityHistory" => new { first = 50, cursor = (string?)null, condition = new { accountIds = new[] { Account } },
            orderBy = new[] { "OCCURRED_AT_DESC" }, accountScope = "OWN" },
        "TraderVITfsaHistoricalOrder" => new { branchId = "TR", externalId = "order-history-fixture" },
        _ => throw new InvalidOperationException()
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        internal int Forwarded;
        internal bool ForeignPosition, GraphQlError, RejectAuth, ForeignHistory, ForeignHistoricalOrder;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Forwarded++;
            if (request.RequestUri == ReadOnlyTransport.InfoUri)
                return new(RejectAuth ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = new StringContent("{\"profiles\":{\"trade\":{}},\"identity_canonical_id\":\"synthetic-identity\"}") };
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            string? operation = body.RootElement.GetProperty("operationName").GetString();
            string result = operation switch
            {
                "TraderVITfsaAccounts" => """
                  {"data":{"identity":{"id":"synthetic-identity","accounts":{"edges":[{"node":{"id":"synthetic-tfsa","type":"tfsa","branch":"TR","unifiedAccountType":"SELF_DIRECTED_TFSA","isOpen":true,"closedAt":null,"archivedAt":null,"nickname":"Synthetic","currency":"CAD"}}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}
                  """,
                "TraderVITfsaFinancials" => GraphQlError ? "{\"errors\":[{\"extensions\":{\"code\":\"FORBIDDEN\"}}],\"data\":{}}" : """
                  {"data":{"accounts":[{"id":"synthetic-tfsa","financials":{"currentCombined":{"netLiquidationValueV2":{"amount":"100","currency":"CAD"},"netDepositsV2":{"amount":"90","currency":"CAD"},"simpleReturns":{"amount":{"amount":"10","currency":"CAD"},"rate":"0.1","referenceDate":"2026-09-15","asOf":"2026-09-16T20:00:00Z"}}}}]}}
                  """,
                "TraderVITfsaBalances" => """
                  {"data":{"account":{"id":"synthetic-tfsa","financials":{"current":{"tradingBalanceViewV2":{"cash":{"quantity":"80","currency":"CAD"},"buyingPower":{"quantity":"80","currency":"CAD"}}}}}}}
                  """,
                "TraderVITfsaPositions" => """
                  {"data":{"identity":{"id":"synthetic-identity","financials":{"current":{"positions":{"edges":[{"node":{"id":"p1","quantity":"2","accounts":[{"id":"POSITION_OWNER"}],"security":{"id":"s1","securityType":"EQUITY","stock":{"symbol":"TEST","name":"Synthetic"}},"totalValue":{"amount":"20","currency":"CAD"},"bookValue":{"amount":"18","currency":"CAD"},"unrealizedReturns":{"amount":"2","currency":"CAD"}}}],"status":["OK"],"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}}}
                  """.Replace("POSITION_OWNER", ForeignPosition ? "other-account" : Account),
                "TraderVITfsaPendingOrders" => """
                  {"data":{"identity":{"id":"synthetic-identity","orderServiceExtendedOrderFeed":{"edges":[{"node":{"id":"feed1","orderId":"order1","canonicalAccountId":"synthetic-tfsa","createdAtUtc":"2026-09-16T19:00:00Z","status":"PLACED","side":"SELL","executionType":"MARKET","submittedQuantity":"1","limitPrice":null,"securityCurrency":"CAD","securityId":"s1","symbol":"TEST"}}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}
                  """,
                "TraderVITfsaDailyPerformance" => """
                  {"data":{"account":{"id":"synthetic-tfsa","financials":{"currentCombined":{"graphData":{"previousClose":{"dateTime":"2026-09-15T20:00:00Z"},"data":[{"dateTime":"2026-09-16T20:00:00Z","simpleReturns":{"amount":{"amount":"1","currency":"CAD"},"rate":"0.01","referenceDateTime":"2026-09-15T20:00:00Z"}}]}}}}}}
                  """,
                "TraderVITfsaActivityHistory" => """
                  {"data":{"activityFeedItems":{"edges":[{"node":{"accountId":"HISTORY_OWNER","canonicalId":"activity-fixture","externalCanonicalId":"order-history-fixture","type":"DIY_BUY","subType":null,"status":"FILLED","securityId":"s1","assetSymbol":"TEST","assetQuantity":"2"}}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}
                  """.Replace("HISTORY_OWNER", ForeignHistory ? "foreign-account" : Account),
                "TraderVITfsaHistoricalOrder" => """
                  {"data":{"orderServiceExtendedOrderByExternalId":{"id":"order-history-fixture","canonicalAccountId":"HISTORY_OWNER","identityId":"synthetic-identity","securityId":"s1","side":"BUY","status":"FILLED","securityCurrency":"CAD","submittedQuantity":"2","fillQuantity":"2","averageFillPrice":"9","filledValue":"18","filledNetValue":"18","filledTotalFee":"0","filledTaxWithholding":"0","createdAtUtc":"2026-09-16T13:59:00Z","lastFilledAtUtc":"2026-09-16T14:00:00Z","security":{"id":"s1","securityType":"EQUITY","stock":{"symbol":"TEST","primaryMic":"XTSE"}}}}}
                  """.Replace("HISTORY_OWNER", ForeignHistoricalOrder ? "foreign-account" : Account),
                _ => throw new InvalidOperationException("Unrecognized synthetic operation.")
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(result, Encoding.UTF8, "application/json") };
        }
    }
}
