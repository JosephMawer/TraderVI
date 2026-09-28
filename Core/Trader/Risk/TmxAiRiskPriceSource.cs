#nullable enable
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using GraphQL.Client.Http;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Risk;

public enum AiRiskPriceStatus { Fresh, MissingCompletedPrice, Stale, InvalidResponse, SourceUnavailable }

/// <summary>One actual completed bar, with separate market and transport times. Not a portfolio valuation.</summary>
public sealed record AiRiskPriceEvidence(string Symbol, OhlcvBar Bar, DateTime FetchStartedUtc,
    DateTime ReceivedUtc, DateTime PriceExpiresUtc, DateTime ReceiptExpiresUtc)
{
    public DateTime CompletedUtc => Bar.TimestampUtc.AddMinutes(1);
    public override string ToString() => "AI risk price evidence (financial data omitted)";
}

public sealed record AiRiskPriceResult(string Symbol, AiRiskPriceStatus Status, AiRiskPriceEvidence? Evidence)
{
    public override string ToString() => $"AI risk price result: {Status} (financial data omitted)";
}

/// <summary>Caller must still reconcile the entire owned account and persist evidence before assessing risk.</summary>
public sealed record AiRiskPriceRead(DateOnly TradingDate, DateTime RequestedEndUtc, DateTime CheckedUtc,
    DateTime SessionCloseUtc, ImmutableArray<AiRiskPriceResult> Results)
{
    // Recheck at use time; completing a later account read cannot renew these prices.
    public bool IsCompleteAndFreshAt(DateTime nowUtc) => nowUtc.Kind == DateTimeKind.Utc && nowUtc >= CheckedUtc &&
        nowUtc < SessionCloseUtc && !Results.IsDefaultOrEmpty && Results.All(r => r.Status == AiRiskPriceStatus.Fresh &&
            r.Evidence is { } e && nowUtc <= e.PriceExpiresUtc && nowUtc <= e.ReceiptExpiresUtc);
    public override string ToString() => "AI risk price read (financial data omitted; not trading authority)";
}

/// <summary>
/// Explicit one-minute TMX source for future AI account valuation. No timer, cache fallback, SQL,
/// risk-journal or order writes. Construction makes no requests and does not own the supplied client.
/// Age limits must be supplied by the caller; this adapter selects no production freshness policy.
/// Up to 32 symbols per read, one bounded request each; existing transport makes at most three attempts.
/// </summary>
public sealed class TmxAiRiskPriceSource
{
    private readonly Func<string, DateTime, DateTime, CancellationToken, Task<TmxIntradayBatch>> fetch;
    private readonly ITsxSessionCalendar calendar;
    private readonly TimeProvider clock;
    private readonly TimeSpan maximumPriceAge, maximumReceiptAge, requestTimeout;

    public TmxAiRiskPriceSource(TmxClient client, ITsxSessionCalendar calendar, TimeSpan maximumPriceAge,
        TimeSpan maximumReceiptAge, TimeSpan requestTimeout, TimeProvider? clock = null)
        : this((symbol, start, end, ct) => client.GetIntradayTimeSeriesBatchAsync(symbol, 1, start, end, ct),
            calendar, maximumPriceAge, maximumReceiptAge, requestTimeout, clock)
    { ArgumentNullException.ThrowIfNull(client); }

