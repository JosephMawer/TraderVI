#nullable enable
using Core.Trader.DelphiLive;
using Core.Trader;
using Core.Trader.Promotion;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;

namespace Core.Runtime;

/// <summary>Collection subscription only. Never grants order authority or substitutes for fresh broker reconciliation.</summary>
public sealed record EngineRealObservation(Guid BindingId, Guid SessionId, long Revision,
    BrokerManagedStrategy Strategy, ImmutableArray<DelphiLiveObservedHolding> Holdings)
{
    public void RequireMatches(BrokerManagedSession session)
    {
        if (DelphiLiveLedgerJson.Serialize(this) != DelphiLiveLedgerJson.Serialize(From(session)))
            throw new InvalidDataException("Real collection subscription differs from the verified current account state.");
    }
    public static EngineRealObservation From(BrokerManagedSession state)
    {
        state.Strategy.Validate();
        return new(state.BindingId, state.SessionId, state.Revision, state.Strategy,
            state.Account.Positions.Select(p => new DelphiLiveObservedHolding(p.Symbol, "BrokerManaged", p.PositionId, false))
            .Concat(state.Orders.Where(o => !o.Reconciled).Select(o => new DelphiLiveObservedHolding(o.Symbol, "BrokerPending", o.RequestId, false)))
            .Distinct().ToImmutableArray());
    }

    public static IReadOnlyList<DelphiLivePolicyAssignment> IncludeAssignments(
        IReadOnlyList<DelphiLivePolicyAssignment> paper, IReadOnlyList<EngineRealObservation> real, DateOnly date) =>
        paper.Concat(real.Where(r => r.Strategy.Strategy.Family == TradingStrategyFamily.DelphiLive &&
                paper.All(p => p.PolicyVersionId != r.Strategy.Strategy.PolicyVersionId))
            .Select(r => new DelphiLivePolicyAssignment(r.BindingId, r.Strategy.Strategy.PolicyVersionId,
                DelphiLivePolicyRole.GateComparison, date)).DistinctBy(a => a.PolicyVersionId)).ToArray();

    public static DelphiLiveSessionContext IncludeDefinitions(DelphiLiveSessionContext context,
        IReadOnlyList<EngineRealObservation> real, IReadOnlyDictionary<Guid, PaperComparisonSource> sources)
    {
        var policies = context.Policies.ToDictionary(p => p.Key, p => p.Value);
        var comparisons = context.ComparisonAccounts.ToDictionary(p => p.Key, p => p.Value);
        var combinedSources = context.ComparisonSources.ToDictionary(p => p.Key, p => p.Value);
        var baselines = context.Baselines.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var item in real.Where(r => r.Strategy.Strategy.Family == TradingStrategyFamily.DelphiLive))
        {
            item.Strategy.Validate();
            var policy = item.Strategy.Strategy.ReadLivePolicy();
            if (policies.TryGetValue(policy.PolicyVersionId, out var existing) &&
                DelphiLiveLedgerJson.Serialize(existing) != DelphiLiveLedgerJson.Serialize(policy))
                throw new InvalidOperationException("One market policy identity has conflicting definitions.");
            policies[policy.PolicyVersionId] = policy;
            if (item.Strategy.Comparison is not { } account) continue;
            // Preserve the originally reviewed source identity, including its separately reviewed successor.
            if (comparisons.TryGetValue(policy.PolicyVersionId, out var prior) && prior != account &&
                prior with { LivePolicyVersionId = account.LivePolicyVersionId } != account)
                throw new InvalidOperationException("One market policy identity has conflicting admission sources.");
            comparisons[policy.PolicyVersionId] = account;
            if (!sources.TryGetValue(account.BatchId, out var source)) continue;
            combinedSources[account.BatchId] = source;
            foreach (var pair in source.Baselines) baselines.TryAdd(pair.Key, pair.Value);
        }
        return context with { Assignments = IncludeAssignments(context.Assignments, real, context.Session.TradingDate),
            Policies = policies, ComparisonAccounts = comparisons, ComparisonSources = combinedSources, Baselines = baselines,
            RealObservedSymbols = real.Where(r => r.Strategy.Strategy.Family == TradingStrategyFamily.DelphiLive)
                .GroupBy(r => r.Strategy.Strategy.PolicyVersionId).ToDictionary(g => g.Key,
                    g => g.SelectMany(r => r.Holdings).Select(h => h.Symbol).Distinct(StringComparer.Ordinal).ToArray()) };
    }
}
