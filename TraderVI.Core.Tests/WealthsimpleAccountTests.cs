using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class WealthsimpleAccountTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopManualContextCombinesScopedQuoteBalancesHoldingsAndPendingOrdersWithoutMutation(bool sell)
    {
        using var f = new Fixture();
        f.Override = (op, vars) => op switch
        {
            "TraderVICanadianSecurity" => Json(new { data = new { securitySearch = new { results = new[] { new { id = "security-fixture", currency = "CAD", securityType = "EQUITY", stock = new { symbol = "FIX.TO", primaryMic = "XTSE" } } } } } }),
            "TraderVIManualBuyQuote" or "TraderVIManualSellQuote" => Json(new { data = new { security = new {
                id = "security-fixture", currency = "CAD", securityType = "EQUITY", active = true, buyable = !sell, sellable = true, wsTradeEligible = true,
                allowedOrderSubtypes = new[] { "MARKET" }, stock = new { primaryMic = "XTSE" },
                marketMetadata = new { currentTradeDay = new { regular = new[] { new { start = f.Clock.Now.AddHours(-1).ToString("O"), end = f.Clock.Now.AddHours(1).ToString("O") } } } },
                quoteV2 = new { __typename = "EquityQuote", securityId = "security-fixture", currency = "CAD", ask = "10.25", bid = "10.24", quotedAsOf = f.Clock.Now.ToString("O"), marketStatus = "OPEN" }
            } } }),
            "TraderVITfsaPendingOrders" => Json(OrderPage(Array.Empty<object>(), false)),
            _ => null
        };
        var connection = new WealthsimpleConnection(new Store(), f.Clock, () => f.Client);
        await connection.SignInAsync("fixture@example.invalid", "fixture-password", false);
        var account = Assert.Single((await connection.ReadTfsaAccountsAsync()).Data);
        if (sell)
        {
            var result = await connection.ReadManualSellContextAsync(account,"FIX",default);
            Assert.Equal(WealthsimpleReadStatus.Success,result.Status); Assert.Equal(1.234567m,result.Data.HeldQuantity);
            Assert.Equal(10.24m,result.Data.Quote.Bid); Assert.Empty(result.Data.PendingOrders.Orders);
            var intent = new global::Core.WSTrade.Orders.ManualSellIntent(Guid.NewGuid(),"identity-fixture","tfsa-fixture-id","security-fixture",
                "TFSA","FIX.TO","XTSE",20,f.Clock.Now,result.Data.Quote,f.Clock.Now);
            var inventory = await connection.ReadManualSellInventoryAsync(intent,default);
            Assert.Equal(WealthsimpleReadStatus.Success,inventory.Status); Assert.Equal(1.234567m,inventory.Data.Quantity);
            Assert.False(inventory.Data.HasPendingOrders);
            Assert.Equal(WealthsimpleReadStatus.AccountUnavailable,(await connection.ReadManualSellInventoryAsync(intent with { IdentityId="wrong" },default)).Status);
        }
        else
        {
            var result = await connection.ReadManualBuyContextAsync(account, "FIX", default);
            Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
            Assert.Equal(5m, result.Data.AvailableCash); Assert.Equal(1.234567m, result.Data.HeldQuantity);
            Assert.Equal(10.25m, result.Data.Quote.Ask); Assert.Empty(result.Data.PendingOrders.Orders);
        }
        Assert.Contains(sell ? "TraderVIManualSellQuote" : "TraderVIManualBuyQuote", f.Operations); Assert.Contains("TraderVITfsaPendingOrders", f.Operations);
        Assert.DoesNotContain("SoOrdersOrderCreate", f.Operations);
        await connection.StopAsync();
    }

    [Fact]
    public async Task PendingOrdersPaginateAndFilterAccountWithoutHidingPartialOrCancelPending()
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Override = (op, vars) =>
        {
            if (op != "TraderVITfsaPendingOrders") return null;
            var statuses = vars.GetProperty("statuses").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Contains("PARTIALLY_FILLED", statuses); Assert.Contains("CANCEL_PENDING", statuses);
            bool first = vars.GetProperty("cursor").ValueKind == JsonValueKind.Null;
            return Json(OrderPage(first ? new[] { OrderNode("other", "managed"), OrderNode("a", status: "PARTIALLY_FILLED") }
                : new[] { OrderNode("b", status: "CANCEL_PENDING") }, first));
        };
        var result = await f.Client.ReadTfsaPendingOrdersAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Equal(new[] { "PARTIALLY_FILLED", "CANCEL_PENDING" }, result.Data.Orders.Select(o => o.Status));
        Assert.All(result.Data.Orders, o => Assert.Equal(17m, o.Quantity));
        Assert.DoesNotContain("tfsa-fixture-id", result.ToString());
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("duplicate")]
    [InlineData("cursor")]
    [InlineData("quantity")]
    public async Task PendingOrderErrorsDoNotReturnAnEmptySuccessfulFeed(string failure)
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Override = (op, vars) => op != "TraderVITfsaPendingOrders" ? null : Json(OrderPage(
            failure == "duplicate" ? new[] { OrderNode("a"), OrderNode("a") } : new[] {
                OrderNode(vars.GetProperty("cursor").ValueKind == JsonValueKind.Null ? "a" : "b", quantity: failure == "quantity" ? "NaN" : "17") },
            failure == "cursor", failure == "identity" ? "another-identity" : "identity-fixture"));
        var result = await f.Client.ReadTfsaPendingOrdersAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
        Assert.True(f.Operations.Count <= 3);
    }

    [Fact]
    public async Task SecurityMatchingDoesNotSubstituteUsListingsOrSimilarSymbols()
    {
        using var f = new Fixture(); await f.Login();
        f.Override = (op, _) => op == "TraderVICanadianSecurity" ? Json(new { data = new { securitySearch = new { results = new[] {
            Security("cad", "FIX", "CAD", "XTSE"), Security("us", "FIX", "USD", "XNYS"), Security("different", "FIX.A", "CAD", "XTSE")
        } } } }) : null;
        var result = await f.Client.FindCanadianSecuritiesAsync("FIX");
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status); Assert.Equal("cad", Assert.Single(result.Data).Id);
    }

    private static object Security(string id, string symbol, string currency, string mic) => new { id, currency, securityType = "EQUITY", stock = new { symbol, primaryMic = mic } };
    private static object OrderNode(string id, string owner = "tfsa-fixture-id", string status = "SUBMITTED", string quantity = "17") =>
        new { id, orderId = id, canonicalAccountId = owner, createdAtUtc = "2026-09-10T01:00:00Z", status, side = "BUY", executionType = "LIMIT", submittedQuantity = quantity,
            limitPrice = "2.50", securityCurrency = "CAD", securityId = "security-fixture", symbol = "FIX" };
    private static object OrderPage(object[] orders, bool more = false, string identity = "identity-fixture") =>
        new { data = new { identity = new { id = identity, orderServiceExtendedOrderFeed = new { edges = orders.Select(n => new { node = n }).ToArray(), pageInfo = Page(more, "next") } } } };

    [Fact]
    public async Task LogoutDiscardsAnInFlightPendingOrderRead()
    {
        var source = new Source();
        var completion = new TaskCompletionSource<WealthsimpleReadResult<WealthsimplePendingOrders>>();
        source.ReadOrders = _ => completion.Task;
        using var vm = new WealthsimpleAccountViewModel(source);
        var refreshing = vm.RefreshAsync();
        Assert.NotNull(vm.Snapshot);
        source.Disconnect();
        completion.SetResult(new(WealthsimpleReadStatus.Success, new(source.Accounts[0], DateTimeOffset.UtcNow, [])));
        await refreshing;
        Assert.Null(vm.Snapshot); Assert.Null(vm.PendingOrders); Assert.Empty(vm.Orders);
    }

    [Fact]
    public async Task FailedOrderRefreshDoesNotKeepAnOldSuccessfulEmptyFeed()
    {
        var source = new Source(); using var vm = new WealthsimpleAccountViewModel(source);
        await vm.RefreshAsync(); Assert.NotNull(vm.PendingOrders);
        source.ReadOrders = _ => Task.FromResult(new WealthsimpleReadResult<WealthsimplePendingOrders>(WealthsimpleReadStatus.Unavailable));
        await vm.RefreshAsync();
        Assert.NotNull(vm.Snapshot); Assert.Null(vm.PendingOrders);
        Assert.Contains("transition review is blocked", vm.Status);
    }

    [Fact]
    public async Task ReadsOnlyOpenSelfDirectedTfsaAndIncludesEveryFractionalHolding()
    {
        using var f = new Fixture(); await f.Login();
        var catalog = await f.Client.ReadTfsaAccountsAsync();
        Assert.Equal(WealthsimpleReadStatus.Success, catalog.Status);
        var account = Assert.Single(catalog.Data);
        Assert.DoesNotContain("tfsa-fixture-id", account.ToString());
        f.Pages = true;
        var result = await f.Client.ReadTfsaSnapshotAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Equal(2, result.Data.Holdings.Count);
        Assert.Equal(1.234567m, result.Data.Holdings[0].Quantity);
        Assert.Equal(123.45m, result.Data.Value.Amount);
        Assert.Equal("CAD", result.Data.Value.Currency);
        Assert.Equal(7.50m, result.Data.Cash.Amount);
        Assert.Equal(5m, result.Data.BuyingPower.Amount);
        Assert.NotEqual(result.Data.Cash, result.Data.BuyingPower);
        Assert.Equal("2020-01-01", result.Data.ReturnReferenceDate);
        Assert.Null(result.Data.ReturnAsOf);
        Assert.Equal(f.Clock.GetUtcNow(), result.Data.RetrievedAt);
        Assert.Equal(2, f.Operations.Count(s => s == "TraderVITfsaPositions"));
    }

    [Fact]
    public async Task AccountCatalogPaginatesAndDoesNotChooseManagedOrClosedTfsa()
    {
        using var f = new Fixture(); await f.Login();
        f.Override = (op, vars) => op != "TraderVITfsaAccounts" ? null :
            Json(new { data = new { identity = new { id = "identity-fixture", accounts = new {
                edges = vars.GetProperty("cursor").ValueKind == JsonValueKind.Null
                    ? new[] { new { node = Account("managed", "MANAGED_PORTFOLIO_TFSA", "WS") } }
                    : new[] { new { node = Account("self", "SELF_DIRECTED_TFSA", "TR") } },
                pageInfo = Page(vars.GetProperty("cursor").ValueKind == JsonValueKind.Null, "next")
            } } } });
        var result = await f.Client.ReadTfsaAccountsAsync();
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Single(result.Data); Assert.Equal(2, f.Operations.Count);
    }

    [Fact]
    public async Task SessionSelectionCannotBeFabricatedOrReusedAfterLogin()
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var before = f.Operations.Count;
        Assert.Equal(WealthsimpleReadStatus.AccountUnavailable,
            (await f.Client.ReadTfsaSnapshotAsync(new WealthsimpleTfsaAccount("other", "Other"))).Status);
        Assert.Equal(before, f.Operations.Count);
        await f.Login();
        Assert.Equal(WealthsimpleReadStatus.AccountUnavailable, (await f.Client.ReadTfsaSnapshotAsync(account)).Status);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("wrong-identity")]
    [InlineData("partial-status")]
    [InlineData("count-mismatch")]
    [InlineData("repeated-cursor")]
    [InlineData("duplicate-position")]
    [InlineData("bad-quantity")]
    public async Task RejectsIncompleteMisScopedOrMalformedHoldings(string failure)
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Override = (op, vars) => op == "TraderVITfsaPositions" ? Json(Positions(
            owner: failure == "wrong-owner" ? "managed" : "tfsa-fixture-id",
            identity: failure == "wrong-identity" ? "other-identity" : "identity-fixture",
            status: failure == "partial-status" ? "PENDING" : "OK",
            total: failure == "count-mismatch" ? 2 : failure is "repeated-cursor" or "duplicate-position" ? 10 : 1,
            more: failure is "repeated-cursor" or "duplicate-position",
            position: failure == "repeated-cursor" && vars.GetProperty("cursor").ValueKind != JsonValueKind.Null ? "p2" : "p1",
            quantity: failure == "bad-quantity" ? "NaN" : "1.234567")) : null;
        var result = await f.Client.ReadTfsaSnapshotAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
        Assert.True(f.Operations.Count <= 5);
    }

    [Theory]
    [InlineData(401, WealthsimpleReadStatus.NotAuthenticated)]
    [InlineData(403, WealthsimpleReadStatus.Forbidden)]
    [InlineData(429, WealthsimpleReadStatus.RateLimited)]
    [InlineData(503, WealthsimpleReadStatus.Unavailable)]
    [InlineData(302, WealthsimpleReadStatus.UnsupportedResponse)]
    public async Task ResourceErrorsHaveSafeStatusAndNeverRetry(int code, WealthsimpleReadStatus expected)
    {
        using var f = new Fixture(); await f.Login();
        f.Override = (_, _) => new((HttpStatusCode)code) { Content = new StringContent("private-server-detail") };
        var result = await f.Client.ReadTfsaAccountsAsync();
        Assert.Equal(expected, result.Status); Assert.Null(result.Data);
        Assert.DoesNotContain("private", result.ToString()); Assert.Single(f.Operations);
        if (code == 401) Assert.Equal(WealthsimpleAuthStatus.NotAuthenticated, (await f.Client.VerifyAsync()).Status);
    }

    [Fact]
    public async Task GraphQlErrorsDiscardPartialDataEvenOnHttp200()
    {
        using var f = new Fixture(); await f.Login();
        f.Override = (_, _) => Json(new { errors = new[] { new { message = "private-info", extensions = new { code = "UNAUTHENTICATED" } } }, data = new { identity = new { id = "identity-fixture" } } });
        var result = await f.Client.ReadTfsaAccountsAsync();
        Assert.Equal(WealthsimpleReadStatus.NotAuthenticated, result.Status); Assert.Null(result.Data);
        Assert.DoesNotContain("private-info", result.ToString());
        Assert.Equal(WealthsimpleAuthStatus.NotAuthenticated, (await f.Client.VerifyAsync()).Status);
    }

    [Fact]
    public async Task MissingValuesStayMissingAndHoldingCurrenciesArePreserved()
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Override = (op, _) => op switch {
            "TraderVITfsaFinancials" => Json(new { data = new { accounts = new[] { new { id = "tfsa-fixture-id", financials = new { currentCombined = new { netLiquidationValueV2 = (object)null, netDepositsV2 = (object)null, simpleReturns = (object)null } } } } } }),
            "TraderVITfsaPositions" => Json(Positions(currency: "USD")), _ => null };
        var result = await f.Client.ReadTfsaSnapshotAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Null(result.Data.Value); Assert.Null(result.Data.Return); Assert.Null(result.Data.NetDeposits);
        Assert.Equal("USD", Assert.Single(result.Data.Holdings).Value.Currency);
    }

    [Fact]
    public async Task ExpiredAccessIsNotSentAndRefreshKeepsSelectionForSameIdentity()
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        Assert.Equal(WealthsimpleReadStatus.NotAuthenticated, (await f.Client.ReadTfsaSnapshotAsync(account)).Status);
        Assert.Single(f.Operations);
        Assert.Equal(WealthsimpleAuthStatus.Authenticated, (await f.Client.RefreshAsync()).Status);
        Assert.Equal(WealthsimpleReadStatus.Success, (await f.Client.ReadTfsaSnapshotAsync(account)).Status);
    }

    [Fact]
    public async Task UiKeepsPreviousSnapshotOnTransportFailureAndClearsOnDisconnect()
    {
        var source = new Source(); using var vm = new WealthsimpleAccountViewModel(source);
        await vm.RefreshAsync(); Assert.NotNull(vm.Snapshot);
        source.Read = _ => Task.FromResult(new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Unavailable));
        await vm.RefreshAsync(); Assert.NotNull(vm.Snapshot); Assert.Contains("previous snapshot", vm.Status);
        source.Disconnect(); Assert.Null(vm.Snapshot); Assert.Empty(vm.Accounts); Assert.False(vm.CanRefresh);
    }

    [Fact]
    public async Task UiNeverPublishesAnInFlightSnapshotAfterDisconnect()
    {
        var source = new Source(); var completion = new TaskCompletionSource<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>>();
        source.Read = _ => completion.Task;
        using var vm = new WealthsimpleAccountViewModel(source); var read = vm.RefreshAsync();
        Assert.True(vm.IsBusy); source.Disconnect(); completion.SetResult(new(WealthsimpleReadStatus.Success, Snapshot(source.Accounts[0])));
        await read; Assert.Null(vm.Snapshot); Assert.Empty(vm.Accounts); Assert.False(vm.CanRefresh);
    }

    [Fact]
    public async Task UiRequiresChoiceWhenMultipleSelfDirectedAccountsExist()
    {
        var source = new Source { Accounts = new[] { new WealthsimpleTfsaAccount("a", "A"), new WealthsimpleTfsaAccount("b", "B") } };
        using var vm = new WealthsimpleAccountViewModel(source); await vm.RefreshAsync();
        Assert.Null(vm.SelectedAccount); Assert.Null(vm.Snapshot); Assert.True(vm.CanSelect);
        vm.SelectedAccount = source.Accounts[1];
        Assert.Same(source.Accounts[1], vm.Snapshot.Account);
    }

    [Fact]
    public async Task ResourceAuthenticationFailureRetiresDesktopSessionAndSavedCopy()
    {
        using var f = new Fixture(); var store = new Store();
        var connection = new WealthsimpleConnection(store, f.Clock, () => f.Client);
        await connection.SignInAsync("fixture@example.invalid", "fixture-password", true);
        Assert.True(connection.IsConnected); Assert.Equal(1, store.Saves);
        f.Override = (_, _) => new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") };
        Assert.Equal(WealthsimpleReadStatus.NotAuthenticated, (await connection.ReadTfsaAccountsAsync()).Status);
        Assert.False(connection.IsConnected); Assert.False(connection.HasSession); Assert.Equal(2, store.Clears);
        await connection.StopAsync();
    }

    [Fact]
    public async Task DesktopRenewsNearExpiryBeforeReadingTheAccount()
    {
        using var f = new Fixture(); var store = new Store();
        var connection = new WealthsimpleConnection(store, f.Clock, () => f.Client);
        await connection.SignInAsync("fixture@example.invalid", "fixture-password", true);
        var account = Assert.Single((await connection.ReadTfsaAccountsAsync()).Data);
        f.Clock.Now = f.Clock.Now.AddMinutes(29);
        Assert.Equal(WealthsimpleReadStatus.Success, (await connection.ReadTfsaSnapshotAsync(account)).Status);
        Assert.True(connection.IsConnected); Assert.Equal(2, store.Saves);
        await connection.StopAsync();
    }

    [Fact]
    public async Task PresentationMarksMissingFinancialValuesAndRetainsFractionalShares()
    {
        var source = new Source(); using var vm = new WealthsimpleAccountViewModel(source);
        Assert.Equal("—", vm.ValueText); Assert.Equal("Unavailable", vm.ReturnText);
        await vm.RefreshAsync(); Assert.Contains("CAD", vm.ValueText); Assert.Equal("Unavailable", vm.ReturnText);
        var row = new WealthsimpleHoldingRow(new("FIX", "Synthetic", "EQUITY", 0.00000001m, null, null, null));
        Assert.Equal(0.00000001m, row.Quantity); Assert.Equal("—", row.Value); Assert.Equal("—", row.UnrealizedGain);
    }
    [Fact]
    public async Task SellRowPinsWholeHoldingAndBecomesStaleOnRefreshAccountChangeOrDisconnect()
    {
        var source = new Source(); var a = source.Accounts[0];
        var h = new WealthsimpleHolding("FIX.TO","Fixture","EQUITY",20,new(200,"CAD"),null,null) { SecurityId="security-fixture" };
        var snapshot = Snapshot(a) with { Holdings = new[] { h } };
        source.Read = _ => Task.FromResult(new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Success,snapshot));
        using var vm = new WealthsimpleAccountViewModel(source); await vm.RefreshAsync();
        var row = Assert.Single(vm.Holdings); var selection=row.SellSelection;
        Assert.True(vm.IsCurrentSellSelection(selection)); Assert.True(selection.IsWholeHolding);
        Assert.Contains("all 20",row.SellAllToolTip); Assert.Same(row,Assert.Single(vm.Holdings));
        await vm.RefreshAsync(); Assert.False(vm.IsCurrentSellSelection(selection));
        var refreshed=Assert.Single(vm.Holdings).SellSelection; Assert.True(vm.IsCurrentSellSelection(refreshed));
        source.Disconnect(); Assert.False(vm.IsCurrentSellSelection(refreshed)); Assert.Empty(vm.Holdings);
        var fractional = new WealthsimpleHoldingRow(h with { Quantity=1.5m },snapshot with { Holdings = new[] { h with { Quantity=1.5m } } },1);
        Assert.False(fractional.SellSelection.IsWholeHolding); Assert.Contains("fractional",fractional.SellAllToolTip);
    }

    private sealed class Store : IWealthsimpleSessionStore
    {
        public int Saves, Clears;
        public void Open() { }
        public WealthsimpleAuthClient Load() => null;
        public Task SaveAsync(WealthsimpleAuthClient client, CancellationToken ct) { Saves++; return Task.CompletedTask; }
        public void Clear() => Clears++;
        public void Dispose() { }
    }
    private static WealthsimpleTfsaSnapshot Snapshot(WealthsimpleTfsaAccount a) => new(a, DateTimeOffset.UtcNow, new(100m, "CAD"), new(10m,"CAD"), new(9m,"CAD"), null, null, null, null, null, Array.Empty<WealthsimpleHolding>());
    private sealed class Source : IWealthsimpleAccountSource
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public bool IsConnected { get; private set; } = true;
        public bool HasSession => IsConnected;
        public long SessionRevision { get; private set; }
        public IReadOnlyList<WealthsimpleTfsaAccount> Accounts = new[] { new WealthsimpleTfsaAccount("tfsa", "TFSA") };
        public Func<WealthsimpleTfsaAccount, Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>>> Read = a => Task.FromResult(new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Success, Snapshot(a)));
        public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct) => Task.FromResult(new WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>(WealthsimpleReadStatus.Success, Accounts));
        public Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount a, CancellationToken ct) => Read(a);
        public Func<WealthsimpleTfsaAccount, Task<WealthsimpleReadResult<WealthsimplePendingOrders>>> ReadOrders = a =>
            Task.FromResult(new WealthsimpleReadResult<WealthsimplePendingOrders>(WealthsimpleReadStatus.Success, new(a, DateTimeOffset.UtcNow, [])));
        public Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount a, CancellationToken ct) =>
            ReadOrders(a);
        public void Disconnect() { IsConnected = false; SessionRevision++; PropertyChanged?.Invoke(this, new(null)); }
    }
    private static object Account(string id, string unified = "SELF_DIRECTED_TFSA", string branch = "TR", bool open = true) => new { id, type = "tfsa", branch, unifiedAccountType = unified, isOpen = open, archivedAt = (string)null, closedAt = (string)null, nickname = "TFSA", currency = "CAD" };
    private static object Page(bool more = false, string cursor = null) => new { hasNextPage = more, endCursor = cursor };
    private static object Money(string amount, string currency = "CAD") => new { amount, currency };
    private static object Positions(string owner = "tfsa-fixture-id", string identity = "identity-fixture", string status = "OK", int total = 1, bool more = false, string position = "p1", string quantity = "1.234567", string currency = "CAD") => new {
        data = new { identity = new { id = identity, financials = new { current = new { positions = new {
            edges = new[] { new { node = new { id = position, quantity, accounts = new[] { new { id = owner } },
                security = new { id = "security-fixture", securityType = "EQUITY", stock = new { symbol = "FIX", name = "Synthetic fixture" } },
                totalValue = Money("115.95", currency), bookValue = Money("110.00", currency), unrealizedReturns = Money("5.95", currency) } } },
            pageInfo = Page(more, "next"), totalCount = total, status = new[] { status }
        } } } } }
    };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2026, 9, 10, 3, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Fixture : HttpMessageHandler
    {
        internal readonly Clock Clock = new();
        internal WealthsimpleAuthClient Client { get; }
        internal List<string> Operations { get; } = new();
        internal bool Pages;
        internal Func<string, JsonElement, HttpResponseMessage> Override;
        internal Fixture() => Client = new("fixture-client", this, Clock);
        internal Task<WealthsimpleAuthResult> Login() => Client.BeginLoginAsync("fixture@example.invalid", "fixture-password");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/token")) return Json(new { access_token = "fixture-access", refresh_token = "fixture-refresh", token_type = "Bearer", created_at = Clock.Now.ToUnixTimeSeconds(), expires_in = 1800 });
            Assert.Equal("fixture-access", request.Headers.Authorization.Parameter);
            if (request.RequestUri.AbsolutePath.EndsWith("/info")) return Json(new { profiles = new { trade = new { @default = "fixture-profile" } }, identity_canonical_id = "identity-fixture" });
            Assert.Equal("https://my.wealthsimple.com/graphql", request.RequestUri.AbsoluteUri); Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("identity-fixture", Assert.Single(request.Headers.GetValues("x-ws-identity-id")));
            using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
            var op = json.RootElement.GetProperty("operationName").GetString(); Operations.Add(op);
            Assert.StartsWith("query ", json.RootElement.GetProperty("query").GetString());
            var vars = json.RootElement.GetProperty("variables");
            if (op == "TraderVITfsaPositions") {
                Assert.Equal("tfsa-fixture-id", Assert.Single(vars.GetProperty("accountIds").EnumerateArray()).GetString());
                Assert.Equal("CAD", vars.GetProperty("currency").GetString());
            }
            return Override?.Invoke(op, vars) ?? (op switch {
                "TraderVITfsaAccounts" => Json(new { data = new { identity = new { id = "identity-fixture", accounts = new {
                    edges = new[] { new { node = Account("tfsa-fixture-id") }, new { node = Account("managed", "MANAGED_PORTFOLIO_TFSA", "WS") }, new { node = Account("closed", open: false) } }, pageInfo = Page()
                } } } }),
                "TraderVITfsaFinancials" => Json(new { data = new { accounts = new[] { new { id = "tfsa-fixture-id", financials = new { currentCombined = new {
                    netLiquidationValueV2 = Money("123.45"), netDepositsV2 = Money("120"), simpleReturns = new { amount = Money("3.45"), rate = "0.02875", referenceDate = "2020-01-01", asOf = (string)null }
                } } } } } }),
                "TraderVITfsaBalances" => Json(new { data = new { account = new { id = "tfsa-fixture-id", financials = new { current = new { tradingBalanceViewV2 = new {
                    cash = new { quantity = "7.50", currency = "CAD" }, buyingPower = new { quantity = "5", currency = "CAD" }
                } } } } } }),
                "TraderVITfsaPositions" => Json(Positions(total: Pages ? 2 : 1, more: Pages && vars.GetProperty("cursor").ValueKind == JsonValueKind.Null,
                    position: vars.GetProperty("cursor").ValueKind == JsonValueKind.Null ? "p1" : "p2")),
                _ => throw new InvalidOperationException("Unknown fixture operation.")
            });
        }
    }
}

