#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed partial class DelphiLiveViewModel
{
    private DelphiLiveWatchlistPreview dailyPreview = DelphiLiveWatchlistPreview.Empty;
    private DelphiLiveReplayReport? replayReport;
    private int displayModeIndex;
    private int replayFrameIndex;
    private DelphiLiveWatchlistRow? selectedWatchlistRow;
    private string watchlistExplanation = "Select a stock to see what its status means.";
    public IReadOnlyList<string> DisplayModes { get; } = ["Current watchlist", "Historical replay"];
    public ObservableCollection<DelphiLiveWatchlistRow> WatchlistRows { get; } = [];
    public ObservableCollection<DelphiLiveReplayPosition> ReplayPositions { get; } = [];
    public ObservableCollection<DelphiLiveReplayTradeRow> ReplayTrades { get; } = [];
    public int DisplayModeIndex { get => displayModeIndex; set { if (Set(ref displayModeIndex, value)) RebuildWatchlist(); } }
    public int ReplayFrameIndex { get => replayFrameIndex; set { if (Set(ref replayFrameIndex, Math.Clamp(value, 0, ReplayFrameMaximum))) RebuildWatchlist(); } }
    public int ReplayFrameMaximum => Math.Max(0, (replayReport?.Frames.Count ?? 1) - 1);
    public bool IsReplayMode => DisplayModeIndex == 1;
    public bool HasSavedReplay => replayReport is not null;
    public bool CanStepBack => HasSavedReplay && ReplayFrameIndex > 0;
    public bool CanStepForward => HasSavedReplay && ReplayFrameIndex < ReplayFrameMaximum;
    public string ReplayButtonText => HasSavedReplay ? $"Replay {replayReport!.SessionDate:ddd, MMM d}" : "Open saved replay…";
    public string WatchlistTitle => IsReplayMode ? "Historical replay" : AllObservations.Count > 0 ? "Live watchlist" : "Delphi picks";
    public string WatchlistSummary => IsReplayMode
        ? replayReport is null ? "No saved replay loaded. Open a replay file to inspect a past session." :
            $"{replayReport.SessionDate:dddd, MMMM d, yyyy} · {WatchlistRows.Count} stocks · historical simulation · estimated trades"
        : AllObservations.Count > 0 ? $"{Status} · {WatchlistRows.Count} observed stocks · {Coverage}"
        : dailyPreview.Run is null ? "No valid saved Delphi run is available yet."
        : $"{dailyPreview.Run.RecommendationDate:dddd, MMMM d, yyyy} · {dailyPreview.Picks.Count} published stocks · {dailyPreview.Picks.Count(p => p.Eligible)} daily-eligible · overlaps combined";
    public string WatchlistHelp => IsReplayMode
        ? "Move the time control to follow the day. Signals use five-minute bars; trade prices are estimates from historical minute bars."
        : AllObservations.Count > 0 ? "Signals use saved live observations. Select a stock for its data quality and explanation."
        : dailyPreview.Run is null ? "Refresh picks to read the latest saved daily run. This does not start monitoring."
        : $"Saved Delphi picks using {dailyPreview.Run.MarketDataAsOf:MMM d} daily data. Live signals will appear here when monitoring begins.";
    public string WatchlistNotice => !IsReplayMode && (AllObservations.Count > 0 || Status.Contains("attention", StringComparison.OrdinalIgnoreCase)) ? Warning : "";
    public string ReplayTime => CurrentFrame is null ? "No replay loaded" :
        $"{TorontoTime(CurrentFrame.BarEndUtc)[..5]} market checkpoint · processed {TorontoTime(CurrentFrame.SimulatedUtc)[..5]}";
    public string ReplayCapitalText => replayReport is null ? "—" : $"{replayReport.StartingCapital:N2} CAD";
    public string ReplayCashText => CurrentFrame is null ? "—" : $"{CurrentFrame.Cash:N2} CAD";
    public string ReplayValueText => CurrentFrame?.AccountValue is decimal value ? $"{value:N2} CAD" : "Unavailable · missing price";
    public string ReplayProfitText => CurrentFrame?.AccountValue is decimal value && replayReport is not null ?
        $"{value - replayReport.StartingCapital:+0.00;-0.00;0.00} CAD ({value / replayReport.StartingCapital - 1:+0.00%;-0.00%;0.00%})" : "—";
    public string ReplayCoverageText => replayReport is null ? "Load a saved replay to see simulated holdings and trades." :
        $"{replayReport.AvailableFiveMinuteBars}/{replayReport.ExpectedFiveMinuteBars} five-minute bars · {replayReport.AvailableMinuteBars}/{replayReport.ExpectedMinuteBars} minute bars · missing intervals are not filled in";
    public string ReplayAssumptions => replayReport?.Assumptions ?? "Historical replay does not activate a live or Shadow account.";
    public string WatchlistExplanation { get => watchlistExplanation; private set => Set(ref watchlistExplanation, value); }
    public DelphiLiveWatchlistRow? SelectedWatchlistRow
    {
        get => selectedWatchlistRow;
        set
        {
            if (!Set(ref selectedWatchlistRow, value) || value is null) return;
            string priceLabel = value.Confidence == "Daily snapshot" ? "Previous daily close" : "Checkpoint close";
            string price = value.Price is decimal amount ? $"{amount:N2} CAD" : "unavailable";
            WatchlistExplanation = $"{value.Symbol} · {value.DailySource} · {priceLabel}: {price} · {value.Confidence}\n{value.Explanation}";
            SelectedDossier = value.Evidence;
        }
    }
    private DelphiLiveReplayFrame? CurrentFrame => replayReport?.Frames.ElementAtOrDefault(ReplayFrameIndex);

    private async Task RefreshDailyPreviewAsync(CancellationToken token)
    {
        dailyPreview = await new DelphiLivePreviewRepository().ReadAsync(cancellationToken: token);
        RebuildWatchlist();
    }
    public void LoadReplay(string path, bool show = true)
    {
        if (new FileInfo(path).Length > 20_000_000) throw new InvalidDataException("Replay file exceeds the supported size.");
        var report = JsonSerializer.Deserialize<DelphiLiveReplayReport>(File.ReadAllText(path)) ?? throw new InvalidDataException("Replay file is empty.");
        if (report.Kind != DelphiLiveHistoricalReplay.Kind || report.SchemaVersion != 1 || report.Frames?.Count != 78 ||
            report.Trades is null || report.Assumptions is null ||
            report.ExecutionConvention != DelphiLiveHistoricalReplay.ExecutionConvention || report.Currency != "CAD" || report.StartingCapital <= 0)
            throw new InvalidDataException("This is not a supported Delphi Live historical replay.");
        var open = ReviewedTsxSessionCalendar.At(report.SessionDate, new(9, 30));
        for (int i = 0; i < report.Frames.Count; i++)
        {
            var frame = report.Frames[i];
            if (frame is null || frame.BarEndUtc.Kind != DateTimeKind.Utc || frame.SimulatedUtc.Kind != DateTimeKind.Utc ||
                frame.BarEndUtc != open.AddMinutes(5 * (i + 1)) || frame.SimulatedUtc != frame.BarEndUtc.AddMinutes(2) ||
                frame.Rows is null || frame.Positions is null || frame.Rows.Any(r => r is null ||
                    r.Symbol is null || r.DailySource is null || r.Momentum is null || r.State is null || r.Confidence is null ||
                    r.Reason is null || r.Families is null || r.Families.Any(f => f is null)) ||
                frame.Positions.Any(p => p is null || p.Symbol is null))
                throw new InvalidDataException("Replay checkpoints or stock details are incomplete.");
        }
        if (report.Trades.Any(t => t is null || t.Symbol is null || t.Side is null || t.Reason is null || t.Outcome is null ||
            t.DecisionUtc.Kind != DateTimeKind.Utc || t.RecordedUtc.Kind != DateTimeKind.Utc ||
            t.RecordedUtc < t.DecisionUtc || t.FilledUtc is DateTime fill && (fill.Kind != DateTimeKind.Utc || fill <= t.DecisionUtc || fill > t.RecordedUtc)))
            throw new InvalidDataException("Replay trade details or times are incomplete.");
        replayReport = report; replayFrameIndex = report.Frames.Count - 1;
        if (show) displayModeIndex = 1;
        RebuildWatchlist();
    }
    private void TryLoadLatestReplay()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TraderVI.sln"))) directory = directory.Parent;
        if (directory is null) return;
        string path = Path.Combine(directory.FullName, "artifacts/delphi-live-replays");
        if (!Directory.Exists(path)) return;
        var latest = new DirectoryInfo(path).GetFiles("*-report.json").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        if (latest is null) return;
        try { LoadReplay(latest.FullName, false); }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or ArgumentException)
        { /* A malformed local replay cannot prevent the live monitor from starting. Explicit load reports the error. */ }
    }
    private void RebuildWatchlist()
    {
        string? selected = SelectedWatchlistRow?.Symbol;
        IEnumerable<DelphiLiveWatchlistRow> rows;
        if (IsReplayMode)
            rows = CurrentFrame?.Rows.Select(r => new DelphiLiveWatchlistRow(r.Symbol, r.DailySource, r.Rank, r.Price,
                r.ChangeFromPreviousClose, Words(r.Momentum), Words(r.State), r.Confidence, Words(r.Reason),
                $"{Words(r.State)}. {Words(r.Reason)}. {string.Join("; ", r.Families.Select(f => $"{Words(f.Family.ToString())}: {Words(f.State.ToString())}"))}.",
                JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }))) ?? [];
        else if (AllObservations.Count > 0)
            rows = AllObservations.GroupBy(r => r.Symbol).Select(g => g.OrderBy(r => r.Role == "OperationalChampion" ? 0 : 1).First())
                .Select(r => new DelphiLiveWatchlistRow(r.Symbol, r.DailySource, r.Rank, r.Price, r.ChangeFromPreviousClose, Words(r.Momentum), Words(r.Lifecycle),
                    Words(r.Confidence), Words(r.Reason), $"{Words(r.Momentum)} · {Words(r.Lifecycle)}. {Words(r.Reason)}.", r.EvidenceJson));
        else rows = dailyPreview.Picks.Select((p, index) => new DelphiLiveWatchlistRow(p.Symbol, p.Source, index + 1, p.PreviousClose,
            null, "Not observed yet", p.Eligible ? "Waiting for monitoring" : "Daily gate blocked", "Daily snapshot",
            p.Eligible ? "No live session recorded" : Words(p.EligibilityReason),
            p.Eligible ? "This stock passed a daily lens. Live strength has not been observed in this view yet." :
                $"This stock is published in Delphi's list but is not eligible for entry under the current daily gates: {Words(p.EligibilityReason)}.",
            JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true })));
        Replace(WatchlistRows, rows);
        Replace(ReplayPositions, CurrentFrame?.Positions ?? []);
        Replace(ReplayTrades, CurrentFrame is null ? [] : replayReport!.Trades.Where(t => t.RecordedUtc <= CurrentFrame.SimulatedUtc)
            .Select(t => new DelphiLiveReplayTradeRow(t.Symbol, t.Side, TorontoTime(t.DecisionUtc), TorontoTime(t.FilledUtc), t.Quantity,
                t.Price, t.RealizedProfit, Words(t.Reason), t.Outcome)));
        SelectedWatchlistRow = WatchlistRows.FirstOrDefault(r => r.Symbol == selected);
        if (SelectedWatchlistRow is null) WatchlistExplanation = "Select a stock to see what its status means.";
        foreach (string property in new[] { nameof(DisplayModeIndex), nameof(ReplayFrameIndex), nameof(ReplayFrameMaximum), nameof(IsReplayMode),
            nameof(HasSavedReplay), nameof(CanStepBack), nameof(CanStepForward), nameof(ReplayButtonText), nameof(WatchlistTitle), nameof(WatchlistSummary),
            nameof(WatchlistHelp), nameof(ReplayTime), nameof(ReplayCapitalText), nameof(ReplayCashText), nameof(ReplayValueText), nameof(ReplayProfitText),
            nameof(ReplayCoverageText), nameof(ReplayAssumptions), nameof(WatchlistNotice) }) OnPropertyChanged(property);
    }
    private static string Words(string text) => Regex.Replace(text, "(?<=[a-z])(?=[A-Z])", " ");
}

public sealed record DelphiLiveWatchlistRow(string Symbol, string DailySource, int? Rank, decimal? Price, decimal? Change,
    string Momentum, string State, string Confidence, string Reason, string Explanation, string Evidence);
public sealed record DelphiLiveReplayTradeRow(string Symbol, string Side, string Decision, string Filled, int Quantity,
    decimal? Price, decimal? RealizedProfit, string Reason, string Outcome);
