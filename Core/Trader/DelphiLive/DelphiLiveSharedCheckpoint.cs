#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveSharedCheckpoint(Guid CycleId, DelphiLiveSessionContext Context,
    DateTime BarEndUtc, int ContinuityEpoch, ImmutableArray<DelphiLiveStoredEvaluation> Evaluations)
{
    public void Validate()
    {
        if (CycleId == Guid.Empty || ContinuityEpoch < 1 || BarEndUtc.Kind != DateTimeKind.Utc ||
            BarEndUtc <= Context.Bounds.OpenUtc || BarEndUtc > Context.Bounds.CloseUtc ||
            Evaluations.Any(e => e.Input.SessionId != Context.Session.SessionId || e.Input.BarEndUtc != BarEndUtc ||
                e.ContinuityEpoch != ContinuityEpoch || e.Result.EvaluationId != e.Input.EvaluationId ||
                !Context.Policies.TryGetValue(e.Input.Policy.PolicyVersionId, out var policy) ||
                DelphiLiveLedgerJson.Serialize(policy) != DelphiLiveLedgerJson.Serialize(e.Input.Policy)) ||
            Evaluations.DistinctBy(e => e.Input.EvaluationId).Count() != Evaluations.Length ||
            Evaluations.DistinctBy(e => (e.Input.Policy.PolicyVersionId, e.Input.Stock.Symbol)).Count() != Evaluations.Length)
            throw new InvalidOperationException("Shared checkpoint identities, policies and original observation boundaries must agree.");
    }
    public IReadOnlyList<DelphiLiveFiveMinuteBar> Bars()
    {
        Validate();
        var groups = Evaluations.SelectMany(e => e.Input.Stock.Bars.Concat(e.Input.Xiu.Bars)).GroupBy(b => b.ObservationId).ToArray();
        if (groups.Any(g => g.Any(b => b != g.First())))
            throw new InvalidOperationException("Shared checkpoint contains conflicting versions of one bar.");
        return groups.Select(g => g.First()).ToArray();
    }
}
