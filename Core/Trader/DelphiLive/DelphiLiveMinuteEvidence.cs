#nullable enable
using Core.TMX.Models.Domain;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveMinuteDisposition { Accepted, Repeated, Missing, UnfinishedRead, Late, Conflict }

/// <summary>Explicit source identity and one requested minute. No selected provider or default deadline.</summary>
public sealed record DelphiLiveMinuteRead(Guid ObservationId, string Symbol, string SourceContract,
    DateTime ExpectedEndUtc, DateTime DeadlineUtc, DateTime FetchStartedUtc, DateTime ReceivedUtc,
    ImmutableArray<OhlcvBar> Bars)
{
    public override string ToString() => "Delphi minute read (market data omitted)";
}

/// <summary>First observed completed values and times; a reread must never replace this record.</summary>
public sealed record DelphiLiveMinuteObservation(Guid ObservationId, OhlcvBar Bar,
    DateTime FetchStartedUtc, DateTime ReceivedUtc, DateTime CheckedUtc, DateTime DeadlineUtc)
{
    public DateTime EndUtc => Bar.TimestampUtc.AddMinutes(1);
    public bool ObservedOnTime => CheckedUtc <= DeadlineUtc;
    public override string ToString() => "Delphi minute observation (market data omitted)";
}

/// <summary>
/// Serializable, source-specific session evidence. Callers must durably save it before publication.
/// This offline successor component is not connected to collection, trading or the existing V1 evaluator.
/// </summary>
public sealed record DelphiLiveMinuteEvidenceBook(string Symbol, string SourceContract,
    DateTime SessionOpenUtc, DateTime SessionCloseUtc,
    ImmutableArray<DelphiLiveMinuteObservation> Observations, ImmutableArray<DateTime> ConflictingStartsUtc)
{
    public override string ToString() => "Delphi minute evidence book (market data omitted)";
}

public sealed record DelphiLiveMinuteEvidenceResult(DelphiLiveMinuteEvidenceBook Book,
    DelphiLiveMinuteDisposition Disposition)
{
    public bool NewOnTimeMinute => Disposition == DelphiLiveMinuteDisposition.Accepted;
}

/// <summary>
/// Pure first-arrival/conflict handling for a future minute collector. Missing minutes remain missing;
/// neither elapsed wall time, historical backfill nor a fresh request identity can refresh old evidence.
/// It qualifies input timing only, not momentum, strategy approval or order authority.
/// </summary>
public static class DelphiLiveMinuteEvidence
{
    public static DelphiLiveMinuteEvidenceResult Observe(DelphiLiveMinuteEvidenceBook book,
        DelphiLiveMinuteRead read, DateTime checkedUtc)
    {
        Validate(book);
        ArgumentNullException.ThrowIfNull(read);
        Utc(checkedUtc);
        if (read.Symbol != book.Symbol || read.SourceContract != book.SourceContract)
            throw new ArgumentException("Minute evidence cannot cross symbol or source contracts.", nameof(read));
        if (read.ObservationId == Guid.Empty || read.Bars.IsDefault)
            throw new ArgumentException("A read identity and explicit rows are required.", nameof(read));
        Minute(read.ExpectedEndUtc); Deadline(read.ExpectedEndUtc, read.DeadlineUtc);
        Utc(read.FetchStartedUtc); Utc(read.ReceivedUtc);
        if (read.ExpectedEndUtc <= book.SessionOpenUtc || read.ExpectedEndUtc > book.SessionCloseUtc ||
            read.FetchStartedUtc < book.SessionOpenUtc || read.ReceivedUtc < read.FetchStartedUtc ||
            checkedUtc < read.ReceivedUtc ||
            book.Observations.Any(o => o.CheckedUtc > checkedUtc))
            throw new ArgumentException("Inconsistent session or observation times.", nameof(read));
        foreach (var bar in read.Bars)
        {
            Bar(bar, book);
            if (bar.TimestampUtc > read.ReceivedUtc)
                throw new ArgumentException("A source row cannot begin in the future.", nameof(read));
        }
        if (read.Bars.Select(b => b.TimestampUtc).Distinct().Count() != read.Bars.Length)
            throw new ArgumentException("Duplicate source timestamps are ambiguous.", nameof(read));
        if (book.Observations.Any(o => o.ObservationId == read.ObservationId && o.EndUtc != read.ExpectedEndUtc))
            throw new ArgumentException("An observation identity cannot represent another minute.", nameof(read));

        // Check all previously accepted complete rows in this response, not just its newest row.
        var conflicts = book.ConflictingStartsUtc.Concat(read.Bars.Where(bar =>
            bar.TimestampUtc.AddMinutes(1) < read.FetchStartedUtc &&
            book.Observations.Any(o => o.Bar.TimestampUtc == bar.TimestampUtc && o.Bar != bar))
            .Select(bar => bar.TimestampUtc)).Distinct().Order().ToImmutableArray();
        if (!conflicts.IsEmpty)
            return new(book with { ConflictingStartsUtc = conflicts }, DelphiLiveMinuteDisposition.Conflict);

        // A request started while the target was forming cannot be made complete by later receipt/use.
        if (read.FetchStartedUtc <= read.ExpectedEndUtc)
            return new(book, DelphiLiveMinuteDisposition.UnfinishedRead);
        var exact = read.Bars.SingleOrDefault(b => b.TimestampUtc == read.ExpectedEndUtc.AddMinutes(-1));
        if (exact is null) return new(book, DelphiLiveMinuteDisposition.Missing);
        if (book.Observations.Any(o => o.EndUtc == read.ExpectedEndUtc))
            return new(book, DelphiLiveMinuteDisposition.Repeated);

        var observation = new DelphiLiveMinuteObservation(read.ObservationId, exact, read.FetchStartedUtc,
            read.ReceivedUtc, checkedUtc, read.DeadlineUtc);
        var updated = book with { Observations = book.Observations.Add(observation).OrderBy(o => o.EndUtc).ToImmutableArray() };
        return new(updated, observation.ObservedOnTime ? DelphiLiveMinuteDisposition.Accepted : DelphiLiveMinuteDisposition.Late);
    }

