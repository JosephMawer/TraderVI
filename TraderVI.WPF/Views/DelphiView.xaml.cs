#nullable enable

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class DelphiView : UserControl
{
    private readonly DelphiViewModel viewModel = new();
    private bool loadedOnce;

    public bool IsRunning => viewModel.IsRunning;

    public DelphiView()
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += DelphiView_Loaded;
    }

    private async void DelphiView_Loaded(object sender, RoutedEventArgs e)
    {
        if (loadedOnce)
            return;
        loadedOnce = true;
        await viewModel.RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RunWithButtonsDisabledAsync(() => viewModel.RefreshAsync());

    private async void RunDelphiButton_Click(object sender, RoutedEventArgs e)
    {
        DateTime recommendationDate = DateTime.Today;
        string existingText = viewModel.HasRecommendationsFor(recommendationDate)
            ? "Saved operational records already exist for today and will be replaced. "
            : "Any saved operational records for today will be replaced. ";
        string message =
            $"Run official Delphi for {recommendationDate:MMMM d, yyyy}?\n\n" +
            "This reads local SQL market data and registered model files. " +
            existingText +
            "A new immutable calibration record will also be appended.\n\n" +
            "It will not place a broker order or create a paper position.";

        MessageBoxResult answer = MessageBox.Show(
            message,
            "Confirm official Delphi run",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        await RunWithButtonsDisabledAsync(() => viewModel.RunOfficialAsync());
    }

    private async Task RunWithButtonsDisabledAsync(Func<Task> action)
    {
        RefreshButton.IsEnabled = false;
        RunDelphiButton.IsEnabled = false;
        try
        {
            await action();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            RunDelphiButton.IsEnabled = true;
        }
    }
}
