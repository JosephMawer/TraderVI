#nullable enable

using System;
using System.Collections.Generic;

namespace Core.DataQuality;

public static class MarketDataFreshness
{
    /// <summary>
    /// Counts completed benchmark sessions strictly later than a symbol's
    /// latest bar. This avoids treating weekends and exchange holidays as
    /// missing trading days.
    /// </summary>
    public static int CountSessionsBehind(
        DateTime latestBarDate,
        IReadOnlyList<DateTime> benchmarkSessions)
    {
        DateTime latest = latestBarDate.Date;
        int behind = 0;

        for (int i = 0; i < benchmarkSessions.Count; i++)
        {
            if (benchmarkSessions[i].Date > latest)
                behind++;
        }

        return behind;
    }
}
