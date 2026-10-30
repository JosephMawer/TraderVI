#nullable enable
using Core.Runtime;
using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class CollectorIntradayRelayTests
{
    [Theory]
    [InlineData(5, true)][InlineData(5, false)][InlineData(15, false)]
    public async Task SameUserRelayPreservesExactOriginalReceiptAndHasNoTradingCapability(int interval, bool observation)
    {
        string name = "TraderVI_Test_" + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var provider = new Provider();
        var server = new CollectorIntradayServer(provider, _ => true, name).RunAsync(2, stop.Token);
        try
        {
            var request = Request(interval, observation);
            var reply = await new CollectorIntradayClient(name).ReadAsync(request, stop.Token);
            Assert.Equal(1, provider.Calls); Assert.Equal(request, provider.Request);
            Assert.Equal(provider.Reply!.FetchStartedUtc, reply.FetchStartedUtc);
            Assert.Equal(provider.Reply.ReceivedUtc, reply.ReceivedUtc);
            Assert.Equal(provider.Reply.Bars[0], reply.Bars[0]);
            Assert.Equal(provider.Reply.AttemptCount, reply.AttemptCount);
        }
        finally { stop.Cancel(); await server; }
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    // A console collector has no synchronization context. Exercise the real pipe in
    // that context, rather than timing xUnit's queue of unrelated parallel tests.
    public Task SourceFailureOrClosedSessionCannotBecomeAnAutomaticRetry(bool sourceFailure) => Task.Run(async () =>
    {
        Assert.Null(SynchronizationContext.Current);
        string name = "TraderVI_Test_" + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var provider = new Provider { Fail = true };
        var server = new CollectorIntradayServer(provider, _ => sourceFailure, name).RunAsync(1, stop.Token);
        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() => new CollectorIntradayClient(name).ReadAsync(Request(), stop.Token));
            Assert.Equal(sourceFailure ? 1 : 0, provider.Calls);
        }
        finally { stop.Cancel(); await server; }
    });

    [Fact]
    public async Task TradingProcessRoutesLegacyBarsLocallyAndRejectsDirectDiagnosticFallback()
    {
        var handler = new NeverHttp(); var local = new Provider();
        using var client = new TmxClient(handler, intradayCollector: local);
        var request = Request();
        await client.GetIntradayObservationBatchAsync(request.Symbol, 5, request.StartUtc, request.EndUtc);
        Assert.True(local.Request!.ObservationContext);
        await client.GetIntradayTimeSeriesBatchAsync(request.Symbol, 5, request.StartUtc, request.EndUtc);
        Assert.False(local.Request!.ObservationContext);
        local.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => client.GetIntradayTimeSeriesBatchAsync(request.Symbol, 5, request.StartUtc, request.EndUtc));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetIntradayTimeSeriesDiagnosticAsync(request.Symbol, 5, request.StartUtc, request.EndUtc));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ProtocolRejectsOversizedFrameAndMismatchedOrFutureReceipt()
    {
        using var bytes = new MemoryStream(BitConverter.GetBytes(int.MaxValue));
        await Assert.ThrowsAsync<InvalidDataException>(() => CollectorIntradayProtocol.ReadAsync<CollectorIntradayRequest>(bytes, default));
        var request = Request(); var reply = await new Provider().ReadAsync(request, default);
        Assert.Throws<InvalidDataException>(() => CollectorIntradayProtocol.ValidateReply(request, reply with { Symbol = "OTHER" }, DateTime.UtcNow));
        Assert.Throws<InvalidDataException>(() => CollectorIntradayProtocol.ValidateReply(request, reply with { ReceivedUtc = DateTime.UtcNow.AddMinutes(1) }, DateTime.UtcNow));
        Assert.Throws<InvalidDataException>(() => (request with { Interval = 1 }).Validate(DateTime.UtcNow));
        Assert.Throws<InvalidDataException>(() => (request with { EndUtc = DateTime.UtcNow.AddMinutes(1) }).Validate(DateTime.UtcNow));
    }

    [Fact]
    public void OptionalRoutingRequiresExactExplicitContractAndRejectsMalformedConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TraderVI_Routing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string file = Path.Combine(directory, CollectorIntradayRouting.FileName);
        try
        {
            Assert.False(CollectorIntradayRouting.Read(directory));
            File.WriteAllText(file, "{\"Contract\":\"IndependentIntradayCollectorRoutingV1\",\"SourceContract\":\"TmxMinuteCollectorV1\"}");
            Assert.True(CollectorIntradayRouting.Read(directory));
            File.WriteAllText(file, "{\"Contract\":\"Other\",\"SourceContract\":\"TmxMinuteCollectorV1\"}");
            Assert.Throws<InvalidDataException>(() => CollectorIntradayRouting.Read(directory));
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }
    private static CollectorIntradayRequest Request(int interval = 5, bool observation = true)
    {
        var now = DateTime.UtcNow;
        var end = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        return new(Guid.NewGuid(), "XIU", interval, end.AddMinutes(-interval), end, observation);
    }
    private sealed class Provider : ICollectorIntradayReader
    {
        public int Calls; public bool Fail; public CollectorIntradayRequest? Request; public TmxIntradayBatch? Reply;
        public Task<TmxIntradayBatch> ReadAsync(CollectorIntradayRequest r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls); Request = r;
            if (Fail) throw new IOException("Synthetic unavailable source.");
            var now = DateTime.UtcNow;
            Reply = new(r.Symbol, r.Interval, r.StartUtc, r.EndUtc, now.AddMilliseconds(-1), now, 1, 1,
                [new OhlcvBar(r.StartUtc, 10, 11, 9, 10, 100)]);
            return Task.FromResult(Reply);
        }
    }
    private sealed class NeverHttp : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Unexpected HTTP call in local consumer."); }
    }
}
