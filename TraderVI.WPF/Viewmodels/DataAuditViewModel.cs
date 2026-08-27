#nullable enable

using Core.DataQuality;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TraderVI.WPF.Viewmodels;

public sealed class DataAuditViewModel : INotifyPropertyChanged
{
    private readonly MarketDataAuditWorkflow workflow;
    private bool isRunning;
    private string status = "Ready · audit has not run in this session";
    private string result = "NOT RUN";
    private string marketDataAsOf = "—";
    private string lastRun = "—";
    private int totalSymbols;
    private int activeSymbols;
    private int errorCount;
    private int warningCount;
    private Brush resultBrush = Brushes.SlateGray;

    public DataAuditViewModel()
    {
        string? connection =
            Environment.GetEnvironmentVariable("TRADERVI_CONNECTION_STRING");
        workflow = new MarketDataAuditWorkflow(connection);
    }

    public ObservableCollection<DataAuditFindingRow> Findings { get; } = [];

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

    public string Result
    {
        get => result;
        private set => Set(ref result, value);
    }

    public string MarketDataAsOf
    {
        get => marketDataAsOf;
        private set => Set(ref marketDataAsOf, value);
    }

    public string LastRun
    {
        get => lastRun;
        private set => Set(ref lastRun, value);
    }

    public int TotalSymbols
    {
        get => totalSymbols;
        private set => Set(ref totalSymbols, value);
    }

    public int ActiveSymbols
    {
        get => activeSymbols;
        private set => Set(ref activeSymbols, value);
    }

    public int ErrorCount
    {
        get => errorCount;
        private set => Set(ref errorCount, value);
    }

    public int WarningCount
    {
        get => warningCount;
        private set => Set(ref warningCount, value);
    }

    public Brush ResultBrush
    {
        get => resultBrush;
        private set => Set(ref resultBrush, value);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Status = "Reading local SQL and checking the full universe…";
        try
        {
            MarketDataAuditRunResult run = await workflow.RunAsync(
                new MarketDataAuditOptions(),
                cancellationToken);
            Apply(run);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Audit cancelled";
        }
        catch (Exception ex)
        {
            Result = "FAILED";
            ResultBrush = Brushes.IndianRed;
            Status = $"Audit unavailable · {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Apply(MarketDataAuditRunResult run)
    {
        MarketDataAuditReport report = run.Report;
        Findings.Clear();
        foreach (AuditFinding finding in report.Findings)
            Findings.Add(DataAuditFindingRow.Create(finding));

        TotalSymbols = report.TotalSymbols;
        ActiveSymbols = report.ActiveSymbols;
        ErrorCount = report.ErrorCount;
        WarningCount = report.WarningCount;
        MarketDataAsOf = report.MarketDataAsOf?.ToString("yyyy-MM-dd") ?? "Unavailable";
        LastRun = run.CompletedUtc.ToLocalTime().ToString("MMM d · HH:mm:ss");

        if (report.ErrorCount > 0)
        {
            Result = "ERRORS";
            ResultBrush = Brushes.IndianRed;
            Status = $"Review required · {report.ErrorCount} error(s), {report.WarningCount} warning(s)";
        }
        else if (report.WarningCount > 0)
        {
            Result = "WARNINGS";
            ResultBrush = Brushes.Goldenrod;
            Status = $"Review candidates · {report.WarningCount} warning(s)";
        }
        else
        {
            Result = "PASS";
            ResultBrush = Brushes.MediumSeaGreen;
            Status = "No findings · the local universe passed all automated checks";
        }
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

public sealed record DataAuditFindingRow(
    string Severity,
    string Code,
    string Symbol,
    string Message,
    Brush SeverityBrush)
{
    public static DataAuditFindingRow Create(AuditFinding finding) =>
        new(
            finding.Severity.ToString().ToUpperInvariant(),
            finding.Code,
            finding.Symbol ?? "—",
            finding.Message,
            finding.Severity == AuditSeverity.Error
                ? Brushes.IndianRed
                : Brushes.Goldenrod);
}

