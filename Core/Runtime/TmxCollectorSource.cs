#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
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
    private const string QueryWithHistory = """
query getTimeSeriesData($symbol: String!, $interval: Int, $startDateTime: Int, $endDateTime: Int, $historyStart: Int!, $historyEnd: Int!) {
 getTimeSeriesData(symbol: $symbol, interval: $interval, startDateTime: $startDateTime, endDateTime: $endDateTime) { dateTime open high low close volume }
 ruleHistory: getTimeSeriesData(symbol: $symbol, interval: $interval, startDateTime: $historyStart, endDateTime: $historyEnd) { dateTime open high low close volume }
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
        DateTime start = attempt.ExpectedEndUtc.AddMinutes(-1);
        if (start < attempt.SessionOpenUtc || attempt.ExpectedEndUtc > attempt.SessionCloseUtc || started <= attempt.ExpectedEndUtc)
            throw new ArgumentException("The requested minute must have completed before the read starts.");
        var variables = new Dictionary<string,object> { ["symbol"] = attempt.Symbol, ["interval"] = 1,
            ["startDateTime"] = new DateTimeOffset(start).ToUnixTimeSeconds(),
            ["endDateTime"] = new DateTimeOffset(attempt.ExpectedEndUtc).ToUnixTimeSeconds() - 1 };
        if (attempt.RequiredHistory is { } gap)
        {
            gap.Validate(attempt.SessionOpenUtc, start);
            variables["historyStart"] = new DateTimeOffset(gap.StartUtc).ToUnixTimeSeconds();
            variables["historyEnd"] = new DateTimeOffset(gap.EndUtc).ToUnixTimeSeconds() - 1;
        }
        string body = JsonSerializer.Serialize(new { operationName = "getTimeSeriesData",
            query = attempt.RequiredHistory is null ? Query : QueryWithHistory, variables });
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
        ImmutableArray<OhlcvBar> bars = [], historical = []; string? failure = null, historyFailure = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!response.IsSuccessStatusCode) failure = "HttpFailure";
            else
            {
                if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind != JsonValueKind.Null)
                    foreach (var error in errors.EnumerateArray())
                        if (attempt.RequiredHistory is not null && error.TryGetProperty("path", out var path) &&
                            path.ValueKind == JsonValueKind.Array && path.GetArrayLength() > 0 && path[0].ValueEquals("ruleHistory"))
                            historyFailure = "HistoryUnavailable";
                        else failure = "GraphQlFailure";
                if (failure is null)
                {
                    var data = document.RootElement.GetProperty("data");
                    bars = ReadBars(data.GetProperty("getTimeSeriesData"), start, attempt.ExpectedEndUtc);
                    if (attempt.RequiredHistory is { } h && historyFailure is null)
                        try { historical = ReadBars(data.GetProperty("ruleHistory"), h.StartUtc, h.EndUtc); }
                        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or
                            FormatException or KeyNotFoundException or OverflowException) { historyFailure = "HistoryUnavailable"; }
                }
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
            attempt.SessionCloseUtc, attempt.ExpectedEndUtc, deadlineOffset, started, received, clock(), bars, envelope, failure)
            { RequestedStartUtc = start, RequestedHistory = attempt.RequiredHistory, HistoryFailureCode = historyFailure };
        try { DelphiLiveCollectorBatches.Validate(batch); }
        catch (ArgumentException) { batch = batch with { Bars = [], FailureCode = "InvalidBars" }; DelphiLiveCollectorBatches.Validate(batch); }
        if (batch.FailureCode is null && !historical.IsEmpty)
        {
            var combined = batch with { Bars = bars.AddRange(historical) };
            try { DelphiLiveCollectorBatches.Validate(combined); return combined; }
            catch (ArgumentException) { return batch with { HistoryFailureCode = "HistoryUnavailable" }; }
        }
        return batch;
    }

    private static ImmutableArray<OhlcvBar> ReadBars(JsonElement rows, DateTime start, DateTime end)
    {
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 1440) throw new InvalidDataException();
        var result = ImmutableArray.CreateBuilder<OhlcvBar>();
        foreach (var row in rows.EnumerateArray())
        {
            string text = row.GetProperty("dateTime").GetString() ?? "";
            if (!Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)) throw new InvalidDataException();
            DateTime timestamp = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).UtcDateTime;
            if (timestamp < start || timestamp >= end) continue;
            result.Add(new(timestamp, row.GetProperty("open").GetDecimal(), row.GetProperty("high").GetDecimal(),
                row.GetProperty("low").GetDecimal(), row.GetProperty("close").GetDecimal(), row.GetProperty("volume").GetInt64()));
        }
        return result.ToImmutable();
    }
    public void Dispose() => http.Dispose();
}
