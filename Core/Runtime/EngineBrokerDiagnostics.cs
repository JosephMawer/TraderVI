#nullable enable
using Core.Trader.Promotion;
using System;

namespace Core.Runtime;

/// <summary>Explicit read-only diagnostics from a fully verified revision, independent of its storage representation.</summary>
public sealed record EngineBrokerDiagnostics(Guid BindingId, Guid SessionId, long Revision, DateTimeOffset SavedAt,
    string Event, string StorageFormat, BrokerDecisionTrace? Decision)
{
    public static EngineBrokerDiagnostics From(BrokerManagedSession state, bool shared) =>
        new(state.BindingId, state.SessionId, state.Revision, state.SavedAt, state.Event, shared ? "Shared" : "Inline", state.LatestDecision);
}
