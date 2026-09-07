#nullable enable
using Core.Db;
using Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed class DelphiSettingsViewModel : INotifyPropertyChanged
{
    private readonly DelphiSettingsRepository repository = new();
    private DelphiSettingsCatalog? catalog;
    private DelphiStrategySettings? selected;
    private bool busy;
    private string status = "Load saved strategies to review their models and thresholds.";
    private string active = "Not loaded";
    private string name = "";
    private string reason = "";

    public ObservableCollection<DelphiStrategySettings> Strategies { get; } = [];
    public ObservableCollection<DelphiSettingsModelRow> Models { get; } = [];
    public ObservableCollection<DelphiThresholdEditor> Thresholds { get; } = [];
    public bool IsBusy { get => busy; set { Set(ref busy, value); OnPropertyChanged(nameof(CanEdit)); } }
    public bool CanEdit => !IsBusy;
    public string Status { get => status; set => Set(ref status, value); }
    public string ActiveSummary { get => active; private set => Set(ref active, value); }
    public string NewVersionName { get => name; set => Set(ref name, value); }
    public string ReviewReason { get => reason; set => Set(ref reason, value); }
    public string PolicySummary => selected is null ? "" :
        $"{selected.Strategy.Description}\nPolicy: {selected.Strategy.DecisionRef ?? "unidentified"} · " +
        $"Inputs: {selected.ModelSet?.InputContract ?? "no preserved assignment"}\n" +
        (DailyBenchmarkPolicy.IsRequired(selected.Strategy.DecisionRef)
            ? "Observed XIU and SPY checks are required."
            : "Historical policy: this strategy does not require the current observed XIU/SPY checks.") +
        (selected.UnavailableReason is null ? "" : $"\nUnavailable: {selected.UnavailableReason}");
    public string PreservedSettings => selected is null ? "" :
        $"Stored risk settings (preserved): stop {selected.Strategy.ToConfig().StopLossPercent:P0}, " +
        $"warning {selected.Strategy.ToConfig().WarningPercent:P0}, max positions {selected.Strategy.ToConfig().MaxPositions}. " +
        "These do not configure the independent paper-account exit policies. Model signal thresholds, weights and deterministic indicators remain attached to the saved strategy.";
    public DelphiStrategySettings? SelectedStrategy
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value)) return;
            Models.Clear(); Thresholds.Clear();
            NewVersionName = "";
            if (value is not null)
            {
                if (value.ModelSet is not null)
                    foreach (var model in value.ModelSet.Models)
                        Models.Add(new(model.Registry.TaskType, model.Registry.Name, model.Registry.ModelId.ToString(),
                            $"{model.Registry.FeatureSet} · {model.Registry.LookbackBars} input sessions · {model.Registry.HorizonBars} forecast sessions",
                            $"Signal buy ≥ {model.Registry.ThresholdBuy:G4}; sell ≤ {model.Registry.ThresholdSell:G4} · weight {model.CompositeWeight:G3}",
                            $"Training data: {model.Registry.TrainedFromUtc:yyyy-MM-dd} – {model.Registry.TrainedToUtc:yyyy-MM-dd}", model.ArtifactSha256));
                var g = DelphiGateSettings.From(value.Strategy.ToConfig());
                Add("Minimum composite", "Blended conviction needed to pass the composite gate.", g.MinCompositeScore);
                Add("Minimum up probability", "Absolute floor for the up model’s probability.", g.MinUpProb);
                Add("Minimum breakout probability", "Minimum breakout probability for the setup gate.", g.MinBreakoutProb);
                Add("Minimum direction edge", "P(up) minus P(down); range −1 to 1.", g.MinDirectionEdge);
                Add("Maximum down probability", "A probability at or above this value blocks the trade.", g.MaxDownProb);
                Add("Breadth veto", "Breadth at or below this value blocks new longs; range −1 to 1.", g.BreadthVetoThreshold);
                Add("Strong breakout override", "Breakout probability needed to bypass the composite gate with strong edge.", g.StrongBreakoutOverride);
                Add("Strong edge override", "Direction edge also needed for the composite override; range −1 to 1.", g.StrongEdgeOverride);
            }
            OnPropertyChanged(nameof(PolicySummary)); OnPropertyChanged(nameof(PreservedSettings));
        }
    }

    private void Add(string label, string help, double value) => Thresholds.Add(new(label, help, value.ToString("G", CultureInfo.InvariantCulture)));

    public async Task RefreshAsync(Guid? select = null)
    {
        IsBusy = true;
        try
        {
            var loaded = await repository.LoadAsync();
            var current = loaded.Active;
            catalog = loaded;
            SelectedStrategy = null;
            Strategies.Clear();
            foreach (var option in loaded.Strategies) Strategies.Add(option);
            ActiveSummary = $"{current.Strategy.VersionName} · shared by desktop and nightly Delphi";
            SelectedStrategy = loaded.Strategies.FirstOrDefault(s => s.Strategy.VersionId == select) ?? current;
            Status = "Select a saved model set to load its strategy. Browsing and editing do not change the engine.";
        }
        catch
        {
            catalog = null; SelectedStrategy = null; Strategies.Clear();
            ActiveSummary = "Unable to load active settings";
            Status = "Settings could not be loaded. Check the local database and migration 026, then Reload.";
        }
        finally { IsBusy = false; }
    }

    public DelphiSettingsChange PrepareReview()
    {
        if (catalog is null || SelectedStrategy is null || Thresholds.Count != 8)
            throw new InvalidOperationException("Load and select a saved strategy first.");
        var values = Thresholds.Select(x => x.Parse()).ToArray();
        var gates = new DelphiGateSettings(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);
        string code = $"assembly-mvid:{typeof(DelphiWorkflow).Module.ModuleVersionId:D}";
        return DelphiSettingsChange.Prepare(catalog, SelectedStrategy, gates, NewVersionName, ReviewReason, code);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(property); return true;
    }
}

public sealed record DelphiSettingsModelRow(string Task, string Name, string Id, string Features, string Signals, string Training, string Hash);

public sealed class DelphiThresholdEditor(string label, string help, string value) : INotifyPropertyChanged
{
    private string text = value;
    public string Label { get; } = label;
    public string Help { get; } = help;
    public string Value
    {
        get => text;
        set { if (text == value) return; text = value; PropertyChanged?.Invoke(this, new(nameof(Value))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public double Parse() => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
        && double.IsFinite(result) ? result : throw new ArgumentException($"{Label}: enter a finite decimal using a dot, such as 0.35.");
}
