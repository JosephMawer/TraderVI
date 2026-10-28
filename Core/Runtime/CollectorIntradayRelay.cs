#nullable enable
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public sealed record CollectorIntradayRequest(Guid RequestId, string Symbol, int Interval,
    DateTime StartUtc, DateTime EndUtc, bool ObservationContext)
{
    public void Validate(DateTime now)
    {
        DelphiLiveFiveMinuteBar.RequireCanonicalSymbol(Symbol);
        DelphiLiveFiveMinuteBar.RequireUtc(StartUtc, nameof(StartUtc));
        DelphiLiveFiveMinuteBar.RequireUtc(EndUtc, nameof(EndUtc));
        if (RequestId == Guid.Empty || Symbol.Length > 20 || Interval is not (5 or 15) || EndUtc <= StartUtc ||
            EndUtc > now || EndUtc - StartUtc > TimeSpan.FromDays(5) ||
            ObservationContext && (Interval != 5 || EndUtc - StartUtc != TimeSpan.FromMinutes(5)))
            throw new InvalidDataException("Collector compatibility request is outside its bounded legacy contract.");
    }
}
public interface ICollectorIntradayReader
{
    Task<TmxIntradayBatch> ReadAsync(CollectorIntradayRequest request, CancellationToken ct);
}

/// <summary>Legacy compatibility only. New minute decisions read SQL books directly. No HTTP fallback.</summary>
public sealed class CollectorIntradayClient(string pipeName = CollectorIntradayProtocol.PipeName) : ICollectorIntradayReader
{
    public async Task<TmxIntradayBatch> ReadAsync(CollectorIntradayRequest request, CancellationToken ct)
    {
        request.Validate(DateTime.UtcNow);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, timeout.Token);
        await CollectorIntradayProtocol.WriteAsync(pipe, request, timeout.Token);
        var reply = await CollectorIntradayProtocol.ReadAsync<CollectorIntradayReply>(pipe, timeout.Token);
        if (reply.RequestId != request.RequestId || reply.Failure is not null || reply.Batch is not { } batch)
            throw new IOException("Independent collector did not supply the requested legacy evidence.");
        CollectorIntradayProtocol.ValidateReply(request, batch, DateTime.UtcNow);
        return batch; // Retain provider fetch/receipt/attempt fields; a local read cannot refresh evidence.
    }
}
public sealed record CollectorIntradayReply(Guid RequestId, TmxIntradayBatch? Batch, string? Failure);

/// <summary>Collector-owned legacy transport. Never constructs a broker or a trading workflow.</summary>
public sealed class CollectorIntradayServer(ICollectorIntradayReader provider, Func<DateTime, bool> allowed,
    string pipeName = CollectorIntradayProtocol.PipeName)
{
    public async Task RunAsync(int concurrency, CancellationToken ct)
    {
        if (concurrency is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(concurrency));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = new Task[concurrency];
        for (int i = 0; i < concurrency; i++) workers[i] = ServeAsync(concurrency, lifetime.Token);
        // A failed listener must not remain hidden behind other indefinitely waiting listeners.
        await Task.WhenAny(workers);
        lifetime.Cancel();
        await Task.WhenAll(workers);
    }
    private async Task ServeAsync(int concurrency, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, concurrency,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(55));
                var request = await CollectorIntradayProtocol.ReadAsync<CollectorIntradayRequest>(pipe, timeout.Token);
                request.Validate(DateTime.UtcNow);
                if (!allowed(DateTime.UtcNow)) throw new InvalidOperationException("Outside the reviewed collection session.");
                CollectorIntradayReply reply;
                try
                {
                    var batch = await provider.ReadAsync(request, timeout.Token);
                    CollectorIntradayProtocol.ValidateReply(request, batch, DateTime.UtcNow);
                    reply = new(request.RequestId, batch, null);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { reply = new(request.RequestId, null, "LegacySourceUnavailable"); }
                await CollectorIntradayProtocol.WriteAsync(pipe, reply, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or ArgumentException or OperationCanceledException)
            { /* One bounded caller fails closed; no retry or reassignment of another caller's receipt. */ }
        }
    }
}
public sealed class CollectorLegacyTmxProvider : ICollectorIntradayReader, IDisposable
{
    private readonly TmxClient client = new();
    public Task<TmxIntradayBatch> ReadAsync(CollectorIntradayRequest request, CancellationToken ct) => request.ObservationContext
        ? client.GetIntradayObservationBatchAsync(request.Symbol, request.Interval, request.StartUtc, request.EndUtc, ct)
        : client.GetIntradayTimeSeriesBatchAsync(request.Symbol, request.Interval, request.StartUtc, request.EndUtc, ct);
    public void Dispose() => client.Dispose();
}
internal static class CollectorIntradayProtocol
{
    public const string PipeName = "TraderVI.IntradayCollector.V1";
    private const int MaximumBytes = 4 * 1024 * 1024;
    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumBytes) throw new InvalidDataException("Collector frame exceeds its bound.");
        byte[] content = new byte[length]; await stream.ReadExactlyAsync(content, ct);
        return JsonSerializer.Deserialize<T>(content) ?? throw new InvalidDataException("Collector frame is empty.");
    }
    internal static async Task WriteAsync<T>(Stream stream, T content, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(content);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Collector frame exceeds its bound.");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    internal static void ValidateReply(CollectorIntradayRequest request, TmxIntradayBatch batch, DateTime now)
    {
        static DateTime Minute(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        if (batch.Symbol != request.Symbol || batch.IntervalMinutes != request.Interval ||
            batch.RequestedStartUtc != Minute(request.StartUtc) || batch.RequestedEndUtc != Minute(request.EndUtc) ||
            batch.FetchStartedUtc.Kind != DateTimeKind.Utc || batch.ReceivedUtc.Kind != DateTimeKind.Utc ||
            batch.ReceivedUtc < batch.FetchStartedUtc || batch.ReceivedUtc > now ||
            batch.RequestCount != 1 || batch.AttemptCount is < 1 or > 3 || batch.Bars is null || batch.Bars.Count > 2000)
            throw new InvalidDataException("Collector legacy receipt does not match its original request.");
    }
}
