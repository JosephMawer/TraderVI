using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using TraderVI.WPF.Architecture;
using TraderVI.WPF.Views;

// Instantiate only the actual explanatory view and navigation style; never the production App/window.
// Rendering is offscreen. No process launch, database, account, model, service or network access.
internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run(string[] args)
    {
        string output = args.Length == 0 ? "artifacts/architecture-view/screenshots" : args[0];
        Directory.CreateDirectory(output);
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Views/WorkspaceStyles.xaml", UriKind.Relative) });
        var view = new ArchitectureView();
        var tabs = (TabControl)view.FindName("ViewTabs");
        var details = (FlowDocumentScrollViewer)view.FindName("Details");
        var mapScroll = (ScrollViewer)view.FindName("MapScroll");
        var detailsPanel = (Border)view.FindName("DetailsPanel");
        var catalog = ArchitectureCatalog.Load();
        var nav = new TabControl { Style = (Style)app.FindResource("WorkspaceNavigation"), Foreground = Brushes.White };
        var otherTab = new TabItem { Header = "Other workspace", Content = new TextBlock { Text = "Offline fixture" } };
        var architectureTab = new TabItem { Header = "Architecture", Content = view };
        WorkspaceNavigationState.SetIcon(architectureTab, "\uE756");
        nav.Items.Add(otherTab); nav.Items.Add(architectureTab);
        nav.SelectedItem = architectureTab;
        var root = new Border { Background = new SolidColorBrush(Color.FromRgb(17, 21, 27)), Child = nav };
        Layout(root, 1480, 900);
        Check(tabs.Items.Count == 4, "Four architecture tabs");
        Check(nav.SelectedContent == view, "Sidebar reaches Architecture");
        WorkspaceNavigationState.SetIsCollapsed(nav, true);
        Layout(root, 1480, 900);
        Check(nav.SelectedContent == view, "Collapsed navigation preserves selected workspace");
        WorkspaceNavigationState.SetIsCollapsed(nav, false);

        foreach (var tab in tabs.Items.Cast<TabItem>())
        {
            tabs.SelectedItem = tab;
            Layout(root, 1480, 900);
            var page = (ArchitecturePage)tab.Tag;
            Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(tab)), "Accessible tab name");
            foreach (string id in page.Groups.SelectMany(g => g.Components))
            {
                var button = Descendants<Button>(view).Single(b => b.Tag is string s && s == id);
                Check(button.Focusable && button.IsTabStop, "Keyboard-focusable card");
                var peer = new ButtonAutomationPeer(button);
                Check(peer.GetName().Contains(catalog.Component(id).Name), "Accessible card name");
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                Pump();
                string content = new TextRange(details.Document.ContentStart, details.Document.ContentEnd).Text;
                Check(content.Contains(catalog.Component(id).Purpose) && content.Contains(catalog.Component(id).Boundaries), "Card invocation opens full description");
                Check(content.Contains("INPUTS") && content.Contains("OUTPUTS") && content.Contains("RESPONSIBILITIES") && content.Contains("REPOSITORY REFERENCES"), "Structured description sections");
                var saved = details.Document;
                button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                Check(ReferenceEquals(saved, details.Document), "Description persists after pointer leaves");
            }
            var selectedCard = Descendants<Button>(view).First(b => b.Tag is string);
            selectedCard.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Layout(root, 1480, 900);
            Save(root, Path.Combine(output, page.Id + "-wide.png"), 1480, 900, 96);
            Check(Grid.GetColumn(detailsPanel) == 1 && Grid.GetRow(detailsPanel) == 0, "Wide details pane at right");
            Layout(root, 980, 680);
            Check(Grid.GetRow(detailsPanel) == 1, "Narrow details pane below map");
            Check(detailsPanel.ActualWidth <= view.ActualWidth && detailsPanel.ActualHeight > 150, "Narrow pane readable and bounded");
            mapScroll.ScrollToBottom(); Pump(); Layout(root, 980, 680);
            Check(mapScroll.VerticalOffset > 0, "Long map scrolls");
            mapScroll.ScrollToTop();
            Layout(root, 980, 680);
            Save(root, Path.Combine(output, page.Id + "-narrow.png"), 980, 680, 144);
        }
        tabs.SelectedIndex = 0; Layout(root, 1480, 900);
        var collectorButton = Descendants<Button>(view).Single(b => b.Tag as string == "collector");
        var keySource = new OfflineKeySource { RootVisual = root };
        collectorButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        Check(AutomationProperties.GetItemStatus(collectorButton).StartsWith("Selected"), "Enter opens card description");
        var relayButton = Descendants<Button>(view).Single(b => b.Tag as string == "relay");
        relayButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
        relayButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
        Check(AutomationProperties.GetItemStatus(relayButton).StartsWith("Selected"), "Space opens card description");
        collectorButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        var paneKey = new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, Key.F6) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        collectorButton.RaiseEvent(paneKey);
        Check(paneKey.Handled, "F6 pane navigation handled");
        tabs.SelectedIndex = 1; tabs.SelectedIndex = 0; Layout(root, 1480, 900);
        Check(new TextRange(details.Document.ContentStart, details.Document.ContentEnd).Text.Contains("Keep shared observations"), "Selection restored per tab");
        var connection = Descendants<Button>(view).First(b => b.Tag is null && AutomationProperties.GetName(b).Contains("to TraderVI.Collector:"));
        connection.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(AutomationProperties.GetName(details).StartsWith("TraderVI.Collector details"), "Connection opens target details");
        Save(root, Path.Combine(output, "collector-details.png"), 1480, 900, 96);
        Layout(root, 640, 620);
        Check(Descendants<UniformGrid>(view).All(g => g.Columns == 1), "Small surface uses one card column");
        Save(root, Path.Combine(output, "compact.png"), 640, 620, 192);

        // Inspect the actual production navigation markup without constructing its service-owning window.
        var shell = XDocument.Load("TraderVI.WPF/PaperDashboardWindow.xaml");
        var entry = shell.Descendants().Single(e => e.Name.LocalName == "TabItem" && (string?)e.Attribute("Header") == "Architecture");
        Check(entry.Elements().Single().Name.LocalName == "ArchitectureView", "Production sidebar wires actual view");
        Console.WriteLine($"PASS: {checks} offline WPF checks; four tabs, persistent descriptions, connection selection, navigation, resizing, scrolling and 96/144/192-DPI renders. No production startup.");
    }

    private sealed class OfflineKeySource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
        checks++;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); Pump();
    }
    private static void Save(FrameworkElement root, string path, int width, int height, double dpi)
    {
        Layout(root, width, height);
        var bitmap = new RenderTargetBitmap((int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
