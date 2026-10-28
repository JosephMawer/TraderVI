#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;
using TraderVI.WPF.Architecture;

namespace TraderVI.WPF.Views;

public partial class ArchitectureView : UserControl
{
    private ArchitectureCatalog? catalog;
    private readonly List<UniformGrid> cardRows = [];
    private readonly Dictionary<string, Button> cards = [];
    private readonly Dictionary<string, string> rememberedSelections = [];
    private string? selectedId;
    private ArchitecturePage? page;
    private static readonly Brush Accent = Brush("#9BC9FF");
    private static readonly Brush Muted = Brush("#ADBBCC");

    public ArchitectureView()
    {
        InitializeComponent();
        PreviewKeyDown += HandlePaneNavigation;
        try
        {
            catalog = ArchitectureCatalog.Load();
            ReviewDate.Text = $"REPOSITORY GUIDE  /  Reviewed as of {catalog.ReviewedAsOf}";
            SnapshotNotice.Text = catalog.Notice;
            foreach (var view in catalog.Views)
            {
                var tab = new TabItem { Header = view.Title, Tag = view };
                AutomationProperties.SetName(tab, view.Title);
                ViewTabs.Items.Add(tab);
            }
            ViewTabs.SelectedIndex = 0;
        }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        {
            SnapshotNotice.Text = "Architecture content is unavailable. Consult Project Docs; no runtime state was read.";
            Map.Children.Add(Text(error.Message));
        }
    }

