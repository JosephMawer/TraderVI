#if GROUPED_HISTORY_CHECK
using Core.WSTrade.Authentication;
using System.Net;
using System.Text.Json;

namespace BrokerReadOnlyChecks;

internal static partial class SelfTests
{
    private const string Group = "TraderVIRegularOrderGroup";
    private static async Task GroupChecksAsync()
    {
        var ids = new { branchId = "TR", id0 = "order-0", id1 = "order-1" };
        using var guard = new ReadOnlyTransport(Token, new GroupFixture(false));
        guard.BindIdentity(Identity); guard.BindAccount(Account);
        async Task Verify(string query, object variables)
        {
            using var request = Request("TraderVITfsaAccounts");
            request.Headers.Remove("x-ws-operation-name"); request.Headers.Add("x-ws-operation-name", Group);
            request.Content = Body(Group, query, variables);
            await guard.ValidateAsync(request);
        }
        string two = ReadOnlyTransport.GroupQuery(2);
        await MustReject(() => Verify(two, ids));
        using var http = new HttpClient(guard);
        using (var feed = Request("TraderVITfsaActivityHistory")) using (await http.SendAsync(feed)) { }
        await Verify(two, ids); checks++;
        await MustReject(() => Verify(two, new { branchId = "TR", id0 = "order-0", id1 = "order-unknown" }));
        await MustReject(() => Verify(two, new { branchId = "TR", id0 = "order-0", id1 = "order-0" }));
        await MustReject(() => Verify(two, new { branchId = "OTHER", id0 = "order-0", id1 = "order-1" }));
        await MustReject(() => Verify(two + " mutation { cancelOrder }", ids));
        await MustReject(() => Verify(two, new { branchId = "TR", id0 = "order-0" }));
        await MustReject(() => Verify(ReadOnlyTransport.GroupQuery(9), Enumerable.Range(0,9)
            .ToDictionary(i => "id" + i, i => "order-" + i).Append(new("branchId","TR")).ToDictionary()));
        foreach (bool different in new[] { false, true })
        {
            using var client = WealthsimpleAuthClient.FromReadOnlySession(Session(), new GroupFixture(different));
            Require(await client.VerifyReadOnlyAsync(default));
            var account = (await client.ReadTfsaAccountsAsync()).Data!.Single();
            client.SelectReadOnlyAccount(account);
            var report = await GroupedHistoryCheck.RunAsync(client, account, default);
            Require(report.Success == !different && report.GroupSizes.SequenceEqual([8]));
            Require(report.DifferingOrderIds == (different ? 1 : 0) && report.SameActivityCheckpoint == true);
            Require(report.IndividualOrders == 9 && report.GroupedOrders == 9 &&
                report.IndividualReadRequests == 10 && report.GroupedReadRequests == 3);
        }
    }
    private sealed class GroupFixture(bool different) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker fallback = new(new FakeHandler());
        protected override void Dispose(bool disposing) { if (disposing) fallback.Dispose(); base.Dispose(disposing); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.Method == HttpMethod.Get) return await fallback.SendAsync(req, ct);
            using var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            var op = body.RootElement.GetProperty("operationName").GetString();
            var vars = body.RootElement.GetProperty("variables");
            object Detail(string id) => new { id, canonicalAccountId = Account, identityId = Identity, securityId = "s1", side = "BUY",
                status = "POSTED", securityCurrency = "CAD", submittedQuantity = 2, fillQuantity = 2, averageFillPrice = 9, filledValue = 18,
                filledNetValue = different && op == Group && id == "order-0" ? 19 : 18, filledTotalFee = different && op == Group && id == "order-0" ? 1 : 0,
                filledTaxWithholding = 0, createdAtUtc = "2026-01-02T14:00:00Z", lastFilledAtUtc = "2026-01-02T14:01:00Z",
                security = new { id = "s1", securityType = "EQUITY", stock = new { symbol = "TEST", primaryMic = "XTSE" } } };
            object data;
            if (op == "TraderVITfsaActivityHistory") data = new { activityFeedItems = new { edges = Enumerable.Range(0, 9).Select(i => new {
                node = new { accountId = Account, canonicalId = "activity-" + i, externalCanonicalId = "order-" + i, type = "DIY_BUY", status = "POSTED",
                    securityId = "s1", assetSymbol = "TEST", assetQuantity = 2 } }), pageInfo = new { hasNextPage = false, endCursor = (string?)null } } };
            else if (op == "TraderVITfsaHistoricalOrder") data = new { orderServiceExtendedOrderByExternalId = Detail(vars.GetProperty("externalId").GetString()!) };
            else if (op == Group) data = vars.EnumerateObject().Where(p => p.Name != "branchId").ToDictionary(p => "order" + p.Name[2..], p => Detail(p.Value.GetString()!));
            else return await fallback.SendAsync(req, ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data })) };
        }
    }
}
#endif
