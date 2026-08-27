#nullable enable

using Core.Documentation;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class ProjectMarkdownCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"TraderVI-project-docs-{Guid.NewGuid():N}");

    public ProjectMarkdownCatalogTests() => Directory.CreateDirectory(root);

    [Fact]
    public void Refresh_discovers_repository_markdown_and_extracts_titles()
    {
        Write("README.md", "# TraderVI\nRoot overview");
        Write("Docs/architecture.md", "# Architecture\nSystem map");
        Write("Core/ML/Documentation/ML.md", "No heading here");

        ProjectMarkdownCatalog catalog = new(root);
        IReadOnlyList<ProjectMarkdownDocument> documents = catalog.Refresh();

        documents.Select(document => document.RelativePath).ShouldBe([
            "Core/ML/Documentation/ML.md",
            "Docs/architecture.md",
            "README.md"
        ]);
        catalog.Find("Docs\\architecture.md")!.Title.ShouldBe("Architecture");
        catalog.Find("Core/ML/Documentation/ML.md")!.Title.ShouldBe("ML");
    }

    [Fact]
    public void Refresh_excludes_generated_and_tool_directories_at_any_depth()
    {
        Write("Docs/keep.md", "# Keep");
        foreach (string excluded in new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules" })
        {
            Write($"{excluded}/hidden.md", "# Hidden");
            Write($"src/{excluded}/nested-hidden.md", "# Hidden");
        }

        ProjectMarkdownCatalog catalog = new(root);
        catalog.Refresh();

        catalog.Documents.Count.ShouldBe(1);
        catalog.Documents[0].RelativePath.ShouldBe("Docs/keep.md");
    }

    [Fact]
    public void Filter_searches_title_path_and_content_using_all_terms()
    {
        Write("Docs/adr/0036-reader.md", "# Native reader\nSafe FlowDocument navigation");
        Write("Docs/running.md", "# Running\nOperator commands");
        ProjectMarkdownCatalog catalog = new(root);
        catalog.Refresh();

        catalog.Filter("native").Single().RelativePath.ShouldBe("Docs/adr/0036-reader.md");
        catalog.Filter("adr 0036").Single().RelativePath.ShouldBe("Docs/adr/0036-reader.md");
        catalog.Filter("safe navigation").Single().RelativePath.ShouldBe("Docs/adr/0036-reader.md");
        catalog.Filter("native commands").ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_opens_relative_catalog_document_and_heading()
    {
        Write("Docs/index.md", "# Index\n[Run](running.md#Safe validation commands)");
        Write("Docs/running.md", "# Running\n## Safe validation commands");
        ProjectMarkdownCatalog catalog = new(root);
        catalog.Refresh();
        MarkdownLinkResolver resolver = new(catalog);

        MarkdownLinkResolution result = resolver.Resolve(
            catalog.Find("Docs/index.md")!,
            "running.md#Safe%20validation%20commands");

        result.Kind.ShouldBe(MarkdownLinkKind.InternalDocument);
        result.Document!.RelativePath.ShouldBe("Docs/running.md");
        result.HeadingId.ShouldBe("safe-validation-commands");
    }

    [Fact]
    public void Resolve_navigates_same_document_heading()
    {
        Write("Docs/index.md", "# Index\n## Details");
        ProjectMarkdownCatalog catalog = new(root);
        catalog.Refresh();
        ProjectMarkdownDocument current = catalog.Documents.Single();

        MarkdownLinkResolution result = new MarkdownLinkResolver(catalog)
            .Resolve(current, "#Details");

        result.Kind.ShouldBe(MarkdownLinkKind.Heading);
        result.Document.ShouldBe(current);
        result.HeadingId.ShouldBe("details");
    }

    [Fact]
    public void Resolve_allows_only_explicit_http_and_https_as_external_web_links()
    {
        Write("Docs/index.md", "# Index");
        ProjectMarkdownCatalog catalog = new(root);
        catalog.Refresh();
        MarkdownLinkResolver resolver = new(catalog);
        ProjectMarkdownDocument current = catalog.Documents.Single();

        resolver.Resolve(current, "https://example.com/docs").Kind
            .ShouldBe(MarkdownLinkKind.ExternalWeb);
        resolver.Resolve(current, "mailto:operator@example.com").Kind
            .ShouldBe(MarkdownLinkKind.Unsupported);
        resolver.Resolve(current, new Uri(Path.Combine(root, "secret.md")).AbsoluteUri).Kind
            .ShouldBe(MarkdownLinkKind.Unsafe);
    }

    [Fact]
    public void Resolve_rejects_traversal_and_undiscovered_local_files()
    {
        Write("Docs/index.md", "# Index");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "not markdown");
        ProjectMarkdownCatalog catalog = new(root);
        catalog.Refresh();
        MarkdownLinkResolver resolver = new(catalog);
        ProjectMarkdownDocument current = catalog.Documents.Single();

        resolver.Resolve(current, "../../outside.md").Kind.ShouldBe(MarkdownLinkKind.Unsafe);
        resolver.Resolve(current, "missing.md").Kind.ShouldBe(MarkdownLinkKind.Unsupported);
        resolver.Resolve(current, "../notes.txt").Kind.ShouldBe(MarkdownLinkKind.Unsupported);
    }

    [Theory]
    [InlineData("Current milestone", "current-milestone")]
    [InlineData("MFE/MAE & coverage", "mfemae--coverage")]
    [InlineData("  Already-slugged  ", "already-slugged")]
    public void Heading_ids_are_deterministic(string heading, string expected) =>
        MarkdownHeadingIds.Create(heading).ShouldBe(expected);

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private void Write(string relativePath, string content)
    {
        string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
