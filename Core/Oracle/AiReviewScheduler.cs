#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Core.Trader.DelphiLive;

namespace Core.Oracle;

public sealed record AiReviewTiming(TimeSpan InitialInterval, TimeSpan MidSessionAfterOpen, TimeSpan PreCloseBeforeClose);
public sealed record AiReviewInterval(DateTimeOffset ChangedUtc, TimeSpan Requested, TimeSpan Applied, string Reason);
public enum AiReviewEventKind { EntrySignal, HoldingClosed, RiskPermissionChanged }
public sealed record AiReviewEvent(string Id, AiReviewEventKind Kind, DateTimeOffset ObservedUtc, string EvidenceId);
public enum AiReviewOutcome { Reviewed, DataUnavailable, BudgetUnavailable, Failed, Cancelled }
public sealed record AiReviewRequest(Guid Id, DateOnly TradingDate, DateTimeOffset StartedUtc,
    ImmutableArray<string> Reasons, ImmutableArray<AiReviewEvent> Events, AiReviewInterval Interval);
public sealed record AiReviewAttempt(AiReviewRequest Request, DateTimeOffset CompletedUtc, AiReviewOutcome Outcome);
public sealed record AiReviewScheduleStatus(DateTimeOffset? NextReviewUtc, bool InFlight,
    string? ReviewBlock, AiReviewInterval Interval, int PendingEvents, AiReviewAttempt? LastAttempt);

/// <summary>
/// Single-session, in-memory scheduling only. The caller supplies reviewed calendar bounds and
/// checkpoint timing. No timer, paid client, protection, risk permission or restart recovery lives here.
/// A production host must persist/recover scheduling state before enabling automatic use.
/// </summary>
public sealed class AiReviewScheduler
{
    private static readonly TimeSpan Minimum = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Maximum = TimeSpan.FromMinutes(15);
    private const int MaxSessionEvents = 4096;
    private readonly object sync = new();
    private readonly DelphiLiveSessionBounds session;
    private readonly (string Name, DateTimeOffset At)[] checkpoints;
    private readonly HashSet<string> completedCheckpoints = [];
    private readonly Dictionary<string, AiReviewEvent> seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AiReviewEvent> pending = new(StringComparer.Ordinal);
    private AiReviewInterval interval;
    private AiReviewRequest? active;
    private AiReviewAttempt? lastAttempt;
    private DateTimeOffset? lastClock, lastStarted, lastCompleted, freshUntil;
    private DateTimeOffset retryNotBefore;
    private string? reviewBlock = "FreshReviewRequired";
    private bool eventFault;
    public DateTimeOffset SessionCloseUtc => session.CloseUtc;

