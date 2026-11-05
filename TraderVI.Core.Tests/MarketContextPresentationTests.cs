#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Core.WSTrade.Orders;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Fact]
    public async Task SectorDisplayUsesExistingVerifiedPurchaseMappingWithoutAlias()
    {
        var fixture = await NewMappedBuy();
        var session = fixture.Next;
        var held = Assert.Single(session.Account.Positions);
        var selected = new ManualSellSelection(new("fixture", "TFSA"), held.SecurityId, held.Symbol,
            held.Quantity, session.SavedAt, 1);
        var metadata = HoldingSectorInfo.From([new("ABC", "ABC", "Materials", "Mining", "^TTMT", DateTime.UtcNow)]);
        Assert.Equal("Materials", HoldingSectorInfo.Resolve(held.Symbol, selected, session, metadata).Sector);
        Assert.Equal("Unknown", HoldingSectorInfo.Resolve(held.Symbol, selected, null, metadata).Sector);
        Assert.Equal("Unknown", HoldingSectorInfo.Resolve(held.Symbol, selected with { SecurityId = "different" }, session, metadata).Sector);
        Assert.Equal("Unknown", HoldingSectorInfo.Resolve(held.Symbol, selected with { Quantity = 1000 }, session, metadata).Sector);
        var damaged = session with { Orders = [fixture.Buy with { Latest = fixture.Buy.Latest! with { SecurityId = "different" } }] };
        Assert.Equal("Unknown", HoldingSectorInfo.Resolve(held.Symbol, selected, damaged, metadata).Sector);
        var conflicting = HoldingSectorInfo.From([
            new("ABC", "ABC", "Materials", "Mining", "^TTMT", DateTime.UtcNow),
            new("ABC.TO", "OTHER", "Energy", "Oil", "^TTEN", DateTime.UtcNow)]);
        Assert.Equal("Unknown", HoldingSectorInfo.Resolve(held.Symbol, selected, session, conflicting).Sector);
    }
}

public sealed class MarketContextPresentationTests
{
    private const string Partial = """
        {"data":{"quotes":[{"symbol":"^TSX","percentChange":0.42},{"symbol":"^TTEN","percentChange":null},
        {"symbol":"^TTMT","percentChange":-1.2}],"breadth":[
        {"exchange":"TSXV","advancers":999,"decliners":999,"unchanged":999},
        {"exchange":"TSX","advancers":617,"decliners":402,"unchanged":81}]}}
        """;

    [Fact]
    public async Task SnapshotPreservesMissingValuesUsesTsxBreadthAndLoadsOnlyOnce()
    {
        int reads = 0;
        var at = new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero);
        using var vm = new MarketContextViewModel(_ => { reads++; return Task.FromResult(MarketContextSnapshot.Parse(Partial, at)); });
        await vm.LoadOnceAsync(); await vm.LoadOnceAsync();
        Assert.Equal(1, reads);
        Assert.Equal("+0.42%", vm.Composite.ChangeText);
        Assert.Equal("Unavailable", vm.Tsx60.ChangeText);
        Assert.Equal("Unavailable", vm.Sectors.Single(s => s.Name == "Energy").ChangeText);
        Assert.Equal("-1.20%", vm.Sectors.Single(s => s.Name == "Materials").ChangeText);
        Assert.Contains("617", vm.Breadth); Assert.DoesNotContain("999", vm.Breadth);
        Assert.Contains("Partial", vm.Status);
        Assert.Contains("2026 17:00:00 Toronto", vm.Timing);
        Assert.Contains("unverified", vm.Timing);
        Assert.Equal(11, vm.Sectors.Count);
    }

    [Theory]
    [InlineData("{\"errors\":[{\"message\":\"fixture\"}],\"data\":{}}")]
    [InlineData("{\"data\":{\"quotes\":[{\"symbol\":\"^TSX\",\"percentChange\":1},{\"symbol\":\"^TSX\",\"percentChange\":2}]}}")]
    [InlineData("{\"data\":{\"breadth\":[{\"exchange\":\"TSX\"},{\"exchange\":\"TSX\"}]}}")]
    public void ErrorsAndAmbiguousRowsAreRejected(string json) =>
        Assert.Throws<InvalidOperationException>(() => MarketContextSnapshot.Parse(json, DateTimeOffset.UtcNow));

    [Fact]
    public async Task FailureDoesNotFabricateDataAndLateReadCannotPublishAfterClose()
    {
        using var failed = new MarketContextViewModel(_ => throw new Exception("private provider failure"));
        await failed.LoadOnceAsync();
        Assert.Equal("Market snapshot unavailable", failed.Status);
        Assert.All(failed.Sectors, row => Assert.Null(row.Change));
        Assert.DoesNotContain("private", failed.Status);
        var completion = new TaskCompletionSource<MarketContextSnapshot>();
        var closed = new MarketContextViewModel(_ => completion.Task);
        var load = closed.LoadOnceAsync();
        closed.Dispose();
        completion.SetResult(MarketContextSnapshot.Parse(Partial, DateTimeOffset.UtcNow));
        await load;
        Assert.Null(closed.Composite.Change);
    }
}
