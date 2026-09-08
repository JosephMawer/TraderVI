#nullable enable
using Core.Db;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace TraderVI.WPF.Views;

public static class OperatorHoldingActions
{
    public static async Task RequestAsync(Window owner,string family,Guid targetId,Guid positionId,string symbol)
    {
        if(!await new TradingControlRepository().IsPausedAsync(family))
        {MessageBox.Show(owner,"Pause new buys for this system first. Existing exits will continue.","Pause before selling",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var dialog=new OperatorActionDialog(owner,"Request simulated sale",
            $"Request sale of all {symbol} shares in this account? The request and entry details will be preserved. The engine will use its next eligible observed execution price; a closed market or missing data leaves the exit pending. This does not place a broker order.");
        if(dialog.ShowDialog()!=true)return;
        var request=await new TradingControlRepository().RequestExitAsync(family,targetId,positionId,dialog.Reason);
        MessageBox.Show(owner,$"Exit request {request.RequestId:D} is recorded. The holding stays open until a valid fill is recorded. New buys remain paused.","Exit requested",MessageBoxButton.OK,MessageBoxImage.Information);
        if(owner is TraderVI.WPF.PaperDashboardWindow host)await host.TradingControlsChangedAsync();
    }
}
