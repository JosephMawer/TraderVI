#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TraderVI.WPF.Views;

public sealed class SystemControlBar : UserControl
{
    public string SystemKey { get; set; } = EngineStrategySettings.Shadow;
    private readonly TextBlock status=new() {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button pause=new() {Content="Pause new buys",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};
    private readonly Button resume=new() {Content="Resume buys",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,12,0)};
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromSeconds(15)};
    private bool busy,refreshing;
    public event EventHandler? StateChanged;
    public SystemControlBar()
    {
        var panel=new DockPanel {Margin=new Thickness(0,8,0,12)};
        DockPanel.SetDock(pause,Dock.Left); DockPanel.SetDock(resume,Dock.Left);
        panel.Children.Add(pause);panel.Children.Add(resume);panel.Children.Add(status);Content=panel;
        pause.Click+=async(_,_)=>await ChangeAsync(true);resume.Click+=async(_,_)=>await ChangeAsync(false);
        Loaded+=async(_,_)=>{await RefreshAsync();timer.Start();}; Unloaded+=(_,_)=>timer.Stop();
        timer.Tick+=async(_,_)=>await RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        if(refreshing||busy) return; refreshing=true;
        try
        {
            var repository=new TradingControlRepository(); var access=await repository.AccessAsync(SystemKey);
            var states=await repository.ReadAsync(); bool ownPause=states.Any(s=>s.SystemKey==SystemKey&&s.Paused);
            bool inherited=SystemKey!=TradingSystems.Daily&&states.Any(s=>s.SystemKey==TradingSystems.Daily&&s.Paused);
            bool legacyPaused=SystemKey==EngineStrategySettings.Shadow &&
                (await new SystemShadowRepository().GetLatestGenerationAsync())?.Status==SystemShadowGenerationStatus.Paused;
            pause.IsEnabled=access.Installed&&!ownPause;
            resume.IsEnabled=access.Installed&&(ownPause || legacyPaused)&&!inherited;
            status.Text=TradingSystems.Name(SystemKey)+" · "+(access.Paused||legacyPaused?"New buys paused; exits continue. ":"New buys enabled, subject to account risk holds. ")+
                (inherited?"Daily Delphi's shared pause must also be resumed. ":"")+access.Description;
        }
        catch {pause.IsEnabled=resume.IsEnabled=false;status.Text="Trading controls unavailable. Reload after checking the local database.";}
        finally {refreshing=false;}
    }
    private async Task ChangeAsync(bool paused)
    {
        if(busy)return;
        var dialog=new OperatorActionDialog(Window.GetWindow(this),paused?"Pause new buys":"Resume buys",
            TradingSystems.Name(SystemKey)+(paused?
            ": stop new buys and cancel pending internal buy attempts. Existing holdings remain monitored and exits continue. No holding will be sold by pausing.":
            ": use the currently assigned strategies for future decisions. Existing loss and capital-review holds remain in force. Resume does not assign the version merely selected in Settings."));
        if(dialog.ShowDialog()!=true)return;
        busy=true;pause.IsEnabled=resume.IsEnabled=false;bool saved=false;
        try
        {
            if(!paused&&SystemKey==EngineStrategySettings.Shadow)
            {
                var shadows=new SystemShadowRepository();var generation=await shadows.GetLatestGenerationAsync();
                if(generation?.Status==SystemShadowGenerationStatus.Paused)
                    await shadows.SetGenerationStatusAsync(generation.GenerationId,SystemShadowGenerationStatus.Active,dialog.Reason);
            }
            await new TradingControlRepository().SetPausedAsync(SystemKey,paused,dialog.Reason);saved=true;
            if(Window.GetWindow(this) is TraderVI.WPF.PaperDashboardWindow host) await host.TradingControlsChangedAsync();
            StateChanged?.Invoke(this,EventArgs.Empty);
        }
        catch(Exception ex){MessageBox.Show(Window.GetWindow(this),(saved?"The control change was saved, but refresh needs attention. ":"")+ex.Message,"Trading controls",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{busy=false;await RefreshAsync();}
    }
}
