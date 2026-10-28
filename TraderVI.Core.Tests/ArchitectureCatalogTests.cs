using System;
using System.IO;
using System.Linq;
using System.Text;
using TraderVI.WPF.Architecture;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class ArchitectureCatalogTests
{
    [Fact]
    public void EmbeddedGuideHasRequiredViewsAndSharedComponentIdentity()
    {
        var catalog = ArchitectureCatalog.Load();
        Assert.Equal(new[] { "Collector & trading", "Components", "Evidence", "AI authority" }, catalog.Views.Select(v => v.Title));
        Assert.True(catalog.Views.Count(v => v.Groups.Any(g => g.Components.Contains("collector"))) >= 3);
        Assert.Contains("not live", catalog.Notice);
        Assert.Contains("2026-09-30", catalog.Component("reviewer").Evidence);
        Assert.Contains("not reverified", catalog.Component("reviewer").Evidence);
        Assert.Contains("Not financially adopted", catalog.Component("minute").Boundaries);
        Assert.All(catalog.Components, c => Assert.NotEmpty(c.References));
    }

    [Fact]
    public void RepositoryReferencesResolveWithoutExternalOrPrivatePaths()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "TraderVI.sln")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository not found.");
        foreach (string reference in ArchitectureCatalog.Load().Components.SelectMany(c => c.References).Distinct())
            Assert.True(File.Exists(Path.Combine(root, reference.Split('#')[0])), reference);
    }

    [Fact]
    public void UnknownEndpointsAndDuplicateIdsFailBeforeRendering()
    {
        var c = ArchitectureCatalog.Load();
        var view = c.Views[0] with { Connections = [new("missing", "engine", "Invalid")] };
        Assert.Throws<InvalidDataException>(() => (c with { Views = [view] }).Validate());
        Assert.Throws<InvalidDataException>(() => (c with { Components = [c.Components[0], c.Components[0]] }).Validate());
        view = c.Views[0] with { Groups = [new("Duplicate", ["collector", "collector"])] };
        Assert.Throws<InvalidDataException>(() => (c with { Views = [view] }).Validate());
        view = c.Views[0] with { Groups = [null!] };
        Assert.Throws<InvalidDataException>(() => (c with { Views = [view] }).Validate());
    }

    [Theory]
    [InlineData("../private.md")]
    [InlineData("https://example.com/secret.md")]
    [InlineData("Docs/../../secret.md")]
    [InlineData("Docs/private.txt")]
    public void ReferencesAreRepositoryMarkdownOnly(string reference)
    {
        var c = ArchitectureCatalog.Load();
        var changed = c.Components.Select((item, index) => index == 0 ? item with { References = [reference] } : item).ToArray();
        Assert.Throws<InvalidDataException>(() => (c with { Components = changed }).Validate());
    }

    [Fact]
    public void MissingDescriptionsAndDatesCannotShipSilently()
    {
        var c = ArchitectureCatalog.Load();
        Assert.Throws<InvalidDataException>(() => (c with { ReviewedAsOf = "today" }).Validate());
        var changed = c.Components.Select((item, index) => index == 0 ? item with { Boundaries = "" } : item).ToArray();
        Assert.Throws<InvalidDataException>(() => (c with { Components = changed }).Validate());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        Assert.Throws<InvalidDataException>(() => ArchitectureCatalog.Read(stream));
    }
}
