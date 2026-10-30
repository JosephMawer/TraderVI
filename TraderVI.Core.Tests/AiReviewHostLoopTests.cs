#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Trader.DelphiLive;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiReviewHostLoopTests
{
    [Fact]
    public async Task CancelledStartupDoesNotTick()
    {
        int calls = 0;
        await new AiReviewHostLoop().RunAsync(_ => { calls++; throw new Exception(); }, _ => { }, new CancellationToken(true));
        Assert.Equal(0, calls);
    }

    [Fact]
    // The engine starts this host on Task.Run. Keep the fixture's cleanup off xUnit's
    // bounded synchronization context so other parallel tests cannot hold its continuation.
    public Task StopWaitsForInFlightWorkAndRejectsSecondOwner() => Task.Run(async () =>
    {
        Assert.Null(SynchronizationContext.Current);
        var loop = new AiReviewHostLoop();
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        async Task<AiScheduledReviewResult> Tick(CancellationToken ct)
        {
            calls++; entered.SetResult();
            await release.Task;
            ct.ThrowIfCancellationRequested();
            throw new Exception("Unreachable");
        }
        Task running = loop.RunAsync(Tick, _ => { }, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunAsync(Tick, _ => { }, stop.Token));
            stop.Cancel();
            Assert.False(running.IsCompleted); // Resources must stay owned until the pending work exits.
        }
        finally
        {
            stop.Cancel();
            release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(1, calls);
    });

    [Fact]
    public async Task UnexpectedFailureStopsWithoutRetryOrPrivateDiagnostics()
    {
        int calls = 0;
        var notices = new List<AiReviewHostNotice>();
        await new AiReviewHostLoop().RunAsync(_ => { calls++; throw new IOException("private payload"); }, notices.Add, default);
        Assert.Equal(1, calls);
        Assert.Equal("NeedsAttention", Assert.Single(notices).State);
        Assert.DoesNotContain("private", notices[0].ToString());
    }

    [Fact]
    public async Task SuccessfulTickReportsStatusAndCanStopDuringIdleWait()
    {
        using var stop = new CancellationTokenSource();
        int calls = 0;
        var result = new AiScheduledReviewResult(new(null, false, "OutsideSession",
            new(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), "Test"), 0, null));
        await new AiReviewHostLoop().RunAsync(_ => { calls++; return Task.FromResult(result); }, notice =>
        {
            Assert.Equal("OutsideSession", notice.State);
            stop.Cancel();
        }, stop.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void MissingSettingsAreDisabledAndIncompleteSettingsAreRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        Assert.Null(AiPaperReviewSettings.Load(path));
        try
        {
            File.WriteAllText(path, "{\"Enabled\":true}");
            Assert.Throws<JsonException>(() => AiPaperReviewSettings.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AgreedCostBoundsExpireAfterOctober31Toronto()
    {
        var spend = AiPaperReviewSettings.AgreedSpend;
        Assert.Equal(90m, spend.MonthlyLimitCad);
        Assert.Equal(1.60m, spend.CadPerUsdCeiling);
        Assert.Equal(1.20m, spend.TaxAndFeeMultiplier);
        spend.Validate(new DateTimeOffset(2026, 11, 1, 3, 59, 59, TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => spend.Validate(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void MissingRuntimeHistoryIsNotCreatedAndNoCredentialIsNeededForThisCheck()
    {
        string root = Path.Combine(Path.GetTempPath(), "ai-host-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var date = new DateOnly(2026, 9, 30);
            string calendar = Path.Combine(root, "calendar.json");
            File.WriteAllText(calendar, JsonSerializer.Serialize(new ReviewedTsxCalendarDocument(
                "test", "synthetic calendar", date, date, [date])));
            string history = Path.Combine(root, "history");
            var settings = new AiPaperReviewSettings(true, Guid.NewGuid(), null, history, calendar,
                date, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), 1,
                TimeSpan.FromMinutes(1), ImmutableArray<string>.Empty);
            var error = Assert.Throws<InvalidDataException>(() => new AiPaperReviewRuntime(settings, new FixedClock()));
            Assert.Contains("Prepare complete private AI history", error.Message);
            Assert.False(Directory.Exists(history));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 30, 16, 0, 0, TimeSpan.Zero);
    }
}
