using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class ManualSellOrdersPanel : UserControl
{
    public ManualSellOrdersPanel() => InitializeComponent();
    public event RoutedEventHandler StatusChecked;
    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ManualSellViewModel model) await model.CheckAsync();
        StatusChecked?.Invoke(this, e);
    }
}