    public AiReviewScheduler(ITsxSessionCalendar calendar, DateOnly tradingDate, AiReviewTiming timing)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(timing);
        session = calendar.GetSessionBounds(tradingDate);
        var mid = (DateTimeOffset)session.OpenUtc + timing.MidSessionAfterOpen;
        var preClose = (DateTimeOffset)session.CloseUtc - timing.PreCloseBeforeClose;
        if (timing.InitialInterval < Minimum || timing.InitialInterval > Maximum ||
            timing.MidSessionAfterOpen <= TimeSpan.Zero || timing.PreCloseBeforeClose <= TimeSpan.Zero ||
            mid <= session.OpenUtc || preClose >= session.CloseUtc || mid >= preClose)
            throw new ArgumentException("Supply ordered in-session checkpoints and a one-to-fifteen-minute interval.");
        checkpoints = [("SessionStart", session.OpenUtc), ("MidSession", mid), ("PreClose", preClose)];
        interval = new(session.OpenUtc, timing.InitialInterval, timing.InitialInterval, "Configured initial interval");
        retryNotBefore = session.OpenUtc;
    }

    public AiReviewInterval RequestInterval(DateTimeOffset now, TimeSpan requested, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new ArgumentException("A bounded explanation for review timing is required.");
        lock (sync)
        {
            CheckClock(now);
            interval = new(now, requested, requested < Minimum ? Minimum : requested > Maximum ? Maximum : requested, reason);
            return interval;
        }
    }

    /// <summary>Returns false for an exact duplicate. Events do not establish current account validity.</summary>
    public bool RequestEvent(AiReviewEvent item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 256 ||
            string.IsNullOrWhiteSpace(item.EvidenceId) || item.EvidenceId.Length > 256 ||
            !Enum.IsDefined(item.Kind) || item.ObservedUtc > now || item.ObservedUtc < session.OpenUtc ||
            item.ObservedUtc >= session.CloseUtc || now >= session.CloseUtc)
            throw new ArgumentException("Review events require identified, dated evidence from this session.");
        lock (sync)
        {
            CheckClock(now);
            if (seen.TryGetValue(item.Id, out var previous))
            {
                if (previous == item) return false;
                eventFault = true;
                throw new InvalidOperationException("A review event identity cannot change its evidence.");
            }
            if (seen.Count >= MaxSessionEvents)
            {
                eventFault = true;
                throw new InvalidOperationException("Review event capacity requires host attention.");
            }
            seen.Add(item.Id, item);
            pending.Add(item.Id, item);
            return true;
        }
    }

    public AiReviewScheduleStatus Read(DateTimeOffset now)
    {
        lock (sync)
        {
            CheckClock(now);
            return Status(now);
        }
    }

    /// <summary>Atomically admits one preparation/review; actual spending admission stays in AiCallJournal.</summary>
    public AiReviewRequest? TryBegin(DateTimeOffset now)
    {
        lock (sync)
        {
            CheckClock(now);
            if (active != null || eventFault || now < session.OpenUtc || now >= session.CloseUtc || Due() > now)
                return null;
            var reasons = checkpoints.Where(c => c.At <= now && !completedCheckpoints.Contains(c.Name))
                .Select(c => c.Name).ToList();
            if (lastCompleted.HasValue && now >= lastCompleted.Value + interval.Applied) reasons.Add("IntervalElapsed");
            var events = pending.Values.OrderBy(e => e.ObservedUtc).ThenBy(e => e.Id, StringComparer.Ordinal).ToImmutableArray();
            reasons.AddRange(events.Select(e => e.Kind.ToString()).Distinct());
            if (reasons.Count == 0) reasons.Add("RetryReview");
            active = new(Guid.NewGuid(), session.TradingDate, now, reasons.ToImmutableArray(), events, interval);
            if (!events.IsEmpty) reviewBlock ??= "PendingEvents";
            pending.Clear();
            lastStarted = now;
            return active;
        }
    }

    /// <summary>Reviewed means the caller validated a fresh result, never that trading is allowed.</summary>
    public void Complete(Guid requestId, DateTimeOffset now, AiReviewOutcome outcome, DateTimeOffset? expiresUtc = null)
    {
        lock (sync)
        {
            CheckClock(now);
            if (active is null || active.Id != requestId || !Enum.IsDefined(outcome))
                throw new InvalidOperationException("Only the current review can complete once.");
            if (outcome == AiReviewOutcome.Reviewed && (expiresUtc is null || expiresUtc <= now || now >= session.CloseUtc))
                outcome = AiReviewOutcome.DataUnavailable;
            if (outcome == AiReviewOutcome.Reviewed)
            {
                foreach (var reason in active.Reasons.Where(r => checkpoints.Any(c => c.Name == r)))
                    completedCheckpoints.Add(reason);
                freshUntil = expiresUtc < session.CloseUtc ? expiresUtc : session.CloseUtc;
                reviewBlock = null;
            }
            else
            {
                foreach (var item in active.Events) pending.TryAdd(item.Id, item);
                freshUntil = null;
                reviewBlock = outcome.ToString();
            }
            lastAttempt = new(active, now, outcome);
            active = null;
            lastCompleted = now;
            // Pace from completion too: slow preparation/calls must not permit adjacent paid calls.
            retryNotBefore = now + Minimum;
        }
    }

    private DateTimeOffset Due()
    {
        DateTimeOffset due = lastCompleted.HasValue ? lastCompleted.Value + interval.Applied : session.OpenUtc;
        foreach (var checkpoint in checkpoints)
            if (!completedCheckpoints.Contains(checkpoint.Name) && checkpoint.At < due) due = checkpoint.At;
        if (pending.Count > 0) due = session.OpenUtc;
        if (lastStarted.HasValue && due < lastStarted.Value + Minimum) due = lastStarted.Value + Minimum;
        if (due < retryNotBefore) due = retryNotBefore;
        return due;
    }

    private AiReviewScheduleStatus Status(DateTimeOffset now)
    {
        var next = Due();
        string? block = eventFault ? "EventHistoryRequiresReview" : now < session.OpenUtc || now >= session.CloseUtc
            ? "OutsideSession" : reviewBlock ?? (pending.Count > 0 ? "PendingEvents" : freshUntil <= now ? "FreshReviewRequired" : null);
        return new(active != null || eventFault || next >= session.CloseUtc || now >= session.CloseUtc ? null : next,
            active != null, block, interval, pending.Count, lastAttempt);
    }

    private void CheckClock(DateTimeOffset now)
    {
        if (lastClock.HasValue && now < lastClock.Value)
            throw new InvalidOperationException("Review scheduling cannot move backwards in time.");
        lastClock = now;
    }
}
