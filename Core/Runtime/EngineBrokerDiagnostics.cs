#nullable enable
using Core.Trader.Promotion;
using System;
using System.Linq;
using System.Collections.Immutable;

namespace Core.Runtime;

/// <summary>Explicit read-only diagnostics from a fully verified revision, independent of its storage representation.</summary>
public sealed record EngineBrokerDiagnostics(Guid BindingId, Guid SessionId, long Revision, DateTimeOffset SavedAt,
    string Event, string StorageFormat, BrokerDecisionTrace? Decision)
{
    public EngineBrokerProtectionStatus? Protection { get; init; }
    public static EngineBrokerDiagnostics From(BrokerManagedSession state, bool shared) =>
        new(state.BindingId, state.SessionId, state.Revision, state.SavedAt, state.Event, shared ? "Shared" : "Inline", state.LatestDecision)
        {
            Protection = new(state.NewBuysStopped, state.Orders.Length, state.Orders.Count(o => o.Reconciled),
                state.Account.NewRiskBlocks, state.Account.Positions.Select(p => {
                    var saved = state.Protection.SingleOrDefault(s => s.PositionId == p.PositionId);
                    var memory = state.Memory.Symbols.TryGetValue(p.Symbol, out var current) && current.PositionId == p.PositionId ? current : saved?.State;
                    var live = memory?.LiveProtection;
                    return new EngineBrokerHoldingProtection(p.Symbol, live?.PositionId == p.PositionId && live.AveragePurchasePrice == p.CostBasis / p.Quantity,
                        saved?.ThroughUtc, live?.Progressive?.LastBidReceivedUtc, live?.Progressive?.NextBidCheckUtc,
                        memory?.ExitReason is not null || live?.Progressive?.ExitTriggered == true, live?.Progressive?.QuoteSourceContract);
                }).ToImmutableArray())
        };
}

public sealed record EngineBrokerHoldingProtection(string Symbol, bool SavedProtectionMatches,
    DateTime? BarsThroughUtc, DateTime? LastUsableBidUtc, DateTime? NextBidCheckUtc, bool ExitLatched, string? BidSourceContract);
public sealed record EngineBrokerProtectionStatus(bool NewBuysStopped, int OwnedOrders, int ReconciledOrders,
    ImmutableArray<string> AccountRiskBlocks, ImmutableArray<EngineBrokerHoldingProtection> Holdings);
