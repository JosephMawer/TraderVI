#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class WealthsimpleAccountTests
{
    // 22 completed round trips, a cancellation and either a holding or an expiry.
    // Six cash activities force a second feed page. Cash and holdings are independent wire fixtures.
    private sealed class TradingHistoryWire(bool holding, DateTimeOffset now)
    {
        private const int Count = 46;
        internal int DetailRows;
        internal string? Fault;
        internal bool CorrectOldFee;
        private static string Id(int i) => $"order-fixture-{i}";
        private static string Side(int i) => i < 44 && i % 2 == 1 ? "SELL" : "BUY";
        private string Status(int i) => i == 44 ? "CANCELLED" : i == 45 && !holding ? "EXPIRED" : "POSTED";

        private object Detail(string id)
        {
            Assert.StartsWith("order-fixture-", id);
            int i = int.Parse(id["order-fixture-".Length..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(i, 0, Count - 1);
            DetailRows++;
            bool filled = Status(i) == "POSTED";
            decimal? fee = filled ? (i == 0 && CorrectOldFee ? 1m : 0m) : null;
            DateTimeOffset created = now.AddDays(-2).AddMinutes(i * 2);
            return new { id, canonicalAccountId = Fault == "foreign" && i == 0 ? "foreign" : "tfsa-fixture-id",
                identityId = "identity-fixture", securityId = "security-fixture", side = Side(i), status = Status(i),
                securityCurrency = "CAD", submittedQuantity = 10, fillQuantity = filled ? 10 : 0, averageFillPrice = filled ? 11m : (decimal?)null,
                filledValue = filled ? 110m : (decimal?)null, filledNetValue = 110m + fee, filledTotalFee = fee,
                filledTaxWithholding = filled ? 0m : (decimal?)null, createdAtUtc = created,
                lastFilledAtUtc = filled ? created.AddMinutes(1) : (DateTimeOffset?)null,
                security = new { id = "security-fixture", securityType = "EQUITY", stock = new { symbol = "FIX", primaryMic = "XTSE" } } };
        }
        internal HttpResponseMessage Reply(string operation, JsonElement variables)
        {
            switch (operation)
            {
                case "TraderVITfsaActivityHistory":
                    bool first = variables.GetProperty("cursor").ValueKind == JsonValueKind.Null;
                    var edges = Enumerable.Range(first ? 0 : 50, first ? 50 : 2).Select(n =>
                    {
                        int i = 51 - n;
                        return new { node = new { accountId = "tfsa-fixture-id", canonicalId = "activity-" + i,
                            externalCanonicalId = i < Count ? Id(i) : null, type = i < Count ? "DIY_" + Side(i) : "INTEREST",
                            status = i < Count ? Status(i) : null, securityId = i < Count ? "security-fixture" : null,
                            assetSymbol = i < Count ? "FIX" : null, assetQuantity = i < Count && Status(i) == "POSTED" ? 10 : (int?)null } };
                    });
                    return Json(new { data = new { activityFeedItems = new { edges, pageInfo = Page(first, first ? "next" : null) } } });
                case "TraderVITfsaHistoricalOrder":
                    return Json(new { data = new { orderServiceExtendedOrderByExternalId = Detail(variables.GetProperty("externalId").GetString()!) } });
                case "TraderVIRegularOrderGroup":
                    var rows = new Dictionary<string, object>();
                    Assert.Equal("TR", variables.GetProperty("branchId").GetString());
                    int length = variables.EnumerateObject().Count() - 1;
                    Assert.InRange(length, 2, 8);
                    for (int i = 0; i < length; i++) rows.Add("order" + i, Detail(variables.GetProperty("id" + i).GetString()!));
                    if (Fault == "swapped") (rows["order0"], rows["order1"]) = (rows["order1"], rows["order0"]);
                    if (Fault == "missing") rows.Remove("order0");
                    if (Fault == "partial") return Json(new { data = rows, errors = new[] { new { message = "fixture" } } });
                    return Json(new { data = rows });
                case "TraderVITfsaFinancials":
                    return Json(new { data = new { accounts = new[] { new { id = "tfsa-fixture-id", financials = new { currentCombined =
                        new { netLiquidationValueV2 = Money("3000"), netDepositsV2 = Money("3000") } } } } } });
                case "TraderVITfsaBalances":
                    var cash = new { quantity = holding ? "2890" : "3000", currency = "CAD" };
                    return Json(new { data = new { account = new { id = "tfsa-fixture-id", financials = new { current = new {
                        tradingBalanceViewV2 = new { cash, buyingPower = cash } } } } } });
                case "TraderVITfsaPositions":
                    if (holding) return Json(Positions(quantity: "10"));
                    return Json(new { data = new { identity = new { id = "identity-fixture", financials = new { current = new {
                        positions = new { edges = Array.Empty<object>(), pageInfo = Page(), totalCount = 0, status = new[] { "OK" } } } } } } });
                case "TraderVITfsaPendingOrders":
                    return Json(new { data = new { identity = new { id = "identity-fixture",
                        orderServiceExtendedOrderFeed = new { edges = Array.Empty<object>(), pageInfo = Page() } } } });
                default: throw new InvalidOperationException("Unexpected broker operation: " + operation);
            }
        }
    }

    [Theory]
    [InlineData("Buy")]
    [InlineData("Sell")]
    [InlineData("Hold")]
    public async Task RegularTradingGroupsAgreeOnBrokerBookAndDelphiLiveDecision(string action)
    {
        var market = TradingRecommendationTests.LiveInput();
        var now = new DateTimeOffset(market.EvaluatedUtc);
        using var http = new Fixture();
        http.Clock.Now = now;
        var broker = new WealthsimpleConnection(new Store(), http.Clock, () => http.Client);
        await broker.SignInAsync("fixture@example.invalid", "fixture-password", false);
        var account = Assert.Single((await broker.ReadTfsaAccountsAsync()).Data!);
        var wire = new TradingHistoryWire(action != "Buy", now);
        http.Override = wire.Reply;
        string? selection = null;
        http.InspectQuery = (operation, query, variables) =>
        {
            string normalized = Regex.Replace(query, @"\s+", " ");
            if (operation == "TraderVITfsaHistoricalOrder")
            {
                const string marker = "externalId: $externalId) { ";
                int start = normalized.IndexOf(marker, StringComparison.Ordinal);
                Assert.True(start >= 0 && normalized.EndsWith(" } }", StringComparison.Ordinal));
                selection = normalized[(start + marker.Length)..^4];
            }
            if (operation != "TraderVIRegularOrderGroup") return;
            Assert.NotNull(selection);
            foreach (var variable in variables.EnumerateObject().Where(p => p.Name != "branchId"))
            {
                string index = variable.Name[2..];
                Assert.Contains($"$id{index}: String!", normalized);
                Assert.Contains($"order{index}: orderServiceExtendedOrderByExternalId(branchId: $branchId, externalId: $id{index}) {{ {selection} }}", normalized);
                Assert.DoesNotContain(variable.Value.GetString()!, query);
            }
        };
        using var gate = new SemaphoreSlim(1, 1);
        var bindingId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var riskReference = new BrokerRiskReference(3000, BrokerRiskReferenceSource.SetupObservation, Guid.NewGuid(), now.AddHours(-1));
        var risk = new BrokerAccountRiskState(Guid.NewGuid(), bindingId, "TFSA", null, market.TradingDate, riskReference,
            riskReference, false, false, null, now.AddHours(-1));
        var hash = new string('A', 64);
        async Task<(string Book, TradingRecommendation Decision)> Replay(bool grouped)
        {
            using var journal = new WealthsimpleReconciliationTests.Store();
            var reader = new BrokerReconciliationWorkflow(grouped ? broker.RegularTradingReads : broker, journal, journal, gate, http.Clock);
            TradingRecommendation? decision = null;
            string book = "";
            for (int poll = 0; poll < 2; poll++)
            {
                http.Operations.Clear(); wire.DetailRows = 0;
                var observed = (await reader.ReadAsync(account, broker.SessionRevision)).Reconciliation;
                Assert.Empty(observed.Blocks); Assert.Empty(observed.HistoryConflicts);
                Assert.Equal(46, wire.DetailRows);
                Assert.Equal(grouped ? 13 : 53, http.Operations.Count);
                Assert.Equal(3, http.Operations.Count(o => o == "TraderVITfsaActivityHistory"));
                Assert.Equal(grouped ? 6 : 46, http.Operations.Count(o => o is "TraderVIRegularOrderGroup" or "TraderVITfsaHistoricalOrder"));
                var binding = new BrokerImportBinding(bindingId, "TFSA", BrokerAccountImport.Key(observed), hash, now.AddDays(-1), "fixture");
                var plan = new BrokerImportPlan("TFSA", binding.BrokerKey, hash, null, observed, [], []);
                var projected = BrokerAccountProjector.Project(binding, new(receiptId, bindingId, hash, now, plan), risk,
                    new(bindingId, risk.Through, now, now, "fixture", hash), now);
                Assert.Empty(projected.NewRiskBlocks);
                var input = market with { Account = projected.Account, CurrentLedgerFingerprint = projected.Account.LedgerFingerprint,
                    ActualFills = projected.Fills, CurrentHoldings = projected.Account.Holdings };
                if (action != "Buy")
                    input = input with { Candidates = [], LiveInputs = [], Quotes =
                        [new("FIX", market.EvaluatedUtc, market.EvaluatedUtc.AddSeconds(1), 11m, action == "Sell" ? 9m : 11m, 11.1m, "TmxQuoteFieldsV1")] };
                decision = Assert.Single(TradingRecommendationEngine.Evaluate(input).Recommendations);
                Assert.Equal(action, decision.Action);
                book = JsonSerializer.Serialize(observed);
            }
            return (book, decision!);
        }
        var baseline = await Replay(false);
        var candidate = await Replay(true);
        Assert.Equal(baseline, candidate);
        Assert.True(candidate.Decision.Quantity > 0 || action == "Hold");
        output.WriteLine($"Two polls each. {action} {candidate.Decision.Symbol}, quantity {candidate.Decision.Quantity}, reason {candidate.Decision.Reason}. " +
            "53 -> 13 requests per poll, 46 order records checked by both readers.");
    }

    [Fact]
    public async Task AnOldFeeCorrectionWithUnchangedActivityStillRequiresReview()
    {
        using var http = new Fixture();
        var broker = new WealthsimpleConnection(new Store(), http.Clock, () => http.Client);
        await broker.SignInAsync("fixture@example.invalid", "fixture-password", false);
        var account = Assert.Single((await broker.ReadTfsaAccountsAsync()).Data!);
        var wire = new TradingHistoryWire(false, http.Clock.Now);
        http.Override = wire.Reply;
        using var gate = new SemaphoreSlim(1, 1);
        async Task<string> Compare(IBrokerReconciliationSource source)
        {
            using var journal = new WealthsimpleReconciliationTests.Store();
            var reader = new BrokerReconciliationWorkflow(source, journal, journal, gate, http.Clock);
            wire.CorrectOldFee = false;
            var original = await reader.RefreshAsync(account, broker.SessionRevision);
            Assert.Empty(original.HistoryConflicts);
            wire.CorrectOldFee = true;
            var corrected = await reader.RefreshAsync(account, broker.SessionRevision);
            Assert.Equal(original.ActivityFingerprint, corrected.ActivityFingerprint);
            Assert.NotEmpty(corrected.HistoryConflicts);
            return JsonSerializer.Serialize(corrected);
        }
        Assert.Equal(await Compare(broker), await Compare(broker.RegularTradingReads));
    }

    [Theory]
    [InlineData(0, 0, 0)][InlineData(1, 1, 0)][InlineData(8, 0, 1)][InlineData(9, 1, 1)]
    public async Task RegularHistoryReadsOnlyDiscoveredOrdersAcrossGroupSizes(int count, int singles, int groups)
    {
        using var http = new Fixture(); await http.Login();
        var account = Assert.Single((await http.Client.ReadTfsaAccountsAsync()).Data!);
        http.Operations.Clear();
        http.Override = (op, vars) =>
        {
            if (op == "TraderVITfsaActivityHistory") return Json(new { data = new { activityFeedItems = new {
                edges = Enumerable.Range(0,count).Select(i => new { node = ActivityNode("activity-" + i, "order-" + i) }), pageInfo = Page() } } });
            if (op == "TraderVITfsaHistoricalOrder") return Json(new { data = new { orderServiceExtendedOrderByExternalId = HistoricalOrder(vars.GetProperty("externalId").GetString()) } });
            return Json(new { data = vars.EnumerateObject().Where(p => p.Name.StartsWith("id", StringComparison.Ordinal))
                .ToDictionary(p => "order" + p.Name[2..], p => HistoricalOrder(p.Value.GetString())) });
        };
        var result = await http.Client.ReadRegularTradingHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Equal(count, result.Data!.Orders.Count);
        Assert.Equal(singles, http.Operations.Count(o => o == "TraderVITfsaHistoricalOrder"));
        Assert.Equal(groups, http.Operations.Count(o => o == "TraderVIRegularOrderGroup"));
    }

    [Theory]
    [InlineData("foreign")][InlineData("swapped")][InlineData("partial")][InlineData("missing")]
    public async Task GroupedHistoryNeverPublishesAnIncompleteOrMisattributedBook(string fault)
    {
        using var http = new Fixture(); await http.Login();
        var account = Assert.Single((await http.Client.ReadTfsaAccountsAsync()).Data!);
        http.Override = new TradingHistoryWire(false, http.Clock.Now) { Fault = fault }.Reply;
        var result = await http.Client.ReadRegularTradingHistoryAsync(account);
        Assert.NotEqual(WealthsimpleReadStatus.Success, result.Status);
        Assert.Null(result.Data);
    }
}
