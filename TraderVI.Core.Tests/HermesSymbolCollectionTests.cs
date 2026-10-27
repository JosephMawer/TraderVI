using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hermes;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class HermesSymbolCollectionTests
{
    [Fact]
    public async Task MissingTickerLeavesExistingDataUntouchedAndNextTickerContinues()
    {
        var saved = new Dictionary<string, string> { ["WM"] = "original mapping" };
        var warnings = new List<string>();
        foreach (var symbol in new[] { "WM", "ABC" })
            await SymbolCollection.CollectAsync("sector-metadata", symbol,
                () => symbol == "WM" ? Task.FromException<string>(new InvalidOperationException("No results")) : Task.FromResult("fresh mapping"),
                value => !string.IsNullOrEmpty(value),
                value => { saved[symbol] = value; return Task.CompletedTask; }, warnings.Add);
        Assert.Equal("original mapping", saved["WM"]);
        Assert.Equal("fresh mapping", saved["ABC"]);
        Assert.StartsWith("HermesSymbolUnavailable: sector-metadata WM:", Assert.Single(warnings));
    }

    [Fact]
    public async Task EmptyBarsAndSourceTimeoutWarnWithoutWriting()
    {
        var warnings = new List<string>();
        int writes = 0;
        foreach (bool timeout in new[] { false, true })
            Assert.False(await SymbolCollection.CollectAsync("daily-bars", "ABC",
                () => timeout ? Task.FromException<int[]>(new TaskCanceledException("HTTP timeout")) : Task.FromResult(Array.Empty<int>()),
                bars => bars.Length > 0, bars => { writes++; return Task.CompletedTask; }, warnings.Add));
        Assert.Equal(0, writes);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public async Task StorageFailureEscapesInsteadOfBecomingTickerWarning()
    {
        var warnings = new List<string>();
        var failure = new InvalidOperationException("Storage unavailable");
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => SymbolCollection.CollectAsync(
            "daily-bars", "ABC", () => Task.FromResult(new[] { 1 }), bars => bars.Length > 0,
            bars => Task.FromException(failure), warnings.Add));
        Assert.Same(failure, caught);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task ProcessingFailureEscapesInsteadOfBecomingTickerWarning()
    {
        var warnings = new List<string>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SymbolCollection.CollectAsync(
            "daily-bars", "ABC", () => Task.FromResult(new[] { 1 }), _ => throw new InvalidOperationException(),
            _ => Task.CompletedTask, warnings.Add));
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task ExplicitCancellationStopsRatherThanContinuingWithPartialWork()
    {
        using var stop = new CancellationTokenSource();
        var warnings = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SymbolCollection.CollectAsync(
            "daily-bars", "ABC", () => { stop.Cancel(); return Task.FromCanceled<int[]>(stop.Token); },
            bars => bars.Length > 0, _ => Task.CompletedTask, warnings.Add, stop.Token));
        Assert.Empty(warnings);
    }
}
