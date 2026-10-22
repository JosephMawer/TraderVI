#nullable enable
using Core.Runtime;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineAuditBatchTests
{
    private static EngineAuditRun Run(long last = 1000)
    {
        var plan = new EngineAuditPlan(EngineAuditKind.NewHistory, DateTime.UtcNow, [new(EngineAuditFamily.SharedContent, null, 1, last)]);
        return new(Guid.NewGuid(), plan, EngineAuditProgress.Start(plan), 0, "Running", DateTime.UtcNow);
    }
    private static Task<EngineAuditProgress> Verify(EngineAuditRun r, CancellationToken _) => Task.FromResult(
        r.Progress.NextRevision > r.Plan.Targets[r.Progress.TargetIndex].ThroughRevision ? r.Progress.NextTarget(r.Plan)
            : r.Progress.Verified(r.Plan, r.Progress.NextRevision + 1, null));
    [Fact]
    public async Task BatchesAdvanceManyRecordsWithoutChangingTheFixedBoundary()
    {
        var run = Run();
        var next = await EngineAuditBatch.VerifyAsync(run, Verify, () => true, maximumSteps: 256);
        Assert.Equal(256, next.VerifiedUnits); Assert.Equal(257, next.NextRevision);
        Assert.Equal(1000, run.Plan.Targets[0].ThroughRevision); Assert.Equal(1, run.Progress.NextRevision);
    }
    [Fact]
    public async Task TimeBudgetAndStopSignalBoundNewAdmissions()
    {
        var clock = new Clock(); var run = Run();
        var next = await EngineAuditBatch.VerifyAsync(run, (r, ct) => { clock.Seconds++; return Verify(r, ct); },
            () => true, time: clock);
        Assert.Equal(5, next.VerifiedUnits);
        next = await EngineAuditBatch.VerifyAsync(run, Verify, () => false);
        Assert.Equal(run.Progress, next);
    }
    [Fact]
    public async Task FailurePreservesOnlyTheVerifiedPrefixAndIdentifiesTheFailingRecord()
    {
        var run = Run();
        var error = await Assert.ThrowsAsync<EngineAuditBatchFailure>(() => EngineAuditBatch.VerifyAsync(run,
            (r, ct) => r.Progress.NextRevision == 4 ? throw new InvalidDataException("Fixture damaged record.") : Verify(r, ct), () => true));
        Assert.Equal(3, error.Verified.VerifiedUnits); Assert.Equal(4, error.Verified.NextRevision);
        Assert.False(error.Verified.Complete);
    }
    [Fact]
    public async Task FinishingOrInterruptionNeverInventsAdditionalCoverage()
    {
        var run = Run(2); int calls = 0;
        var next = await EngineAuditBatch.VerifyAsync(run, (r, ct) => { calls++; return Verify(r, ct); }, () => true);
        Assert.True(next.Complete); Assert.Equal(2, next.VerifiedUnits); Assert.Equal(3, calls);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => EngineAuditBatch.VerifyAsync(run, Verify, () => true, ct: cancelled.Token));
    }
    private sealed class Clock : TimeProvider
    {
        public long Seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Seconds;
    }
}
