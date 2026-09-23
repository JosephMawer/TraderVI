using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class WealthsimpleAccountPanel : UserControl
{
    public event RoutedEventHandler ReviewTransitionRequested;
    public WealthsimpleAccountPanel() => InitializeComponent();
    private void ReviewTransition_Click(object sender, RoutedEventArgs e) => ReviewTransitionRequested?.Invoke(this, e);
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    { if (DataContext is WealthsimpleAccountViewModel vm) await vm.RefreshAsync(); }
    private async void StopBuying_Click(object sender, RoutedEventArgs e)
    { if (DataContext is WealthsimpleAccountViewModel { ManagedStrategy: { } strategy }) await strategy.StopBuyingAsync(); }
    private async void ResumeBuying_Click(object sender, RoutedEventArgs e)
    { if (DataContext is WealthsimpleAccountViewModel { ManagedStrategy: { } strategy }) await strategy.ResumeBuyingAsync(); }
}
