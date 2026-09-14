using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class PortfolioDisplayRefreshTests
{
    private static DateTime Wednesday(int hour, int minute) => new(2026, 9, 9, hour, minute, 0);

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(9, 29, false)]
    [InlineData(9, 30, true)]
    [InlineData(15, 59, true)]
    [InlineData(16, 7, true)]
    [InlineData(16, 9, true)]
    [InlineData(16, 10, false)]
    [InlineData(20, 30, false)]
    public void DisplayReadsAllowClosingWorkButAreQuietOutsideTheWindow(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, new PortfolioDisplayRefreshSchedule().ShouldRefresh(Wednesday(hour, minute)));
    }

    [Fact]
    public void OneFinalReadAtCloseThenNoOvernightPollingAndAutomaticMorningResumption()
    {
        var schedule = new PortfolioDisplayRefreshSchedule();
        Assert.True(schedule.ShouldRefresh(Wednesday(16, 9)));
        Assert.True(schedule.ShouldRefresh(Wednesday(16, 10)));
        for (int minute = 11; minute < 60; minute++) Assert.False(schedule.ShouldRefresh(Wednesday(16, minute)));
        Assert.False(schedule.ShouldRefresh(Wednesday(9, 29).AddDays(1)));
        Assert.True(schedule.ShouldRefresh(Wednesday(9, 30).AddDays(1)));
    }

    [Fact]
    public void WeekendDisplayDoesNotPoll()
    {
        var schedule = new PortfolioDisplayRefreshSchedule();
        Assert.False(schedule.ShouldRefresh(new(2026, 9, 12, 12, 0, 0)));
        Assert.False(schedule.ShouldRefresh(new(2026, 9, 13, 12, 0, 0)));
    }

    private sealed record Row(string Name, int Value);

    [Fact]
    public void UnchangedSnapshotPreservesObjectsAndEmitsNoCollectionEvents()
    {
        var row = new Row("Account A", 1000);
        var rows = new ObservableCollection<Row> { row };
        int changes = 0;
        rows.CollectionChanged += (_, _) => changes++;
        for (int tick = 0; tick < 20; tick++) StableDisplayRows.Apply(rows, [new Row("Account A", 1000)]);
        Assert.Same(row, rows[0]);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AChangedValueUpdatesOnlyThatRowWithoutResettingTheGrid()
    {
        var first = new Row("A", 1000);
        var rows = new ObservableCollection<Row> { first, new("B", 1000) };
        NotifyCollectionChangedEventArgs change = null;
        rows.CollectionChanged += (_, args) => { Assert.Null(change); change = args; };
        StableDisplayRows.Apply(rows, [new("A", 1000), new("B", 1004)]);
        Assert.Same(first, rows[0]);
        Assert.Equal(1004, rows[1].Value);
        Assert.Equal(NotifyCollectionChangedAction.Replace, change.Action);
        Assert.Equal(1, change.NewStartingIndex);
    }

    [Fact]
    public void AddingRemovingAndReorderingRowsNeverIssuesAReset()
    {
        var rows = new ObservableCollection<int> { 1, 2 };
        rows.CollectionChanged += (_, args) => Assert.NotEqual(NotifyCollectionChangedAction.Reset, args.Action);
        StableDisplayRows.Apply(rows, [1, 2, 3]);
        Assert.Equal(new[] { 1, 2, 3 }, rows);
        StableDisplayRows.Apply(rows, rows.Reverse());
        Assert.Equal(new[] { 3, 2, 1 }, rows);
        StableDisplayRows.Apply(rows, [3]);
        Assert.Equal(new[] { 3 }, rows);
        StableDisplayRows.Apply(rows, []);
        Assert.Empty(rows);
    }
}
