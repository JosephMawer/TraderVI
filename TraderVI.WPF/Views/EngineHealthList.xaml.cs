#nullable enable
using System.Collections.Generic;
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
}
