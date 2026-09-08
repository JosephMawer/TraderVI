#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class DelphiSettingsView : UserControl
{
    public Func<Task<string>>? ReevaluateAsync { get; set; }
    public bool IsBusy => model.IsBusy;
    public bool HasUnsavedDrafts => model.HasAnyDrafts;
    public Task RefreshAccessAsync() => model.RefreshAccessAsync();
    private readonly DelphiSettingsViewModel model = new();
    private bool loaded;
    public DelphiSettingsView()
    {
        InitializeComponent(); DataContext = model;
        Loaded += async (_, _) => { if (!loaded) { loaded = true; await model.RefreshAsync(); } };
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await model.RefreshAsync();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (model.IsBusy) return;
        model.IsBusy = true;
        try
        {
            var change = model.PrepareReview();
            if (MessageBox.Show(Window.GetWindow(this), change.ReviewSummary + "\n\nSave all threshold edits as one version? Assignments remain unchanged.",
                "Review daily strategy version", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel)!=MessageBoxResult.OK) return;
            await Task.Run(() => new Core.Db.DelphiSettingsRepository().SaveVersionAsync(change));
            model.DiscardCurrentDraft();
            await model.RefreshAsync(change.TargetId);
            model.Status = $"Saved {change.TargetName}. The daily engine assignment has not changed.";
        }
        catch (Exception ex) { model.Status = "Save was not confirmed. Reload before retrying. " + (ex is ArgumentException or InvalidOperationException ? ex.Message : "Check the database and preserved model files."); }
        finally { model.IsBusy = false; }
    }

    private async void Review_Click(object sender, RoutedEventArgs e)
    {
        if (model.IsBusy) return;
        bool applying = false;
        model.IsBusy = true;
        try
        {
            var change = model.PrepareReview();
            if (change.CreatesVersion) throw new InvalidOperationException("Save Version first, then assign that saved version.");
            model.Status = "Verifying the four preserved model files and prediction schemas…";
            await Task.Run(change.VerifyArtifacts);
            model.Status = "Model files verified. Review the proposed selection.";
            if (MessageBox.Show(Window.GetWindow(this), change.ReviewSummary + "\n\nAll dependent accounts must be paused with no holdings or pending orders. Assignment leaves buying paused. Resume separately after reviewing the new daily results.",
                "Assign daily Delphi strategy?", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            {
                model.Status = "Review cancelled. No settings were changed.";
                return;
            }
            applying = true;
            // File verification and SQL run off the dispatcher; the editor stays disabled.
            await Task.Run(() => new Core.Db.DelphiSettingsRepository().ApplyAsync(change));
            model.Status = $"Assigned {change.TargetName}. Starting daily reevaluation…";
            string outcome;
            try { outcome = ReevaluateAsync is null ? "Reevaluation is awaiting the desktop host." : await ReevaluateAsync(); }
            catch { outcome = "The assignment is active, but reevaluation failed. Run Delphi again to retry."; }
            await model.RefreshAsync();
            model.Status = $"Assigned {change.TargetName}. {outcome}";
        }
        catch (Exception ex)
        {
            string detail = ex switch
            {
                ArgumentException or InvalidOperationException => ex.Message,
                SqlException sql when sql.Number is >= 51300 and <= 51304 => sql.Message,
                IOException => "A model file is unavailable or changed, or the nightly runner holds the selection lock.",
                _ => "The model files or database could not be verified. Reload and review before retrying."
            };
            model.Status = applying ? "Apply did not return a confirmed result. Reload to check the active selection before retrying. " + detail
                : "Review could not complete. " + detail;
        }
        finally { model.IsBusy = false; }
    }
    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if(MessageBox.Show(Window.GetWindow(this),"Discard this unsaved daily strategy draft?","Discard draft",MessageBoxButton.OKCancel,MessageBoxImage.Question,MessageBoxResult.Cancel)==MessageBoxResult.OK)
            model.DiscardCurrentDraft();
    }
}
