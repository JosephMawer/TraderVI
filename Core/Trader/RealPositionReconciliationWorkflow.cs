#nullable enable

using Core.Db;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader;

/// <summary>
/// Records facts the operator reports from a broker. It has no order-routing
/// dependency and never treats a policy signal as a real fill.
/// </summary>
public sealed class RealPositionReconciliationWorkflow
{
    public async Task<PositionModeChangeResult> MarkAsRealAsync(
        Guid positionId,
        string accountLabel,
        CancellationToken cancellationToken = default)
    {
        if (positionId == Guid.Empty)
            throw new ArgumentException("Select a tracked position first.", nameof(positionId));
        string normalizedAccount =
            TrackedExecutionModeContract.NormalizeAccountLabel(
                TrackedExecutionMode.Real,
                accountLabel)!;
        return await new TrackedPositionExecutionRepository().MarkActiveGhostAsRealAsync(
            positionId,
            normalizedAccount,
            "Operator confirmed that the tracked position represents a real broker-held fill.",
            cancellationToken);
    }

    public async Task<TrackedRealExitResult> RecordManualExitAsync(
        Guid positionId,
        decimal fillPrice,
        DateTime filledAtLocal,
        CancellationToken cancellationToken = default)
    {
        if (positionId == Guid.Empty)
            throw new ArgumentException("Select a tracked position first.", nameof(positionId));
        if (fillPrice <= 0m)
            throw new ArgumentOutOfRangeException(nameof(fillPrice), "The real exit fill must be positive.");
        if (filledAtLocal == default)
            throw new ArgumentException("The real exit fill time is required.", nameof(filledAtLocal));

        TrackedRealExitResult? result = await new TrackedPositionExecutionRepository()
            .TryRecordManualRealExitAsync(
                positionId,
                fillPrice,
                filledAtLocal,
                "Operator-reported real exit",
                "Manual broker fill recorded by operator; TraderVI sent no order.",
                cancellationToken);
        return result ?? throw new InvalidOperationException(
            "The selected active Real position no longer exists. No exit was recorded.");
    }
}
