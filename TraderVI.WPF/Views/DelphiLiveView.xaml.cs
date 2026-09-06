#nullable enable

using Core.Runtime;
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

    public DelphiLiveView()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => viewModel.RefreshAsync(cancellationToken);
    public Task TickAsync(CancellationToken cancellationToken = default) => viewModel.TickAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => viewModel.StopAsync(cancellationToken);

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
    private void ExperimentEvidenceButton_Click(object sender, RoutedEventArgs e) => viewModel.ShowExperimentEvidence();
    private void ResearchEvidenceButton_Click(object sender, RoutedEventArgs e) => viewModel.ShowResearchEvidence();
    private async void LoadResearchButton_Click(object sender, RoutedEventArgs e)
    {
        try { await viewModel.LoadResearchAsync(); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Research report could not be loaded", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async void DiscoveryButton_Click(object sender, RoutedEventArgs e) =>
        await ReviewCommandAsync(viewModel.CanScheduleDiscovery, "Schedule discovery", viewModel.DiscoveryReview, () => viewModel.ScheduleDiscoveryAsync());
    private async void UntouchedButton_Click(object sender, RoutedEventArgs e) =>
        await ReviewCommandAsync(viewModel.CanScheduleUntouched, "Start untouched confirmation",
            $"Freeze contender {viewModel.SelectedChallenger?.PolicyId} for untouched confirmation at the next regular session?\n\n" +
            "Aligned comparison portfolios restart cash-only. The contender settings cannot be tuned during untouched confirmation.\n\n" +
            $"Reason: {viewModel.OperatorReason.Trim()}", () => viewModel.ScheduleUntouchedAsync());
    private async void PromotionButton_Click(object sender, RoutedEventArgs e) =>
        await ReviewCommandAsync(viewModel.CanApprovePromotion, "Approve champion promotion",
            $"Approve the frozen contender for the next regular-session boundary?\n\n{viewModel.PromotionText}\n\n" +
            "The operational portfolio retains its cash, open positions, fills and protective floors under the promoted policy.\n\n" +
            $"Reason: {viewModel.OperatorReason.Trim()}", () => viewModel.ApprovePromotionAsync());
    private async void DefectButton_Click(object sender, RoutedEventArgs e) =>
        await ReviewCommandAsync(viewModel.CanRecordMeasurementDefect, "Record measurement defect",
            "Record a measurement or implementation defect and restart the ten-session engineering shakedown count? " +
            "Pending experiment changes are cleared and the prior comparison run is archived at the next session boundary.\n\n" +
            $"Reason: {viewModel.OperatorReason.Trim()}", () => viewModel.RecordMeasurementDefectAsync());
    private async void ResumeCapitalButton_Click(object sender, RoutedEventArgs e) =>
        await ReviewCommandAsync(viewModel.CanResumeCapitalReview, "Resume after capital review",
            $"Resume new-risk consideration for {viewModel.SelectedPortfolio?.Role} ({viewModel.SelectedPortfolio?.PortfolioId}) " +
            "using its latest complete checkpoint NAV as the reviewed capital baseline? Daily buying pauses and all exit protections continue to apply.\n\n" +
            $"Reason: {viewModel.OperatorReason.Trim()}", () => viewModel.ResumeCapitalReviewAsync());
    private async void CorporateActionButton_Click(object sender, RoutedEventArgs e) =>
        await ReviewCommandAsync(viewModel.CanRecordCorporateAction, "Record unsupported corporate action",
            $"Record an unsupported corporate action for {viewModel.CorporateSymbol.Trim().ToUpperInvariant()}, " +
            $"{viewModel.CorporateFrom} through {viewModel.CorporateThrough}, inclusive?\n\n" +
            "Affected research and comparison cohorts will be excluded. This audit is append-only and does not adjust shares or prices.\n\n" +
            $"Reason: {viewModel.OperatorReason.Trim()}", () => viewModel.RecordCorporateActionAsync());

    private static async Task ReviewCommandAsync(bool allowed, string title, string message, Func<Task> command)
    {
        if (!allowed || MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;
        try { await command(); }
        catch (Exception exception) { MessageBox.Show(exception.Message, title + " was not completed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanActivate)
            return;
        MessageBoxResult answer = MessageBox.Show(
            $"Activate Delphi Live using {viewModel.StartingCapital} {viewModel.Currency.Trim().ToUpperInvariant()} as explicit simulation capital?\n\n" +
            $"Reason: {viewModel.ActivationReason.Trim()}\n\n" +
            "Activation takes effect at the next regular TSX session. While TraderVI is open, Delphi Live will collect market evidence " +
            "and record paper decisions and fills. It cannot place a broker order. This portfolio does not accept deposits or withdrawals in V1.",
            "Confirm Delphi Live activation", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;
        try { await viewModel.ActivateAsync(); }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Delphi Live activation was not completed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
