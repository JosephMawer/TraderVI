using Core.WSTrade.Authentication;
using Core.WSTrade.Accounts;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class WealthsimpleAccountTests
{
    [Fact]
    public async Task DailyPerformanceRequestsCapturedNativeReturnsAndUsesLatestBrokerAmountAndRate()
    {
        using var f = new Fixture(); f.Clock.Now = DateTimeOffset.Parse("2026-09-10T15:00:00Z");
        await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data); f.Operations.Clear();
        f.Override = (op, vars) =>
        {
            Assert.Equal("TraderVITfsaDailyPerformance", op);
            Assert.Equal("tfsa-fixture-id", vars.GetProperty("id").GetString());
            Assert.Equal("ONE_DAY", vars.GetProperty("timeRange").GetString());
            Assert.Equal("REGULAR", vars.GetProperty("marketSession").GetString());
            Assert.Equal("CAD", vars.GetProperty("currency").GetString());
            Assert.True(vars.GetProperty("includeSimpleReturns").GetBoolean());
            return Json(PerformanceResponse());
        };
        var result = await f.Client.ReadTfsaDailyPerformanceAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status); Assert.Same(account, result.Data.Account);
        Assert.Equal(-37.25m, result.Data.Amount.Amount); Assert.Equal(-0.00372m, result.Data.Rate);
        Assert.Equal(DateTimeOffset.Parse("2026-09-10T13:35:00Z"), result.Data.AsOf);
        Assert.Equal(DateTimeOffset.Parse("2026-09-09T20:00:00Z"), result.Data.ReferenceAt);
        Assert.Equal(f.Clock.Now, result.Data.RetrievedAt);
        Assert.Single(f.Operations);
        Assert.DoesNotContain("37.25", result.Data.ToString()); Assert.DoesNotContain("tfsa-fixture-id", result.ToString());
    }

    [Theory]
    [InlineData("wrong-account")]
    [InlineData("wrong-currency")]
    [InlineData("missing-previous")]
    [InlineData("null-previous")]
    [InlineData("missing-points")]
    [InlineData("duplicate")]
    [InlineData("reversed")]
    [InlineData("future")]
    [InlineData("mixed-days")]
    [InlineData("missing-offset")]
    [InlineData("reference-offset")]
    [InlineData("invalid-time")]
    [InlineData("wrong-reference")]
    [InlineData("missing-reference")]
    [InlineData("reference-after-point")]
    [InlineData("same-day-reference")]
    [InlineData("invalid-rate")]
    [InlineData("invalid-amount")]
    [InlineData("partial-error")]
    public async Task DailyPerformanceRejectsAmbiguousScopeTimesReferenceOrMoney(string fault)
    {
        using var f = new Fixture(); f.Clock.Now = DateTimeOffset.Parse("2026-09-10T15:00:00Z");
        await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var response = PerformanceResponse(); var graph = DayGraph(response); var points = graph["data"].AsArray();
        var last = points[1]; var returns = last["simpleReturns"];
        switch (fault)
        {
            case "wrong-account": response["data"]["account"]["id"] = "other"; break;
            case "wrong-currency": returns["amount"]["currency"] = "USD"; break;
            case "missing-previous": graph.Remove("previousClose"); break;
            case "null-previous": graph["previousClose"] = null; break;
            case "missing-points": graph.Remove("data"); break;
            case "duplicate": points.Add(last.DeepClone()); break;
            case "reversed": last["dateTime"] = "2026-09-10T13:29:00Z"; break;
            case "future": last["dateTime"] = "2026-09-10T16:00:00Z"; break;
            case "mixed-days": points[0]["dateTime"] = "2026-09-09T20:01:00Z"; break;
            case "missing-offset": last["dateTime"] = "2026-09-10T13:35:00"; break;
            case "reference-offset": returns["referenceDateTime"] = "2026-09-09T20:00:00"; break;
            case "invalid-time": last["dateTime"] = "invalidZ"; break;
            case "wrong-reference": returns["referenceDateTime"] = "2026-09-08T20:00:00Z"; break;
            case "missing-reference": returns.AsObject().Remove("referenceDateTime"); break;
            case "reference-after-point": graph["previousClose"]["dateTime"] = "2026-09-10T14:00:00Z"; break;
            case "same-day-reference":
                graph["previousClose"]["dateTime"] = "2026-09-10T13:00:00Z";
                foreach (var p in points) p["simpleReturns"]["referenceDateTime"] = "2026-09-10T13:00:00Z";
                break;
            case "invalid-rate": returns["rate"] = "NaN"; break;
            case "invalid-amount": returns["amount"]["amount"] = "bad"; break;
            case "partial-error": response["errors"] = new JsonArray(new JsonObject { ["message"] = "private response" }); break;
        }
        f.Override = (_, _) => Json(response);
        var result = await f.Client.ReadTfsaDailyPerformanceAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status); Assert.Null(result.Data);
        Assert.DoesNotContain("private response", result.ToString());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null-return")]
    [InlineData("missing-return")]
    [InlineData("null-money")]
    [InlineData("null-rate")]
    [InlineData("zero")]
    public async Task LatestMissingReturnNeverFallsBackAndZeroIsARealValue(string condition)
    {
        using var f = new Fixture(); f.Clock.Now = DateTimeOffset.Parse("2026-09-10T15:00:00Z");
        await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var response = PerformanceResponse(); var points = DayGraph(response)["data"].AsArray(); var last = points[1];
        switch (condition)
        {
            case "empty": points.Clear(); break;
            case "null-return": last["simpleReturns"] = null; break;
            case "missing-return": last.AsObject().Remove("simpleReturns"); break;
            case "null-money": last["simpleReturns"]["amount"]["amount"] = null; break;
            case "null-rate": last["simpleReturns"]["rate"] = null; break;
            case "zero": last["simpleReturns"]["amount"]["amount"] = "0"; last["simpleReturns"]["rate"] = "0"; break;
        }
        f.Override = (_, _) => Json(response);
        var result = await f.Client.ReadTfsaDailyPerformanceAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Equal(condition == "zero" ? 0m : condition == "null-rate" ? -37.25m : (decimal?)null, result.Data.Amount?.Amount);
        Assert.Equal(condition == "zero" ? 0m : condition == "null-money" ? -0.00372m : (decimal?)null, result.Data.Rate);
        Assert.Equal(condition == "empty" ? null : DateTimeOffset.Parse("2026-09-10T13:35:00Z"), result.Data.AsOf);
    }

    [Fact]
    public async Task DailyReadRequiresOwnedAccountAndHonorsCancellation()
    {
        using var f = new Fixture(); var invented = new WealthsimpleTfsaAccount("tfsa-fixture-id", "TFSA");
        Assert.Equal(WealthsimpleReadStatus.NotAuthenticated, (await f.Client.ReadTfsaDailyPerformanceAsync(invented)).Status);
        await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data); f.Operations.Clear();
        Assert.Equal(WealthsimpleReadStatus.AccountUnavailable, (await f.Client.ReadTfsaDailyPerformanceAsync(invented)).Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Client.ReadTfsaDailyPerformanceAsync(account, new CancellationToken(true)));
        Assert.Empty(f.Operations);
    }

    [Theory]
    [InlineData(401, WealthsimpleReadStatus.NotAuthenticated)]
    [InlineData(429, WealthsimpleReadStatus.RateLimited)]
    [InlineData(503, WealthsimpleReadStatus.Unavailable)]
    public async Task FailedDailyReadDoesNotBecomeSuccessfulZero(int code, WealthsimpleReadStatus expected)
    {
        using var f = new Fixture(); await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Override = (_, _) => new HttpResponseMessage((HttpStatusCode)code);
        var result = await f.Client.ReadTfsaDailyPerformanceAsync(account);
        Assert.Equal(expected, result.Status); Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("2026-09-10T23:00:00Z", true)]
    [InlineData("2026-09-11T02:00:00Z", true)] // still Thursday in Toronto
    [InlineData("2026-09-13T15:00:00Z", false)] // weekend retains Thursday's explicit date
    public async Task UiUsesTorontoDateAndNativeFractionalRateWhileOverallStaysSeparate(string now, bool today)
    {
        var clock = new Clock { Now = DateTimeOffset.Parse(now) }; var source = new Source();
        source.ReadPerformance = a => Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Success,
            Daily(a, clock.Now)));
        using var vm = new WealthsimpleAccountViewModel(source, clock); await vm.RefreshAsync();
        Assert.Contains(today ? "Today's" : new DateOnly(2026, 9, 10).ToString("MMM d, yyyy"), vm.PerformanceLabel);
        Assert.Equal("-37.25 CAD", vm.PerformanceAmount); Assert.Equal("-0.37%", vm.PerformanceRate);
        Assert.Contains("Toronto", vm.PerformanceContext); Assert.Contains("since", vm.PerformanceContext);
        clock.Now += TimeSpan.FromMinutes(6); Assert.Contains("Refresh overdue", vm.PerformanceContext);
        vm.ShowOverallPerformance = true; Assert.Equal("Unavailable", vm.PerformanceAmount); Assert.Equal("", vm.PerformanceRate);
        vm.ShowOverallPerformance = false; Assert.Equal("-37.25 CAD", vm.PerformanceAmount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DailyFailureDoesNotEraseBalancesAndDoesNotReusePreviousDailyReturn(bool throws)
    {
        var source = new Source(); source.ReadPerformance = a => Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Success, Daily(a, DateTimeOffset.UtcNow)));
        using var vm = new WealthsimpleAccountViewModel(source); await vm.RefreshAsync(); Assert.Equal("-37.25 CAD", vm.PerformanceAmount);
        source.ReadPerformance = _ => throws ? throw new InvalidOperationException("private error") :
            Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Unavailable));
        await vm.RefreshAsync(); Assert.NotNull(vm.Snapshot); Assert.NotNull(vm.PendingOrders);
        Assert.Equal("Unavailable", vm.PerformanceAmount); Assert.Equal("", vm.PerformanceRate);
        Assert.DoesNotContain("private error", vm.PerformanceContext);
    }

    [Fact]
    public async Task DailyReadNeverPublishesAcrossDisconnectOrWrongAccount()
    {
        var source = new Source(); var pending = new TaskCompletionSource<WealthsimpleReadResult<WealthsimpleDailyPerformance>>();
        source.ReadPerformance = _ => pending.Task;
        using var vm = new WealthsimpleAccountViewModel(source); var refresh = vm.RefreshAsync();
        source.Disconnect(); pending.SetResult(new(WealthsimpleReadStatus.Success, Daily(source.Accounts[0], DateTimeOffset.UtcNow)));
        await refresh; Assert.Null(vm.Snapshot); Assert.Equal("Unavailable", vm.PerformanceAmount);
        var otherSource = new Source(); otherSource.ReadPerformance = _ => Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(
            WealthsimpleReadStatus.Success, Daily(new("other", "Other"), DateTimeOffset.UtcNow)));
        using var other = new WealthsimpleAccountViewModel(otherSource); await other.RefreshAsync();
        Assert.NotNull(other.Snapshot); Assert.Equal("Unavailable", other.PerformanceAmount);
    }

    [Fact]
    public async Task SynchronizedAccountAlsoReadsDailyPerformanceWithoutRereadingSnapshotOrOrders()
    {
        var source = new Source(); var a = source.Accounts[0]; int dailyReads = 0;
        source.Read = _ => throw new InvalidOperationException("Snapshot must come from synchronization");
        source.ReadOrders = _ => throw new InvalidOperationException("Orders must come from synchronization");
        source.ReadPerformance = account =>
        {
            dailyReads++; Assert.Same(a, account);
            return Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Success, Daily(a, DateTimeOffset.UtcNow)));
        };
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (account, revision, ct) =>
            Task.FromResult(new BrokerAccountSynchronization(new(null, Snapshot(account), new(account, DateTimeOffset.UtcNow, [])), null, [])));
        await vm.RefreshAsync(); Assert.NotNull(vm.Snapshot); Assert.NotNull(vm.PendingOrders);
        Assert.Equal(1, dailyReads); Assert.Equal("-37.25 CAD", vm.PerformanceAmount);
    }

    [Fact]
    public async Task UiClearsDailyReturnOnAccountChoiceAndSnapshotReadFailure()
    {
        var source = new Source { Accounts = [new("a", "A"), new("b", "B")] };
        source.ReadPerformance = a => Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Success, Daily(a, DateTimeOffset.UtcNow)));
        using var vm = new WealthsimpleAccountViewModel(source); await vm.RefreshAsync();
        vm.SelectedAccount = source.Accounts[0]; Assert.Equal("-37.25 CAD", vm.PerformanceAmount);
        source.ReadPerformance = _ => Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Unavailable));
        vm.SelectedAccount = source.Accounts[1]; Assert.Equal("Unavailable", vm.PerformanceAmount);
        source.ReadPerformance = a => Task.FromResult(new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Success, Daily(a, DateTimeOffset.UtcNow)));
        await vm.RefreshAsync(); Assert.Equal("-37.25 CAD", vm.PerformanceAmount);
        source.Read = _ => Task.FromResult(new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Unavailable));
        await vm.RefreshAsync(); Assert.NotNull(vm.Snapshot); Assert.Equal("Unavailable", vm.PerformanceAmount);
    }

    [Fact]
    public async Task DesktopConnectionUsesItsExistingAuthenticatedClientForDailyReturn()
    {
        using var f = new Fixture(); f.Clock.Now = DateTimeOffset.Parse("2026-09-10T15:00:00Z");
        var connection = new WealthsimpleConnection(new Store(), f.Clock, () => f.Client);
        await connection.SignInAsync("fixture@example.invalid", "fixture-password", false);
        var account = Assert.Single((await connection.ReadTfsaAccountsAsync()).Data);
        f.Override = (op, _) => { Assert.Equal("TraderVITfsaDailyPerformance", op); return Json(PerformanceResponse()); };
        var result = await connection.ReadTfsaDailyPerformanceAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status); Assert.Equal(-37.25m, result.Data.Amount.Amount);
        await connection.StopAsync();
    }

    private static WealthsimpleDailyPerformance Daily(WealthsimpleTfsaAccount a, DateTimeOffset retrievedAt) =>
        new(a, retrievedAt, DateTimeOffset.Parse("2026-09-10T13:35:00Z"), DateTimeOffset.Parse("2026-09-09T20:00:00Z"), new(-37.25m, "CAD"), -0.00372m);

    // Synthetic envelope, deliberately unrelated to sampled value differences or net deposits.
    private static JsonObject PerformanceResponse()
    {
        var response = DayValuesResponse();
        foreach (var point in DayGraph(response)["data"].AsArray())
            point["simpleReturns"] = new JsonObject { ["amount"] = new JsonObject { ["amount"] = "-37.25", ["currency"] = "CAD" },
                ["rate"] = "-0.00372", ["referenceDateTime"] = "2026-09-09T20:00:00Z" };
        return response;
    }
}
