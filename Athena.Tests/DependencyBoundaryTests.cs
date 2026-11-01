using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Athena.Tests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void AthenaRuntimeGraphDoesNotPullInCoreOrMachineLearning()
    {
        // Inspect the resolved graph, including transitive project/package references.
        // Assembly references alone can omit an unused but still copied dependency.
        string manifest = Path.Combine(AppContext.BaseDirectory,
            typeof(DependencyBoundaryTests).Assembly.GetName().Name + ".deps.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        string[] dependencies = document.RootElement.GetProperty("libraries")
            .EnumerateObject().Select(entry => entry.Name.Split('/')[0]).ToArray();

        dependencies.ShouldContain("Athena");
        dependencies.ShouldContain("TraderVI.MarketData");
        dependencies.Where(name =>
            name.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("TraderVI.MarketProviders", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Microsoft.ML", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LightGBM", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }
}
