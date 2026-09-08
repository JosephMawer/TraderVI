#nullable enable
using Core.Db;
using Core.Runtime;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class GlobalSettingsView : UserControl
{
    private readonly GlobalSettingsViewModel model = new();
    private bool loaded;
    public bool IsBusy => model.Busy || DailyEditor.IsBusy;
    public bool HasUnsavedDrafts => model.HasAnyDrafts || DailyEditor.HasUnsavedDrafts;
    public async Task RefreshAccessAsync() { await model.RefreshAccessAsync(); await DailyEditor.RefreshAccessAsync(); }
    public Func<string, Task<string>>? ReevaluateAsync { get; set; }
    public Action<bool>? PreferencesChanged { get; set; }
    private static string PreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "settings.json");
    public GlobalSettingsView()
    {
        InitializeComponent(); DataContext = model;
        DailyEditor.ReevaluateAsync = () => ReevaluateAsync?.Invoke("Daily") ?? Task.FromResult("Assigned; reevaluation is awaiting the desktop host.");
        Loaded += async (_, _) => { if (!loaded) { loaded = true; await model.RefreshAsync(); } };
    }
    public void InitializePreferences(bool fallback)
    {
        bool automatic = fallback;
        try { if (File.Exists(PreferencesPath)) automatic = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath))?.AutomaticGhostExits ?? fallback; }
        catch { PreferenceStatus.Text = "Saved preferences could not be read. The current host setting is shown."; }
        GhostExits.IsChecked = automatic; PreferencesChanged?.Invoke(automatic);
    }
    public async void Navigate(string section)
    {
        GeneralPanel.Visibility = section == "General" ? Visibility.Visible : Visibility.Collapsed;
        AccountsPanel.Visibility = section == "Accounts" ? Visibility.Visible : Visibility.Collapsed;
        DailyEditor.Visibility = section == "Daily" ? Visibility.Visible : Visibility.Collapsed;
        bool strategy = section is EngineStrategySettings.Live or EngineStrategySettings.Shadow or EngineStrategySettings.Tracked;
        StrategyPanel.Visibility = strategy ? Visibility.Visible : Visibility.Collapsed;
        if (strategy) { model.Family = section; StrategyControls.SystemKey = section; await model.RefreshAccessAsync(); await StrategyControls.RefreshAsync(); }
        if (section == "Daily") await DailyEditor.RefreshAccessAsync();
    }
    private void Navigate_Click(object sender, RoutedEventArgs e) => Navigate((string)((Button)sender).Tag);
    private void OpenAccount_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is not EngineSettingsTarget target) return;
        Navigate(target.Family); model.Target = target;
        model.Selected = model.Versions.FirstOrDefault(v => v.VersionId == target.VersionId) ?? model.Versions.Last();
    }
    private async void Reload_Click(object sender, RoutedEventArgs e) => await model.RefreshAsync();
    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            string temporary = PreferencesPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preferences(GhostExits.IsChecked == true)));
            File.Move(temporary, PreferencesPath, true);
            PreferencesChanged?.Invoke(GhostExits.IsChecked == true);
            PreferenceStatus.Text = "Saved. Applies at the next Trading monitor cycle.";
        }
        catch { PreferenceStatus.Text = "Preferences could not be saved. The host setting was not changed."; }
    }
    private async void SaveVersion_Click(object sender, RoutedEventArgs e)
    {
        if (model.Busy) return;
        model.Busy = true;
        try
        {
            var settings = model.EditedSettings();
            string changes = string.Join("\n", model.Fields.Where(f => f.Value != f.Field.Value).Select(f => $"{f.Label}: {f.Field.Value} → {f.Value}"));
            if (MessageBox.Show(Window.GetWindow(this), $"Save '{model.NewName}' for {model.Title}?\n\n" +
                (changes.Length == 0 ? "Preserve this template as a new version." : changes) +
                "\n\nThis saves all sections together. Assignments and trading remain unchanged.", "Review complete strategy version", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            var saved = await new EngineSettingsRepository().SaveAsync(model.Family, model.NewName, settings, model.Reason);
            model.DiscardCurrentDraft();
            await model.RefreshAsync(saved.VersionId);
            model.Status = $"Saved {saved.Name}. No assignment changed. Select a target below to assign it.";
        }
        catch (Exception ex) { model.Status = "Save failed. " + SafeMessage(ex); }
        finally { model.Busy = false; }
    }
    private async void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (model.Busy) return;
        model.Busy = true; bool assigned = false;
        try
        {
            model.RequireSavedUneditedAssignment();
            var target = model.Target!; var version = model.Selected!;
            if (MessageBox.Show(Window.GetWindow(this), $"Assign {version.Name} to {target.Name}?\n\nCurrent: {target.ActiveName}\n\n" +
                "All affected accounts must be paused, with no holdings or pending orders. No sale is made by this action. " +
                "The selected target receives this version; buying stays paused until you resume it separately. Financial history and risk-review holds are preserved.\n\n" +
                (target.Family == EngineStrategySettings.Live ? "This operator-managed account remains outside automatic research promotion until a fresh comparison is reviewed.\n\n" : "") +
                "The host will begin reevaluation. Execution still needs eligible market evidence; closed markets may leave it waiting. No broker order is sent.",
                "Assign strategy now?", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            await new EngineSettingsRepository().AssignAsync(target, version, model.Reason); assigned = true;
            model.Status = $"Assigned {version.Name}. Reevaluating…";
            string outcome = ReevaluateAsync is null ? "Awaiting the next host evaluation." : await ReevaluateAsync(target.Family);
            await model.RefreshAsync(version.VersionId);
            model.Status = $"Assigned {version.Name}. {outcome}";
        }
        catch (Exception ex) { model.Status = assigned ? "The new assignment is active. Reevaluation did not complete; the next host cycle will retry. " + SafeMessage(ex) : "Assignment was not confirmed. Reload before retrying. " + SafeMessage(ex); }
        finally { model.Busy = false; }
    }
    private static string SafeMessage(Exception ex) => ex is ArgumentException or InvalidOperationException ? ex.Message : "Check the local database and reload settings.";
    private void DiscardDraft_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this), "Discard the unsaved changes for this strategy draft? Saved versions and assignments stay unchanged.", "Discard draft", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel)==MessageBoxResult.OK)
            model.DiscardCurrentDraft();
    }
    private sealed record Preferences(bool AutomaticGhostExits);
}
