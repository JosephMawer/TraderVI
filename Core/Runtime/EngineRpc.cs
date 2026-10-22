#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public enum EngineOperation
{
    Status, Stop, Connection, SignIn, SubmitOtp, CancelSignIn, ConnectProtectedSession,
    CheckConnection, RememberSession, Disconnect, Accounts, AccountSnapshot, DailyPerformance,
    PendingOrders, Securities, BuyContext, SellContext, BuyOrder, SellOrder, SellInventory, OrderHistory, ActivityCheckpoint,
    Orders, PrepareBuy, SubmitBuy, CheckBuy, SellAll, CheckSell, Reconcile,
    ExecutionStatus, EnableExecution, DisableExecution, LiveCommand, PollTrackedPaper, PollShadowPaper,
    IntegrityStatus, VerifyRecovery, ClearIntegrityHolds
}

public sealed record EngineClearIntegrityHolds(Guid RecoveryRunId, System.Collections.Immutable.ImmutableArray<Guid> Incidents, string Reason);

public sealed record EngineRequest(int Version, Guid RequestId, EngineOperation Operation, JsonElement Payload);
public sealed record EngineReply(int Version, Guid RequestId, bool Succeeded, JsonElement Payload);

public interface IEngineRpc
{
    Task<T> CallAsync<T>(EngineOperation operation, object? payload = null, CancellationToken ct = default);
}

/// <summary>Local, current-user-only framed messages. No automatic replay of an uncertain command.</summary>
public sealed class EngineRpcClient(string pipeName) : IEngineRpc
{
    public async Task<T> CallAsync<T>(EngineOperation operation, object? payload = null, CancellationToken ct = default)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connecting.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(connecting.Token).ConfigureAwait(false);
        var request = new EngineRequest(1, Guid.NewGuid(), operation, JsonSerializer.SerializeToElement(payload));
        await EngineRpcFrames.WriteAsync(pipe, request, ct).ConfigureAwait(false);
        var reply = await EngineRpcFrames.ReadAsync<EngineReply>(pipe, ct).ConfigureAwait(false);
        if (reply.Version != 1 || reply.RequestId != request.RequestId)
            throw new InvalidDataException("The engine reply does not match this command.");
        if (!reply.Succeeded) throw new InvalidOperationException("The engine could not complete this request. Check its current status before retrying.");
        return reply.Payload.Deserialize<T>() ?? throw new InvalidDataException("The engine returned no result.");
    }
}

public sealed class EngineRpcServer(string pipeName, Func<EngineRequest, Task<object>> dispatch)
{
    private readonly ConcurrentDictionary<long, Task> active = new();
    private long sequence;
    public async Task RunAsync(CancellationToken stopAccepting)
    {
        using var slots = new SemaphoreSlim(16, 16);
        try
        {
            while (!stopAccepting.IsCancellationRequested)
            {
                await slots.WaitAsync(stopAccepting).ConfigureAwait(false);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new(pipeName, PipeDirection.InOut, 16, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stopAccepting).ConfigureAwait(false);
                    long id = Interlocked.Increment(ref sequence);
                    var accepted = pipe; pipe = null;
                    var task = ServeAsync(accepted);
                    active[id] = task;
                    _ = task.ContinueWith(_ => { active.TryRemove(id, out var _removed); slots.Release(); },
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                catch { pipe?.Dispose(); slots.Release(); throw; }
            }
        }
        catch (OperationCanceledException) when (stopAccepting.IsCancellationRequested) { }
        finally { await Task.WhenAll(active.Values).ConfigureAwait(false); }
    }
    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                using var receive = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var request = await EngineRpcFrames.ReadAsync<EngineRequest>(pipe, receive.Token).ConfigureAwait(false);
                if (request.Version != 1 || request.RequestId == Guid.Empty || !Enum.IsDefined(request.Operation))
                    throw new InvalidDataException("Unsupported engine request.");
                EngineReply reply;
                try
                {
                    // A disappearing dashboard does not cancel an admitted financial operation.
                    var result = await dispatch(request).ConfigureAwait(false);
                    reply = new(1, request.RequestId, true, JsonSerializer.SerializeToElement(result, result.GetType()));
                }
                catch { reply = new(1, request.RequestId, false, JsonSerializer.SerializeToElement(false)); }
                using var sending = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await EngineRpcFrames.WriteAsync(pipe, reply, sending.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or InvalidOperationException)
            { /* Failed transport is not a command retry or an engine stop. Never log payloads. */ }
        }
    }
}

internal static class EngineRpcFrames
{
    internal const int MaximumBytes = 16 * 1024 * 1024;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("Engine message exceeds its size limit.");
            byte[] length = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            await stream.WriteAsync(length, ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        byte[] length = new byte[4]; await stream.ReadExactlyAsync(length, ct).ConfigureAwait(false);
        int count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count <= 0 || count > MaximumBytes) throw new InvalidDataException("Invalid engine message size.");
        byte[] bytes = new byte[count];
        try
        {
            await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Missing engine message.");
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
