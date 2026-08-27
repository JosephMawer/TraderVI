#nullable enable

using Core.Documentation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TraderVI.WPF.Documentation;
using TraderVI.WPF.Viewmodels;

namespace TraderVI.WPF.Views;

public partial class ProjectDocsView : UserControl
{
    private readonly ProjectDocsViewModel viewModel = new();
    private readonly MarkdownFlowDocumentRenderer renderer = new();
    private IReadOnlyDictionary<string, FrameworkContentElement> headings =
        new Dictionary<string, FrameworkContentElement>();
    private bool loadedOnce;

    public ProjectDocsView()
    {
        InitializeComponent();
        DataContext = viewModel;
        DocumentViewer.AddHandler(Hyperlink.ClickEvent, new RoutedEventHandler(Hyperlink_Click));
        Loaded += ProjectDocsView_Loaded;
    }

    private void ProjectDocsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (loadedOnce)
            return;
        loadedOnce = true;
        if (viewModel.Refresh())
            RenderCurrentDocument();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Refresh())
            RenderCurrentDocument();
    }

    private void NavigationTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not ProjectDocsTreeNode { Document: { } document })
            return;
        viewModel.Open(document);
        RenderCurrentDocument();
    }

    private void RenderCurrentDocument(string? headingId = null)
    {
        ProjectMarkdownDocument? source = viewModel.CurrentDocument;
        if (source is null)
            return;
        MarkdownRenderResult result = renderer.Render(source);
        headings = result.Headings;
        DocumentViewer.Document = result.Document;
        if (!string.IsNullOrWhiteSpace(headingId))
            NavigateToHeading(headingId);
    }

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        Hyperlink? hyperlink = e.OriginalSource as Hyperlink ?? e.Source as Hyperlink;
        if (hyperlink?.Tag is not string target)
            return;
        e.Handled = true;
        MarkdownLinkResolution resolution = viewModel.ResolveLink(target);
        switch (resolution.Kind)
        {
            case MarkdownLinkKind.Heading:
                NavigateToHeading(resolution.HeadingId);
                break;
            case MarkdownLinkKind.InternalDocument when resolution.Document is not null:
                viewModel.Open(resolution.Document);
                RenderCurrentDocument(resolution.HeadingId);
                break;
            case MarkdownLinkKind.ExternalWeb when resolution.ExternalUri is not null:
                OpenExternalLink(resolution.ExternalUri);
                break;
            default:
                viewModel.ReportNavigation(resolution.Message ?? "That link cannot be opened safely.");
                break;
        }
    }

    private void NavigateToHeading(string? headingId)
    {
        if (string.IsNullOrWhiteSpace(headingId))
            return;
        if (headings.TryGetValue(headingId, out FrameworkContentElement? heading))
        {
            heading.BringIntoView();
            viewModel.ReportNavigation($"Heading · #{headingId}");
        }
        else
        {
            viewModel.ReportNavigation($"Heading not found · #{headingId}");
        }
    }

    private void OpenExternalLink(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            viewModel.ReportNavigation($"Opened external link · {uri.Host}");
        }
        catch (Exception ex)
        {
            viewModel.ReportNavigation($"External link failed · {ex.Message}");
        }
    }
}