    /// <summary>Recheck the original deadline at publication/use; no cached result extends it.</summary>
    public static DelphiLiveMinuteObservation? AvailableAt(DelphiLiveMinuteEvidenceBook book,
        DateTime endUtc, DateTime nowUtc)
    {
        Validate(book); Minute(endUtc); Utc(nowUtc);
        if (!book.ConflictingStartsUtc.IsEmpty) return null;
        return book.Observations.SingleOrDefault(o => o.EndUtc == endUtc && o.ObservedOnTime &&
            nowUtc >= o.CheckedUtc && nowUtc <= o.DeadlineUtc);
    }

    private static void Validate(DelphiLiveMinuteEvidenceBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        DelphiLiveFiveMinuteBar.RequireCanonicalSymbol(book.Symbol);
        if (string.IsNullOrWhiteSpace(book.SourceContract) || book.SourceContract != book.SourceContract.Trim())
            throw new ArgumentException("A pinned source contract is required.", nameof(book));
        Minute(book.SessionOpenUtc); Minute(book.SessionCloseUtc);
        if (book.SessionCloseUtc <= book.SessionOpenUtc || book.SessionCloseUtc - book.SessionOpenUtc > TimeSpan.FromHours(24) ||
            book.Observations.IsDefault || book.ConflictingStartsUtc.IsDefault)
            throw new ArgumentException("Explicit session bounds and evidence arrays are required.", nameof(book));
        DateTime? prior = null;
        foreach (var observation in book.Observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            Bar(observation.Bar, book);
            Deadline(observation.EndUtc, observation.DeadlineUtc);
            Utc(observation.FetchStartedUtc); Utc(observation.ReceivedUtc); Utc(observation.CheckedUtc);
            if (observation.ObservationId == Guid.Empty || observation.FetchStartedUtc <= observation.EndUtc ||
                observation.ReceivedUtc < observation.FetchStartedUtc || observation.CheckedUtc < observation.ReceivedUtc ||
                prior >= observation.EndUtc)
                throw new ArgumentException("Invalid saved minute evidence.", nameof(book));
            prior = observation.EndUtc;
        }
        if (book.Observations.Select(o => o.ObservationId).Distinct().Count() != book.Observations.Length ||
            book.ConflictingStartsUtc.Distinct().Count() != book.ConflictingStartsUtc.Length)
            throw new ArgumentException("Duplicate saved evidence identities.", nameof(book));
        foreach (var start in book.ConflictingStartsUtc)
        {
            Minute(start);
            if (!book.Observations.Any(o => o.Bar.TimestampUtc == start))
                throw new ArgumentException("A conflict must identify preserved evidence.", nameof(book));
        }
    }

    private static void Bar(OhlcvBar bar, DelphiLiveMinuteEvidenceBook book)
    {
        ArgumentNullException.ThrowIfNull(bar); Minute(bar.TimestampUtc);
        if (bar.TimestampUtc < book.SessionOpenUtc || bar.TimestampUtc.AddMinutes(1) > book.SessionCloseUtc ||
            bar.Low <= 0 || bar.High < bar.Low || bar.Open < bar.Low || bar.Open > bar.High ||
            bar.Close < bar.Low || bar.Close > bar.High || bar.Volume < 0)
            throw new ArgumentException("Invalid regular-session minute OHLCV.", nameof(bar));
    }

    private static void Deadline(DateTime end, DateTime deadline)
    {
        Utc(deadline);
        if (deadline <= end || deadline >= end.AddMinutes(1))
            throw new ArgumentException("The explicit publication deadline must precede the next minute.", nameof(deadline));
    }

    private static void Minute(DateTime time)
    {
        Utc(time);
        if (time.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("An exact minute boundary is required.", nameof(time));
    }

    private static void Utc(DateTime time) => DelphiLiveFiveMinuteBar.RequireUtc(time, nameof(time));
}
