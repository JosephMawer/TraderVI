#nullable enable
using Core.Runtime;
using Core.Db;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
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

        var tracked = new DelphiLiveObservedHolding("FIX.TO", "Real",
            BrokerTradeProjector.TrackedPositionId(account.Import.Plan.BrokerKey, order.ExternalId), false);
        int reads = 0;
        var combined = await DelphiLiveHoldingSource.ResolveSymbolsAsync([subscription.Holdings[0], tracked], ["FIX"], [], [subscription],
            expected => {
                reads++;
                expected.RequireMatches(state);
                return Task.FromResult(BrokerMarketSymbols.ForTrackedHoldings(state));
            });
        Assert.Equal(1, reads);
        Assert.Equal(subscription.Holdings[0], combined[0]);
        Assert.Equal(tracked with { Symbol = "FIX" }, combined[1]);
        Assert.Equal(original, DelphiLiveLedgerJson.Serialize(state));
        Assert.Empty(BrokerMarketSymbols.ForTrackedHoldings(state with { Orders = [] }));
        Assert.Empty(BrokerMarketSymbols.ForTrackedHoldings(state with { Account = account with {
            Positions = [holding with { LegacyPositionId = tracked.OwnerRecordId }] } }));
    }

    [Theory]
    [InlineData("valid")][InlineData("wrong-position")][InlineData("wrong-broker-symbol")]
    [InlineData("unreconciled")][InlineData("missing-listing")][InlineData("wrong-exchange")]
    [InlineData("missing-canonical")][InlineData("changed-head")][InlineData("wrong-owner")]
    public async Task SuccessiveManagedPurchasesResolveTrackedReferencesWithoutAliasWrites(string evidence)
    {
        var x = await OngoingFixture(); var state = await x.Workflow().ApproveAsync(x.Review);
        var tracked = new List<DelphiLiveObservedHolding>();
        var marketNames = new List<string>();
        state = state with { Account = state.Account with { Positions = [] }, Orders = [] };
        foreach (var name in new[] { "NEW1", "NEW2", "NEW3" })
        {
            var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-" + name,
                name, "BUY", 2, 10, "Initial", null, x.F.Now);
            order = order with { Reconciled = true, Latest = new(order.ExternalId, order.SecurityId, name + ".TO", "BUY",
                "FILLED", "CAD", "EQUITY", 2, 2, 20, 20, 0, 0, x.F.Now, x.F.Now) };
            var holding = new BrokerPositionCycle(Guid.NewGuid(), order.SecurityId, name + ".TO", 2, 20,
                x.F.Now, order.ExternalId, null);
            var observation = state.Account.Import.Plan.Observation;
            observation = observation with { Instruments = (observation.Instruments ?? []).Append(
                new BrokerInstrumentIdentity(order.SecurityId, name + ".TO", "XTSE", "CAD", "EQUITY")).ToArray() };
            state = state with { Revision = state.Revision + 1, Orders = state.Orders.Add(order), Account = state.Account with {
                Positions = state.Account.Positions.Add(holding), Import = state.Account.Import with {
                    Plan = state.Account.Import.Plan with { Observation = observation } } } };
            tracked.Add(new(name + ".TO", "Real", BrokerTradeProjector.TrackedPositionId(
                state.Account.Import.Plan.BrokerKey, order.ExternalId), false));
            marketNames.Add(name);
            var subscription = EngineRealObservation.From(state);
            var untouched = DelphiLiveLedgerJson.Serialize(state);
            IReadOnlyList<DelphiLiveObservedHolding> input = tracked.ToArray();
            var readState = state;
            var canonical = marketNames.ToArray();
            if (evidence == "wrong-position") input = input.Select(h => h with { OwnerRecordId = Guid.NewGuid() }).ToArray();
            if (evidence == "wrong-broker-symbol") input = input.Select(h => h with { Symbol = "OTHER.TO" }).ToArray();
            if (evidence == "wrong-owner") input = input.Select(h => h with { Owner = "SystemShadow" }).ToArray();
            if (evidence == "missing-canonical") canonical = [];
            if (evidence == "unreconciled") readState = state with { Orders = state.Orders.Select(o => o with { Reconciled = false }).ToImmutableArray() };
            if (evidence == "changed-head") readState = state with { Revision = state.Revision + 1 };
            if (evidence is "missing-listing" or "wrong-exchange") readState = state with { Account = state.Account with {
                Import = state.Account.Import with { Plan = state.Account.Import.Plan with { Observation = observation with {
                    Instruments = evidence == "missing-listing" ? [] : observation.Instruments!.Select(i => i with { PrimaryMic = "XTSX" }).ToArray()
                } } } } };
            Task<IReadOnlyList<DelphiLiveObservedHolding>> Resolve() => DelphiLiveHoldingSource.ResolveSymbolsAsync(
                input, canonical, [], [subscription], expected => {
                    expected.RequireMatches(readState);
                    return Task.FromResult(BrokerMarketSymbols.ForTrackedHoldings(readState));
                });
            if (evidence == "valid")
            {
                var result = await Resolve();
                Assert.Equal(marketNames, result.Select(h => h.Symbol));
                Assert.Equal(tracked.Select(h => h.OwnerRecordId), result.Select(h => h.OwnerRecordId));
                Assert.All(result, h => { Assert.Equal("Real", h.Owner); Assert.False(h.DelphiLiveMayAct); });
            }
            else if (evidence is "changed-head" or "unreconciled") await Assert.ThrowsAsync<InvalidDataException>(Resolve);
            else await Assert.ThrowsAsync<InvalidOperationException>(Resolve);
            Assert.Equal(untouched, DelphiLiveLedgerJson.Serialize(state));
        }
    }
}
