using System.Windows;
using System.Windows.Input;

namespace TraderVI.WPF.Views;

// Window-local presentation state. Collapsing navigation never changes the selected workspace.
public static class WorkspaceNavigationState
{
    public static readonly RoutedUICommand OpenWealthsimple = new("Wealthsimple connection", "OpenWealthsimple", typeof(WorkspaceNavigationState));
    public static readonly DependencyProperty ConnectionProperty = DependencyProperty.RegisterAttached(
        "Connection", typeof(object), typeof(WorkspaceNavigationState), new PropertyMetadata(null));
    public static object GetConnection(DependencyObject element) => element.GetValue(ConnectionProperty);
    public static void SetConnection(DependencyObject element, object value) => element.SetValue(ConnectionProperty, value);

    public static readonly DependencyProperty IsCollapsedProperty = DependencyProperty.RegisterAttached(
        "IsCollapsed", typeof(bool), typeof(WorkspaceNavigationState),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    // Bind the actual property, avoiding namespace-string resolution in compiled resource dictionaries.
    public static readonly PropertyPath IsCollapsedBindingPath = new("(0)", IsCollapsedProperty);

    public static bool GetIsCollapsed(DependencyObject element) => (bool)element.GetValue(IsCollapsedProperty);
    public static void SetIsCollapsed(DependencyObject element, bool value) => element.SetValue(IsCollapsedProperty, value);

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(WorkspaceNavigationState), new PropertyMetadata("\uE10F"));

    public static string GetIcon(DependencyObject element) => (string)element.GetValue(IconProperty);
    public static void SetIcon(DependencyObject element, string value) => element.SetValue(IconProperty, value);
}
