#nullable enable
using Core.Db;
using Core.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace TraderVI.WPF.Viewmodels;

public sealed class GlobalSettingsViewModel : INotifyPropertyChanged
{
    private EngineSettingsCatalog? catalog;
    private EngineStrategyVersion? selected;
    private EngineSettingsTarget? target;
    private string family = EngineStrategySettings.Live;
    private string status = "Load the saved versions and current assignments.";
    private string name = "", reason = "";
    private bool busy, installed;
    public ObservableCollection<EngineStrategyVersion> Versions { get; } = [];
    public ObservableCollection<EngineSettingsTarget> Targets { get; } = [];
    public ObservableCollection<EngineSettingsTarget> Accounts { get; } = [];
    public ObservableCollection<SettingEditor> Fields { get; } = [];
    public string Family { get => family; set { family = value; LoadFamily(); Changed(); Changed(nameof(Description)); Changed(nameof(Title)); } }
    public string Title => Family switch { EngineStrategySettings.Live => "Delphi Live", EngineStrategySettings.Shadow => "System Shadow", _ => "Trading monitor" };
    public string Description => Family switch
    {
        EngineStrategySettings.Live => "Intraday signals, entry sizing and protection rules. Manual assignment pauses the existing research promotion protocol. Confirmation restarts; current holdings receive immediate protection review.",
        EngineStrategySettings.Shadow => "Daily picks feed independent Shadow portfolios. Entry allocation, risk limits, friction and re-entry rules are versioned here. Each portfolio retains its existing lens and 3/5-slot definition.",
        _ => "Exit strategy for all tracked positions. Real exits remain manual. Ghost exits follow the General & operations switch. The reviewed 15-minute evidence and freshness contract stays fixed."
    };
    public string Status { get => status; set => Set(ref status, value); }
    public string NewName { get => name; set => Set(ref name, value); }
    public string Reason { get => reason; set => Set(ref reason, value); }
    public bool Busy { get => busy; set { Set(ref busy, value); Changed(nameof(CanEdit)); Changed(nameof(CanWrite)); } }
    public bool CanEdit => !Busy;
    public bool CanWrite => !Busy && installed;
    public EngineStrategyVersion? Selected
    {
        get => selected;
        set
        {
            Set(ref selected, value); Fields.Clear(); NewName = "";
            if (value is not null)
                foreach (var setting in EngineStrategySettings.Fields(Family, EngineStrategySettings.Read(Family, value.SettingsJson))) Fields.Add(new(setting));
        }
    }
    public EngineSettingsTarget? Target { get => target; set { Set(ref target, value); Changed(nameof(TargetSummary)); } }
    public string TargetSummary => Target is null ? "Choose an existing system or portfolio. No account will be created by saving a strategy." :
        $"Current assignment: {Target.ActiveName}\nTarget: {Target.Name}\nState: {Target.State}";
    public async Task RefreshAsync(Guid? select = null)
    {
        Busy = true;
        try
        {
            catalog = await new EngineSettingsRepository().LoadAsync(); installed = catalog.Installed;
            Accounts.Clear(); foreach (var account in catalog.Targets) Accounts.Add(account);
            LoadFamily();
            if (select is Guid id) Selected = Versions.FirstOrDefault(v => v.VersionId == id) ?? Selected;
            Status = installed ? "Save Version preserves the strategy. Assign changes the selected target immediately." :
                $"Preview available. Saving and assignment require reviewed migration {EngineSettingsRepository.Migration}.";
        }
        catch
        {
            catalog = null; installed = false; Versions.Clear(); Targets.Clear(); Accounts.Clear(); Fields.Clear();
            Status = "Settings could not be loaded. Check the local database and reload.";
        }
        finally { Busy = false; }
    }
    private void LoadFamily()
    {
        var priorTarget = Target?.TargetId;
        Selected = null; Versions.Clear(); Targets.Clear();
        if (catalog is not null)
        {
            foreach (var version in catalog.Versions.Where(v => v.Family == Family)) Versions.Add(version);
            foreach (var item in catalog.Targets.Where(t => t.Family == Family)) Targets.Add(item);
        }
        string json = EngineStrategySettings.Serialize(EngineStrategySettings.Defaults(Family));
        Versions.Add(new(Guid.Empty, Family, "Version 1 template · save before assigning", json, EngineStrategySettings.Hash(json), DateTime.MinValue));
        Target = Targets.FirstOrDefault(t => t.TargetId == priorTarget) ?? Targets.FirstOrDefault();
        Selected = Versions.FirstOrDefault(v => v.VersionId == Target?.VersionId) ?? Versions.First();
    }
    public object EditedSettings()
    {
        if (Selected is null) throw new InvalidOperationException("Select a source version first.");
        return EngineStrategySettings.Edit(Family, EngineStrategySettings.Read(Family, Selected.SettingsJson), Fields.Select(f => f.Field with { Value = f.Value }), Guid.NewGuid());
    }
    public void RequireSavedUneditedAssignment()
    {
        if (Reason.Trim().Length is < 1 or > 512) throw new ArgumentException("Enter a reason for this assignment.");
        if (Selected is null || Selected.VersionId == Guid.Empty || Target is null) throw new InvalidOperationException("Select a saved version and a target.");
        // Compare parsed values, allowing harmless formatting differences.
        var edited = EngineStrategySettings.Edit(Family, EngineStrategySettings.Read(Family, Selected.SettingsJson), Fields.Select(f => f.Field with { Value = f.Value }), Selected.VersionId);
        using var actual = JsonDocument.Parse(EngineStrategySettings.Serialize(edited));
        using var saved = JsonDocument.Parse(Selected.SettingsJson);
        if (!JsonElement.DeepEquals(actual.RootElement, saved.RootElement)) throw new InvalidOperationException("These fields have unsaved changes. Save Version first, then assign that saved version.");
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { field = value; Changed(name); }
}

public sealed class SettingEditor(EngineSettingField field) : INotifyPropertyChanged
{
    public EngineSettingField Field { get; } = field;
    public string Label => Field.Label;
    public string Help => Field.Help;
    private string value = field.Value;
    public string Value { get => value; set { this.value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
