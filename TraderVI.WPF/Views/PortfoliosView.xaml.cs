#nullable enable

using Core.Trader;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class PortfoliosView : UserControl
{
    private readonly PortfoliosViewModel viewModel = new();
    private readonly SystemShadowController controller = new();
    private bool loadedOnce;

    public PortfoliosView()
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += PortfoliosView_Loaded;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        viewModel.RefreshAsync(cancellationToken);

    public async Task<SystemShadowPollResult> RunScheduledCycleAsync(
        CancellationToken cancellationToken = default)
    {
        SystemShadowPollResult result = await controller.PollOnceAsync(cancellationToken);
        viewModel.ApplyPollResult(result);
        await viewModel.RefreshAsync(cancellationToken);
        return result;
    }

    private async void PortfoliosView_Loaded(object sender, RoutedEventArgs e)
    {
        if (loadedOnce) return;
        loadedOnce = true;
        await RunUiActionAsync(() => viewModel.RefreshAsync());
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.RefreshAsync());

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(
            "Start four independent System-selected Ghost portfolios using the entered total TFSA value as each portfolio's virtual starting cash?\n\n" +
            "This can request delayed TMX evidence and record virtual trades. It cannot connect to Wealthsimple or place a real order.",
            "Start Shadow V1",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        await RunUiActionAsync(async () =>
        {
            await viewModel.StartAsync();
            DateTime localNow = PaperTradingMonitor.ToToronto(DateTime.UtcNow);
            if (PaperTradingMonitor.IsAutomaticPollTime(localNow))
                await RunScheduledCycleAsync();
        });
    }

    private async void SnapshotButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.RecordAccountSnapshotAsync());

    private async void PauseButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.PauseAsync());

    private async void ResumeButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.ResumeAsync());

    private async void RenameButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.RenameSelectedAsync());

    private async void PortfoliosGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.RefreshDetailsAsync());

    private static async Task RunUiActionAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Portfolio operation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
