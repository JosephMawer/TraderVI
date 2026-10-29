using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class DelphiLiveHoldingSymbolsTests
{
    private static readonly HoldingSymbolAlias Mapping = new("Broker", "FIX.TO", "FIX");
    private static DelphiLiveObservedHolding Holding(string symbol, string owner = "Real", bool mayAct = false)
        => new(symbol, owner, Guid.NewGuid(), mayAct);

    [Fact]
    public void BrokerAliasChangesOnlyObservationSymbolAndRetainsEveryOwner()
    {
        var real = Holding(" fix.to ");
        var shadow = Holding("FIX", "SystemShadow");
        var live = Holding("FIX", "DelphiLiveShadow", true);
        var result = DelphiLiveHoldingSymbols.Resolve([real, shadow, live], ["FIX"], [Mapping]);
        Assert.Equal(3, result.Count);
        Assert.Equal(real with { Symbol = "FIX" }, result[0]);
        Assert.Equal(shadow, result[1]);
        Assert.Equal(live, result[2]);
        Assert.Equal(" fix.to ", real.Symbol);
        var normalized = DelphiLiveSessionRepository.NormalizeObservedHoldings(result);
        Assert.False(normalized[0].DelphiLiveMayAct);
        Assert.True(normalized[2].DelphiLiveMayAct);
    }

    [Theory]
    [InlineData("BrokerManaged")]
    [InlineData("BrokerPending")]
    public void IndependentBrokerOwnersUseReviewedAliasesWithoutChangingOwnership(string owner)
    {
        var holding = Holding("FIX.TO", owner);
        Assert.Equal(holding with { Symbol = "FIX" }, Assert.Single(DelphiLiveHoldingSymbols.Resolve([holding], ["FIX"], [Mapping])));
    }

    [Fact]
    public void CanonicalHoldingNeedsNoAlias()
    {
        var holding = Holding("FIX");
        Assert.Equal(holding, Assert.Single(DelphiLiveHoldingSymbols.Resolve([holding], ["FIX"], [])));
    }

    [Theory]
    [InlineData("FIX.TO", "Real")]
    [InlineData("FIX.V", "Real")]
    [InlineData("FIX.TO", "SystemShadow")]
    [InlineData("FIX.TO", "DelphiLiveShadow")]
    public void MissingOrWrongSourceMappingDoesNotGuessOrDropHolding(string symbol, string owner)
    {
        var aliases = owner == "Real" ? new[] { Mapping with { Source = "Other" } } : [Mapping];
        var error = Assert.Throws<InvalidOperationException>(() =>
            DelphiLiveHoldingSymbols.Resolve([Holding(symbol, owner)], ["FIX"], aliases));
        Assert.Contains("cannot identify", error.Message);
    }

    [Fact]
    public void AliasMustLeadDirectlyToExistingCanonicalSymbol()
    {
        Assert.Throws<InvalidOperationException>(() => DelphiLiveHoldingSymbols.Resolve(
            [Holding("FIX.TO")], ["OTHER"], [Mapping, new("Broker", "FIX", "OTHER")]));
    }

    [Fact]
    public void MultipleMappingsCannotChooseAnArbitraryStock()
    {
        Assert.Throws<InvalidOperationException>(() => DelphiLiveHoldingSymbols.Resolve(
            [Holding("FIX.TO")], ["FIX", "OTHER"], [Mapping, Mapping with { Symbol = "OTHER" }]));
    }

    [Fact]
    public void AliasCannotHideAnExistingDifferentCanonicalStock()
    {
        Assert.Throws<InvalidOperationException>(() => DelphiLiveHoldingSymbols.Resolve(
            [Holding("FIX.TO")], ["FIX", "FIX.TO"], [Mapping]));
    }

    [Theory]
    [InlineData("alias-conflict")][InlineData("canonical-conflict")][InlineData("ambiguous-owner")]
    public void ManagedProofCannotOverrideConflictingReferences(string conflict)
    {
        var holding = Holding("FIX.TO");
        var proof = new TrackedHoldingMarketSymbol(holding.OwnerRecordId, holding.Symbol, "FIX");
        Assert.Throws<InvalidOperationException>(() => DelphiLiveHoldingSymbols.Resolve([holding],
            conflict == "canonical-conflict" ? ["FIX", "FIX.TO", "OTHER"] : ["FIX", "OTHER"],
            conflict == "alias-conflict" ? [Mapping with { Symbol = "OTHER" }] : [],
            conflict == "ambiguous-owner" ? [proof, proof] : [proof]));
    }

    [Theory]
    [InlineData("FIX")][InlineData("FIX.TO")]
    public async Task AlreadyResolvedReferencesDoNotReadBrokerArchives(string name)
    {
        var holding = Holding(name);
        var observation = new EngineRealObservation(Guid.NewGuid(), Guid.NewGuid(), 1, null!, []);
        var result = await DelphiLiveHoldingSource.ResolveSymbolsAsync([holding], ["FIX"], [Mapping], [observation],
            _ => throw new Exception("Unexpected broker archive read"));
        Assert.Equal(holding with { Symbol = "FIX" }, Assert.Single(result));
    }
}
