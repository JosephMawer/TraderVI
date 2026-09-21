using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class WealthsimpleAccountTests
{
    [Theory] [InlineData("EQUITY")] [InlineData("EXCHANGE_TRADED_FUND")]
    public async Task HistoryPreservesListingEvidenceBoundToTheOwnedOrderSecurity(string type)
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(HistoricalOrder("order-1")));
        details["security"] = new { id = "security-fixture", securityType = type, stock = new { symbol = "FIX.TO", primaryMic = "XTSE" } };
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = ActivityNode("activity", "order-1") } }, pageInfo = Page() } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = details } });
        var read = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, read.Status);
        var instrument = Assert.Single(read.Data.Instruments!);
        Assert.Equal(new BrokerInstrumentIdentity("security-fixture", "FIX.TO", "XTSE", "CAD", type), instrument);
        Assert.Equal(type, Assert.Single(read.Data.Orders).SecurityType);
        Assert.Equal("FIX", read.Data.Orders[0].Symbol); // Do not replace the source activity label.
        var snapshot = new WealthsimpleTfsaSnapshot(account, read.Data.RetrievedAt, new(100, "CAD"), new(0, "CAD"), new(0, "CAD"),
            null, null, null, null, null, []);
        var compared = WealthsimpleReconciliation.Compare(read.Data, snapshot, new(account, snapshot.RetrievedAt, []), snapshot.RetrievedAt, false);
        Assert.Equal(instrument, Assert.Single(compared.Instruments!));
    }
    [Theory] [InlineData("absent")] [InlineData("null")] [InlineData("missing-mic")]
    public async Task IncompleteHistoricalListingDoesNotInventAliasEvidence(string shape)
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(HistoricalOrder("order-1")));
        if (shape != "absent") details["security"] = new { id = "security-fixture", securityType = "EQUITY",
            stock = shape == "null" ? null : new { symbol = "FIX.TO" } };
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = ActivityNode("activity", "order-1") } }, pageInfo = Page() } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = details } });
        var read = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, read.Status); Assert.Null(read.Data.Instruments);
        Assert.DoesNotContain("Instruments", JsonSerializer.Serialize(read.Data));
    }
    [Theory]
    [InlineData("EXPIRED")] [InlineData("CANCELLED")] [InlineData("CANCELED")]
    public async Task EmptyTerminalOrderIsRetainedWithNullProvenanceAndDoesNotCreateASale(string status)
    {
        using var capture = EmptyTerminalCapture(status);
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Operations.Clear();
        f.Override = (operation, _) => Json(capture.RootElement.GetProperty(operation == "TraderVITfsaActivityHistory" ? "activity" : "detail"));
        var history = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, history.Status);
        var order = Assert.Single(history.Data.Orders);
        Assert.Equal(status, order.Status); Assert.Equal(0, order.FilledQuantity);
        Assert.True(order.FillQuantityWasNull); Assert.Null(order.GrossValue); Assert.Null(order.NetValue);
        var at = history.Data.RetrievedAt;
        var snapshot = new WealthsimpleTfsaSnapshot(account, at, new(100, "CAD"), new(100, "CAD"), new(100, "CAD"),
            null, null, null, null, null, []);
        var reconciled = WealthsimpleReconciliation.Compare(history.Data, snapshot, new(account, at, []), at, false);
        Assert.True(reconciled.HoldingsMatch); Assert.Empty(reconciled.Holdings);
        Assert.Equal(order, JsonSerializer.Deserialize<BrokerOrderFact>(JsonSerializer.Serialize(order)));
        Assert.Equal(new[] { "TraderVITfsaActivityHistory", "TraderVITfsaHistoricalOrder" }, f.Operations);
    }

    public static IEnumerable<object[]> EmptyTerminalExecutionConflicts() =>
        from status in new[] { "EXPIRED", "CANCELLED", "CANCELED" }
        from change in new[] { "filledValue", "filledNetValue", "filledTotalFee", "filledTaxWithholding", "averageFillPrice",
            "lastFilledAtUtc", "missing-average", "missing-timestamp", "invalid-timestamp", "reported-fill",
            "reported-partial-fill", "reported-posting" }
        select new object[] { status, change };

    [Theory]
    [MemberData(nameof(EmptyTerminalExecutionConflicts))]
    public async Task EmptyTerminalOrderNeverConvertsAnIncompleteOrContradictoryExecutionIntoZero(string status, string change)
    {
        using var capture = EmptyTerminalCapture(status);
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(capture.RootElement.GetProperty("detail").GetProperty("data")
            .GetProperty("orderServiceExtendedOrderByExternalId").GetRawText());
        var activity = JsonSerializer.Deserialize<Dictionary<string, object>>(capture.RootElement.GetProperty("activity").GetProperty("data")
            .GetProperty("activityFeedItems").GetProperty("edges")[0].GetProperty("node").GetRawText());
        if (change == "missing-average") details.Remove("averageFillPrice");
        else if (change == "missing-timestamp") details.Remove("lastFilledAtUtc");
        else if (change == "invalid-timestamp") details["lastFilledAtUtc"] = true;
        else if (change.StartsWith("reported")) activity["status"] = change == "reported-fill" ? "FILLED" : change == "reported-partial-fill" ? "PARTIALLY_FILLED" : "POSTED";
        else details[change] = change == "lastFilledAtUtc" ? "2026-09-09T14:00:00Z" : "1";
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = activity } }, pageInfo = Page() } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = details } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.NotEqual(WealthsimpleReadStatus.Success, result.Status); Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("REJECTED")] [InlineData("SUBMITTED")] [InlineData("CANCEL_PENDING")]
    [InlineData("PARTIALLY_FILLED")] [InlineData("UNKNOWN")]
    [InlineData("FILLED")] [InlineData("POSTED")]
    public async Task EmptyTerminalCompatibilityDoesNotRelaxMissingQuantitiesForOtherStatuses(string status)
    {
        using var capture = EmptyTerminalCapture();
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(capture.RootElement.GetProperty("detail").GetProperty("data")
            .GetProperty("orderServiceExtendedOrderByExternalId").GetRawText());
        details["status"] = status;
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory" ? Json(capture.RootElement.GetProperty("activity"))
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = details } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Unavailable, result.Status); Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("EXPIRED")] [InlineData("CANCELLED")] [InlineData("CANCELED")]
    public async Task TerminalPartialFillKeepsItsActualQuantityAndMoney(string status)
    {
        using var capture = EmptyTerminalCapture(status);
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(capture.RootElement.GetProperty("detail").GetProperty("data")
            .GetProperty("orderServiceExtendedOrderByExternalId").GetRawText());
        details["fillQuantity"] = "2"; details["filledValue"] = "20"; details["filledNetValue"] = "19";
        details["filledTotalFee"] = "1"; details["filledTaxWithholding"] = "0"; details["averageFillPrice"] = "10";
        details["lastFilledAtUtc"] = "2026-09-09T14:00:00Z";
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory" ? Json(capture.RootElement.GetProperty("activity"))
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = details } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        var order = Assert.Single(result.Data.Orders);
        Assert.Equal(2, order.FilledQuantity); Assert.Equal(19, order.NetValue); Assert.Equal(1, order.Fees);
        Assert.False(order.FillQuantityWasNull); Assert.True(WealthsimpleReconciliation.Valid(order, result.Data.RetrievedAt));
        Assert.DoesNotContain("FillQuantityWasNull", JsonSerializer.Serialize(order));
    }

    private static JsonDocument EmptyTerminalCapture(string status = "EXPIRED")
    {
        using var stream = typeof(WealthsimpleAccountTests).Assembly.GetManifestResourceStream(
            "TraderVI.Core.Tests.Fixtures.Wealthsimple.history-expired-empty.json");
        using var capture = JsonDocument.Parse(stream);
        return JsonDocument.Parse(capture.RootElement.GetRawText().Replace("\"EXPIRED\"", JsonSerializer.Serialize(status)));
    }

    [Fact]
    public async Task CapturedCompletedActivityCanHavePostedHistoricalDetailsWithoutSubmittingAnOrder()
    {
        using var stream = typeof(WealthsimpleAccountTests).Assembly.GetManifestResourceStream(
            "TraderVI.Core.Tests.Fixtures.Wealthsimple.history-posted-sell.json");
        using var capture = JsonDocument.Parse(stream);
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Operations.Clear();
        f.Override = (operation, _) => Json(capture.RootElement.GetProperty(operation == "TraderVITfsaActivityHistory" ? "activity" : "detail"));
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        var order = Assert.Single(result.Data.Orders);
        Assert.Equal("POSTED", order.Status); Assert.True(order.IsTerminal);
        Assert.Equal(10, order.FilledQuantity); Assert.Equal(120, order.NetValue);
        Assert.True(WealthsimpleReconciliation.Valid(order, result.Data.RetrievedAt));
        Assert.Equal(new[] { "TraderVITfsaActivityHistory", "TraderVITfsaHistoricalOrder" }, f.Operations);
    }

    [Theory]
    [InlineData("EXPIRED", false)]
    [InlineData("CANCELLED", false)]
    [InlineData("FILLED", true)]
    [InlineData("POSTED", true)]
    [InlineData("private-status-value", false)]
    public async Task MissingFillQuantityReportsSafeStateAndPresenceWithoutGuessingZero(string status, bool hasFillFacts)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(HistoricalOrder("order-1")));
        details["status"] = status; details["fillQuantity"] = null;
        if (!hasFillFacts)
            foreach (var field in new[] { "filledValue", "filledNetValue", "filledTotalFee", "filledTaxWithholding", "lastFilledAtUtc" }) details[field] = null;
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = ActivityNode("activity", "order-1") } }, pageInfo = Page() } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = details } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Unavailable, result.Status); Assert.Null(result.Data);
        Assert.Contains("status " + (status.StartsWith("private") ? "unrecognized" : status), result.Stage);
        Assert.Contains("fill amounts " + (hasFillFacts ? "present" : "absent"), result.Stage);
        Assert.Contains("fill timestamp " + (hasFillFacts ? "present" : "absent"), result.Stage);
        Assert.DoesNotContain("private", result.ToString()); Assert.DoesNotContain("order-1", result.ToString());
    }

    [Theory]
    [InlineData(200, "GRAPHQL_VALIDATION_FAILED", "GraphQL validation failed")]
    [InlineData(400, "GRAPHQL_VALIDATION_FAILED", "GraphQL validation failed")]
    [InlineData(200, "private-code", "GraphQL request rejected")]
    public async Task HistoryGraphQlDiagnosticsExposeOnlyFixedCategoriesAndQueryLocations(int httpStatus, string code, string category)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Operations.Clear();
        f.Override = (_, _) =>
        {
            var response = Json(new { errors = new[] { new { message = "private-account-and-token",
                extensions = new { code }, locations = new[] { new { line = 1, column = 7 } },
                path = new[] { "private-identifier" } } }, data = new { activityFeedItems = new {
                    edges = Array.Empty<object>(), pageInfo = Page() } } });
            response.StatusCode = (System.Net.HttpStatusCode)httpStatus;
            return response;
        };
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
        Assert.Contains("TraderVITfsaActivityHistory / HTTP " + httpStatus, result.Stage);
        Assert.Contains(category + " at query line 1, column 7", result.Stage);
        Assert.DoesNotContain("private", result.ToString()); Assert.DoesNotContain(account.Id, result.ToString());
        Assert.Single(f.Operations);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("data-without-errors")]
    [InlineData("invalid-error-location")]
    public async Task BadRequestNeverBecomesSuccessfulHistoryOrLeaksMalformedErrorDetails(string shape)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Operations.Clear();
        f.Override = (_, _) =>
        {
            var response = shape == "invalid-json"
                ? new System.Net.Http.HttpResponseMessage { Content = new System.Net.Http.StringContent("private-server-content") }
                : shape == "data-without-errors"
                    ? Json(new { data = new { activityFeedItems = new { edges = Array.Empty<object>(), pageInfo = Page() } } })
                    : Json(new { errors = new[] { new { message = "private-server-message", locations = new[] {
                        new { line = "private-invalid-location", column = -1 } } } } });
            response.StatusCode = System.Net.HttpStatusCode.BadRequest;
            return response;
        };
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
        Assert.Contains("HTTP 400", result.Stage); Assert.DoesNotContain("private", result.ToString());
        Assert.Single(f.Operations);
    }

    [Theory]
    [InlineData("securityType", "missing or invalid text securityType")]
    [InlineData("order", "missing field data.orderServiceExtendedOrderByExternalId")]
    public async Task HistoricalResponseDiagnosticsIdentifyMissingFieldsWithoutOrderOrAccountValues(string missing, string diagnostic)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(HistoricalOrder("order-1")));
        details["security"] = new { id = "security-fixture" };
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = ActivityNode("activity", "order-1") } }, pageInfo = Page() } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = missing == "order" ? null : details } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
        Assert.Contains("order history / " + diagnostic, result.Stage);
        Assert.DoesNotContain("order-1", result.ToString()); Assert.DoesNotContain(account.Id, result.ToString());
    }

    [Fact]
    public async Task HistoryDiscoveryExhaustsPagesScopesAccountAndReadsVerifiedOrderDetailsWithoutMutation()
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        int pages = 0, details = 0;
        f.Override = (op, vars) =>
        {
            if (op == "TraderVITfsaActivityHistory")
            {
                Assert.Equal("OWN", vars.GetProperty("accountScope").GetString());
                Assert.Equal(account.Id, Assert.Single(vars.GetProperty("condition").GetProperty("accountIds").EnumerateArray()).GetString());
                pages++;
                return Json(new { data = new { activityFeedItems = new {
                    edges = new[] { new { node = ActivityNode("activity-" + pages, "order-" + pages) } }, pageInfo = Page(pages == 1, "cursor-" + pages) } } });
            }
            if (op == "TraderVITfsaHistoricalOrder")
            {
                details++;
                return Json(new { data = new { orderServiceExtendedOrderByExternalId = HistoricalOrder(vars.GetProperty("externalId").GetString()) } });
            }
            throw new InvalidOperationException("Unexpected operation.");
        };
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status); Assert.Equal(2, pages); Assert.Equal(2, details);
        Assert.Equal(2, result.Data.Orders.Count); Assert.Equal("identity-fixture", result.Data.IdentityId);
        Assert.Equal(10, result.Data.Orders[0].FilledQuantity); Assert.Equal(100, result.Data.Orders[0].NetValue);
        Assert.Empty(result.Data.Limitations); Assert.DoesNotContain("SoOrdersOrderCreate", f.Operations);
    }
    [Theory]
    [InlineData("foreign-activity")] [InlineData("foreign-order")] [InlineData("wrong-identity")]
    [InlineData("wrong-side")] [InlineData("wrong-security")] [InlineData("duplicate-page")]
    public async Task HistoryRejectsMismatchedIdentitiesAndDuplicatePaginationWithoutPartialSuccess(string change)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Override = (op, vars) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = ActivityNode("activity", "order-1", change == "foreign-activity" ? "other" : account.Id) } },
                pageInfo = Page(change == "duplicate-page", "repeated") } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = HistoricalOrder("order-1",
                change == "foreign-order" ? "other" : account.Id, change == "wrong-identity" ? "other" : "identity-fixture",
                change == "wrong-side" ? "SELL" : "BUY", change == "wrong-security" ? "other" : "security-fixture") } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
    }
    [Fact]
    public async Task ActivityCheckpointChangesWhenAnOrderProgressesWithoutANewActivityId()
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        string status = "SUBMITTED", quantity = "0";
        f.Override = (_, _) => Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = new {
            accountId = account.Id, canonicalId = "activity", externalCanonicalId = "order-fixture", type = "DIY_BUY", status, assetQuantity = quantity } } }, pageInfo = Page() } } });
        var first = await f.Client.ReadTfsaActivityCheckpointAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, first.Status);
        status = "FILLED"; quantity = "10";
        var second = await f.Client.ReadTfsaActivityCheckpointAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, second.Status);
        Assert.NotEqual(first.Data.Fingerprint, second.Data.Fingerprint);
        Assert.DoesNotContain("order-fixture", second.Data.ToString());
    }

    [Theory]
    [InlineData("DEPOSIT", null)]
    [InlineData("DEPOSIT", "")]
    [InlineData("DIY_BUY", null)]
    [InlineData("DIY_SELL", null)]
    [InlineData("UNKNOWN", null)]
    public async Task NullableFeedStatusDoesNotInventAnOrderStateOrSkipUnsupportedActivity(string type, string status)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        bool trade = type is "DIY_BUY" or "DIY_SELL";
        f.Operations.Clear();
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = new {
                accountId = account.Id, canonicalId = "activity", externalCanonicalId = trade ? "order-1" : null,
                type, status, securityId = trade ? "security-fixture" : null, assetSymbol = trade ? "FIX" : null,
                assetQuantity = (string)null } } }, pageInfo = Page() } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = HistoricalOrder("order-1", side: type == "DIY_SELL" ? "SELL" : "BUY") } });
        var history = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, history.Status);
        if (trade)
        {
            Assert.Equal("FILLED", Assert.Single(history.Data.Orders).Status);
            Assert.Equal(10, history.Data.Orders[0].FilledQuantity);
            Assert.Contains("TraderVITfsaHistoricalOrder", f.Operations);
        }
        else Assert.Empty(history.Data.Orders);
        Assert.Equal(type == "UNKNOWN", history.Data.Limitations.Any());
        var checkpoint = await f.Client.ReadTfsaActivityCheckpointAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, checkpoint.Status);
        Assert.Equal(history.Data.ActivityFingerprint, checkpoint.Data.Fingerprint);
        Assert.DoesNotContain("SoOrdersOrderCreate", f.Operations);
    }

    [Fact]
    public async Task CheckpointPreservesChangesBetweenNullEmptyAndKnownFeedStatus()
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        string status = null;
        f.Override = (_, _) => Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = new {
            accountId = account.Id, canonicalId = "activity", type = "DEPOSIT", status, assetQuantity = (string)null } } }, pageInfo = Page() } } });
        var first = await f.Client.ReadTfsaActivityCheckpointAsync(account);
        status = ""; var second = await f.Client.ReadTfsaActivityCheckpointAsync(account);
        status = "completed"; var third = await f.Client.ReadTfsaActivityCheckpointAsync(account);
        Assert.All(new[] { first, second, third }, result => Assert.Equal(WealthsimpleReadStatus.Success, result.Status));
        Assert.Equal(3, new[] { first.Data.Fingerprint, second.Data.Fingerprint, third.Data.Fingerprint }.Distinct().Count());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("number")]
    public async Task HistoricalOrderStatusRemainsRequiredEvenWhenFeedStatusIsNull(string shape)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(HistoricalOrder("order-1")));
        if (shape == "missing") details.Remove("status");
        else details["status"] = shape == "null" ? null : shape == "empty" ? "" : 1;
        var activity = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(ActivityNode("activity", "order-1")));
        activity["status"] = null;
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = activity } }, pageInfo = Page() } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = details } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
        Assert.Contains("historical order status missing or invalid", result.Stage);
        Assert.DoesNotContain("order-1", result.ToString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("number")]
    [InlineData("object")]
    [InlineData("oversized")]
    public async Task NullableFeedStatusDoesNotAcceptMissingFieldsOrMalformedValues(string shape)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var activity = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(ActivityNode("activity", "order-1")));
        if (shape == "missing") activity.Remove("status");
        else activity["status"] = shape == "number" ? 1 : shape == "object" ? new { secret = "private-value" } : new string('x', 513);
        f.Override = (_, _) => Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = activity } }, pageInfo = Page() } } });
        var result = await f.Client.ReadTfsaActivityCheckpointAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
        Assert.Contains("activity feed status", result.Stage); Assert.DoesNotContain("private", result.ToString());
    }

    [Theory]
    [InlineData("fillQuantity")] [InlineData("filledNetValue")] [InlineData("filledTotalFee")] [InlineData("lastFilledAtUtc")]
    public async Task MissingConfirmedQuantitiesOrCostsFailTheReadInsteadOfBecomingZeroOrReplacingHistory(string missing)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var details = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(HistoricalOrder("order-1")));
        details[missing] = null;
        f.Override = (op, _) => op == "TraderVITfsaActivityHistory"
            ? Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = ActivityNode("activity", "order-1") } }, pageInfo = Page() } } })
            : Json(new { data = new { orderServiceExtendedOrderByExternalId = details } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Unavailable, result.Status); Assert.Null(result.Data);
    }
    private static object ActivityNode(string canonicalId, string externalCanonicalId, string accountId = "tfsa-fixture-id") =>
        new { canonicalId, externalCanonicalId, accountId, type = "DIY_BUY", status = "FILLED", securityId = "security-fixture", assetSymbol = "FIX" };
    private static object HistoricalOrder(string id, string canonicalAccountId = "tfsa-fixture-id", string identityId = "identity-fixture",
        string side = "BUY", string securityId = "security-fixture") => new {
            id, canonicalAccountId, identityId, securityId, side, status = "FILLED", securityCurrency = "CAD", submittedQuantity = "10", fillQuantity = "10",
            averageFillPrice = "10", filledValue = "100", filledNetValue = "100", filledTotalFee = "0", filledTaxWithholding = "0",
            createdAtUtc = "2026-09-09T14:00:00Z", lastFilledAtUtc = "2026-09-09T14:01:00Z", security = new { id = securityId, securityType = "EQUITY" }
        };

    [Theory]
    [InlineData("INTERNAL_TRANSFER", null, null, false)]
    [InlineData("INTERNAL_TRANSFER", "security-fixture", "10", true)]
    [InlineData("DEPOSIT", null, null, false)]
    [InlineData("UNKNOWN", null, null, true)]
    public async Task PureCashTransfersDoNotPretendToBeStockTransfersAndUnsupportedActivityRemainsExplicit(string type, string securityId, string assetQuantity, bool blocked)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Override = (_, _) => Json(new { data = new { activityFeedItems = new { edges = new[] { new { node = new {
            accountId = account.Id, canonicalId = "activity", type, status = "completed", securityId, assetQuantity } } }, pageInfo = Page() } } });
        var result = await f.Client.ReadTfsaOrderHistoryAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status); Assert.Empty(result.Data.Orders);
        Assert.Equal(blocked, result.Data.Limitations.Any());
    }
}
