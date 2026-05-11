using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Oracle.Llm;

/// <summary>
/// Offline echo client. Default when <c>ORACLE_LLM_PROVIDER</c> is unset or
/// set to <c>mock</c>. Useful for prompt iteration without spending tokens,
/// and as the regression-test driver for the eval suite (Rule R6).
///
/// Produces a deterministic, dossier-grounded text by extracting a handful of
/// recognizable fields from the user prompt — so it satisfies Rule R2 (no
/// hallucinated numbers) by construction.
/// </summary>
public sealed class MockLlmClient : ILlmClient
{
    public string Provider => "mock";
    public string Model => "mock-echo-v1";

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var sb = new StringBuilder();
        sb.AppendLine("[MOCK NARRATIVE — no model called]");
        sb.AppendLine("This run was produced offline by MockLlmClient.");
        sb.AppendLine("Set ORACLE_LLM_PROVIDER=openai (and OPENAI_API_KEY) for a real call.");
        sb.AppendLine();
        sb.AppendLine("Echoed system prompt length: " + request.SystemPrompt.Length);
        sb.AppendLine("Echoed user prompt length:   " + request.UserPrompt.Length);

        // Cheap, deterministic "summary": pull the first non-empty user lines.
        sb.AppendLine();
        sb.AppendLine("--- First dossier excerpt ---");
        int shown = 0;
        foreach (var line in request.UserPrompt.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            sb.AppendLine(trimmed);
            if (++shown >= 12) break;
        }

        sw.Stop();

        // Rough token approximation: 1 token ≈ 4 chars (good enough for mock).
        int inTokens = (request.SystemPrompt.Length + request.UserPrompt.Length) / 4;
        int outTokens = sb.Length / 4;

        var resp = new LlmResponse(
            Text: sb.ToString(),
            Usage: new LlmUsage(InputTokens: inTokens, OutputTokens: outTokens, CostUsd: 0m),
            Latency: sw.Elapsed,
            Provider: Provider,
            Model: Model);

        return Task.FromResult(resp);
    }
}
