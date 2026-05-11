using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Core.Oracle.Llm;

/// <summary>
/// Configuration-driven LLM client selection. Single source of truth so call sites
/// stay provider-agnostic (Rule R7).
///
/// Recognized keys (read via <see cref="IConfiguration"/>, so they may come from
/// user-secrets, appsettings.json, environment variables, etc.):
///
/// - <c>Oracle:Llm:Provider</c>      / env <c>ORACLE_LLM_PROVIDER</c>     — "mock" (default), "openai", or "dotllm".
/// - <c>Oracle:Llm:Model</c>         / env <c>ORACLE_LLM_MODEL</c>        — model name (defaults per provider).
/// - <c>Oracle:OpenAi:ApiKey</c>     / env <c>OPENAI_API_KEY</c>          — required when provider=openai.
/// - <c>Oracle:OpenAi:InputPer1K</c> / env <c>ORACLE_OPENAI_INPUT_PER_1K</c>  — USD per 1K input tokens.
/// - <c>Oracle:OpenAi:OutputPer1K</c>/ env <c>ORACLE_OPENAI_OUTPUT_PER_1K</c> — USD per 1K output tokens.
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// Builds a client from an explicit configuration (user-secrets, appsettings, env, etc.).
    /// </summary>
    public static ILlmClient FromConfiguration(IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        var provider = (Get(cfg, "Oracle:Llm:Provider", "ORACLE_LLM_PROVIDER") ?? "mock")
            .Trim()
            .ToLowerInvariant();

        return provider switch
        {
            "openai" => BuildOpenAi(cfg),
            "dotllm" => new DotLlmClient(
                Get(cfg, "Oracle:Llm:Model", "ORACLE_LLM_MODEL") ?? "local-default"),
            _ => new MockLlmClient(),
        };
    }

    /// <summary>
    /// Backwards-compatible helper that reads only from environment variables.
    /// Prefer <see cref="FromConfiguration"/> with user-secrets for development.
    /// </summary>
    public static ILlmClient FromEnvironment()
    {
        var cfg = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        return FromConfiguration(cfg);
    }

    private static OpenAiLlmClient BuildOpenAi(IConfiguration cfg)
    {
        var key = Get(cfg, "Oracle:OpenAi:ApiKey", "OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "Provider=openai but no API key found. Set it via user-secrets " +
                "(Oracle:OpenAi:ApiKey) or the OPENAI_API_KEY environment variable.");

        var model = Get(cfg, "Oracle:Llm:Model", "ORACLE_LLM_MODEL") ?? "gpt-4o-mini";

        decimal inRate  = ParseDecimal(Get(cfg, "Oracle:OpenAi:InputPer1K",  "ORACLE_OPENAI_INPUT_PER_1K"),  0.00015m);
        decimal outRate = ParseDecimal(Get(cfg, "Oracle:OpenAi:OutputPer1K", "ORACLE_OPENAI_OUTPUT_PER_1K"), 0.0006m);

        return new OpenAiLlmClient(key, model, inRate, outRate);
    }

    /// <summary>
    /// Reads a value from configuration, falling back to a legacy flat environment
    /// variable name for ergonomics.
    /// </summary>
    private static string? Get(IConfiguration cfg, string key, string envFallback)
    {
        var v = cfg[key];
        if (!string.IsNullOrWhiteSpace(v)) return v;
        v = Environment.GetEnvironmentVariable(envFallback);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static decimal ParseDecimal(string? raw, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;
    }
}

