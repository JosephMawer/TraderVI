using System;
using System.Threading.Tasks;
using Core.Oracle.Llm;

namespace Sandbox.Probes;

/// <summary>One explicitly requested model-metadata GET; no model generation, SQL or journals.</summary>
public sealed class AiMomentumAccessProbe : IProbe
{
    public string Slug => "ai-momentum-access";
    public string Description => "Check Oracle's saved key against AI Momentum model metadata (one OpenAI GET).";

    public async Task RunAsync()
    {
        try
        {
            var configuration = LlmClientFactory.LoadOracleConfiguration();
            using var ownedConfiguration = configuration as IDisposable;
            var result = await AiMomentumApiAccess.CheckAsync(configuration);
            Console.WriteLine($"UTC: {DateTimeOffset.UtcNow:O}; HTTP: {result.HttpStatus}; " +
                $"requested model visible: {result.RequestedModelVisible}; elapsed ms: {result.Elapsed.TotalMilliseconds:F0}.");
            Console.WriteLine("Metadata only; generation access, available funding and actual review costs remain unverified.");
            if (!result.RequestedModelVisible) throw new InvalidOperationException("Requested model metadata is unavailable.");
        }
        catch
        {
            // Configuration/HTTP exception messages can contain sensitive provider or file content.
            Console.WriteLine("AI access check failed. No credential or provider response was logged.");
            throw new InvalidOperationException("AI access check did not qualify the requested model metadata.");
        }
    }
}
