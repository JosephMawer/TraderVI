#nullable enable
using System.Collections.Generic;
using System;
using Core.Runtime;
using System.Windows;
using System.Windows.Controls;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class EngineHealthList : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items),
        typeof(IEnumerable<EngineHealthIssue>), typeof(EngineHealthList));
    public IEnumerable<EngineHealthIssue>? Items
    {
        get => (IEnumerable<EngineHealthIssue>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
    public EngineHealthList() => InitializeComponent();
    private void ReviewRecovery_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EngineHealthIssue { Recovery: { } recovery }) return;
        if (!Enum.TryParse<EngineRecoveryScope>(recovery.Scope, out var scope)) return;
        new EngineIntegrityWindow(scope, recovery.IncidentId, recovery.RunId) { Owner = Window.GetWindow(this) }.Show();
    }
}
