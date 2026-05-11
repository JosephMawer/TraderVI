using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Oracle.Llm;

/// <summary>
/// Provider-neutral contract for the Oracle's LLM calls.
/// Concrete implementations: <c>OpenAiLlmClient</c>, <c>DotLlmClient</c>, <c>MockLlmClient</c>.
///
/// Per <c>Docs/oracle-rules.md</c>:
/// - R3: every call must carry enough metadata to be fully reproducible
///       (model, provider, temperature, prompt text, prompt hash, token usage).
/// - R7: all call sites go through this interface, so providers are swappable.
/// </summary>
public interface ILlmClient
{
    /// <summary>Stable provider name persisted alongside every narrative (e.g., "openai", "dotllm", "mock").</summary>
    string Provider { get; }

    /// <summary>Stable model name persisted alongside every narrative (e.g., "gpt-4o-mini").</summary>
    string Model { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

/// <summary>
/// Provider-neutral request. Keep this free of provider-specific concepts so the
/// same payload can be replayed across backends (Rule R7).
/// </summary>
public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    double Temperature = 0.2,
    int? MaxOutputTokens = null,
    int? Seed = null,
    IReadOnlyDictionary<string, string>? Tags = null
);

/// <summary>
/// Provider-neutral response. <see cref="Usage"/> drives cost tracking + R4 guardrails.
/// </summary>
public sealed record LlmResponse(
    string Text,
    LlmUsage Usage,
    TimeSpan Latency,
    string Provider,
    string Model
);

/// <summary>
/// Token + cost accounting. <see cref="CostUsd"/> is computed by the client using
/// per-provider rate tables (see <c>OpenAiLlmClient</c>).
/// </summary>
public sealed record LlmUsage(
    int InputTokens,
    int OutputTokens,
    decimal CostUsd
)
{
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Hard limit guardrail (Rule R4). Thrown when a request would breach a budget.
/// </summary>
public sealed class LlmBudgetExceededException : Exception
{
    public LlmBudgetExceededException(string message) : base(message) { }
}
