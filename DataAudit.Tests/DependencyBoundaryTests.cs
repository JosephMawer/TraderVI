using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace DataAudit.Tests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void AuditRuntimeGraphDoesNotPullInCoreOrMachineLearning()
    {
        // Inspect the resolved graph, including transitive project/package references.
        // Assembly references alone can omit an unused but still copied dependency.
        string manifest = Path.Combine(AppContext.BaseDirectory,
            typeof(DependencyBoundaryTests).Assembly.GetName().Name + ".deps.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        string[] dependencies = document.RootElement.GetProperty("libraries")
            .EnumerateObject().Select(entry => entry.Name.Split('/')[0]).ToArray();

        dependencies.ShouldContain("DataAudit");
        dependencies.ShouldContain("TraderVI.MarketData");
        dependencies.Where(name =>
            name.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("TraderVI.Data", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Microsoft.ML", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LightGBM", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }
}
