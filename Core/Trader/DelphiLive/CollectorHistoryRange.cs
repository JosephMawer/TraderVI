#nullable enable
using System;

namespace Core.Trader.DelphiLive;

/// <summary>A half-open range of missing, already completed minute starts for one active rule.</summary>
public sealed record CollectorHistoryRange(DateTime StartUtc, DateTime EndUtc)
{
    public const int MaximumMinutes = 15;

    public void Validate(DateTime sessionOpen, DateTime latestStart)
    {
        if (StartUtc.Kind != DateTimeKind.Utc || EndUtc.Kind != DateTimeKind.Utc ||
            StartUtc.Ticks % TimeSpan.TicksPerMinute != 0 || EndUtc.Ticks % TimeSpan.TicksPerMinute != 0 ||
            StartUtc < sessionOpen || EndUtc > latestStart || EndUtc <= StartUtc ||
            EndUtc - StartUtc > TimeSpan.FromMinutes(MaximumMinutes))
            throw new ArgumentException("History recovery requires a bounded earlier window within this session.");
    }

    public bool Contains(DateTime start) => start >= StartUtc && start < EndUtc;
}
