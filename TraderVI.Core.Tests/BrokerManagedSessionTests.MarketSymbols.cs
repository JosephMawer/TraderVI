#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData("valid")][InlineData("missing-listing")][InlineData("wrong-exchange")]
    [InlineData("wrong-security")][InlineData("wrong-symbol")][InlineData("duplicate")]
    public async Task ManagedMarketNameRequiresOriginalPurchaseAndMatchingListing(string evidence)
    {
        var x = await OngoingFixture(); var state = await x.Workflow().ApproveAsync(x.Review);
        var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "fixture-security", "FIX", "BUY", 2, 10, "Initial", null, x.F.Now);
        var fact = new BrokerOrderFact(order.ExternalId, order.SecurityId, "FIX.TO", "BUY", "FILLED", "CAD", "EQUITY", 2, 2, 20, 20, 0, 0, x.F.Now, x.F.Now);
        order = order with { Latest = fact, Reconciled = true };
        var holding = new BrokerPositionCycle(Guid.NewGuid(), order.SecurityId, "FIX.TO", 2, 20, x.F.Now, order.ExternalId, null);
        var listing = new BrokerInstrumentIdentity(evidence == "wrong-security" ? "other" : order.SecurityId,
            evidence == "wrong-symbol" ? "OTHER.TO" : "FIX.TO", evidence == "wrong-exchange" ? "XTSX" : "XTSE", "CAD", "EQUITY");
        var observation = state.Account.Import.Plan.Observation with { Instruments = evidence switch {
            "missing-listing" => [], "duplicate" => [listing, listing], _ => [listing] } };
        var account = state.Account with { Positions = [holding], Import = state.Account.Import with {
            Plan = state.Account.Import.Plan with { Observation = observation } } };
        state = state with { Account = account, Orders = [order] };
        if (evidence != "valid")
        {
            Assert.Throws<InvalidOperationException>(() => BrokerMarketSymbols.From(account, [order]));
            return;
        }
        var original = DelphiLiveLedgerJson.Serialize(state);
        Assert.Equal("FIX", BrokerMarketSymbols.From(account, [order])["FIX.TO"]);
        var subscription = EngineRealObservation.From(state);
        Assert.Equal("FIX", Assert.Single(subscription.Holdings).Symbol);
        Assert.Equal(holding.PositionId, Assert.Single(subscription.Holdings).OwnerRecordId);
        subscription.RequireMatches(state);
        var legacy = subscription with { Holdings = [new("FIX.TO", "BrokerManaged", holding.PositionId, false)] };
        legacy.RequireMatches(state);
        Assert.Equal(original, DelphiLiveLedgerJson.Serialize(state));
        Assert.Equal("FIX.TO", BrokerMarketSymbols.From(account, [])["FIX.TO"]);
    }
}
