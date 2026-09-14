using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
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
using Xunit;

namespace TraderVI.Core.Tests;

public class WealthsimpleMarketBuyTests
{
    [Fact]
    public async Task SendsCapturedMarketQuantityContractAndAcknowledgementIsNotAFill()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        var result = await f.Client.SubmitMarketBuyAsync(order, f.Journal);
        Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, result.Status);
        Assert.Equal("broker-ack-fixture", result.BrokerOrderId);
        Assert.Equal(1, f.Mutations); Assert.Equal(0, f.DetailReads);
        var body = f.LastMutation.RootElement;
        Assert.Equal("SoOrdersOrderCreate", body.GetProperty("operationName").GetString());
        Assert.Contains("$input: SoOrders_CreateOrderInput!", body.GetProperty("query").GetString());
        var input = body.GetProperty("variables").GetProperty("input");
        Assert.Equal(7, input.EnumerateObject().Count());
        Assert.Equal("MARKET", input.GetProperty("executionType").GetString());
        Assert.Equal("BUY_QUANTITY", input.GetProperty("orderType").GetString());
        Assert.Equal("DAY", input.GetProperty("timeInForce").GetString());
        Assert.Equal(20, input.GetProperty("quantity").GetInt32());
        Assert.Equal("order-" + order.RequestId.ToString("D"), input.GetProperty("externalId").GetString());
        Assert.Equal("owned-tfsa", input.GetProperty("canonicalAccountId").GetString());
        Assert.Equal("cad-security", input.GetProperty("securityId").GetString());
        Assert.DoesNotContain("owned-tfsa", order.ToString());
        Assert.DoesNotContain("broker-ack-fixture", result.ToString());
    }

    [Fact]
    public async Task LaterDetailResponseVerifiesOwnerAndActualCumulativeFill()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        var result = await f.Client.ReadMarketBuyOrderAsync(order);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.True(result.Data.IsFilled); Assert.Equal(20m, result.Data.FilledQuantity);
        Assert.Equal(10.25m, result.Data.AverageFillPrice.Amount);
        Assert.Equal(205m, result.Data.FilledNetValue.Amount); Assert.Equal(0m, result.Data.Fees.Amount);
        var again = await f.Client.ReadMarketBuyOrderAsync(order);
        Assert.Equal(result.Data.FilledQuantity, again.Data.FilledQuantity); Assert.Equal(0, f.Mutations);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("identity")]
    [InlineData("security")]
    [InlineData("external")]
    [InlineData("quantity")]
    [InlineData("incomplete-fill")]
    [InlineData("missing-price")]
    public async Task MisScopedOrIncompleteFillNeverBecomesConfirmedHoldings(string failure)
    {
        using var f = new Fixture(); var order = await f.Prepare(); f.DetailFailure = failure;
        var result = await f.Client.ReadMarketBuyOrderAsync(order);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
    }

    [Fact]
    public async Task UnknownFillQuantityRemainsUnknown()
    {
        using var f = new Fixture(); var order = await f.Prepare(); f.DetailFailure = "pending-unknown";
        var result = await f.Client.ReadMarketBuyOrderAsync(order);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Null(result.Data.FilledQuantity); Assert.False(result.Data.IsFilled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.125)]
    [InlineData(20.5)]
    [InlineData(2147483648)]
    public void WholeShareInvariantIsEnforcedBeforeAnyNetworkCall(decimal quantity) =>
        Assert.Throws<ArgumentException>(() => new WealthsimpleMarketBuy(new("owned", "TFSA"), new("security", "FIX.TO", "XTSE"), quantity, Guid.NewGuid()));

    [Fact]
    public async Task ForeignAccountAndFabricatedInstrumentCannotBeSubmitted()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        Assert.Equal(WealthsimpleSubmissionStatus.AccountUnavailable, (await f.Client.SubmitMarketBuyAsync(
            new(new("foreign", "TFSA"), order.Security, 20, Guid.NewGuid()), f.Journal)).Status);
        Assert.Equal(WealthsimpleSubmissionStatus.SecurityUnavailable, (await f.Client.SubmitMarketBuyAsync(
            new(order.Account, new("cad-security", "FIX.TO", "XTSE"), 20, Guid.NewGuid()), f.Journal)).Status);
        Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task BuyabilityIsRecheckedAndFailureDoesNotConsumeAttempt()
    {
        using var f = new Fixture(); var order = await f.Prepare(); f.Buyable = false;
        Assert.Equal(WealthsimpleSubmissionStatus.SecurityUnavailable, (await f.Client.SubmitMarketBuyAsync(order, f.Journal)).Status);
        Assert.Equal(0, f.Mutations); Assert.False(Directory.Exists(f.JournalPath));
        f.Buyable = true;
        Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, (await f.Client.SubmitMarketBuyAsync(order, f.Journal)).Status);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("http500")]
    [InlineData("http429")]
    [InlineData("malformed")]
    [InlineData("graphql-partial")]
    public async Task AmbiguousSubmissionIsNeverRetriedAndMarkerContainsOnlyHashes(string failure)
    {
        using var f = new Fixture(); var order = await f.Prepare(); f.MutationFailure = failure;
        var first = await f.Client.SubmitMarketBuyAsync(order, f.Journal);
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, first.Status);
        f.MutationFailure = null;
        Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketBuyAsync(order, new WealthsimpleOrderAttemptJournal(f.JournalPath))).Status);
        Assert.Equal(1, f.Mutations);
        string marker = File.ReadAllText(Assert.Single(Directory.GetFiles(f.JournalPath)));
        Assert.DoesNotContain("fixture", marker); Assert.DoesNotContain("owned", marker); Assert.DoesNotContain(order.RequestId.ToString(), marker);
    }

    [Fact]
    public async Task SameRequestIdWithDifferentQuantityStillCannotTransmitTwice()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        await f.Client.SubmitMarketBuyAsync(order, f.Journal);
        var changed = new WealthsimpleMarketBuy(order.Account, order.Security, 30, order.RequestId);
        Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketBuyAsync(changed, f.Journal)).Status);
        Assert.Equal(1, f.Mutations);
    }

    [Fact]
    public async Task DpopNonceDoesNotAutomaticallyReplayAMutation()
    {
        using var f = new Fixture { Dpop = true }; var order = await f.Prepare(); f.MutationFailure = "nonce";
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, (await f.Client.SubmitMarketBuyAsync(order, f.Journal)).Status);
        Assert.Equal(1, f.Mutations);
    }

    [Fact]
    public async Task ExplicitRejectionAndMixedAcknowledgementAreDifferentOutcomes()
    {
        using var f = new Fixture(); var order = await f.Prepare(); f.MutationFailure = "reject";
        Assert.Equal(WealthsimpleSubmissionStatus.Rejected, (await f.Client.SubmitMarketBuyAsync(order, f.Journal)).Status);
        f.MutationFailure = "mixed";
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, (await f.Client.SubmitMarketBuyAsync(
            new(order.Account, order.Security, 20, Guid.NewGuid()), f.Journal)).Status);
    }

    [Fact]
    public async Task JournalStorageFailurePreventsTransmission()
    {
        using var f = new Fixture(); var order = await f.Prepare(); Directory.CreateDirectory(f.RootPath);
        File.WriteAllText(f.JournalPath, "not a directory");
        Assert.Equal(WealthsimpleSubmissionStatus.JournalUnavailable, (await f.Client.SubmitMarketBuyAsync(order, f.Journal)).Status);
        Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task TwoJournalInstancesCanReserveTheSameIntentOnlyOnce()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        var results = await Task.WhenAll(Task.Run(() => f.Journal.TryBegin("identity-fixture", order)),
            Task.Run(() => new WealthsimpleOrderAttemptJournal(f.JournalPath).TryBegin("identity-fixture", order)));
        Assert.Single(results.Where(r => r)); Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task EmptyCrashMarkerStillPreventsResubmission()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        Assert.True(f.Journal.TryBegin("identity-fixture", order));
        File.WriteAllText(Assert.Single(Directory.GetFiles(f.JournalPath)), "");
        Assert.Equal(WealthsimpleSubmissionStatus.AlreadyAttempted, (await f.Client.SubmitMarketBuyAsync(order, f.Journal)).Status);
        Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task ManualQuoteUsesScopedCadAskAndCapturedRegularSession()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        var quote = await f.Client.ReadManualBuyQuoteAsync(order.Security);
        Assert.Equal(WealthsimpleReadStatus.Success, quote.Status);
        Assert.Equal(10.25m, quote.Data.Ask); Assert.Equal("OPEN", quote.Data.MarketStatus);
        Assert.Equal(Clock.Now, quote.Data.QuotedAt); Assert.True(quote.Data.SessionOpen < Clock.Now);
        Assert.True(quote.Data.SessionClose > Clock.Now); Assert.Equal(0, f.Mutations);
    }

    [Theory]
    [InlineData("identity")] [InlineData("currency")] [InlineData("type")] [InlineData("no-ask")] [InlineData("no-session")]
    public async Task UnsupportedQuoteCannotBecomeABuyEstimate(string failure)
    {
        using var f = new Fixture(); var order = await f.Prepare(); f.QuoteFailure = failure;
        var quote = await f.Client.ReadManualBuyQuoteAsync(order.Security);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, quote.Status); Assert.Null(quote.Data); Assert.Equal(0, f.Mutations);
    }

    [Fact]
    public async Task RecoveryUsesPersistedScopeWithoutSearchingOrResubmitting()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        var quote = new ManualBuyQuote(10.25m,10.24m,Clock.Now,"OPEN",Clock.Now.AddHours(-1),Clock.Now.AddHours(1),Clock.Now);
        var intent = new ManualBuyIntent(order.RequestId,"identity-fixture","owned-tfsa","cad-security","TFSA","FIX.TO","XTSE",20,
            new("FIX","Fixture source","Hold"),10.25m,quote,1000,Clock.Now);
        var recovered = await f.Client.ReadManualBuyOrderAsync(intent);
        Assert.Equal(WealthsimpleReadStatus.Success,recovered.Status); Assert.True(recovered.Data.IsFilled); Assert.Equal(0,f.Mutations);
        int reads = f.DetailReads;
        Assert.Equal(WealthsimpleReadStatus.AccountUnavailable,(await f.Client.ReadManualBuyOrderAsync(intent with { IdentityId = "another-identity" })).Status);
        Assert.Equal(reads,f.DetailReads);
    }

    [Fact]
    public async Task RepeatedAccountDiscoveryPreservesStillVerifiedSelections()
    {
        using var f = new Fixture(); var order = await f.Prepare();
        Assert.Same(order.Account, Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data));
        Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged,(await f.Client.SubmitMarketBuyAsync(order,f.Journal)).Status);
    }

    internal sealed class Fixture : HttpMessageHandler
    {
        internal readonly string RootPath = Path.Combine(Path.GetTempPath(), "TraderVI-market-buy-fixture-" + Guid.NewGuid().ToString("N"));
        internal string JournalPath => Path.Combine(RootPath, "attempts");
        internal WealthsimpleOrderAttemptJournal Journal => new(JournalPath);
        internal WealthsimpleAuthClient Client;
        internal int Mutations, DetailReads;
        internal bool Dpop, Buyable = true, Sellable = true;
        internal string MutationFailure, DetailFailure, QuoteFailure;
        internal string EligibilityFailure;
        internal readonly Dictionary<string, object> DetailOverrides = new();
        internal JsonElement? CapturedDetail, CapturedAcknowledgement;
        internal JsonDocument LastMutation;
        internal async Task<WealthsimpleMarketSell> PrepareSell(decimal quantity = 20)
        {
            var buy = await Prepare();
            return new(buy.Account, buy.Security, quantity, buy.RequestId);
        }
        internal async Task<WealthsimpleMarketBuy> Prepare()
        {
            Client = new("fixture-client", this, new Clock(), requestDpop: Dpop);
            Assert.Equal(WealthsimpleAuthStatus.Authenticated, (await Client.BeginLoginAsync("fixture@example.invalid", "fixture-password")).Status);
            var account = Assert.Single((await Client.ReadTfsaAccountsAsync()).Data);
            var security = Assert.Single((await Client.FindCanadianSecuritiesAsync("FIX")).Data);
            Assert.Equal("FIX.TO", security.Symbol);
            return new(account, security, 20, Guid.NewGuid());
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.RequestUri.AbsolutePath.EndsWith("/token")) return Json(new { access_token = "fixture-access", refresh_token = "fixture-refresh", token_type = Dpop ? "DPoP" : "Bearer", created_at = Clock.Now.ToUnixTimeSeconds(), expires_in = 1800 });
            Assert.Equal("fixture-access", req.Headers.Authorization.Parameter);
            if (req.RequestUri.AbsolutePath.EndsWith("/info")) return Json(new { identity_canonical_id = "identity-fixture", profiles = new { trade = new { @default = "fixture" } } });
            Assert.Equal("https://my.wealthsimple.com/graphql", req.RequestUri.AbsoluteUri);
            using var doc = JsonDocument.Parse(await req.Content.ReadAsStringAsync(ct));
            var op = doc.RootElement.GetProperty("operationName").GetString();
            var vars = doc.RootElement.GetProperty("variables");
            if (op == "SoOrdersOrderCreate")
            {
                Mutations++; LastMutation?.Dispose(); LastMutation = JsonDocument.Parse(doc.RootElement.GetRawText());
                Assert.True(Directory.GetFiles(JournalPath).Length >= Mutations); // The durable fence predates transport.
                Assert.Equal("identity-fixture", Assert.Single(req.Headers.GetValues("x-ws-identity-id")));
                Assert.Equal("12", Assert.Single(req.Headers.GetValues("x-ws-api-version")));
                Assert.Equal("trade", Assert.Single(req.Headers.GetValues("x-ws-profile")));
                Assert.Equal("SoOrdersOrderCreate", Assert.Single(req.Headers.GetValues("x-ws-operation-name")));
                Assert.Equal("3b3bbe3e7c87ef7dbe00f8041fcfa911d84a73ce2f19cdc76ffefc76a79c046c", Assert.Single(req.Headers.GetValues("x-ws-operation-hash")));
                if (CapturedAcknowledgement.HasValue) return Json(CapturedAcknowledgement.Value);
                if (MutationFailure == "timeout") throw new HttpRequestException("fixture-private-token");
                if (MutationFailure == "cancel") throw new OperationCanceledException("fixture-private-token");
                if (MutationFailure == "http500") return new(HttpStatusCode.InternalServerError);
                if (MutationFailure == "http429") return new((HttpStatusCode)429);
                if (MutationFailure == "http401") return new(HttpStatusCode.Unauthorized);
                if (MutationFailure == "malformed") return new(HttpStatusCode.OK) { Content = new StringContent("private invalid json") };
                if (MutationFailure == "nonce") { var nonce = Json(new { error = "use_dpop_nonce" }); nonce.StatusCode = HttpStatusCode.BadRequest; nonce.Headers.Add("DPoP-Nonce", "fixture-nonce"); return nonce; }
                object ack = new { orderId = "broker-ack-fixture", createdAt = Clock.Now.ToString("O") };
                object errors = new[] { new { code = "ORDER.fixture_rejected", message = "private-account-detail" } };
                if (MutationFailure == "graphql-partial") return Json(new { errors, data = new { soOrdersCreateOrder = new { errors = (object)null, order = ack } } });
                return Json(new { data = new { soOrdersCreateOrder = new { errors = MutationFailure is "reject" or "mixed" ? errors : null,
                    order = MutationFailure == "reject" ? null : ack } } });
            }
            if (op is "TraderVIMarketBuyOrder" or "TraderVIMarketSellOrder")
            {
                DetailReads++; Assert.Equal("TR", vars.GetProperty("branchId").GetString());
                Assert.Contains("query " + op + "(", doc.RootElement.GetProperty("query").GetString());
                if (CapturedDetail.HasValue) return Json(CapturedDetail.Value);
                if (DetailFailure == "not-found") return Json(new { data = new { orderServiceExtendedOrderByExternalId = (object)null } });
                bool sell = op == "TraderVIMarketSellOrder";
                var values = new Dictionary<string, object> {
                    ["id"] = vars.GetProperty("externalId").GetString(), ["canonicalAccountId"] = "owned-tfsa", ["identityId"] = "identity-fixture",
                    ["securityId"] = "cad-security", ["side"] = sell ? "SELL" : "BUY", ["orderType"] = sell ? "SELL_QUANTITY" : "BUY_QUANTITY", ["executionType"] = "MARKET", ["timeInForce"] = "DAY",
                    ["securityCurrency"] = "CAD", ["tradingSession"] = "REGULAR", ["status"] = "FILLED", ["submittedQuantity"] = "20", ["fillQuantity"] = "20",
                    ["averageFillPrice"] = "10.25", ["filledValue"] = "205", ["filledNetValue"] = "205", ["filledTotalFee"] = "0", ["filledTaxWithholding"] = "0",
                    ["createdAtUtc"] = Clock.Now.ToString("O"), ["lastFilledAtUtc"] = Clock.Now.ToString("O"), ["expiredAtUtc"] = Clock.Now.AddHours(4).ToString("O") };
                if (DetailFailure == "owner") values["canonicalAccountId"] = "another-account";
                if (DetailFailure == "identity") values["identityId"] = "another-identity";
                if (DetailFailure == "security") values["securityId"] = "another-security";
                if (DetailFailure == "external") values["id"] = "another-external-id";
                if (DetailFailure == "quantity") values["submittedQuantity"] = "21";
                if (DetailFailure == "incomplete-fill") values["fillQuantity"] = "19";
                if (DetailFailure == "missing-price") values["averageFillPrice"] = null;
                if (DetailFailure == "pending-unknown") { values["status"] = "SUBMITTED"; values["fillQuantity"] = null; values["averageFillPrice"] = null; values["lastFilledAtUtc"] = null; }
                foreach (var pair in DetailOverrides) values[pair.Key] = pair.Value;
                return Json(new { data = new { orderServiceExtendedOrderByExternalId = values } });
            }
            if (op is "TraderVIMarketBuyEligibility" or "TraderVIMarketSellEligibility")
            {
                bool sell = op == "TraderVIMarketSellEligibility";
                var query = doc.RootElement.GetProperty("query").GetString();
                Assert.Contains("query " + op + "(", query);
                Assert.Contains(sell ? "sellable" : "buyable", query);
                Assert.DoesNotContain(sell ? "buyable" : "sellable", query);
                if (EligibilityFailure == "timeout") throw new HttpRequestException("private preflight detail");
                var values = new Dictionary<string, object> {
                    ["id"] = "cad-security", ["currency"] = "CAD", ["securityType"] = "EQUITY", ["active"] = true,
                    ["buyable"] = Buyable, ["sellable"] = Sellable, ["wsTradeEligible"] = true,
                    ["allowedOrderSubtypes"] = new[] { "MARKET", "LIMIT" }, ["stock"] = new { primaryMic = "XTSE" } };
                if (EligibilityFailure == "missing-permission") values.Remove(sell ? "sellable" : "buyable");
                if (EligibilityFailure == "currency") values["currency"] = "USD";
                if (EligibilityFailure == "security") values["id"] = "other-security";
                if (EligibilityFailure == "inactive") values["active"] = false;
                if (EligibilityFailure == "type") values["securityType"] = "OPTION";
                if (EligibilityFailure == "ineligible") values["wsTradeEligible"] = false;
                if (EligibilityFailure == "no-market") values["allowedOrderSubtypes"] = new[] { "LIMIT" };
                if (EligibilityFailure == "venue") values["stock"] = new { primaryMic = "XNAS" };
                return Json(new { data = new { security = values } });
            }
            if (op is "TraderVIManualBuyQuote" or "TraderVIManualSellQuote")
            {
                Assert.Equal("cad-security", vars.GetProperty("id").GetString());
                return Json(new { data = new { security = new {
                    id="cad-security",currency="CAD",securityType="EQUITY",active=true,buyable=Buyable,sellable=Sellable,wsTradeEligible=true,
                    allowedOrderSubtypes=new[]{"MARKET"},stock=new{primaryMic="XTSE"},
                    marketMetadata=new{currentTradeDay=new{regular=QuoteFailure=="no-session" ? Array.Empty<object>() : new object[]{new{start=Clock.Now.AddHours(-1).ToString("O"),end=Clock.Now.AddHours(1).ToString("O")}}}},
                    quoteV2=new{__typename=QuoteFailure=="type"?"OptionQuote":"EquityQuote",securityId=QuoteFailure=="identity"?"other-security":"cad-security",
                        currency=QuoteFailure=="currency"?"USD":"CAD",ask=QuoteFailure=="no-ask"?null:"10.25",bid="10.24",quotedAsOf=Clock.Now.ToString("O"),marketStatus="OPEN"}
                } } });
            }
            return op switch {
                "TraderVITfsaAccounts" => Json(new { data = new { identity = new { id = "identity-fixture", accounts = new { edges = new[] { new { node = new { id = "owned-tfsa", type = "tfsa", branch = "TR", unifiedAccountType = "SELF_DIRECTED_TFSA", isOpen = true, closedAt = (string)null, archivedAt = (string)null, nickname = "TFSA" } } }, pageInfo = new { hasNextPage = false, endCursor = (string)null } } } } }),
                "TraderVICanadianSecurity" => Json(new { data = new { securitySearch = new { results = new[] { new { id = "cad-security", currency = "CAD", securityType = "EQUITY", stock = new { symbol = "FIX.TO", primaryMic = "XTSE" } } } } } }),
                _ => throw new InvalidOperationException("Unrecognized fixture operation") };
        }
        private bool cleaned;
        protected override void Dispose(bool disposing)
        {
            if (cleaned) { base.Dispose(disposing); return; }
            cleaned = true;
            Client?.Dispose();
            LastMutation?.Dispose();
            // Delete only this fixture's verified random temp directory, never a supplied path.
            var full = Path.GetFullPath(RootPath);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("TraderVI-market-buy-fixture-") && Directory.Exists(full)) Directory.Delete(full, recursive: true);
            base.Dispose(disposing);
        }
    }
    private sealed class Clock : TimeProvider { internal static DateTimeOffset Now = new(2026, 9, 10, 16, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
}
