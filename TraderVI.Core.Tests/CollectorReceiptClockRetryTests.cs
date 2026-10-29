#nullable enable
using Core.Db;
using Core.Runtime;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class CollectorReceiptClockRetryTests
{
    [Fact]
    public void OnlyExplicitFutureClockRejectionCanRetry()
    {
        Assert.True(CollectorReceiptClockRetry.CanRetry([54561]));
        Assert.True(CollectorReceiptClockRetry.CanRetry([54561, 3621]));
        Assert.False(CollectorReceiptClockRetry.CanRetry([]));
        foreach (int other in new[] { -2, 1205, 1222, 229, 2627, 54562, 54573, 3621 })
        {
            Assert.False(CollectorReceiptClockRetry.CanRetry([other]));
            if (other != 3621) Assert.False(CollectorReceiptClockRetry.CanRetry([54561, other]));
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task SameEvidenceWaitsOutsideRolledBackTransactionUntilDatabaseClockCatchesUp(int lagTicks)
    {
        var original = new { RequestId = Guid.NewGuid(), ValidatedUtc = DateTime.UtcNow, Value = 10m };
        string before = JsonSerializer.Serialize(original);
        int calls = 0, disposed = 0, waits = 0;
        var result = await CollectorReceiptClockRetry.RunAsync(_ =>
        {
            Assert.Equal(calls, disposed);
            try
            {
                calls++;
                if (calls <= lagTicks) throw new IOException("synthetic clock rejection");
                return Task.FromResult(original);
            }
            finally { disposed++; }
        }, _ => [54561], (duration, _) =>
        {
            Assert.Equal(calls, disposed);
            Assert.Equal(TimeSpan.FromMilliseconds(25), duration);
            waits++; return Task.CompletedTask;
        }, default);
        Assert.Same(original, result);
        Assert.Equal(before, JsonSerializer.Serialize(result));
        Assert.Equal(lagTicks + 1, calls); Assert.Equal(lagTicks, waits);
    }

    [Fact]
    public async Task PersistentFutureClockFailsAfterFiftyMillisecondsOfBoundedWaiting()
    {
        int calls = 0, waits = 0;
        var error = new IOException("clock still ahead");
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => CollectorReceiptClockRetry.RunAsync<int>(
            _ => { calls++; throw error; }, _ => [54561], (_, _) => { waits++; return Task.CompletedTask; }, default)));
        Assert.Equal(3, calls); Assert.Equal(2, waits);
    }

    [Theory]
    [InlineData(54562)] [InlineData(54573)] [InlineData(-2)] [InlineData(1205)]
    public async Task OtherFailuresAreNotRetried(int number)
    {
        int calls = 0;
        await Assert.ThrowsAsync<IOException>(() => CollectorReceiptClockRetry.RunAsync<int>(
            _ => { calls++; throw new IOException(); }, _ => [number],
            (_, _) => throw new Exception("Must not wait"), default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationDuringWaitDoesNotAppendAgain()
    {
        using var cancellation = new CancellationTokenSource(); int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectorReceiptClockRetry.RunAsync<int>(
            _ => { calls++; throw new IOException(); }, _ => [54561], (_, token) =>
            { cancellation.Cancel(); return Task.Delay(25, token); }, cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void StatusDoesNotExposePrivateExceptionMessageOrStack()
    {
        var failure = CollectorFailureDiagnostic.From("Collection", new IOException("private SQL/account/price payload"));
        string json = JsonSerializer.Serialize(new CollectorTickResult("CollectorAttention", 0, 0, 0) { Failure = failure });
        Assert.Contains("IOException", json); Assert.Contains("Collection", json);
        Assert.DoesNotContain("private", json); Assert.Empty(failure.SqlErrors);
    }
}
