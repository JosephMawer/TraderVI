using System;
using System.Windows;
using System.Windows.Media;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public sealed class ManualSellWindow : Window
{
    public event EventHandler StatusChecked;
    public ManualSellWindow(ManualSellViewModel model)
    {
        Title = "Manual sells"; Width = 620; Height = 700; MinWidth = 490; MinHeight = 450;
        MaxHeight = SystemParameters.WorkArea.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(26, 32, 41));
        var panel = new ManualSellOrdersPanel { DataContext = model };
        panel.StatusChecked += (_, _) => StatusChecked?.Invoke(this, EventArgs.Empty);
        Content = panel;
        Closing += (_, e) => { if (model.IsBusy) e.Cancel = true; };
    }
}
