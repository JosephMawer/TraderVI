#nullable enable
using Core.Db;
using Shouldly;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class SqlObservedTimeBarrierTests
{
    private static readonly DateTime Observed = new(2026, 10, 14, 13, 37, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ResolutionLagWaitsForRealStoreTimeWithoutChangingTheObservation()
    {
        DateTime store = Observed.AddMilliseconds(-2);
        int reads = 0, waits = 0;
        await SqlObservedTimeBarrier.WaitAsync(Observed, _ => { reads++; return Task.FromResult(store); },
            (delay, _) => { waits++; delay.ShouldBeGreaterThan(TimeSpan.Zero); store = Observed; return Task.CompletedTask; }, CancellationToken.None);
        reads.ShouldBe(2); waits.ShouldBe(1);
        store.ShouldBe(Observed);
    }

    [Fact]
    public async Task AlreadyObservedTimeNeedsNoWait()
    {
        await SqlObservedTimeBarrier.WaitAsync(Observed, _ => Task.FromResult(Observed),
            (_, _) => throw new Exception("Unexpected wait"), CancellationToken.None);
    }

    [Fact]
    public async Task LargeFutureTimestampIsRejectedWithoutRetryOrTolerance()
    {
        int waits = 0;
        await Should.ThrowAsync<InvalidOperationException>(() => SqlObservedTimeBarrier.WaitAsync(Observed,
            _ => Task.FromResult(Observed.AddSeconds(-1)), (_, _) => { waits++; return Task.CompletedTask; }, CancellationToken.None));
        waits.ShouldBe(0);
    }

    [Fact]
    public async Task ClockThatNeverAdvancesFailsAfterBoundedAttempts()
    {
        int reads = 0;
        await Should.ThrowAsync<InvalidOperationException>(() => SqlObservedTimeBarrier.WaitAsync(Observed,
            _ => { reads++; return Task.FromResult(Observed.AddMilliseconds(-1)); }, (_, _) => Task.CompletedTask, CancellationToken.None));
        reads.ShouldBe(8);
    }

    [Fact]
    public async Task CancellationAndNonUtcInputsRemainRejected()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => SqlObservedTimeBarrier.WaitAsync(Observed,
            _ => throw new Exception("Unexpected read"), (_, _) => Task.CompletedTask, cancelled.Token));
        await Should.ThrowAsync<ArgumentException>(() => SqlObservedTimeBarrier.WaitAsync(DateTime.SpecifyKind(Observed, DateTimeKind.Unspecified),
            _ => Task.FromResult(Observed), (_, _) => Task.CompletedTask, CancellationToken.None));
    }
}
