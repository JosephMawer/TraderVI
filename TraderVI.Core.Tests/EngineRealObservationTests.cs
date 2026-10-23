#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
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
    public void ReviewedSuccessorAdmitsTheOriginalSourceWithoutChangingSavedIdentity()
    {
        var f = new BrokerManagedSessionTests.Fixture(); BrokerManagedSessionTests.UseNormalLive(f, progressive: false);
        var original = f.Strategy;
        var policy = original.Strategy.ReadLivePolicy() with { PolicyVersionId = Guid.NewGuid(), UseProgressiveProfitProtection = true };
        var reviewed = original with { Strategy = original.Strategy with { PolicyVersionId = policy.PolicyVersionId, LivePolicy = policy },
            LiveComparisonSuccessor = new(original.PaperAccountId, Guid.NewGuid(), original.Strategy.ReadLivePolicy()) };
        reviewed.Validate();
        var before = DelphiLiveLedgerJson.Serialize(reviewed); var fingerprint = reviewed.Fingerprint;
        var account = original.Comparison!; var at = account.EffectiveUtc;
        var source = new PaperComparisonSource(Guid.NewGuid(), account.BatchId, Guid.NewGuid(), Guid.NewGuid(),
            account.DailyStrategyVersionId, at, at, at, [], new Dictionary<string, DelphiLiveFrozenBaseline>());
        var date = DateOnly.FromDateTime(at);
        var empty = new DelphiLiveSessionContext(new(source.SessionId, date, null, null, "Frozen", at, ["XIU"]),
            new(date, at, at.AddHours(6.5)), [], new Dictionary<Guid, DelphiLivePolicyDefinition>(),
            new Dictionary<string, DelphiLiveFrozenCandidate>(), new Dictionary<string, DelphiLiveFrozenBaseline>());
        var real = new EngineRealObservation(Guid.NewGuid(), Guid.NewGuid(), 1, reviewed, []);
        var sources = new Dictionary<Guid, PaperComparisonSource> { [account.BatchId] = source };
        var context = EngineRealObservation.IncludeDefinitions(empty, [real], sources);
        var admission = Assert.IsType<PaperGateAdmission>(context.AdmissionFor(policy.PolicyVersionId));
        admission.Validate(source.RunId, source.DailyStrategyVersionId, policy.PolicyVersionId, at);
        Assert.Equal(account.AccountId, admission.AccountId); Assert.Equal(account.Profile, admission.Profile);
        Assert.Equal(account.BatchId, admission.BatchId); Assert.Equal(source.RevisionId, admission.SourceRevisionId);
        Assert.Equal(before, DelphiLiveLedgerJson.Serialize(reviewed)); Assert.Equal(fingerprint, reviewed.Fingerprint);
        Assert.Equal(original.Strategy.PolicyVersionId, reviewed.Comparison!.LivePolicyVersionId);
        // An already projected Paper source may coexist, but another source/profile may not.
        Assert.Equal(admission, EngineRealObservation.IncludeDefinitions(context, [real], sources).AdmissionFor(policy.PolicyVersionId));
        var conflict = context with { ComparisonAccounts = new Dictionary<Guid, PaperComparisonAccount>
            { [policy.PolicyVersionId] = account with { PairId = Guid.NewGuid(), LivePolicyVersionId = policy.PolicyVersionId } } };
        Assert.Throws<InvalidOperationException>(() => EngineRealObservation.IncludeDefinitions(conflict, [real], sources));
        Assert.Throws<ArgumentException>(() => EngineRealObservation.IncludeDefinitions(empty,
            [real with { Strategy = reviewed with { LiveComparisonSuccessor = null } }], sources));
        Assert.Throws<ArgumentException>(() => EngineRealObservation.IncludeDefinitions(empty,
            [real with { Strategy = reviewed with { Strategy = reviewed.Strategy with { LivePolicy = policy with { EntryCutoff = new TimeOnly(15, 40) } } } }], sources));
    }
    [Fact]
    public async Task ExecutionSubscriptionMustMatchTheEntireVerifiedCurrentState()
    {
        var f = new BrokerManagedSessionTests.Fixture(); BrokerManagedSessionTests.UseNormalLive(f);
        await f.Workflow().ApproveAsync(f.Approval(retain: true));
        var state = f.Store.State!; var observation = EngineRealObservation.From(state);
        observation.RequireMatches(state);
        foreach (var changed in new[] { observation with { BindingId = Guid.NewGuid() },
            observation with { SessionId = Guid.NewGuid() }, observation with { Revision = observation.Revision + 1 },
            observation with { Holdings = [] }, observation with { Holdings = observation.Holdings.Add(new("DIFFERENT", "BrokerPending", Guid.NewGuid(), false)) } })
            Assert.Throws<System.IO.InvalidDataException>(() => changed.RequireMatches(state));
        Assert.Throws<System.IO.InvalidDataException>(() => observation.RequireMatches(state with { Revision = state.Revision + 1 }));
    }
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
