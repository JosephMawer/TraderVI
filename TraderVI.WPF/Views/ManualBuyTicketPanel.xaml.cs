#nullable enable
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class ManualBuyTicketPanel : UserControl
{
    public System.Action? Connect { get; set; }
    public ManualBuyTicketPanel() => InitializeComponent();
    private ManualBuyTicketViewModel? Model => DataContext as ManualBuyTicketViewModel;
    public void ShowOrders() => Sections.SelectedItem = OrdersTab;
    public void ShowBuy() { Sections.SelectedIndex = 0; SharesInput.Focus(); }
    private void Minus_Click(object sender, RoutedEventArgs e) => Model?.AddShares(-10);
    private void Plus_Click(object sender, RoutedEventArgs e) => Model?.AddShares(10);
    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (Model != null) await Model.RefreshAsync(); }
    private async void Submit_Click(object sender, RoutedEventArgs e)
    { if (Model != null) { await Model.SubmitAsync(); if (Model.HasSelectedOrder) ShowOrders(); } }
    private async void Check_Click(object sender, RoutedEventArgs e) { if (Model != null) await Model.CheckOrderAsync(); }
    private void New_Click(object sender, RoutedEventArgs e) { Model?.StartNew(); ShowBuy(); }
    private void Connection_Click(object sender, RoutedEventArgs e) => Connect?.Invoke();
}
