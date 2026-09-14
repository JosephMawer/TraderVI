using Core.WSTrade.Orders;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace TraderVI.WPF.Views;

public static class ManualBuyCommands
{
    public static readonly RoutedUICommand Open = new("Open manual buy ticket", "ManualMarketBuy", typeof(ManualBuyCommands));
    public static readonly RoutedUICommand Orders = new("Review manual orders", "ManualOrders", typeof(ManualBuyCommands));
}

/// <summary>A reusable launcher. It carries row context; it never submits or sizes an order.</summary>
public sealed class MarketBuyButton : Button
{
    public static readonly DependencyProperty OriginProperty = DependencyProperty.Register(nameof(Origin), typeof(ManualBuyOrigin), typeof(MarketBuyButton));
    public ManualBuyOrigin Origin { get => (ManualBuyOrigin)GetValue(OriginProperty); set => SetValue(OriginProperty, value); }
    public MarketBuyButton()
    {
        Content = "Buy…"; Command = ManualBuyCommands.Open;
        SetBinding(CommandParameterProperty, new Binding(nameof(Origin)) { Source = this });
        ToolTip = "Open a manual market-buy ticket. Enter the share quantity before submitting.";
        Padding = new Thickness(8, 3, 8, 3); MinHeight = 26;
    }
}
