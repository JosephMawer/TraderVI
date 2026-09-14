#nullable enable
using Core.WSTrade.Orders;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public sealed class ManualBuyWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ManualBuyTicketViewModel model;
    private bool closed;
    public bool IsSubmitting => model.IsSubmitting;
    public ManualBuyWindow(ManualMarketBuyWorkflow workflow, ManualBuyOrigin? origin, WealthsimpleConnection? connection = null)
    {
        model = new(workflow, origin);
        var panel = new ManualBuyTicketPanel { DataContext = model };
        panel.Connect = async () =>
        {
            if (connection == null) return;
            new WealthsimpleConnectionWindow(connection) { Owner = this }.ShowDialog();
            if (!model.HasSelectedOrder) await model.RefreshAsync();
            else model.RefreshValidation();
        };
        Title = model.Title; Width = 610; Height = 850; MinWidth = 490; MinHeight = 540;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(26, 32, 41)); Content = panel;
        Loaded += async (_, _) => { await model.InitializeAsync(); if (closed) return; if (origin == null || model.HasSelectedOrder) panel.ShowOrders(); else panel.ShowBuy(); timer.Start(); };
        timer.Tick += (_, _) => model.RefreshValidation();
        Closing += (_, e) => { if (model.IsSubmitting) e.Cancel = true; };
        Closed += (_, _) => { closed = true; timer.Stop(); model.Dispose(); };
    }
}
