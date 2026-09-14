using System.Windows.Input;
namespace TraderVI.WPF.Views;
public static class ManualSellCommands
{
    public static readonly RoutedUICommand Market = new("Immediately sell the entire holding at market", "ManualMarketSellAll", typeof(ManualSellCommands));
    public static readonly RoutedUICommand Limit = new("Open limit sell preview", "ManualLimitSell", typeof(ManualSellCommands));
    public static readonly RoutedUICommand Orders = new("Review saved manual sells", "ManualSellOrders", typeof(ManualSellCommands));
}
