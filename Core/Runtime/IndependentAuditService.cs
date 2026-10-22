#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Resumable off-hours verification. An absent or incomplete receipt never means clean.</summary>
public sealed class IndependentAuditService(ReviewedTsxSessionCalendar calendar, IDelphiLiveNotifier notifier)
{
    private readonly EngineIntegrityRepository repository = new();
    private readonly EngineHistoryVerifier verifier = new(calendar);
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile bool closing;
    public void BeginStop() => closing = true;
    public async Task DrainAsync() { await gate.WaitAsync().ConfigureAwait(false); gate.Release(); }
    public async Task TickAsync()
    {
        if (!await gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (closing) return;
            DateTime now = DateTime.UtcNow;
            EngineIntegrityStatus status;
            try { status = await repository.ReadStatusAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException or ArgumentException)
            {
                await repository.RaiseAsync(EngineIntegrityScope.Global, null, "AuditReceipt", "AuditReceiptUnverifiable", true).ConfigureAwait(false);
                throw new InvalidOperationException("Historical audit receipts need recovery verification.");
            }
            if (!await repository.GuardsAvailableAsync().ConfigureAwait(false))
            {
                await repository.RaiseAsync(EngineIntegrityScope.Global, null, "ImmutableGuards", "ImmutableGuardsUnavailable", true).ConfigureAwait(false);
                throw new InvalidOperationException("Immutable history guards are unavailable.");
            }
            await NotifyAsync(status, now).ConfigureAwait(false);
            bool recovery = status.Runs.Any(r => r.Plan.Kind == EngineAuditKind.Recovery && r.Status == "Running");
            DateOnly local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")));
            if (calendar.IsRegularSession(local))
            {
                var bounds = calendar.GetSessionBounds(local);
                if (!recovery && now >= bounds.OpenUtc.AddMinutes(-30) && now < bounds.CloseUtc.AddMinutes(10)) return;
            }
            // The last fully closed session defines nightly work, including weekends and the next pre-open window.
            DateTime? closed = null;
            for (int days = 0; days < 10; days++)
            {
                var date = local.AddDays(-days);
                if (!calendar.IsRegularSession(date)) continue;
                var end = calendar.GetSessionBounds(date).CloseUtc.AddMinutes(10);
                if (end <= now) { closed = end; break; }
            }
            if (closed is null) throw new InvalidOperationException("The reviewed calendar cannot establish the audit session boundary.");
            if (!recovery && !status.Runs.Any(r => r.Plan.Kind == EngineAuditKind.NewHistory && r.Plan.BoundaryUtc >= closed))
                await repository.StartAsync(EngineAuditKind.NewHistory).ConfigureAwait(false);
            if (!recovery && !status.Runs.Any(r => r.Plan.Kind == EngineAuditKind.Archive && now - r.Plan.BoundaryUtc < TimeSpan.FromDays(7)))
                await repository.StartAsync(EngineAuditKind.Archive).ConfigureAwait(false);
            status = await repository.ReadStatusAsync().ConfigureAwait(false);
            var run = status.Runs.Where(r => r.Status == "Running").OrderByDescending(r => r.Plan.Kind == EngineAuditKind.Recovery)
                .ThenBy(r => r.UpdatedUtc).FirstOrDefault();
            if (run is null) return;
            EngineAuditProgress next;
            try
            {
                next = await EngineAuditBatch.VerifyAsync(run, verifier.VerifyNextAsync,
                    () => !closing && (run.Plan.Kind == EngineAuditKind.Recovery || OutsideTradingWindow(DateTime.UtcNow))).ConfigureAwait(false);
            }
            catch (EngineAuditBatchFailure failure)
            {
                // Classification remains conservative unless isolation is proven. A broker row has an exact owner;
                // shared/paper dependencies are uncertain and therefore create a global hold.
                var target = run.Plan.Targets[failure.Verified.TargetIndex];
                bool broker = target.Family == EngineAuditFamily.BrokerRevisions;
                await repository.RaiseAsync(broker ? EngineIntegrityScope.Broker : EngineIntegrityScope.Global,
                    broker ? target.OwnerId : null, $"{target.Family}/{target.OwnerId:D}/{failure.Verified.NextRevision}/{failure.Verified.LastKey}",
                    "ArchiveVerificationFailed", protectionUntrusted: true).ConfigureAwait(false);
                await repository.SaveProgressAsync(run, failure.Verified, failed: true).ConfigureAwait(false);
                await notifier.NotifyAsync(new("Error", "IntegrityIncident",
                    "Historical verification found a problem. A durable hold blocks affected new exposure; affected automatic protection is unavailable until recovery is verified.")).ConfigureAwait(false);
                return;
            }
            if (next != run.Progress) await repository.SaveProgressAsync(run, next).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task<Guid> StartRecoveryAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (closing) throw new InvalidOperationException("Engine is stopping.");
            return (await repository.StartAsync(EngineAuditKind.Recovery).ConfigureAwait(false)).RunId;
        }
        finally { gate.Release(); }
    }
    public async Task ClearAsync(Guid recovery, ImmutableArray<Guid> incidents, string reason)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (closing) throw new InvalidOperationException("Engine is stopping.");
            await repository.ClearVerifiedAsync(recovery, incidents, reason).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private Task NotifyAsync(EngineIntegrityStatus status, DateTime now) => notifier.NotifyAsync(new(
        status.Incidents.IsEmpty ? "Information" : "Error", "IntegrityCoverage", status.Summary(now)));
    private bool OutsideTradingWindow(DateTime now)
    {
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, ReviewedTsxSessionCalendar.Toronto));
        if (!calendar.IsRegularSession(local)) return true;
        var bounds = calendar.GetSessionBounds(local);
        return now < bounds.OpenUtc.AddMinutes(-30) || now >= bounds.CloseUtc.AddMinutes(10);
    }
}
