using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class WealthsimpleAccountPanel : UserControl
{
    public WealthsimpleAccountPanel() => InitializeComponent();
    private void ViewStrategy_Click(object sender, RoutedEventArgs e)
    {
        var source = (DataContext as WealthsimpleAccountViewModel)?.ManagedStrategy?.PaperSourceId;
        (Window.GetWindow(this) as PaperDashboardWindow)?.OpenPortfolios(livePortfolio: source, strategyWorkspace: true);
    }
    private async void RefreshBroker_Click(object sender, RoutedEventArgs e)
    { if (DataContext is WealthsimpleAccountViewModel vm) await vm.RefreshAsync(); }
    private void Today_Checked(object sender, RoutedEventArgs e)
    { if (DataContext is WealthsimpleAccountViewModel vm) vm.ShowOverallPerformance = false; }
}
