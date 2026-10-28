#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

public sealed record CollectorSubscriptionSnapshot(DateTime SessionOpenUtc, DateTime SessionCloseUtc,
    ImmutableArray<string> Candidates, ImmutableArray<string> Holdings, ImmutableArray<string> PendingExits)
{
    public ImmutableArray<string> Symbols()
    {
        DelphiLiveFiveMinuteBar.RequireUtc(SessionOpenUtc, nameof(SessionOpenUtc));
        DelphiLiveFiveMinuteBar.RequireUtc(SessionCloseUtc, nameof(SessionCloseUtc));
        if (SessionCloseUtc <= SessionOpenUtc || SessionCloseUtc - SessionOpenUtc > TimeSpan.FromHours(24) ||
            Candidates.IsDefault || Holdings.IsDefault || PendingExits.IsDefault)
            throw new ArgumentException("Complete dated subscription evidence is required.");
        // Protection subscriptions survive an empty candidate list and entry pauses.
        var symbols = new[] { "XIU" }.Concat(Holdings).Concat(PendingExits).Concat(Candidates)
            .Select(DelphiLiveFiveMinuteBar.RequireCanonicalSymbol).Distinct(StringComparer.Ordinal).ToImmutableArray();
        if (symbols.Any(s => s.Length > 20)) throw new ArgumentException("Invalid subscription symbol.");
        return symbols;
    }
}

public sealed record CollectorLimits(TimeSpan DeadlineOffset, TimeSpan RequestTimeout,
    int MaximumSymbols, int MaximumRequestsPerSession)
{
    public void Validate()
    {
        if (DeadlineOffset <= TimeSpan.Zero || DeadlineOffset >= TimeSpan.FromMinutes(1) ||
            RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromSeconds(30) ||
            MaximumSymbols is < 1 or > 500 || MaximumRequestsPerSession is < 1 or > 200000)
            throw new ArgumentException("Explicit bounded collector limits are required.");
    }
}

public sealed record CollectorAttempt(Guid RequestId, string SourceContract, string Symbol,
    DateTime SessionOpenUtc, DateTime SessionCloseUtc, DateTime ExpectedEndUtc, DateTime ReservedUtc);

public interface IIntradayCollectorStore
{
    Task<MarketCollectorLease?> AcquireAsync(string source, Guid owner, CancellationToken ct);
    Task<bool> RenewAsync(MarketCollectorLease lease, CancellationToken ct);
    Task ReleaseAsync(MarketCollectorLease lease, CancellationToken ct);
    // Persist intent BEFORE network I/O. Once attempted, a minute is never automatically retried.
    Task<CollectorAttempt?> ReserveAsync(MarketCollectorLease lease, CollectorSubscriptionSnapshot subscriptions,
        string symbol, DateTime endUtc, int requestBudget, CancellationToken ct);
    Task SaveAsync(MarketCollectorLease lease, DelphiLiveCollectorBatch batch, CancellationToken ct);
}

public interface IIntradayCollectorSource
{
    string SourceContract { get; }
    Task<DelphiLiveCollectorBatch> FetchAsync(CollectorAttempt attempt, TimeSpan deadlineOffset, CancellationToken ct);
}

public sealed record CollectorTickResult(string State, int Attempted, int Saved, int Failed);

/// <summary>
/// Single-flight collector loop with no account/order capability. Fresh endpoints have priority;
/// the bounded same-session source response also supplies recovery history. No catch-up request loop.
/// </summary>
public sealed class IntradayCollector
{
    private readonly IIntradayCollectorStore store;
    private readonly IIntradayCollectorSource source;
    private readonly CollectorLimits limits;
    private readonly Func<DateTime> clock;
    private readonly Guid owner = Guid.NewGuid();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly System.Collections.Generic.Dictionary<string, DateTime> lastStarts = new(StringComparer.Ordinal);
    private readonly DateTime createdUtc;
    private MarketCollectorLease? lease;

    public IntradayCollector(IIntradayCollectorStore store, IIntradayCollectorSource source,
        CollectorLimits limits, Func<DateTime>? clock = null)
    {
        this.store = store; this.source = source; this.limits = limits; limits.Validate();
        this.clock = clock ?? (() => DateTime.UtcNow);
        createdUtc = this.clock();
        DelphiLiveFiveMinuteBar.RequireUtc(createdUtc, nameof(clock));
        if (string.IsNullOrWhiteSpace(source.SourceContract)) throw new ArgumentException("Source identity required.");
    }

