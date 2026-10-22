#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public sealed class EngineAuditBatchFailure(EngineAuditProgress verified, Exception cause)
    : Exception("Historical verification failed after the last verified cursor.", cause)
{
    public EngineAuditProgress Verified { get; } = verified;
}

/// <summary>Bounded verified progress per wake. A failure never consumes its failing record.</summary>
public static class EngineAuditBatch
{
    public static async Task<EngineAuditProgress> VerifyAsync(EngineAuditRun run,
        Func<EngineAuditRun, CancellationToken, Task<EngineAuditProgress>> verify,
        Func<bool> mayContinue, int maximumSteps = 256, TimeSpan? budget = null,
        TimeProvider? time = null, CancellationToken ct = default)
    {
        var clock = time ?? TimeProvider.System;
        var duration = budget ?? TimeSpan.FromSeconds(5);
        if (maximumSteps is < 1 or > 4096 || duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        run.Progress.Validate(run.Plan);
        long started = clock.GetTimestamp();
        var current = run;
        for (int i = 0; i < maximumSteps && !current.Progress.Complete && mayContinue() && clock.GetElapsedTime(started) < duration; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var next = await verify(current, ct).ConfigureAwait(false);
                next.Validate(run.Plan);
                if (next == current.Progress) throw new InvalidDataException("Historical verification did not advance its cursor.");
                current = current with { Progress = next };
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or JsonException or ArgumentException)
            { throw new EngineAuditBatchFailure(current.Progress, error); }
        }
        return current.Progress;
    }
}
