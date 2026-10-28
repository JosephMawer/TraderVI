#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>One bounded public read, no transport retries, redirects, cookies or credentials.</summary>
public sealed class TmxCollectorSource : IIntradayCollectorSource, IDisposable
{
    public const string Contract = "TmxMinuteCollectorV1";
    public string SourceContract => Contract;
    private readonly HttpClient http;
    private readonly Func<DateTime> clock;
    private const string Query = """
query getTimeSeriesData($symbol: String!, $interval: Int, $startDateTime: Int, $endDateTime: Int) {
 getTimeSeriesData(symbol: $symbol, interval: $interval, startDateTime: $startDateTime, endDateTime: $endDateTime) {
  dateTime open high low close volume
 }
}
""";
    public TmxCollectorSource() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }) { }
    internal TmxCollectorSource(HttpMessageHandler handler, Func<DateTime>? clock = null)
    {
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("Origin", "https://money.tmx.com");
        http.DefaultRequestHeaders.Referrer = new Uri("https://money.tmx.com/");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public async Task<DelphiLiveCollectorBatch> FetchAsync(CollectorAttempt attempt, TimeSpan deadlineOffset, CancellationToken ct)
    {
        if (attempt.SourceContract != Contract) throw new ArgumentException("Wrong source contract.");
        DateTime started = clock();
        string body = JsonSerializer.Serialize(new { operationName = "getTimeSeriesData", query = Query,
            variables = new { symbol = attempt.Symbol, interval = 1,
                startDateTime = new DateTimeOffset(attempt.SessionOpenUtc).ToUnixTimeSeconds(),
                endDateTime = new DateTimeOffset(attempt.ExpectedEndUtc).ToUnixTimeSeconds() } });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://app-money.tmx.com/graphql")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + read > 1048576) throw new InvalidDataException("Collector response exceeds limit.");
            bytes.Write(buffer, 0, read);
        }
        DateTime received = clock();
        string raw = Encoding.UTF8.GetString(bytes.ToArray());
        // Invalid JSON is still saved as explicit failure evidence, never accepted as prices.
        string envelope = raw;
        ImmutableArray<OhlcvBar> bars = []; string? failure = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!response.IsSuccessStatusCode) failure = "HttpFailure";
            else if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind != JsonValueKind.Null && errors.GetArrayLength() > 0)
                failure = "GraphQlFailure";
            else
            {
                var rows = document.RootElement.GetProperty("data").GetProperty("getTimeSeriesData");
                if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 1440) throw new InvalidDataException();
                var builder = ImmutableArray.CreateBuilder<OhlcvBar>();
                foreach (var row in rows.EnumerateArray())
                {
                    string text = row.GetProperty("dateTime").GetString() ?? "";
                    if (!Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)) throw new InvalidDataException();
                    DateTime timestamp = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).UtcDateTime;
                    builder.Add(new(timestamp, row.GetProperty("open").GetDecimal(), row.GetProperty("high").GetDecimal(),
                        row.GetProperty("low").GetDecimal(), row.GetProperty("close").GetDecimal(), row.GetProperty("volume").GetInt64()));
                }
                bars = builder.ToImmutable();
            }
        }
        catch
        {
            failure = "InvalidResponse"; bars = [];
            envelope = JsonSerializer.Serialize(new { invalidRaw = raw });
            // Keep the receipt bounded even when escaping expands malformed input.
            if (Encoding.UTF8.GetByteCount(envelope) > 1048576)
                envelope = JsonSerializer.Serialize(new { invalidRawSha256 = DelphiLiveCollectorBatches.Hash(raw), oversizedEscapedEvidence = true });
        }
        var batch = new DelphiLiveCollectorBatch(attempt.RequestId, Contract, attempt.Symbol, attempt.SessionOpenUtc,
            attempt.SessionCloseUtc, attempt.ExpectedEndUtc, deadlineOffset, started, received, clock(), bars, envelope, failure);
        try { DelphiLiveCollectorBatches.Validate(batch); }
        catch (ArgumentException) { batch = batch with { Bars = [], FailureCode = "InvalidBars" }; DelphiLiveCollectorBatches.Validate(batch); }
        return batch;
    }
    public void Dispose() => http.Dispose();
}
