#nullable enable
using Core.TMX.Models.Domain;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveMinuteWindowStatus { Available, NotMature, MissingMinute, LateMinute, FutureEvidence, SourceConflict }

/// <summary>A duration-labelled aggregate; never reinterpret it as a canonical V1 five-minute bar.</summary>
public sealed record DelphiLiveMinuteWindow(DateTime StartUtc, DateTime EndUtc,
    DelphiLiveMinuteWindowStatus Status, OhlcvBar? Aggregate, ImmutableArray<Guid> ObservationIds)
{
    public ImmutableArray<Guid> RecoveredObservationIds { get; init; } = [];
    public override string ToString() => $"Delphi minute window: {Status} (market data omitted)";
}

public sealed record DelphiLiveMinuteLegSet(DelphiLiveMinuteWindowStatus Status,
    ImmutableArray<DelphiLiveMinuteWindow> Legs);

/// <summary>
/// Offline successor preparation: express indicator windows in elapsed time, independently of the
/// confirmation clock. No missing/no-trade inference, provider selection, trading or V1 rule changes.
/// </summary>
public static class DelphiLiveMinuteWindows
{
    public static DelphiLiveMinuteWindow Build(DelphiLiveMinuteEvidenceBook book, DateTime endUtc,
        TimeSpan horizon, DateTime evaluatedUtc) => BuildCore(book, endUtc, horizon, evaluatedUtc, false);

    /// <summary>
    /// ADR-0111 present-time reconstruction. Late receipts may supply context after actual validation;
    /// event endpoints stay fixed and no future bar is used. This grants no current entry authority.
    /// The historical/on-time Build path is deliberately unchanged.
    /// </summary>
    public static DelphiLiveMinuteWindow BuildRecoveredContext(DelphiLiveMinuteEvidenceBook book,
        DateTime endUtc, TimeSpan horizon, DateTime evaluatedUtc) => BuildCore(book, endUtc, horizon, evaluatedUtc, true);

    private static DelphiLiveMinuteWindow BuildCore(DelphiLiveMinuteEvidenceBook book, DateTime endUtc,
        TimeSpan horizon, DateTime evaluatedUtc, bool allowRecovered)
    {
        DelphiLiveMinuteEvidence.Validate(book);
        DelphiLiveFiveMinuteBar.RequireUtc(endUtc, nameof(endUtc));
        DelphiLiveFiveMinuteBar.RequireUtc(evaluatedUtc, nameof(evaluatedUtc));
        if (endUtc.Ticks % TimeSpan.TicksPerMinute != 0 || horizon <= TimeSpan.Zero ||
            horizon > TimeSpan.FromHours(24) || horizon.Ticks % TimeSpan.TicksPerMinute != 0 ||
            endUtc < book.SessionOpenUtc || endUtc > book.SessionCloseUtc)
            throw new ArgumentException("A minute boundary and explicit integral-minute horizon within the session are required.");
        DateTime start = endUtc.Subtract(horizon);
        DelphiLiveMinuteWindow Unavailable(DelphiLiveMinuteWindowStatus status) => new(start, endUtc, status, null, []);
        if (!book.ConflictingStartsUtc.IsEmpty) return Unavailable(DelphiLiveMinuteWindowStatus.SourceConflict);
        if (start < book.SessionOpenUtc) return Unavailable(DelphiLiveMinuteWindowStatus.NotMature);
        if (evaluatedUtc <= endUtc) return Unavailable(DelphiLiveMinuteWindowStatus.FutureEvidence);
        var observations = book.Observations.Where(o => o.Bar.TimestampUtc >= start && o.EndUtc <= endUtc).ToArray();
        int minutes = checked((int)horizon.TotalMinutes);
        if (observations.Length != minutes || observations.Where((o, i) => o.Bar.TimestampUtc != start.AddMinutes(i)).Any())
            return Unavailable(DelphiLiveMinuteWindowStatus.MissingMinute);
        if (observations.Any(o => o.CheckedUtc > evaluatedUtc)) return Unavailable(DelphiLiveMinuteWindowStatus.FutureEvidence);
        if (!allowRecovered && observations.Any(o => !o.ObservedOnTime)) return Unavailable(DelphiLiveMinuteWindowStatus.LateMinute);

        // Past timely evidence remains historical context. The current observation's original
        // deadline is separately rechecked by the publisher/entry evaluator through AvailableAt.
        long volume = 0;
        foreach (var observation in observations) volume = checked(volume + observation.Bar.Volume);
        var aggregate = new OhlcvBar(start, observations[0].Bar.Open, observations.Max(o => o.Bar.High),
            observations.Min(o => o.Bar.Low), observations[^1].Bar.Close, volume);
        return new(start, endUtc, DelphiLiveMinuteWindowStatus.Available, aggregate,
            observations.Select(o => o.ObservationId).ToImmutableArray())
        {
            RecoveredObservationIds = observations.Where(o => !o.ObservedOnTime).Select(o => o.ObservationId).ToImmutableArray()
        };
    }

    /// <summary>Four original five-minute measurement legs still span twenty minutes at a minute endpoint.</summary>
    public static DelphiLiveMinuteLegSet FiveMinuteLegs(DelphiLiveMinuteEvidenceBook book,
        DateTime endUtc, int count, DateTime evaluatedUtc) => Legs(book, endUtc, count, evaluatedUtc, false);

    public static DelphiLiveMinuteLegSet RecoveredFiveMinuteLegs(DelphiLiveMinuteEvidenceBook book,
        DateTime endUtc, int count, DateTime evaluatedUtc) => Legs(book, endUtc, count, evaluatedUtc, true);

    private static DelphiLiveMinuteLegSet Legs(DelphiLiveMinuteEvidenceBook book,
        DateTime endUtc, int count, DateTime evaluatedUtc, bool allowRecovered)
    {
        if (count is < 1 or > 288) throw new ArgumentOutOfRangeException(nameof(count));
        // Validate the whole requested horizon before building its legs; an unfilled opening
        // horizon has an explicit NotMature result rather than partially shortened measurements.
        var whole = BuildCore(book, endUtc, TimeSpan.FromMinutes(checked(count * 5)), evaluatedUtc, allowRecovered);
        if (whole.Status != DelphiLiveMinuteWindowStatus.Available) return new(whole.Status, []);
        return new(whole.Status, Enumerable.Range(0, count).Select(i => BuildCore(book, endUtc.AddMinutes(-5 * (count - i - 1)),
            TimeSpan.FromMinutes(5), evaluatedUtc, allowRecovered)).ToImmutableArray());
    }
}
