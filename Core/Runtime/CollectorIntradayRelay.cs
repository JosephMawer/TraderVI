#nullable enable
using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

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
