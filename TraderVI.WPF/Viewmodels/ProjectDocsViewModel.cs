#nullable enable

using Core.Documentation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TraderVI.WPF.Viewmodels;

public sealed class ProjectDocsViewModel : INotifyPropertyChanged
{
    private const string DefaultDocumentPath = "Docs/project-status.md";
    private readonly ProjectMarkdownCatalog? catalog;
    private readonly MarkdownLinkResolver? linkResolver;
    private string filterText = string.Empty;
    private string status = "Locating the TraderVI repository…";
    private string repositoryRoot = "Unavailable";
    private string lastRefresh = "—";
    private int filteredDocumentCount;
    private int totalDocumentCount;
    private ProjectMarkdownDocument? currentDocument;

    public ProjectDocsViewModel()
    {
        string? root = ProjectRepositoryLocator.Find(
            Environment.CurrentDirectory,
            AppContext.BaseDirectory);
        if (root is null)
        {
            Status = "Repository not found · Project Docs is unavailable";
            return;
        }

        RepositoryRoot = root;
        catalog = new ProjectMarkdownCatalog(root);
        linkResolver = new MarkdownLinkResolver(catalog);
    }

    public ObservableCollection<ProjectDocsTreeNode> Navigation { get; } = [];

    public string FilterText
    {
        get => filterText;
        set
        {
            if (Set(ref filterText, value))
                ApplyFilter();
        }
    }

    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public string RepositoryRoot
    {
        get => repositoryRoot;
        private set => Set(ref repositoryRoot, value);
    }

    public string LastRefresh
    {
        get => lastRefresh;
        private set => Set(ref lastRefresh, value);
    }

    public int FilteredDocumentCount
    {
        get => filteredDocumentCount;
        private set => Set(ref filteredDocumentCount, value);
    }

    public int TotalDocumentCount
    {
        get => totalDocumentCount;
        private set => Set(ref totalDocumentCount, value);
    }

    public ProjectMarkdownDocument? CurrentDocument
    {
        get => currentDocument;
        private set
        {
            Set(ref currentDocument, value);
            OnPropertyChanged(nameof(CurrentTitle));
            OnPropertyChanged(nameof(CurrentPath));
        }
    }

    public string CurrentTitle => CurrentDocument?.Title ?? "Select a document";
    public string CurrentPath => CurrentDocument?.RelativePath ?? "—";

    public bool Refresh()
    {
        if (catalog is null)
            return false;

        string? selectedPath = CurrentDocument?.RelativePath;
        try
        {
            catalog.Refresh();
            TotalDocumentCount = catalog.Documents.Count;
            ApplyFilter();
            CurrentDocument = selectedPath is null ? null : catalog.Find(selectedPath);
            CurrentDocument ??= catalog.Find(DefaultDocumentPath) ?? catalog.Documents.FirstOrDefault();
            LastRefresh = DateTime.Now.ToString("MMM d · HH:mm:ss");
            Status = $"Loaded {TotalDocumentCount} read-only Markdown document(s)";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Refresh failed · {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public void Open(ProjectMarkdownDocument document)
    {
        CurrentDocument = document;
        Status = $"Reading {document.RelativePath}";
    }

    public MarkdownLinkResolution ResolveLink(string target)
    {
        if (linkResolver is null || CurrentDocument is null)
            return new(MarkdownLinkKind.Unsupported, Message: "No document is open.");
        return linkResolver.Resolve(CurrentDocument, target);
    }

    public void ReportNavigation(string message) => Status = message;

    private void ApplyFilter()
    {
        if (catalog is null)
            return;
        IReadOnlyList<ProjectMarkdownDocument> documents = catalog.Filter(FilterText);
        FilteredDocumentCount = documents.Count;
        Navigation.Clear();
        foreach (ProjectDocsTreeNode node in BuildTree(documents))
            Navigation.Add(node);
        Status = string.IsNullOrWhiteSpace(FilterText)
            ? $"Showing all {documents.Count} document(s)"
            : $"{documents.Count} document(s) match '{FilterText.Trim()}'";
    }

    private static IReadOnlyList<ProjectDocsTreeNode> BuildTree(
        IReadOnlyList<ProjectMarkdownDocument> documents)
    {
        ProjectDocsTreeNode root = new("Repository", null, true);
        Dictionary<string, ProjectDocsTreeNode> folders =
            new(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };

        foreach (ProjectMarkdownDocument document in documents)
        {
            string[] parts = document.RelativePath.Split('/');
            string parentPath = string.Empty;
            ProjectDocsTreeNode parent = root;
            for (int index = 0; index < parts.Length - 1; index++)
            {
                string folderPath = parentPath.Length == 0
                    ? parts[index]
                    : $"{parentPath}/{parts[index]}";
                if (!folders.TryGetValue(folderPath, out ProjectDocsTreeNode? folder))
                {
                    folder = new ProjectDocsTreeNode(parts[index], null, index == 0);
                    parent.Children.Add(folder);
                    folders[folderPath] = folder;
                }
                parent = folder;
                parentPath = folderPath;
            }
            parent.Children.Add(new ProjectDocsTreeNode(document.Title, document, false));
        }

        SortChildren(root);
        return root.Children.ToArray();
    }

    private static void SortChildren(ProjectDocsTreeNode node)
    {
        foreach (ProjectDocsTreeNode child in node.Children)
            SortChildren(child);
        ProjectDocsTreeNode[] sorted = node.Children
            .OrderBy(child => child.Document is not null)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        node.Children.Clear();
        foreach (ProjectDocsTreeNode child in sorted)
            node.Children.Add(child);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ProjectDocsTreeNode(
    string name,
    ProjectMarkdownDocument? document,
    bool isExpanded) : INotifyPropertyChanged
{
    private bool expanded = isExpanded;

    public string Name { get; } = name;
    public ProjectMarkdownDocument? Document { get; } = document;
    public string ToolTip => Document?.RelativePath ?? Name;
    public string Icon => Document is null ? "▸" : "•";
    public ObservableCollection<ProjectDocsTreeNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => expanded;
        set
        {
            if (expanded == value)
                return;
            expanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
