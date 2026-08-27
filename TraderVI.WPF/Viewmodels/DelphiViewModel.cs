#nullable enable

using Core.Db;
using Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TraderVI.WPF.Viewmodels;

public sealed class DelphiViewModel : INotifyPropertyChanged
{
    private readonly DelphiPublishedRecommendationReader reader = new();
    private readonly DelphiWorkflow workflow = new();
    private bool isRunning;
    private string status = "Loading the latest saved recommendations…";
    private string recommendationDate = "—";
    private string savedAt = "—";
    private string topPick = "—";
    private string lastRunReport = "No Delphi evaluation has been started from this tab.";
    private Brush statusBrush = Brushes.SlateGray;
    private DateTime? publishedPickDate;

    public ObservableCollection<DelphiPickRow> ContinuationPicks { get; } = [];
    public ObservableCollection<DelphiPickRow> BreakoutPicks { get; } = [];

    public bool IsRunning
    {
        get => isRunning;
        private set => Set(ref isRunning, value);
    }

    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public string RecommendationDate
    {
        get => recommendationDate;
        private set => Set(ref recommendationDate, value);
    }

    public string SavedAt
    {
        get => savedAt;
        private set => Set(ref savedAt, value);
    }

    public string TopPick
    {
        get => topPick;
        private set => Set(ref topPick, value);
    }

    public int ContinuationCount => ContinuationPicks.Count;

    public int BreakoutCount => BreakoutPicks.Count;

    public string LastRunReport
    {
        get => lastRunReport;
        private set => Set(ref lastRunReport, value);
    }

    public Brush StatusBrush
    {
        get => statusBrush;
        private set => Set(ref statusBrush, value);
    }

    public bool HasRecommendationsFor(DateTime date) =>
        publishedPickDate == date.Date;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return;

        Status = "Reading the latest saved Delphi recommendations…";
        StatusBrush = Brushes.SlateGray;
        try
        {
            await LoadLatestAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Refresh cancelled";
        }
        catch (Exception ex)
        {
            Status = $"Saved recommendations unavailable · {ex.GetType().Name}: {ex.Message}";
            StatusBrush = Brushes.IndianRed;
        }
    }

    public async Task RunOfficialAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Status = "Official Delphi evaluation running · please keep TraderVI open…";
        StatusBrush = Brushes.Goldenrod;
        LastRunReport = "Delphi is evaluating the local market snapshot. Results will appear here when it finishes.";

        using var output = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            DelphiWorkflowRunResult result = await Task.Run(
                () => workflow.RunAsync(
                    new DelphiWorkflowOptions(),
                    output,
                    cancellationToken),
                cancellationToken);

            LastRunReport = result.SummaryReport ?? output.ToString();
            if (result.Succeeded)
            {
                await LoadLatestAsync(cancellationToken, updateStatus: false);
                Status = $"Official Delphi run completed · {result.ContinuationPickCount} continuation and {result.BreakoutPickCount} breakout picks";
                StatusBrush = Brushes.MediumSeaGreen;
            }
            else
            {
                Status = $"Delphi did not publish recommendations · {result.Status}";
                StatusBrush = Brushes.Goldenrod;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Delphi run cancelled";
            StatusBrush = Brushes.Goldenrod;
            LastRunReport = output.ToString();
        }
        catch (Exception ex)
        {
            Status = $"Delphi failed · {ex.GetType().Name}: {ex.Message}";
            StatusBrush = Brushes.IndianRed;
            string diagnostics = output.ToString();
            LastRunReport = string.IsNullOrWhiteSpace(diagnostics)
                ? ex.Message
                : diagnostics;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task LoadLatestAsync(
        CancellationToken cancellationToken,
        bool updateStatus = true)
    {
        DelphiPublishedRecommendations? published =
            await reader.LoadLatestAsync(cancellationToken);

        ContinuationPicks.Clear();
        BreakoutPicks.Clear();

        if (published is null)
        {
            publishedPickDate = null;
            RecommendationDate = "None saved";
            SavedAt = "—";
            TopPick = "—";
            NotifyCounts();
            if (updateStatus)
                Status = "No saved Delphi recommendations were found";
            return;
        }

        publishedPickDate = published.PickDate.Date;
        foreach (DailyPickInfo pick in published.Continuation)
            ContinuationPicks.Add(DelphiPickRow.Create(pick));
        foreach (DailyPickInfo pick in published.Breakout)
            BreakoutPicks.Add(DelphiPickRow.Create(pick));

        RecommendationDate = published.PickDate.ToString("MMM d, yyyy");
        DateTime savedUtc = DateTime.SpecifyKind(published.LatestCreatedUtc, DateTimeKind.Utc);
        SavedAt = published.LatestCreatedUtc == DateTime.MinValue
            ? "—"
            : savedUtc.ToLocalTime().ToString("MMM d · HH:mm:ss");
        TopPick = published.Continuation.Count > 0
            ? published.Continuation[0].Symbol
            : published.Breakout.Count > 0
                ? published.Breakout[0].Symbol
                : "—";
        NotifyCounts();

        if (updateStatus)
        {
            Status = "Showing saved recommendations only · no Delphi evaluation was run";
            StatusBrush = Brushes.MediumSeaGreen;
        }
    }

    private void NotifyCounts()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContinuationCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BreakoutCount)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record DelphiPickRow(
    int Rank,
    string Symbol,
    string Direction,
    string Composite,
    string UpProbability,
    string BreakoutProbability,
    string VolumeExpansion,
    string ExpectedReturn,
    string SuggestedSize,
    string Notes)
{
    public static DelphiPickRow Create(DailyPickInfo pick) =>
        new(
            pick.Rank,
            pick.Symbol,
            pick.Direction,
            pick.CompositeScore.ToString("P1"),
            FormatPercent(pick.DirectionProb),
            FormatPercent(pick.BreakoutProb),
            FormatPercent(pick.VolExpansionProb),
            FormatPercent(pick.ExpectedReturn),
            pick.SuggestedSize?.ToString("C0") ?? "—",
            pick.Notes ?? "");

    private static string FormatPercent(double? value) =>
        value?.ToString("P1") ?? "—";
}
