using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class WealthsimpleAccountTests
{
    [Theory]
    [InlineData("2026-09-10T09:30:00-04:00", "2026-09-10T13:30:00Z")]
    [InlineData("2026-11-02T09:30:00-05:00", "2026-11-02T14:30:00Z")]
    public async Task DayValuesRecoverExactDatedPointAfterLateStartWithoutUsingPreviousClose(string opening, string utc)
    {
        using var f = new Fixture();
        f.Clock.Now = DateTimeOffset.Parse(utc).AddHours(1);
        await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Operations.Clear();
        f.Override = (op, vars) =>
        {
            Assert.Equal("TraderVITfsaDayValues", op);
            Assert.Equal("tfsa-fixture-id", vars.GetProperty("id").GetString());
            Assert.Equal("CAD", vars.GetProperty("currency").GetString());
            Assert.Equal("ONE_DAY", vars.GetProperty("timeRange").GetString());
            Assert.Equal("REGULAR", vars.GetProperty("marketSession").GetString());
            return Json(DayValuesResponse(opening));
        };
        var result = await f.Client.ReadTfsaDayValuesAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Same(account, result.Data.Account);
        Assert.Equal(f.Clock.Now, result.Data.RetrievedAt);
        var exact = result.Data.FindExact(DateTimeOffset.Parse(utc));
        Assert.Equal(10000m, exact.Value);
        Assert.Equal(8000m, exact.NetDeposits);
        Assert.Equal(TimeSpan.Zero, exact.At.Offset);
        Assert.Equal(9900m, result.Data.PreviousClose.Value);
        Assert.Null(result.Data.PreviousClose.NetDeposits);
        Assert.Null(result.Data.FindExact(exact.At.AddDays(-1)));
        Assert.Null(result.Data.FindExact(exact.At.AddMinutes(1)));
        Assert.Null(result.Data.FindExact(result.Data.PreviousClose.At));
        Assert.Equal("TraderVITfsaDayValues", Assert.Single(f.Operations));
        Assert.DoesNotContain("10000", exact.ToString());
        Assert.DoesNotContain("tfsa-fixture-id", result.Data.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOpeningRemainsMissingEvenWhenOtherValuesAreAvailable(bool empty)
    {
        using var f = new Fixture(); f.Clock.Now = DateTimeOffset.Parse("2026-09-10T15:00:00Z");
        await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var response = DayValuesResponse(); var data = DayGraph(response)["data"].AsArray();
        if (empty) data.Clear(); else data.RemoveAt(0);
        f.Override = (_, _) => Json(response);
        var result = await f.Client.ReadTfsaDayValuesAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.NotNull(result.Data.PreviousClose);
        Assert.Null(result.Data.FindExact(DateTimeOffset.Parse("2026-09-10T13:30:00Z")));
    }

    [Theory]
    [InlineData("wrong-account")]
    [InlineData("wrong-currency")]
    [InlineData("deposit-currency")]
    [InlineData("missing-deposits")]
    [InlineData("missing-value")]
    [InlineData("negative-value")]
    [InlineData("missing-previous")]
    [InlineData("previous-after-opening")]
    [InlineData("missing-points")]
    [InlineData("duplicate-time")]
    [InlineData("reversed-time")]
    [InlineData("future-time")]
    [InlineData("missing-offset")]
    [InlineData("invalid-time")]
    [InlineData("null-time")]
    [InlineData("partial-error")]
    public async Task DayValuesRejectAmbiguousOrInvalidEvidence(string fault)
    {
        using var f = new Fixture(); f.Clock.Now = DateTimeOffset.Parse("2026-09-10T15:00:00Z");
        await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var response = DayValuesResponse(); var graph = DayGraph(response); var points = graph["data"].AsArray();
        var first = points[0];
        switch (fault)
        {
            case "wrong-account": response["data"]["account"]["id"] = "other-account"; break;
            case "wrong-currency": first["netLiquidationValue"]["currency"] = "USD"; break;
            case "deposit-currency": first["netDeposits"]["currency"] = "USD"; break;
            case "missing-deposits": first.AsObject().Remove("netDeposits"); break;
            case "missing-value": first["netLiquidationValue"]["amount"] = null; break;
            case "negative-value": first["netLiquidationValue"]["amount"] = "-1"; break;
            case "missing-previous": graph.Remove("previousClose"); break;
            case "previous-after-opening": graph["previousClose"]["dateTime"] = "2026-09-10T13:31:00Z"; break;
            case "missing-points": graph.Remove("data"); break;
            case "duplicate-time": points.Add(first.DeepClone()); break;
            case "reversed-time": first["dateTime"] = "2026-09-10T14:00:00Z"; break;
            case "future-time": points[1]["dateTime"] = "2026-09-10T16:00:00Z"; break;
            case "missing-offset": first["dateTime"] = "2026-09-10T13:30:00"; break;
            case "invalid-time": first["dateTime"] = "not-a-timeZ"; break;
            case "null-time": first["dateTime"] = null; break;
            case "partial-error": response["errors"] = new JsonArray(new JsonObject { ["message"] = "private response" }); break;
        }
        f.Override = (_, _) => Json(response);
        var result = await f.Client.ReadTfsaDayValuesAsync(account);
        Assert.Equal(WealthsimpleReadStatus.UnsupportedResponse, result.Status);
        Assert.Null(result.Data);
        Assert.DoesNotContain("private response", result.ToString());
    }

    [Fact]
    public async Task EmptyAccountAndNetWithdrawalsRemainValidEvidence()
    {
        using var f = new Fixture(); f.Clock.Now = DateTimeOffset.Parse("2026-09-10T15:00:00Z");
        await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        var response = DayValuesResponse(); var graph = DayGraph(response);
        graph["previousClose"] = null;
        foreach (var point in graph["data"].AsArray())
        { point["netLiquidationValue"]["amount"] = "0"; point["netDeposits"]["amount"] = "-25"; }
        f.Override = (_, _) => Json(response);
        var result = await f.Client.ReadTfsaDayValuesAsync(account);
        Assert.Equal(WealthsimpleReadStatus.Success, result.Status);
        Assert.Null(result.Data.PreviousClose);
        Assert.All(result.Data.Points, p => { Assert.Equal(0m, p.Value); Assert.Equal(-25m, p.NetDeposits); });
    }

    [Fact]
    public async Task DayValuesRequireAuthenticatedSelectedAccountAndHonorCancellation()
    {
        using var f = new Fixture();
        var invented = new WealthsimpleTfsaAccount("tfsa-fixture-id", "TFSA");
        Assert.Equal(WealthsimpleReadStatus.NotAuthenticated, (await f.Client.ReadTfsaDayValuesAsync(invented)).Status);
        await f.Login(); var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data); f.Operations.Clear();
        Assert.Equal(WealthsimpleReadStatus.AccountUnavailable, (await f.Client.ReadTfsaDayValuesAsync(invented)).Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Client.ReadTfsaDayValuesAsync(account, new CancellationToken(true)));
        Assert.Empty(f.Operations);
    }

    [Theory]
    [InlineData(401, WealthsimpleReadStatus.NotAuthenticated)]
    [InlineData(429, WealthsimpleReadStatus.RateLimited)]
    [InlineData(503, WealthsimpleReadStatus.Unavailable)]
    public async Task DayValueReadFailureNeverBecomesAnEmptySuccessfulHistory(int status, WealthsimpleReadStatus expected)
    {
        using var f = new Fixture(); await f.Login();
        var account = Assert.Single((await f.Client.ReadTfsaAccountsAsync()).Data);
        f.Override = (_, _) => new HttpResponseMessage((HttpStatusCode)status);
        var result = await f.Client.ReadTfsaDayValuesAsync(account);
        Assert.Equal(expected, result.Status); Assert.Null(result.Data);
    }

    // Invented identities, values and dates in the captured GraphQL envelope; no private capture data.
    private static JsonObject DayValuesResponse(string opening = "2026-09-10T13:30:00Z")
    {
        var at = DateTimeOffset.Parse(opening);
        return System.Text.Json.JsonSerializer.SerializeToNode(new { data = new { account = new {
            id = "tfsa-fixture-id", financials = new { currentCombined = new { graphData = new {
                previousClose = new { dateTime = at.AddDays(-1).AddHours(6.5).ToString("O"), netLiquidationValue = Money("9900") },
                data = new[] {
                    new { dateTime = opening, netLiquidationValue = Money("10000"), netDeposits = Money("8000") },
                    new { dateTime = at.AddMinutes(5).ToString("O"), netLiquidationValue = Money("9975"), netDeposits = Money("8000") }
                }
            } } }
        } } }).AsObject();
    }

    private static JsonObject DayGraph(JsonObject response) => response["data"]["account"]["financials"]["currentCombined"]["graphData"].AsObject();
}
