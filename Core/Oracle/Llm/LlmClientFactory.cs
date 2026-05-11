using System;
using System.Globalization;

namespace Core.Oracle.Llm;

/// <summary>
/// Env-driven LLM client selection. Single source of truth so call sites stay
/// provider-agnostic (Rule R7).
///
/// Environment variables:
/// - <c>ORACLE_LLM_PROVIDER</c>     — "mock" (default), "openai", or "dotllm".
/// - <c>ORACLE_LLM_MODEL</c>        — model name (defaults per provider).
/// - <c>OPENAI_API_KEY</c>          — required when provider=openai.
/// - <c>ORACLE_OPENAI_INPUT_PER_1K</c>  — USD per 1K input tokens.
/// - <c>ORACLE_OPENAI_OUTPUT_PER_1K</c> — USD per 1K output tokens.
/// </summary>
public static class LlmClientFactory
{
    public static ILlmClient FromEnvironment()
    {
        var provider = (Environment.GetEnvironmentVariable("ORACLE_LLM_PROVIDER") ?? "mock")
            .Trim()
            .ToLowerInvariant();

        return provider switch
        {
            "openai" => BuildOpenAi(),
            "dotllm" => new DotLlmClient(
                Environment.GetEnvironmentVariable("ORACLE_LLM_MODEL") ?? "local-default"),
            _ => new MockLlmClient(),
        };
    }

    private static OpenAiLlmClient BuildOpenAi()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "ORACLE_LLM_PROVIDER=openai but OPENAI_API_KEY is not set.");

        var model = Environment.GetEnvironmentVariable("ORACLE_LLM_MODEL") ?? "gpt-4o-mini";

        decimal inRate = ParseDecimal("ORACLE_OPENAI_INPUT_PER_1K", fallback: 0.00015m);
        decimal outRate = ParseDecimal("ORACLE_OPENAI_OUTPUT_PER_1K", fallback: 0.0006m);

        return new OpenAiLlmClient(key, model, inRate, outRate);
    }

    private static decimal ParseDecimal(string name, decimal fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;
    }
}