    private void ViewTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != ViewTabs || catalog is null || ViewTabs.SelectedItem is not TabItem { Tag: ArchitecturePage selected }) return;
        page = selected;
        Map.Children.Clear(); cards.Clear(); cardRows.Clear();
        Map.Children.Add(Text(page.Introduction, 17, margin: new Thickness(0, 4, 0, 12)));
        Map.Children.Add(new Border
        {
            Background = Brush("#202F40"), CornerRadius = new CornerRadius(6), Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 18), Child = Text(page.Boundary, 13)
        });
        Map.Children.Add(Text("CONNECTIONS  /  Solid: described relationship   ·   Dashed: pending or unverified integration", 12, Muted,
            new Thickness(0, 0, 0, 15)));
        foreach (var group in page.Groups)
        {
            var section = new StackPanel();
            section.Children.Add(Text(group.Title.ToUpperInvariant(), 12, Accent, new Thickness(0, 0, 0, 10)));
            var grid = new UniformGrid { Columns = 2 };
            cardRows.Add(grid);
            foreach (string id in group.Components)
            {
                var component = catalog.Component(id);
                var contents = new StackPanel();
                var heading = new StackPanel { Orientation = Orientation.Horizontal };
                heading.Children.Add(new TextBlock { Text = Icon(component.Kind), FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16, Foreground = Accent, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                heading.Children.Add(Text(component.Kind.ToUpperInvariant(), 11, Muted));
                contents.Children.Add(heading);
                contents.Children.Add(Text(component.Name, 17, margin: new Thickness(0, 9, 0, 5)));
                contents.Children.Add(Text(component.Summary, 13, Muted));
                var button = new Button { Content = contents, Tag = id, Style = (Style)FindResource("ArchitectureCard") };
                AutomationProperties.SetName(button, $"{component.Name}. {component.Kind}. {component.Summary}");
                AutomationProperties.SetHelpText(button, "Press Enter or Space to read purpose, inputs, outputs, responsibilities, boundaries and references. F6 moves to details.");
                button.Click += (_, _) => SelectComponent(id);
                cards.Add(id, button);
                grid.Children.Add(button);
            }
            section.Children.Add(grid);
            Map.Children.Add(section);
            foreach (var connection in page.Connections.Where(c => group.Components.Contains(c.From)))
                Map.Children.Add(Connection(connection));
            Map.Children.Add(new Border { Height = 1, Background = Brush("#344151"), Margin = new Thickness(0, 10, 0, 20) });
        }
        MapScroll.ScrollToTop();
        SelectComponent(rememberedSelections.GetValueOrDefault(page.Id) ?? page.Groups[0].Components[0]);
        ArrangePanes();
    }

    private Button Connection(ArchitectureConnection connection)
    {
        var from = catalog!.Component(connection.From);
        var to = catalog.Component(connection.To);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var arrow = new Path { Data = Geometry.Parse("M 1,6 L 28,6 M 21,1 L 28,6 L 21,11"), Stroke = Accent,
            StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center };
        if (connection.Pending) arrow.StrokeDashArray = new DoubleCollection([3, 3]);
        row.Children.Add(arrow);
        var labels = new StackPanel();
        labels.Children.Add(Text($"{from.Name} → {to.Name}", 12, Accent));
        labels.Children.Add(Text(connection.Label + (connection.Pending ? " · PENDING / UNVERIFIED" : ""), 12, Muted));
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        var button = new Button { Content = row, Style = (Style)FindResource("ArchitectureCard"),
            Background = Brushes.Transparent, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetName(button, $"{from.Name} to {to.Name}: {connection.Label}. {(connection.Pending ? "Pending or unverified. " : "")}Read {to.Name} details.");
        button.Click += (_, _) => SelectComponent(connection.To);
        return button;
    }

    private void SelectComponent(string id)
    {
        if (catalog is null || page is null) return;
        selectedId = id;
        rememberedSelections[page.Id] = id;
        foreach (var (key, button) in cards)
        {
            button.Background = Brush(key == id ? "#253B55" : "#1A222D");
            AutomationProperties.SetItemStatus(button, key == id ? "Selected; description open" : "");
        }
        var c = catalog.Component(id);
        var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 14, Foreground = Brush("#F4F7FB"),
            Background = Brushes.Transparent, TextAlignment = TextAlignment.Left,
            PagePadding = new Thickness(16, 0, 16, 18) };
        doc.Blocks.Add(new Paragraph(new Run(c.Name)) { FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        AddSection(doc, "Purpose", c.Purpose);
        AddSection(doc, "Inputs", c.Inputs);
        AddSection(doc, "Outputs", c.Outputs);
        AddSection(doc, "Responsibilities", c.Responsibilities);
        AddSection(doc, "Important boundaries", c.Boundaries);
        AddSection(doc, "Dated evidence / qualification", c.Evidence);
        AddSection(doc, "Repository references", string.Join("\n\n", c.References));
        AddSection(doc, "Source of truth", $"Reviewed as of {catalog.ReviewedAsOf}. Read the cited files in Project Docs for authoritative decisions and dated operational facts. This embedded guide changes only with a reviewed app update.");
        Details.Document = doc;
        AutomationProperties.SetName(Details, $"{c.Name} details. Purpose, inputs, outputs, responsibilities, boundaries and repository references.");
        // No popup, hover lifetime or focus-triggered replacement: selection remains until another explicit selection.
    }

    private static void AddSection(FlowDocument document, string heading, string content)
    {
        document.Blocks.Add(new Paragraph(new Run(heading.ToUpperInvariant()))
            { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Accent, Margin = new Thickness(0, 14, 0, 5) });
        document.Blocks.Add(new Paragraph(new Run(content)) { Margin = new Thickness(0), LineHeight = 21 });
    }

    private void Body_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangePanes();
    private void ArrangePanes()
    {
        bool narrow = Body.ActualWidth < 1000;
        DetailsColumn.Width = new GridLength(narrow ? 0 : 350);
        DetailsRow.Height = narrow ? new GridLength(Math.Max(160, Body.ActualHeight * .43)) : new GridLength(0);
        Grid.SetColumn(DetailsPanel, narrow ? 0 : 1);
        Grid.SetRow(DetailsPanel, narrow ? 1 : 0);
        DetailsPanel.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(12, 0, 0, 0);
        double mapWidth = Body.ActualWidth - (narrow ? 0 : 350) - 30;
        foreach (var row in cardRows) row.Columns = Math.Clamp((int)(mapWidth / 255), 1, 3);
    }

    private void HandlePaneNavigation(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F6) return;
        if (Details.IsKeyboardFocusWithin && selectedId is not null && cards.TryGetValue(selectedId, out var button))
        {
            button.BringIntoView(); button.Focus();
        }
        else Details.Focus();
        e.Handled = true;
    }

    private static TextBlock Text(string value, double size = 14, Brush? foreground = null, Thickness? margin = null) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size,
        Foreground = foreground ?? Brush("#F4F7FB"), Margin = margin ?? new Thickness(0)
    };
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static string Icon(string kind) => kind switch
    {
        "Provider" => "\uE774", "Storage" => "\uE8B7", "Process" => "\uE756", "Library" => "\uE943",
        "Authority" => "\uE72E", "Evidence" => "\uE9D9", "Pending" => "\uE823", "AI design" => "\uE8F1",
        _ => "\uE8A5"
    };
}
