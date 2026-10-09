#nullable enable
using Core.TMX.Models.Dto;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Core.TMX;

/// <summary>Timestamp-only, unvalidated diagnostic copy; never usable price evidence.</summary>
public sealed record TmxIntradayTimestampPoint(string SourceText, bool Complete, DateTime? MappedUtc, string? MappingError);

public sealed record TmxIntradayTimestampInspection(int SourcePointCount, int AttemptCount,
    DateTime ReceivedUtc, ImmutableArray<TmxIntradayTimestampPoint> Points)
{
    internal static TmxIntradayTimestampInspection Create(IReadOnlyList<TmxTimeSeriesPointDto> points,
        int attempts, DateTime receivedUtc) => new(points.Count, attempts, receivedUtc,
            points.Take(32).Select(p =>
            {
                DateTime? mapped = null;
                string? error = null;
                if (p.IsComplete)
                {
                    try { mapped = TmxMapper.ToOhlcvBar(p).TimestampUtc; }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                    { error = ex.GetType().Name; }
                }
                return new TmxIntradayTimestampPoint(SafeSourceText(p.dateTime), p.IsComplete, mapped, error);
            }).ToImmutableArray());

    // Only recognized date syntax can reach logs. Reject arbitrary provider text, controls and oversized values.
    internal static string SafeSourceText(string? value)
    {
        if (value is null) return "[missing]";
        if (value.Length is < 1 or > 64 || value.Any(c => char.IsControl(c)) ||
            !DateTimeOffset.TryParseExact(value,
                ["yyyy-MM-dd h:mm:ss tt", "yyyy-MM-dd hh:mm:ss tt", "yyyy-MM-dd HH:mm:ss",
                 "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "[unrecognized timestamp redacted]";
        return value;
    }
}
