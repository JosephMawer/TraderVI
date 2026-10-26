#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Explicit offline recovery window: preserve stable input heads without relaxing clearance proof.</summary>
public static class EngineRecoveryMaintenance
{
    public static ImmutableArray<EngineWork> Work(Func<Task> continuity, Func<Task> realRecovery) =>
        [new("Engine continuity", TimeSpan.FromSeconds(10), continuity),
         new("Real recovery audit", TimeSpan.FromSeconds(1), realRecovery)];

    public static void RequireOperation(EngineOperation operation)
    {
        if (operation is not (EngineOperation.Status or EngineOperation.Stop or EngineOperation.IntegrityStatus or
            EngineOperation.Connection or EngineOperation.ExecutionStatus or EngineOperation.VerifyRealRecovery or
            EngineOperation.ClearIntegrityHolds))
            throw new InvalidOperationException("Recovery maintenance permits only local status, Real recovery, verified clearance and normal stop.");
    }

    public static void RequireClearScope(EngineRecoveryScope scope)
    {
        if (scope != EngineRecoveryScope.Real)
            throw new InvalidOperationException("Recovery maintenance can clear only the verified Real scope.");
    }
}