    public async Task<CollectorTickResult> TickAsync(CollectorSubscriptionSnapshot subscriptions, CancellationToken ct = default)
    {
        if (!await gate.WaitAsync(0, ct)) return new("Busy", 0, 0, 0);
        try
        {
            var symbols = subscriptions.Symbols();
            if (symbols.Length > limits.MaximumSymbols) return new("SubscriptionBudgetExceeded", 0, 0, 0);
            DateTime now = clock();
            if (now < createdUtc) throw new InvalidOperationException("Collector clock moved backwards.");
            // Retrieve the bar ending at close within its original deadline. This is evidence
            // collection only; it does not extend the trading session or any entry authority.
            if (now <= subscriptions.SessionOpenUtc || now >= subscriptions.SessionCloseUtc.Add(limits.DeadlineOffset)) return new("OutsideSession", 0, 0, 0);
            // A restart cannot immediately repeat an in-flight request from a previous owner.
            // Persisted history remains readable during this transport cooldown; confirmation is not reset.
            if (now < createdUtc.AddMinutes(1)) return new("StartupTransportCooldown", 0, 0, 0);
            lease ??= await store.AcquireAsync(source.SourceContract, owner, ct);
            if (lease is null) return new("OtherCollectorOwnsLease", 0, 0, 0);
            if (!await store.RenewAsync(lease, ct)) { lease = null; return new("LeaseLost", 0, 0, 0); }
            var end = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
            if (end <= subscriptions.SessionOpenUtc || now <= end) return new("WaitingForCompletedMinute", 0, 0, 0);
            if (now >= end.Add(limits.DeadlineOffset)) return new("MinuteDeadlineElapsed", 0, 0, 0);
            int attempted = 0, saved = 0, failed = 0;
            foreach (string symbol in symbols)
            {
                ct.ThrowIfCancellationRequested();
                now = clock();
                if (now >= end.Add(limits.DeadlineOffset)) break;
                if (lastStarts.TryGetValue(symbol, out var prior) && now < prior.AddMinutes(1)) continue;
                if (!await store.RenewAsync(lease, ct)) { lease = null; return new("LeaseLost", attempted, saved, failed); }
                var attempt = await store.ReserveAsync(lease, subscriptions, symbol, end, limits.MaximumRequestsPerSession, ct);
                if (attempt is null) continue;
                attempted++;
                var started = clock(); lastStarts[symbol] = started;
                DelphiLiveCollectorBatch receipt;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(limits.RequestTimeout);
                try
                {
                    receipt = await source.FetchAsync(attempt, limits.DeadlineOffset, timeout.Token);
                    if (receipt.RequestId != attempt.RequestId || receipt.SourceContract != source.SourceContract ||
                        receipt.Symbol != symbol || receipt.SessionOpenUtc != subscriptions.SessionOpenUtc ||
                        receipt.SessionCloseUtc != subscriptions.SessionCloseUtc || receipt.ExpectedEndUtc != end ||
                        receipt.DeadlineOffset != limits.DeadlineOffset || receipt.FetchStartedUtc < started)
                        throw new InvalidOperationException("Source receipt identity changed.");
                    DelphiLiveCollectorBatches.Validate(receipt);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch
                {
                    var ended = clock();
                    receipt = new(attempt.RequestId, source.SourceContract, symbol, subscriptions.SessionOpenUtc,
                        subscriptions.SessionCloseUtc, end, limits.DeadlineOffset, started, ended, ended, [], "{}", "SourceReadFailed");
                }
                // SQL append fences again. A storage error stops the cycle; never make a second request.
                await store.SaveAsync(lease, receipt, ct);
                saved++; if (receipt.FailureCode is not null) failed++;
            }
            return new(failed > 0 ? "SourceAttention" : attempted == 0 ? "NoNewRequestAdmitted" :
                attempted < symbols.Length ? "PartialCollection" : "Collected", attempted, saved, failed);
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { if (lease is not null) await store.ReleaseAsync(lease, ct); lease = null; }
        finally { gate.Release(); }
    }
}
