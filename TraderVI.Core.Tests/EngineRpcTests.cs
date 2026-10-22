using Core.Runtime;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class EngineRpcTests
{
    [Fact]
    public async Task SeparateClientConnectionsShareOneEngineAndDoNotReplayCommands()
    {
        string pipe = "tradervi-test-" + Guid.NewGuid().ToString("N");
        int calls = 0;
        using var shutdown = new CancellationTokenSource();
        var server = new EngineRpcServer(pipe, request => Task.FromResult<object>(Interlocked.Increment(ref calls)));
        var serving = server.RunAsync(shutdown.Token);
        try
        {
            Assert.Equal(1, await new EngineRpcClient(pipe).CallAsync<int>(EngineOperation.Status));
            Assert.Equal(2, await new EngineRpcClient(pipe).CallAsync<int>(EngineOperation.Status));
        }
        finally { shutdown.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ClientCancellationDoesNotAbandonAnAdmittedCommand()
    {
        string pipe = "tradervi-test-" + Guid.NewGuid().ToString("N");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completed = 0;
        using var shutdown = new CancellationTokenSource(); using var dashboard = new CancellationTokenSource();
        var server = new EngineRpcServer(pipe, async request =>
        { entered.TrySetResult(); await finish.Task; Interlocked.Increment(ref completed); return true; });
        var serving = server.RunAsync(shutdown.Token);
        try
        {
            var client = new EngineRpcClient(pipe).CallAsync<bool>(EngineOperation.Stop, ct: dashboard.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); dashboard.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client);
            Assert.Equal(0, completed); finish.SetResult();
            shutdown.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, completed);
        }
        finally { finish.TrySetResult(); shutdown.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(16777217)]
    public async Task RejectsInvalidFrameLengthsBeforePayloadAllocation(int length)
    {
        byte[] bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => EngineRpcFrames.ReadAsync<EngineRequest>(stream, default));
    }

    [Fact]
    public async Task ServerFailureDoesNotDisclosePrivateExceptionOrStopOtherClients()
    {
        string pipe = "tradervi-test-" + Guid.NewGuid().ToString("N");
        using var shutdown = new CancellationTokenSource();
        var server = new EngineRpcServer(pipe, request => request.Operation == EngineOperation.Stop
            ? throw new InvalidOperationException("private account evidence") : Task.FromResult<object>(true));
        var serving = server.RunAsync(shutdown.Token);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new EngineRpcClient(pipe).CallAsync<bool>(EngineOperation.Stop));
            Assert.DoesNotContain("private account evidence", error.Message);
            Assert.True(await new EngineRpcClient(pipe).CallAsync<bool>(EngineOperation.Status));
        }
        finally { shutdown.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}