    internal TmxAiRiskPriceSource(Func<string, DateTime, DateTime, CancellationToken, Task<TmxIntradayBatch>> fetch,
        ITsxSessionCalendar calendar, TimeSpan maximumPriceAge, TimeSpan maximumReceiptAge, TimeSpan requestTimeout,
        TimeProvider? clock = null)
    {
        this.fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        this.calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        this.clock = clock ?? TimeProvider.System;
        if (maximumPriceAge <= TimeSpan.Zero || maximumPriceAge > TimeSpan.FromDays(1) ||
            maximumReceiptAge <= TimeSpan.Zero || maximumReceiptAge > TimeSpan.FromDays(1) ||
            requestTimeout <= TimeSpan.Zero || requestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentException("Explicit bounded price/receipt ages and request timeout are required.");
        this.maximumPriceAge = maximumPriceAge;
        this.maximumReceiptAge = maximumReceiptAge;
        this.requestTimeout = requestTimeout;
    }

    public async Task<AiRiskPriceRead> ReadAsync(DateOnly date, ImmutableArray<string> symbols, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (symbols.IsDefaultOrEmpty || symbols.Length > 32 || symbols.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("One to 32 explicit canonical stock symbols required.", nameof(symbols));
        var targets = symbols.Select(s => s.Trim().ToUpperInvariant()).Order(StringComparer.Ordinal).ToArray();
        if (targets.Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw new ArgumentException("Duplicate stock symbols are ambiguous.", nameof(symbols));
        DateTime began = clock.GetUtcNow().UtcDateTime;
        var bounds = calendar.GetSessionBounds(date);
        DateTime end = Minute(began);
        if (date != DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(began, ReviewedTsxSessionCalendar.Toronto)) ||
            began < bounds.OpenUtc.AddMinutes(1) || began >= bounds.CloseUtc)
            throw new InvalidOperationException("A current reviewed regular session with a completed minute is required.");
        DateTime start = Minute(end.Subtract(maximumPriceAge).AddMinutes(-1));
        if (start < bounds.OpenUtc) start = bounds.OpenUtc;
        var results = ImmutableArray.CreateBuilder<AiRiskPriceResult>();
        foreach (string symbol in targets)
        {
            ct.ThrowIfCancellationRequested();
            DateTime requestBegan = clock.GetUtcNow().UtcDateTime;
            if (requestBegan < began || requestBegan >= bounds.CloseUtc)
                throw new InvalidOperationException("Clock/session changed during price collection.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(requestTimeout);
            try
            {
                var batch = await fetch(symbol, start, end, timeout.Token).WaitAsync(timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                results.Add(Qualify(symbol, start, end, requestBegan, clock.GetUtcNow().UtcDateTime, batch));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { results.Add(new(symbol, AiRiskPriceStatus.SourceUnavailable, null)); }
            catch (Exception ex) when (ex is HttpRequestException or GraphQLHttpRequestException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
            { results.Add(new(symbol, AiRiskPriceStatus.SourceUnavailable, null)); }
        }
        ct.ThrowIfCancellationRequested();
        DateTime checkedUtc = clock.GetUtcNow().UtcDateTime;
        if (checkedUtc < began) throw new InvalidOperationException("Clock moved backwards during price collection.");
        return new(date, end, checkedUtc, bounds.CloseUtc, results.Select(r => r.Evidence is { } e &&
            (checkedUtc > e.PriceExpiresUtc || checkedUtc > e.ReceiptExpiresUtc || checkedUtc >= bounds.CloseUtc)
            ? r with { Status = AiRiskPriceStatus.Stale } : r).ToImmutableArray());
    }

    private AiRiskPriceResult Qualify(string symbol, DateTime start, DateTime end, DateTime began, DateTime now, TmxIntradayBatch b)
    {
        AiRiskPriceResult Invalid() => new(symbol, AiRiskPriceStatus.InvalidResponse, null);
        if (b is null || b.Symbol != symbol || b.IntervalMinutes != 1 || b.RequestedStartUtc.Kind != DateTimeKind.Utc ||
            b.RequestedEndUtc.Kind != DateTimeKind.Utc || b.RequestedStartUtc != start || b.RequestedEndUtc != end ||
            b.FetchStartedUtc.Kind != DateTimeKind.Utc || b.FetchStartedUtc < began || b.ReceivedUtc.Kind != DateTimeKind.Utc ||
            b.ReceivedUtc < b.FetchStartedUtc || b.ReceivedUtc > now || now < began || b.RequestCount != 1 ||
            b.AttemptCount is < 1 or > 3 || b.Bars is null || b.Bars.Any(x => x is null || x.TimestampUtc.Kind != DateTimeKind.Utc ||
                x.TimestampUtc != Minute(x.TimestampUtc) || x.TimestampUtc < start.AddMinutes(-1) || x.TimestampUtc > end.AddMinutes(1) ||
                x.Low <= 0 || x.High < x.Low || x.Open < x.Low || x.Open > x.High || x.Close < x.Low || x.Close > x.High || x.Volume < 0) ||
            b.Bars.Select(x => x.TimestampUtc).Distinct().Count() != b.Bars.Count)
            return Invalid();
        var bar = b.Bars.Where(x => x.TimestampUtc >= start && x.TimestampUtc.AddMinutes(1) <= end &&
            x.TimestampUtc.AddMinutes(1) <= b.ReceivedUtc).OrderBy(x => x.TimestampUtc).LastOrDefault();
        if (bar is null) return new(symbol, AiRiskPriceStatus.MissingCompletedPrice, null);
        var evidence = new AiRiskPriceEvidence(symbol, bar, b.FetchStartedUtc, b.ReceivedUtc,
            bar.TimestampUtc.AddMinutes(1).Add(maximumPriceAge), b.ReceivedUtc.Add(maximumReceiptAge));
        return new(symbol, now > evidence.PriceExpiresUtc || now > evidence.ReceiptExpiresUtc
            ? AiRiskPriceStatus.Stale : AiRiskPriceStatus.Fresh, evidence);
    }

    private static DateTime Minute(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
}
