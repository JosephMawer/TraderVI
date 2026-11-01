#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using Core.Trader;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class DelphiView : UserControl
{
    private void Portfolios_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as PaperDashboardWindow)?.OpenPortfolios();
    private readonly DelphiViewModel viewModel = new();
    private bool loadedOnce;

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as PaperDashboardWindow)?.OpenSettings("Daily");
    public event EventHandler<PaperTradeEntryResult>? PaperPositionOpened;

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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        try
        {
            await viewModel.RefreshAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void PickGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;
        if (grid.SelectedItem is not DelphiPickRow pick)
        {
            if (ContinuationGrid.SelectedItem == null && BreakoutGrid.SelectedItem == null)
                viewModel.SelectPaperPick(null);
            return;
        }
        if (ReferenceEquals(grid, ContinuationGrid))
            BreakoutGrid.SelectedItem = null;
        else
            ContinuationGrid.SelectedItem = null;
        viewModel.SelectPaperPick(pick);
    }

    private async void AddToPaperButton_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.TryBuildPaperEntry(
                out DelphiPickRow? pick,
                out int shares,
                out decimal fillPrice,
                out TrackedExecutionMode executionMode,
                out string? accountLabel,
                out string error) || pick is null)
        {
            MessageBox.Show(error, "Position entry", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        decimal cost = decimal.Round(fillPrice * shares, 2);
        bool isDiscretionaryRealOverride =
            executionMode == TrackedExecutionMode.Real &&
            !string.Equals(pick.Direction, "Buy", StringComparison.OrdinalIgnoreCase);
        string exploratory = string.Equals(pick.Lens, "Breakout", StringComparison.OrdinalIgnoreCase)
            ? "\n\nThis is an exploratory Breakout-lens selection; Continuation remains the production lens."
            : "";
        string overrideWarning = isDiscretionaryRealOverride
            ? $"\n\nDELPHI DIRECTION: {pick.Direction.ToUpperInvariant()}. This is a discretionary override, not a Delphi Buy recommendation. " +
              "TraderVI will track the actual holding without original-pick attribution or the fresh-Delphi loss exception."
            : "";
        MessageBoxResult answer = MessageBox.Show(
            $"Track {shares} share(s) of {pick.Symbol} at {fillPrice:C2} as {executionMode.ToStorageValue().ToUpperInvariant()}?\n\n" +
            $"Book cost: {cost:C2}\nSaved pick: {pick.Lens} #{pick.Rank} from {pick.PickDate:MMM d, yyyy}" +
            (executionMode == TrackedExecutionMode.Real ? $"\nAccount: {accountLabel}" : "") +
            exploratory +
            overrideWarning +
            (executionMode == TrackedExecutionMode.Real
                ? "\n\nConfirm only if this fill already happened at your broker. TraderVI records it but sends no order."
                : "\n\nThis creates a simulated Ghost fill. No broker order can be placed."),
            "Confirm tracked position",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        AddToPaperButton.IsEnabled = false;
        try
        {
            PaperTradeEntryResult result = await viewModel.OpenPaperPositionAsync(
                pick,
                shares,
                fillPrice,
                executionMode,
                accountLabel,
                confirmNonBuyRealOverride: isDiscretionaryRealOverride);
            PaperPositionOpened?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Position entry failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AddToPaperButton.IsEnabled = true;
        }
    }

    private void ExistingFill_Click(object sender, RoutedEventArgs e)
    { viewModel.SelectedExecutionMode = TrackedExecutionMode.Real.ToStorageValue(); RecordedFillSection.Visibility = Visibility.Visible; RecordedFillSection.IsExpanded = true; }
    private void GhostFill_Click(object sender, RoutedEventArgs e)
    { viewModel.SelectedExecutionMode = TrackedExecutionMode.Ghost.ToStorageValue(); RecordedFillSection.Visibility = Visibility.Visible; RecordedFillSection.IsExpanded = true; }
    private void RecordedFill_Collapsed(object sender, RoutedEventArgs e) => RecordedFillSection.Visibility = Visibility.Collapsed;
}
