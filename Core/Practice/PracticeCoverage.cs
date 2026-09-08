#nullable enable
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Practice;

public sealed record PracticeCoverageRow(DateOnly Session, string Symbol, int IntervalMinutes,
    int ExpectedSlots, int AvailableSlots, int MissingSlots, int InvalidRows);

public static class PracticeCoverage
{
    public static IReadOnlyList<PracticeCoverageRow> Inspect(PracticeDataset dataset)
    {
        var source = dataset.Document.Intraday.GroupBy(x => (x.Symbol.ToUpperInvariant(), x.IntervalMinutes))
            .ToDictionary(g => g.Key, g => g.SelectMany(x => x.Bars).ToArray());
        var symbols = dataset.Document.Daily.Membership.SelectMany(x => x.Equities.Select(e => e.Symbol))
            .Concat(dataset.Document.Intraday.Select(x => x.Symbol)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var result = new List<PracticeCoverageRow>();
        foreach (var day in dataset.Sessions)
        {
            var bounds = dataset.Calendar.GetSessionBounds(day);
            foreach (var symbol in symbols)
                foreach (int interval in new[] { 5, 15 })
                {
                    var rows = source.GetValueOrDefault((symbol.ToUpperInvariant(), interval), [])
                        .Where(b => b.TimestampUtc >= bounds.OpenUtc && b.TimestampUtc < bounds.CloseUtc).ToArray();
                    int valid = rows.Count(b => IsValid(b) && (b.TimestampUtc - bounds.OpenUtc).Ticks % TimeSpan.FromMinutes(interval).Ticks == 0);
                    int expected = (int)(bounds.CloseUtc - bounds.OpenUtc).TotalMinutes / interval;
                    result.Add(new(day, symbol, interval, expected, valid, expected - valid, rows.Length - valid));
                }
        }
        return result;
    }
    internal static bool IsValid(OhlcvBar bar) => bar.Open > 0 && bar.Low > 0 && bar.Close > 0 && bar.Volume >= 0 &&
        bar.High >= bar.Low && bar.Open >= bar.Low && bar.Open <= bar.High && bar.Close >= bar.Low && bar.Close <= bar.High;
}
