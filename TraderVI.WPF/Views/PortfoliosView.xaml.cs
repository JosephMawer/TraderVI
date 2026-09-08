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
    private void OpenTrading_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as PaperDashboardWindow)?.OpenTrading();
    private void ReviewPromotion_Click(object sender, RoutedEventArgs e) { if (viewModel.CanReview) viewModel.SelectedView = 2; }
    private void BackToCompare_Click(object sender, RoutedEventArgs e) => viewModel.SelectedView = 0;
    private void ReviewAssignments_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as PaperDashboardWindow)?.OpenSettings("Daily");
    private void InspectStrategy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PortfolioComparisonRow row })
        {
            viewModel.SelectedComparison = row;
            viewModel.SelectedView = 0;
        }
    }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as PaperDashboardWindow)?.OpenSettings("Accounts");

    public PortfoliosView()
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += async (_, change) =>
        {
            if (change.PropertyName == nameof(PortfoliosViewModel.SelectedPortfolio))
                await RunUiActionAsync(() => viewModel.RefreshDetailsAsync());
        };
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
            await viewModel.StartDailyShadowAsync();
            DateTime localNow = PaperTradingMonitor.ToToronto(DateTime.UtcNow);
            if (PaperTradingMonitor.IsAutomaticPollTime(localNow))
                await RunScheduledCycleAsync();
        });
    }

    private async void SnapshotButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.RecordAccountSnapshotAsync());

    private async void PauseButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.PauseAsync());

    private async void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if(MessageBox.Show(Window.GetWindow(this),"Confirm that you have reviewed this account's drawdown and current value. Clear its capital-review hold? The separate operator pause remains in force.",
            "Capital review",MessageBoxButton.OKCancel,MessageBoxImage.Question,MessageBoxResult.Cancel)==MessageBoxResult.OK)
            await RunUiActionAsync(() => viewModel.ResumeAsync());
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(() => viewModel.RenameSelectedAsync());
    private async void SellHolding_Click(object sender, RoutedEventArgs e)
    {
        if(sender is Button {DataContext:PortfolioHoldingRow row} && row.CanRequestExit)
            await RunUiActionAsync(async()=>
            {
                await OperatorHoldingActions.RequestAsync(Window.GetWindow(this),row.Family,row.TargetId,row.PositionId,row.Symbol);
                await viewModel.RefreshDetailsAsync();
            });
    }

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
