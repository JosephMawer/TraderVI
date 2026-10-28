#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TraderVI.WPF.Architecture;

// Presentation-only content. No runtime, account, repository or network dependencies.
public sealed record ArchitectureCatalog(
    string ReviewedAsOf, string Notice, ArchitectureComponent[] Components, ArchitecturePage[] Views)
{
    public static ArchitectureCatalog Load()
    {
        using var stream = typeof(ArchitectureCatalog).Assembly.GetManifestResourceStream("TraderVI.Architecture.json")
            ?? throw new InvalidDataException("Architecture catalog resource is missing.");
        return Read(stream);
    }

    public static ArchitectureCatalog Read(Stream stream)
    {
        var catalog = JsonSerializer.Deserialize<ArchitectureCatalog>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Architecture catalog is empty.");
        catalog.Validate();
        return catalog;
    }

    public ArchitectureComponent Component(string id) => Components.Single(c => c.Id == id);

    public void Validate()
    {
        void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidDataException(message);
        }
        bool Present(params string[] values) => values.All(v => !string.IsNullOrWhiteSpace(v));
        Require(DateOnly.TryParseExact(ReviewedAsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _), "Use an ISO reviewed-as-of date.");
        Require(Present(Notice), "A dated, non-live notice is required.");
        Require(Components is { Length: > 0 } && Views is { Length: > 0 }, "Components and views are required.");
        Require(Components!.All(c => c is not null) && Views!.All(v => v is not null), "Null definitions are not allowed.");
        Require(Components.Select(c => c.Id).Distinct().Count() == Components.Length, "Duplicate component ID.");
        Require(Views.Select(v => v.Id).Distinct().Count() == Views.Length, "Duplicate view ID.");
        foreach (var c in Components)
        {
            Require(Present(c.Id, c.Name, c.Kind, c.Summary, c.Purpose, c.Inputs, c.Outputs,
                c.Responsibilities, c.Boundaries, c.Evidence), $"Incomplete component: {c.Id}");
            Require(c.References is { Length: > 0 } && c.References.All(r =>
                !string.IsNullOrWhiteSpace(r) && (r.StartsWith("Docs/", StringComparison.Ordinal) || r == "README.md")
                && !r.Contains("..") && !r.Contains('\\') && !r.Contains(':') && r.Split('#')[0].EndsWith(".md", StringComparison.Ordinal)),
                $"Use repository Markdown references: {c.Id}");
        }
        foreach (var view in Views)
        {
            Require(Present(view.Id, view.Title, view.Introduction, view.Boundary), "Incomplete view.");
            Require(view.Groups is { Length: > 0 } && view.Connections is not null, $"Missing groups/connections: {view.Id}");
            Require(view.Groups!.All(g => g is not null && Present(g.Title) && g.Components is { Length: > 0 }), "Incomplete group.");
            var ids = view.Groups.SelectMany(g => g.Components).ToArray();
            Require(ids.Length > 0 && ids.Distinct().Count() == ids.Length, $"Duplicate/empty view membership: {view.Id}");
            Require(ids.All(id => Components.Any(c => c.Id == id)), $"Unknown component in {view.Id}");
            Require(view.Connections!.All(e => e is not null && ids.Contains(e.From) && ids.Contains(e.To)
                && e.From != e.To && Present(e.Label)), $"Invalid connection in {view.Id}");
        }
    }
}

public sealed record ArchitectureComponent(string Id, string Name, string Kind, string Summary,
    string Purpose, string Inputs, string Outputs, string Responsibilities, string Boundaries,
    string Evidence, string[] References);
public sealed record ArchitecturePage(string Id, string Title, string Introduction, string Boundary,
    ArchitectureGroup[] Groups, ArchitectureConnection[] Connections);
public sealed record ArchitectureGroup(string Title, string[] Components);
public sealed record ArchitectureConnection(string From, string To, string Label, bool Pending = false);
