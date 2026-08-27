#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Documentation;

public sealed record ProjectMarkdownDocument(
    string RelativePath,
    string Title,
    string Content,
    DateTime LastWriteTimeUtc);

public sealed class ProjectMarkdownCatalog
{
    private static readonly HashSet<string> ExcludedDirectories = new(
        [".git", ".vs", "bin", "obj", "packages", "node_modules"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex HeadingPattern = new(
        @"^\s{0,3}#{1,6}\s+(?<title>.+?)\s*#*\s*$",
        RegexOptions.Compiled);

    private readonly Dictionary<string, ProjectMarkdownDocument> byPath =
        new(StringComparer.OrdinalIgnoreCase);

    public ProjectMarkdownCatalog(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(RepositoryRoot))
            throw new DirectoryNotFoundException(
                $"Repository root does not exist: {RepositoryRoot}");
    }

    public string RepositoryRoot { get; }

    public IReadOnlyList<ProjectMarkdownDocument> Documents { get; private set; } =
        Array.Empty<ProjectMarkdownDocument>();

    public IReadOnlyList<ProjectMarkdownDocument> Refresh()
    {
        List<ProjectMarkdownDocument> documents = [];
        foreach (string fullPath in EnumerateMarkdownFiles(RepositoryRoot))
        {
            string content = File.ReadAllText(fullPath);
            string relativePath = NormalizeRelativePath(
                Path.GetRelativePath(RepositoryRoot, fullPath));
            documents.Add(new ProjectMarkdownDocument(
                relativePath,
                ExtractTitle(content, relativePath),
                content,
                File.GetLastWriteTimeUtc(fullPath)));
        }

        documents.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
        Documents = documents;
        byPath.Clear();
        foreach (ProjectMarkdownDocument document in documents)
            byPath[document.RelativePath] = document;
        return Documents;
    }

    public IReadOnlyList<ProjectMarkdownDocument> Filter(string? query)
    {
        string[] terms = (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
            return Documents;

        return Documents
            .Where(document => terms.All(term =>
                document.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                document.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                document.Content.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public ProjectMarkdownDocument? Find(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        byPath.TryGetValue(NormalizeRelativePath(relativePath), out ProjectMarkdownDocument? document);
        return document;
    }

    internal static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static IEnumerable<string> EnumerateMarkdownFiles(string root)
    {
        Stack<DirectoryInfo> pending = new();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            DirectoryInfo directory = pending.Pop();
            foreach (FileInfo file in directory.EnumerateFiles("*.md", SearchOption.TopDirectoryOnly))
            {
                if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    yield return file.FullName;
            }

            foreach (DirectoryInfo child in directory.EnumerateDirectories()
                         .OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (ExcludedDirectories.Contains(child.Name) ||
                    child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                pending.Push(child);
            }
        }
    }

    private static string ExtractTitle(string content, string relativePath)
    {
        using StringReader reader = new(content);
        while (reader.ReadLine() is { } line)
        {
            Match match = HeadingPattern.Match(line);
            if (!match.Success)
                continue;
            string title = match.Groups["title"].Value.Trim();
            title = Regex.Replace(title, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            title = title.Trim('`', '*', '_', ' ');
            if (title.Length > 0)
                return title;
        }

        return Path.GetFileNameWithoutExtension(relativePath)
            .Replace('-', ' ')
            .Replace('_', ' ');
    }
}

public static class ProjectRepositoryLocator
{
    public static string? Find(params string?[] startingPaths)
    {
        foreach (string? startingPath in startingPaths)
        {
            if (string.IsNullOrWhiteSpace(startingPath))
                continue;
            DirectoryInfo? current = new(Path.GetFullPath(startingPath));
            if (!current.Exists && File.Exists(current.FullName))
                current = current.Parent;
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "TraderVI.sln")) &&
                    File.Exists(Path.Combine(current.FullName, "Docs", "project-status.md")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        return null;
    }
}
