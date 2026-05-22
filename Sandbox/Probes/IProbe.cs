using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// A one-shot exploratory script that hits an API or the local DB and prints a
/// human-readable result. Probes are not production code; they live in Sandbox
/// so we can rerun them at any time (data-source sanity checks, threshold
/// calibrations, etc.) without polluting the main programs.
/// </summary>
public interface IProbe
{
    /// <summary>Short slug used to select this probe from the command line.</summary>
    string Slug { get; }

    /// <summary>One-line human-readable description, shown in the usage banner.</summary>
    string Description { get; }

    /// <summary>Run the probe to completion. Probes are expected to print their own output.</summary>
    Task RunAsync();
}
