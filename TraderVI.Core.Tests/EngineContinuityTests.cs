#nullable enable
using Core.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineContinuityTests
{
    [Fact]
    public async Task NewInstallationRequiresRecoveryAndOnlyDrainedCloseCreatesCleanWitness()
    {
        var store = new Store(); var file = new MemoryFile(); var engine = new EngineContinuity(store, file);
        await engine.StartAsync(); Assert.Single(store.Holds); Assert.False(file.Value!.Clean);
        await engine.VerifyAsync(); await engine.TickAsync(); Assert.False(file.Value.Clean);
        await engine.CloseAsync(); Assert.True(file.Value.Clean);
        var restarted = new EngineContinuity(store, file); await restarted.StartAsync();
        Assert.Single(store.Holds); Assert.False(file.Value.Clean);
        // A successful restart never resolves the first installation's outstanding incident.
    }
    [Fact]
    public async Task InterruptedProcessOrLostLocalWitnessRequiresExplicitRecovery()
    {
        var store = new Store(); var file = new MemoryFile(); await new EngineContinuity(store, file).StartAsync();
        var previous = file.Value;
        await new EngineContinuity(store, file).StartAsync(); Assert.Equal(2, store.Holds.Count);
        Assert.NotEqual(previous!.RunId, file.Value!.RunId);
        file.Value = null;
        await new EngineContinuity(store, file).StartAsync(); Assert.Equal(3, store.Holds.Count);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RestoreOrLocalRollbackStopsAdmissionAndCannotBeMarkedClean(bool forkChanged)
    {
        var store = new Store(); var file = new MemoryFile(); var engine = new EngineContinuity(store, file);
        await engine.StartAsync(); var old = file.Value!; await engine.TickAsync();
        if (forkChanged) store.Identity = store.Identity with { RecoveryFork = Guid.NewGuid() };
        else store.Value = old;
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.VerifyAsync());
        Assert.Equal(2, store.Holds.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.VerifyAsync());
        await engine.CloseAsync(); Assert.False(file.Value!.Clean);
        await new EngineContinuity(store, file).StartAsync(); Assert.Equal(3, store.Holds.Count);
    }
    [Fact]
    public async Task FileWriteFailureAfterSqlCommitLeavesAnUncleanRecoverableMismatch()
    {
        var store = new Store(); var file = new MemoryFile(); var engine = new EngineContinuity(store, file);
        await engine.StartAsync(); var original = file.Value;
        file.FailWrite = true; await Assert.ThrowsAsync<IOException>(() => engine.TickAsync());
        Assert.NotEqual(original, store.Value); Assert.Equal(original, file.Value);
        await engine.CloseAsync(); Assert.False(file.Value!.Clean);
        file.FailWrite = false; await new EngineContinuity(store, file).StartAsync();
        Assert.Equal(store.Value, file.Value); Assert.Equal(3, store.Holds.Count);
    }
    [Fact]
    public async Task ConcurrentChecksCannotOverwriteNewerWitnessAndSerializedStampRoundTrips()
    {
        var store = new Store(); var file = new MemoryFile(); var engine = new EngineContinuity(store, file);
        await engine.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => engine.TickAsync()));
        Assert.Equal(30, file.Value!.Sequence); Assert.Equal(store.Value, file.Value);
        Assert.Equal(file.Value, JsonSerializer.Deserialize<EngineContinuityStamp>(file.Value.Json));
        Assert.Equal(64, file.Value.Hash.Length); Assert.DoesNotContain("Hash", file.Value.Json);
    }
    private sealed class MemoryFile : IEngineContinuityFile
    {
        public EngineContinuityStamp? Value;
        public bool FailWrite;
        public EngineContinuityStamp? Read() => Value;
        public void Write(EngineContinuityStamp value) { if (FailWrite) throw new IOException("Fixture write failure."); Value = value; }
    }
    private sealed class Store : IEngineContinuityStore
    {
        public EngineDatabaseIdentity Identity = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        public EngineContinuityStamp? Value;
        public readonly List<string> Holds = [];
        public Task<EngineContinuityObservation> ReadAsync(CancellationToken ct) => Task.FromResult(new EngineContinuityObservation(Identity, Value));
        public Task WriteAsync(EngineContinuityStamp? expected, EngineContinuityStamp next, CancellationToken ct)
        { Assert.Equal(expected, Value); Assert.Equal(Identity, next.Database); Value = next; return Task.CompletedTask; }
        public Task HoldAsync(string key, string code, CancellationToken ct) { Holds.Add(code); return Task.CompletedTask; }
    }
}
