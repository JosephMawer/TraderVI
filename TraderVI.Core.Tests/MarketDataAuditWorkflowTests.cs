#nullable enable

using Core.DataQuality;
using Shouldly;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class MarketDataAuditWorkflowTests
{
    [Fact]
    public async Task RunAsync_LoadsOnceAndReturnsStructuredReport()
    {
        var source = new StubSnapshotSource(CleanSnapshot());
        var options = new MarketDataAuditOptions(2, 5, 14);
        var workflow = new MarketDataAuditWorkflow(source);

        MarketDataAuditRunResult result = await workflow.RunAsync(options);

        source.LoadCount.ShouldBe(1);
        result.Options.ShouldBe(options);
        result.Report.TotalSymbols.ShouldBe(1);
        result.Report.ActiveSymbols.ShouldBe(1);
        result.Report.ErrorCount.ShouldBe(0);
        result.Report.WarningCount.ShouldBe(0);
        result.CompletedUtc.ShouldBeGreaterThanOrEqualTo(result.StartedUtc);
    }

    [Fact]
    public async Task RunAsync_ValidatesOptionsBeforeLoadingSnapshot()
    {
        var source = new StubSnapshotSource(CleanSnapshot());
        var workflow = new MarketDataAuditWorkflow(source);
        var invalid = new MarketDataAuditOptions(5, 2, 14);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            workflow.RunAsync(invalid));

        source.LoadCount.ShouldBe(0);
    }

    private static MarketDataAuditSnapshot CleanSnapshot() =>
        new(
            [new DateTime(2026, 8, 26)],
            [new AuditedSymbol(
                "XIU",
                "iShares S&P/TSX 60 Index ETF",
                "XIU",
                "ETF",
                true,
                false,
                100,
                new DateTime(2026, 1, 1),
                new DateTime(2026, 8, 26),
                0,
                0)],
            [],
            [],
            [],
            []);

    private sealed class StubSnapshotSource(MarketDataAuditSnapshot snapshot)
        : IMarketDataAuditSnapshotSource
    {
        public int LoadCount { get; private set; }

        public Task<MarketDataAuditSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return Task.FromResult(snapshot);
        }
    }
}

