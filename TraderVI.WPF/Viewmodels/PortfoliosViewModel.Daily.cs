#nullable enable
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class PortfoliosViewModel
{
    private readonly Dictionary<string, IReadOnlyList<PortfolioDailyObservation>> dailyHistory = [];
    private DateOnly? selectedDay;
    public ObservableCollection<DateOnly> DailyDates { get; } = [];
    public DateOnly? SelectedDay
    {
        get => selectedDay;
        set
        {
            if (!Set(ref selectedDay, value)) return;
            OnPropertyChanged(nameof(DayHeading));
            RebuildComparison();
        }
    }
    public string DayHeading => SelectedDay.HasValue ? $"Day gain · {SelectedDay:dd MMM yyyy}" : "Day gain · no saved sessions";
    public string DayNotes => "Day gain = saved value minus this session's opening value, including recorded trading costs and changes in held stocks. " +
        "A new account uses its first session's opening value. Partial results are saved observations, not a completed day's return.";

    private void RebuildDailyDates()
    {
        var previous = selectedDay;
        bool followLatest = selectedDay is null || selectedDay == DailyDates.FirstOrDefault();
        Replace(DailyDates, dailyHistory.Values.SelectMany(d => d).Select(d => d.Date).Distinct().OrderByDescending(d => d));
        selectedDay = followLatest || !previous.HasValue || !DailyDates.Contains(previous.Value)
            ? DailyDates.Count > 0 ? DailyDates[0] : null : previous;
        OnPropertyChanged(nameof(SelectedDay));
        OnPropertyChanged(nameof(DayHeading));
    }

    // Offline fixtures use the same date selection/calculation path as the SELECT-only refresh.
    public void ApplyDailyHistory(IReadOnlyDictionary<string, IReadOnlyList<PortfolioDailyObservation>> history)
    {
        dailyHistory.Clear();
        foreach (var item in history) dailyHistory[item.Key] = item.Value;
        RebuildDailyDates();
        RebuildComparison();
    }
}
