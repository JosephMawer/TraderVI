#nullable enable
using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

/// <summary>Explicitly invoked provider adapter; construction never requests market data.</summary>
public sealed class TmxDelphiLiveMarketDataSource : IDelphiLiveMarketDataSource, IDisposable
{
    private readonly TmxClient client = new();

    public async Task<DelphiLiveMarketDataReceipt> GetExactFiveMinuteBarAsync(
        DelphiLiveMarketDataRequest request, CancellationToken cancellationToken = default)
    {
        var batch = await client.GetIntradayTimeSeriesBatchAsync(request.Symbol, 5,
            request.BarStartUtc, request.BarEndUtc, cancellationToken);
        return FromBatch(request, batch);
    }

    internal static DelphiLiveMarketDataReceipt FromBatch(DelphiLiveMarketDataRequest request, TmxIntradayBatch batch)
    {
        if (batch.Symbol != request.Symbol || batch.IntervalMinutes != 5 ||
            batch.RequestedStartUtc != request.BarStartUtc || batch.RequestedEndUtc != request.BarEndUtc)
            throw new ArgumentException("Provider batch does not match its exact collection request.", nameof(batch));
        var exact = batch.Bars.Where(b => b.TimestampUtc == request.BarStartUtc).ToArray();
        bool conflict = exact.Length > 1 && exact.Any(b => b != exact[0]);
        string disposition = conflict ? "StructurallyInvalid" : exact.Length > 0 ? "OperationalOnTime" :
            batch.Bars.Any() && batch.Bars.Max(b => b.TimestampUtc) < request.BarStartUtc ? "StaleNoNewBar" : "NoCompletedBar";
        return new(request, conflict ? null : exact.FirstOrDefault(), batch.ReceivedUtc, disposition)
        {
            ProviderAttemptCount = batch.AttemptCount,
            ProviderRequestCount = batch.RequestCount,
            ProviderFetchStartedUtc = batch.FetchStartedUtc
        };
    }

    public async Task<DelphiLiveQuoteReceipt> GetQuoteAsync(DelphiLiveQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var quotes = await client.GetQuotesBySymbolsAsync([request.Symbol], cancellationToken);
        DateTime received = DateTime.UtcNow;
        var matching = quotes.Where(q => string.Equals(q.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase)).ToArray();
        var quote = matching.Length == 1 ? matching[0] : null;
        return new(request, quote?.Price, quote?.Bid, quote?.Ask, received, "TmxQuoteFieldsV1");
    }

    public void Dispose() => client.Dispose();
}
