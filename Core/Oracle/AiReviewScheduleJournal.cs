#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Trader.DelphiLive;

namespace Core.Oracle;

public sealed record AiReviewScheduleScope(Guid AccountScopeId, Guid PortfolioId, string AccountMode);

/// <summary>
/// Account-scoped, single-owner schedule history. Persist before admitting model work. Recovery
/// replays transitions and marks an interrupted attempt without retrying its paid call identity.
/// Checksums detect corruption, not malicious edits or rollback between process lifetimes.
/// </summary>
public sealed class AiReviewScheduleJournal : IAiReviewSchedule, IDisposable
{
    private enum Kind { Interval, Event, Begin, Complete, Recover, Advance }
    private sealed record Command(Kind Kind, DateTimeOffset At, Guid? RequestId = null,
        AiReviewEvent? Event = null, TimeSpan? Interval = null, string? Reason = null,
        AiReviewOutcome? Outcome = null, DateTimeOffset? ExpiresUtc = null, DateOnly? Date = null,
        DateTime? OpenUtc = null, DateTime? CloseUtc = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AiNextReview? NextReview = null);
    private sealed record History(int Version, AiReviewScheduleScope Scope, DateOnly InitialDate,
        DateTime OpenUtc, DateTime CloseUtc, AiReviewTiming Timing, DateTimeOffset CreatedUtc, List<Command> Commands);
    private sealed record Envelope(string Payload, string Sha256);
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private const int MaximumBytes = 32_000_000;
    private readonly object sync = new();
    private readonly string path;
    private readonly FileStream lease;
    private readonly ITsxSessionCalendar calendar;
    private readonly HashSet<Guid> requestIds = [];
    private History history = null!;
    private AiReviewScheduler scheduler = null!;
    private string knownHash = "";
    private bool disposed, faulted;
    public AiReviewScheduleScope Scope { get; }
    public DateTimeOffset SessionCloseUtc { get { lock (sync) { Ensure(); return scheduler.SessionCloseUtc; } } }

    public AiReviewScheduleJournal(string directory, AiReviewScheduleScope scope, ITsxSessionCalendar calendar,
        DateOnly initialDate, AiReviewTiming timing, DateTimeOffset now, bool initializeNew = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(timing);
        if (!Path.IsPathFullyQualified(directory) || scope.AccountScopeId == Guid.Empty || scope.PortfolioId == Guid.Empty ||
            scope.AccountMode is not ("paper" or "live"))
            throw new ArgumentException("An absolute private path and exact account/portfolio/mode scope are required.");
        Scope = scope;
        this.calendar = calendar;
        path = Path.Combine(directory, "ai-review-schedule.json");
        if (initializeNew) Directory.CreateDirectory(directory);
        lease = new FileStream(Path.Combine(directory, "ai-review-schedule.lock"),
            initializeNew ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (initializeNew)
            {
                if (Path.Exists(path) || Path.Exists(path + ".pending"))
                    throw new InvalidDataException("Scheduling history already exists; reviewed recovery is required.");
                var bounds = calendar.GetSessionBounds(initialDate);
                scheduler = new(calendar, initialDate, timing);
                scheduler.Read(now);
                history = new(1, scope, initialDate, bounds.OpenUtc, bounds.CloseUtc, timing, now, []);
                Save(history);
            }
            else
            {
                history = Load();
                if (history.Version != 1 || history.Scope != scope || history.InitialDate != initialDate || history.Timing != timing || history.Commands is null)
                    throw new InvalidDataException("Scheduling scope or configuration differs from saved history.");
                VerifyBounds(initialDate, history.OpenUtc, history.CloseUtc);
                scheduler = new(calendar, initialDate, timing);
                scheduler.Read(history.CreatedUtc);
                foreach (var command in history.Commands) Replay(command);
                // Saved success is not permission to resume after a process gap. Preserve old failures.
                scheduler.Recover(now);
                Append(new(Kind.Recover, now));
            }
        }
        catch { lease.Dispose(); throw; }
    }

    public AiReviewScheduleStatus Read(DateTimeOffset now)
    {
        lock (sync) { Ensure(); return scheduler.Read(now); }
    }

    public AiReviewInterval RequestInterval(DateTimeOffset now, TimeSpan requested, string reason)
    {
        lock (sync)
        {
            Ensure();
            var result = scheduler.RequestInterval(now, requested, reason);
            Append(new(Kind.Interval, now, Interval: requested, Reason: reason));
            return result;
        }
    }

    public bool RequestEvent(AiReviewEvent item, DateTimeOffset now)
    {
        lock (sync)
        {
            Ensure();
            bool changed;
            try { changed = scheduler.RequestEvent(item, now); }
            catch (InvalidOperationException) when (scheduler.Read(now).ReviewBlock == "EventHistoryRequiresReview")
            {
                // The rejected conflict/overflow itself is required recovery evidence.
                Append(new(Kind.Event, now, Event: item));
                throw;
            }
            if (changed) Append(new(Kind.Event, now, Event: item));
            return changed;
        }
    }

    public AiReviewRequest? TryBegin(DateTimeOffset now)
    {
        lock (sync)
        {
            Ensure();
            var request = scheduler.TryBegin(now);
            if (request != null)
            {
                if (!requestIds.Add(request.Id)) { faulted = true; throw new InvalidDataException("Review identity was reused."); }
                Append(new(Kind.Begin, now, RequestId: request.Id));
            }
            return request;
        }
    }

