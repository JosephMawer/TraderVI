#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Display reads only. Never use this schedule to gate engine ticks or operator commands.</summary>
public sealed class PortfolioDisplayRefreshSchedule
{
    private bool wasInDisplayWindow;

    public bool ShouldRefresh(DateTime torontoNow)
    {
        // Keep the display following Live's opening and allow its seven-minute closing workflow
        // to settle. The engines retain their own reviewed calendars and execution schedules.
        bool inWindow = torontoNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
            torontoNow.TimeOfDay >= new TimeSpan(9, 30, 0) && torontoNow.TimeOfDay < new TimeSpan(16, 10, 0);
        bool refresh = inWindow || wasInDisplayWindow; // One final read on leaving the window.
        wasInDisplayWindow = inWindow;
        return refresh;
    }
}

public static class StableDisplayRows
{
    /// <summary>Leave equal rows and their UI containers in place; never reset a bound collection.</summary>
    public static void Apply<T>(ObservableCollection<T> target, IEnumerable<T> rows,
        Func<T, T, bool>? sameValues = null)
    {
        var next = rows.ToArray();
        sameValues ??= EqualityComparer<T>.Default.Equals;
        int common = System.Math.Min(target.Count, next.Length);
        for (int i = 0; i < common; i++)
            if (!sameValues(target[i], next[i])) target[i] = next[i];
        while (target.Count > next.Length) target.RemoveAt(target.Count - 1);
        for (int i = common; i < next.Length; i++) target.Add(next[i]);
    }
}
