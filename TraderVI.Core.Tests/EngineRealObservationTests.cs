#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineRealObservationTests
{
    [Fact]
    public async Task ApprovedRealDefinitionAndHeldSymbolsSurviveAbsentOrReassignedPaper()
    {
        var f = new BrokerManagedSessionTests.Fixture(); BrokerManagedSessionTests.UseNormalLive(f);
        await f.Workflow().ApproveAsync(f.Approval(retain: true));
        var saved = f.Store.State!;
        var real = EngineRealObservation.From(saved);
        Assert.Equal(saved.Strategy.Fingerprint, real.Strategy.Fingerprint);
        var holding = Assert.Single(real.Holdings);
        Assert.Equal("AAA", holding.Symbol); Assert.False(holding.DelphiLiveMayAct);
        var date = saved.Account.Account.TradingDate;
        var open = new DateTime(2026, 9, 23, 13, 30, 0, DateTimeKind.Utc);
        var empty = new DelphiLiveSessionContext(new(Guid.NewGuid(), date, null, null, "Frozen", open, ["XIU", "AAA"]),
            new(date, open, open.AddHours(6.5)), [], new Dictionary<Guid, DelphiLivePolicyDefinition>(),
            new Dictionary<string, DelphiLiveFrozenCandidate>(), new Dictionary<string, DelphiLiveFrozenBaseline>());
        var context = EngineRealObservation.IncludeDefinitions(empty, [real], new Dictionary<Guid, PaperComparisonSource>());
        Assert.Equal(saved.Strategy.Strategy.ReadLivePolicy(), context.Policies[saved.Strategy.Strategy.PolicyVersionId]);
        Assert.Equal(saved.Strategy.Comparison, context.ComparisonAccounts[saved.Strategy.Strategy.PolicyVersionId]);
        Assert.Equal("AAA", Assert.Single(context.RealObservedSymbols[saved.Strategy.Strategy.PolicyVersionId]));
        Assert.Equal(real.BindingId, Assert.Single(context.Assignments).AssignmentId);
        var paper = new DelphiLivePolicyAssignment(Guid.NewGuid(), Guid.NewGuid(), DelphiLivePolicyRole.OperationalChampion, date);
        Assert.Equal(2, EngineRealObservation.IncludeAssignments([paper], [real], date).Count);
        var collision = context with { Policies = new Dictionary<Guid, DelphiLivePolicyDefinition>
            { [saved.Strategy.Strategy.PolicyVersionId] = saved.Strategy.Strategy.ReadLivePolicy() with { MaximumHoldings = 99 } } };
        Assert.Throws<InvalidOperationException>(() => EngineRealObservation.IncludeDefinitions(collision, [real], new Dictionary<Guid, PaperComparisonSource>()));
    }

    [Fact]
    public async Task SubscriptionSizeDependsOnCurrentHoldingsAndPendingOrdersNotCompletedOrders()
    {
        var f = new BrokerManagedSessionTests.Fixture();
        await f.Workflow().ApproveAsync(f.Approval(retain: true));
        var state = f.Store.State!;
        var archived = Enumerable.Range(0, 10000).Select(_ => new global::Core.Trader.Promotion.BrokerStrategyOrder(
            Guid.NewGuid(), Guid.NewGuid(), null, "security-OLD", "OLD", "BUY", 1, 10, "Buy", null, f.Now,
            Reconciled: true)).ToImmutableArray();
        var pending = archived[0] with { RequestId = Guid.NewGuid(), Symbol = "NEW", Reconciled = false };
        var result = EngineRealObservation.From(state with { Orders = archived.Add(pending) });
        Assert.Equal(new[] { "AAA", "NEW" }, result.Holdings.Select(h => h.Symbol).Order().ToArray());
        Assert.DoesNotContain(result.Holdings, h => h.Symbol == "OLD");
        Assert.All(result.Holdings, h => Assert.False(h.DelphiLiveMayAct));
    }
}