    public void Complete(Guid requestId, DateTimeOffset now, AiReviewOutcome outcome, DateTimeOffset? expiresUtc = null, AiNextReview? nextReview = null)
    {
        lock (sync)
        {
            Ensure();
            scheduler.Complete(requestId, now, outcome, expiresUtc, nextReview);
            Append(new(Kind.Complete, now, RequestId: requestId, Outcome: outcome, ExpiresUtc: expiresUtc, NextReview: nextReview));
        }
    }

    public void AdvanceSession(DateOnly date, DateTimeOffset now)
    {
        lock (sync)
        {
            Ensure();
            var bounds = calendar.GetSessionBounds(date);
            scheduler.AdvanceSession(calendar, date, history.Timing, now);
            Append(new(Kind.Advance, now, Date: date, OpenUtc: bounds.OpenUtc, CloseUtc: bounds.CloseUtc));
        }
    }

    private void Replay(Command c)
    {
        if (c is null) throw new InvalidDataException("Missing schedule transition.");
        // Reject unexpected fields as well as invalid transition order; a checksum is not validation.
        Command canonical = c.Kind switch
        {
            Kind.Interval when c.Interval.HasValue && c.Reason != null => new(c.Kind, c.At, Interval: c.Interval, Reason: c.Reason),
            Kind.Event when c.Event != null => new(c.Kind, c.At, Event: c.Event),
            Kind.Begin when c.RequestId.HasValue => new(c.Kind, c.At, RequestId: c.RequestId),
            Kind.Complete when c.RequestId.HasValue && c.Outcome.HasValue => new(c.Kind, c.At, RequestId: c.RequestId, Outcome: c.Outcome, ExpiresUtc: c.ExpiresUtc, NextReview: c.NextReview),
            Kind.Recover => new(c.Kind, c.At),
            Kind.Advance when c.Date.HasValue && c.OpenUtc.HasValue && c.CloseUtc.HasValue => new(c.Kind, c.At, Date: c.Date, OpenUtc: c.OpenUtc, CloseUtc: c.CloseUtc),
            _ => throw new InvalidDataException("Invalid schedule transition.")
        };
        if (canonical != c) throw new InvalidDataException("Unexpected schedule transition fields.");
        switch (c.Kind)
        {
            case Kind.Interval: scheduler.RequestInterval(c.At, c.Interval!.Value, c.Reason!); break;
            case Kind.Event:
                try
                {
                    if (!scheduler.RequestEvent(c.Event!, c.At)) throw new InvalidDataException("Duplicate saved event.");
                }
                catch (InvalidOperationException) when (scheduler.Read(c.At).ReviewBlock == "EventHistoryRequiresReview") { }
                break;
            case Kind.Begin:
                if (!requestIds.Add(c.RequestId!.Value)) throw new InvalidDataException("Saved review identity was reused.");
                if (scheduler.TryBegin(c.At, c.RequestId!.Value) is null) throw new InvalidDataException("Saved review was not due.");
                break;
            case Kind.Complete: scheduler.Complete(c.RequestId!.Value, c.At, c.Outcome!.Value, c.ExpiresUtc, c.NextReview); break;
            case Kind.Recover: scheduler.Recover(c.At); break;
            case Kind.Advance:
                VerifyBounds(c.Date!.Value, c.OpenUtc!.Value, c.CloseUtc!.Value);
                scheduler.AdvanceSession(calendar, c.Date.Value, history.Timing, c.At);
                break;
        }
    }

    private void VerifyBounds(DateOnly date, DateTime open, DateTime close)
    {
        var bounds = calendar.GetSessionBounds(date);
        if (bounds.OpenUtc != open || bounds.CloseUtc != close)
            throw new InvalidDataException("Saved review session differs from the reviewed calendar.");
    }

    private void Append(Command command)
    {
        try
        {
            var next = history with { Commands = history.Commands.Append(command).ToList() };
            Save(next);
            history = next;
        }
        catch { faulted = true; throw; }
    }

    private History Load()
    {
        if (!File.Exists(path) || Path.Exists(path + ".pending") || new FileInfo(path).Length > MaximumBytes)
            throw new InvalidDataException("Scheduling history is missing, oversized or interrupted; reviewed recovery is required.");
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), Json) ?? throw new JsonException();
            if (Hash(envelope.Payload) != envelope.Sha256 || (knownHash.Length > 0 && envelope.Sha256 != knownHash))
                throw new InvalidDataException("Scheduling history changed or failed its checksum.");
            var saved = JsonSerializer.Deserialize<History>(envelope.Payload, Json) ?? throw new JsonException();
            knownHash = envelope.Sha256;
            return saved;
        }
        catch (JsonException ex) { throw new InvalidDataException("Scheduling history is invalid; reviewed recovery is required.", ex); }
    }

    private void Ensure()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (faulted) throw new InvalidDataException("Scheduling storage failed; reopen only through verified recovery.");
        try { _ = Load(); }
        catch { faulted = true; throw; }
    }

    private void Save(History state)
    {
        string payload = JsonSerializer.Serialize(state, Json);
        string hash = Hash(payload);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(payload, hash), Json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Scheduling history requires reviewed retention.");
        using (var file = new FileStream(path + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { file.Write(bytes); file.Flush(true); }
        File.Move(path + ".pending", path, overwrite: true);
        knownHash = hash;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; lease.Dispose(); } }
}
