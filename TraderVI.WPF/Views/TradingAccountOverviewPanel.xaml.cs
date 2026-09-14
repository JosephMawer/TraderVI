#nullable enable
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;
namespace TraderVI.WPF.Views;
public partial class TradingAccountOverviewPanel : UserControl
{
    public TradingAccountOverviewPanel() => InitializeComponent();
    private void Portfolios_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as PaperDashboardWindow)?.OpenPortfolios((DataContext as TradingAccountOverview)?.Account ?? "TFSA");
    private void Monitor_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as PaperDashboardWindow)?.OpenTradingMonitor();
}
