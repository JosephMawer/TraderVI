#nullable enable
using Core.Runtime;
using Microsoft.Win32;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class DelphiLiveView : UserControl
{
    private readonly DelphiLiveViewModel viewModel = new(new DelphiLiveDesktopService());
    public DelphiLiveView() { InitializeComponent(); DataContext = viewModel; }
    public Task RefreshAsync(CancellationToken cancellationToken = default) => viewModel.RefreshAsync(cancellationToken);
    public Task TickAsync(CancellationToken cancellationToken = default) => viewModel.TickAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => viewModel.StopAsync(cancellationToken);
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as PaperDashboardWindow)?.OpenSettings(EngineStrategySettings.Live);
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
    private void AdvancedButton_Click(object sender, RoutedEventArgs e) => MainSections.SelectedIndex = 2;
    private void PreviousButton_Click(object sender, RoutedEventArgs e) => viewModel.ReplayFrameIndex--;
    private void NextButton_Click(object sender, RoutedEventArgs e) => viewModel.ReplayFrameIndex++;
    private void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.HasSavedReplay) { viewModel.DisplayModeIndex = 1; MainSections.SelectedIndex = 0; return; }
        var picker = new OpenFileDialog { Title = "Open Delphi Live historical replay", Filter = "Replay report (*-report.json)|*-report.json", CheckFileExists = true };
        if (picker.ShowDialog() != true) return;
        try { viewModel.LoadReplay(picker.FileName); MainSections.SelectedIndex = 0; }
        catch (Exception error) { MessageBox.Show(error.Message, "Replay could not be opened", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void MainSections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, MainSections) && MainSections.SelectedIndex == 1)
            viewModel.DisplayModeIndex = 1;
        else if (ReferenceEquals(e.Source, MainSections) && MainSections.SelectedIndex == 2)
            viewModel.DisplayModeIndex = 0;
    }
    private void DisplayMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: 0 } && MainSections?.SelectedIndex == 1)
            MainSections.SelectedIndex = 0;
    }
}
