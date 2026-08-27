#nullable enable

using System;
using System.IO;
using System.Text;

namespace Core.Documentation;

public enum MarkdownLinkKind
{
    InternalDocument,
    Heading,
    ExternalWeb,
    Unsupported,
    Unsafe
}

public sealed record MarkdownLinkResolution(
    MarkdownLinkKind Kind,
    ProjectMarkdownDocument? Document = null,
    string? HeadingId = null,
    Uri? ExternalUri = null,
    string? Message = null);

public sealed class MarkdownLinkResolver(ProjectMarkdownCatalog catalog)
{
    private readonly string rootWithSeparator =
        EnsureTrailingSeparator(catalog.RepositoryRoot);

    public MarkdownLinkResolution Resolve(
        ProjectMarkdownDocument currentDocument,
        string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return Unsupported("The link target is empty.");

        string trimmed = target.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? absolute))
        {
            if (absolute.Scheme is "http" or "https")
                return new(MarkdownLinkKind.ExternalWeb, ExternalUri: absolute);
            if (absolute.IsFile)
                return Unsafe("Absolute filesystem links are not allowed.");
            return Unsupported($"The '{absolute.Scheme}' link scheme is not supported.");
        }

        int hashIndex = trimmed.IndexOf('#');
        string pathPart = hashIndex >= 0 ? trimmed[..hashIndex] : trimmed;
        string? fragment = hashIndex >= 0 ? trimmed[(hashIndex + 1)..] : null;
        if (pathPart.Contains('?'))
            return Unsupported("Local documentation links cannot contain a query string.");

        string headingId;
        try
        {
            pathPart = Uri.UnescapeDataString(pathPart);
            headingId = MarkdownHeadingIds.Create(Uri.UnescapeDataString(fragment ?? string.Empty));
        }
        catch (UriFormatException)
        {
            return Unsafe("The link contains invalid escaping.");
        }

        if (pathPart.Length == 0)
        {
            return headingId.Length == 0
                ? Unsupported("The link does not identify a document or heading.")
                : new(MarkdownLinkKind.Heading, currentDocument, headingId);
        }

        if (Path.IsPathFullyQualified(pathPart) ||
            pathPart.StartsWith('/') ||
            pathPart.StartsWith('\\'))
            return Unsafe("Absolute filesystem links are not allowed.");

        string currentDirectory = Path.GetDirectoryName(
            currentDocument.RelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(
                catalog.RepositoryRoot,
                currentDirectory,
                pathPart.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unsafe("The local link path is invalid.");
        }

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            return Unsafe("The link resolves outside the repository.");
        if (!string.Equals(Path.GetExtension(candidate), ".md", StringComparison.OrdinalIgnoreCase))
            return Unsupported("Only discovered Markdown documents open inside Project Docs.");

        string relativePath = ProjectMarkdownCatalog.NormalizeRelativePath(
            Path.GetRelativePath(catalog.RepositoryRoot, candidate));
        ProjectMarkdownDocument? document = catalog.Find(relativePath);
        if (document is null)
            return Unsupported("The linked Markdown document is not in the current catalog.");

        return new(
            MarkdownLinkKind.InternalDocument,
            document,
            headingId.Length == 0 ? null : headingId);
    }

    private static MarkdownLinkResolution Unsupported(string message) =>
        new(MarkdownLinkKind.Unsupported, Message: message);

    private static MarkdownLinkResolution Unsafe(string message) =>
        new(MarkdownLinkKind.Unsafe, Message: message);

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}

public static class MarkdownHeadingIds
{
    public static string Create(string? heading)
    {
        if (string.IsNullOrWhiteSpace(heading))
            return string.Empty;

        StringBuilder slug = new();
        foreach (char character in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
                slug.Append(character);
            else if (char.IsWhiteSpace(character))
                slug.Append('-');
        }

        return slug.ToString().Trim('-');
    }
}
