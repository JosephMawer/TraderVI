#nullable enable
using System;

namespace Core.Trader;

/// <summary>Operational adoption is causal; the historical opening freeze is retained separately.</summary>
public static class PaperDailySourcePolicy
{
    public static DateTime EffectiveBoundary(DateTime adoptedUtc)
    {
        if (adoptedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Adoption time must be UTC.");
        long interval = TimeSpan.FromMinutes(5).Ticks;
        return new DateTime(checked((adoptedUtc.Ticks + interval - 1) / interval * interval), DateTimeKind.Utc);
    }

    public static bool ShouldAdopt(Guid? currentId, DateTime? currentPublishedUtc,
        Guid nextId, DateTime nextPublishedUtc, DateTime nowUtc) =>
        nextId != Guid.Empty && nextId != currentId && nextPublishedUtc <= nowUtc &&
        (!currentPublishedUtc.HasValue || nextPublishedUtc > currentPublishedUtc.Value);

    public static bool HasFreshPair(DateTime previousBarEndUtc, DateTime latestBarEndUtc, DateTime effectiveUtc) =>
        previousBarEndUtc >= effectiveUtc && latestBarEndUtc > effectiveUtc &&
        latestBarEndUtc - previousBarEndUtc == TimeSpan.FromMinutes(5);
}
