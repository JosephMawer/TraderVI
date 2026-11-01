#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.DelphiLive;

public sealed record MissingRuleMinute(string Symbol, DateTime StartUtc, string Rule);

/// <summary>Transient requests from missing windows consulted by this evaluation. No I/O.</summary>
public sealed class CollectorRuleHistory(string sourceContract, DateTime sessionOpenUtc, DateTime sessionCloseUtc)
{
    private readonly Dictionary<(string, DateTime), MissingRuleMinute> missing = new();
    private readonly Dictionary<DelphiLiveMinuteEvidenceBook, HashSet<DateTime>> observed = new(ReferenceEqualityComparer.Instance);
    public string SourceContract => sourceContract;
    public DateTime SessionOpenUtc => sessionOpenUtc;
    public DateTime SessionCloseUtc => sessionCloseUtc;
    public IReadOnlyCollection<MissingRuleMinute> Minutes => missing.Values;

    public void ObserveWindow(DelphiLiveMinuteEvidenceBook book, DelphiLiveMinuteWindow window, DateTime now, string rule)
    {
        if (window.Status != DelphiLiveMinuteWindowStatus.MissingMinute || now >= sessionCloseUtc ||
            window.StartUtc < sessionOpenUtc || window.EndUtc > sessionCloseUtc || window.EndUtc >= now ||
            book.SourceContract != sourceContract || book.SessionOpenUtc != sessionOpenUtc || book.SessionCloseUtc != sessionCloseUtc ||
            !book.ConflictingStartsUtc.IsEmpty) return;
        if (!observed.TryGetValue(book, out var present))
            observed.Add(book, present = book.Observations.Select(o => o.Bar.TimestampUtc).ToHashSet());
        for (var start = window.StartUtc; start < window.EndUtc; start = start.AddMinutes(1))
            if (!present.Contains(start))
                missing.TryAdd((book.Symbol, start), new(book.Symbol, start, rule));
    }
}
