using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Oracle.Llm;

/// <summary>
/// OpenAI Chat Completions client. Uses raw <see cref="HttpClient"/> to keep
/// the dependency surface minimal (no new NuGet package).
///
/// Notes:
/// - Cost is computed from per-1K-token rates passed at construction. The
///   factory reads these from env vars so pricing changes are a config edit.
/// - Per Rule R3 the caller persists prompt text, prompt hash, model, etc.
///   This client just executes and returns usage.
/// - Per Rule R4, callers should enforce token budgets *before* calling.
/// </summary>
public sealed class OpenAiLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly decimal _inputPer1K;
    private readonly decimal _outputPer1K;

    public string Provider => "openai";
    public string Model => _model;

    public OpenAiLlmClient(
        string apiKey,
        string model,
        decimal inputPer1KUsd,
        decimal outputPer1KUsd,
        HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenAI API key is required.", nameof(apiKey));

        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        _model = model;
        _inputPer1K = inputPer1KUsd;
        _outputPer1K = outputPer1KUsd;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // GPT-5 family quirks (as of 2025):
        //   - `temperature` only accepts the default (1); sending any other value 400s.
        //   - `max_tokens` was renamed to `max_completion_tokens`.
        // Detect the family and shape the request accordingly.
        bool isGpt5Family = _model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

        var body = new ChatRequest
        {
            Model = _model,
            Temperature = isGpt5Family ? null : request.Temperature,
            MaxTokens = isGpt5Family ? null : request.MaxOutputTokens,
            MaxCompletionTokens = isGpt5Family ? request.MaxOutputTokens : null,
            Seed = request.Seed,
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = request.SystemPrompt },
                new ChatMessage { Role = "user",   Content = request.UserPrompt }
            }
        };

        using var resp = await _http.PostAsJsonAsync(
            "https://api.openai.com/v1/chat/completions", body, JsonOpts, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"OpenAI request failed: {(int)resp.StatusCode} {resp.ReasonPhrase} — {err}");
        }

        var parsed = await resp.Content.ReadFromJsonAsync<ChatResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("OpenAI response was empty.");

        sw.Stop();

        string text = parsed.Choices is { Length: > 0 }
            ? (parsed.Choices[0].Message?.Content ?? string.Empty)
            : string.Empty;

        int inTokens = parsed.Usage?.PromptTokens ?? 0;
        int outTokens = parsed.Usage?.CompletionTokens ?? 0;

        decimal cost =
            (inTokens / 1000m) * _inputPer1K +
            (outTokens / 1000m) * _outputPer1K;

        return new LlmResponse(
            Text: text,
            Usage: new LlmUsage(inTokens, outTokens, cost),
            Latency: sw.Elapsed,
            Provider: Provider,
            Model: Model);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed class ChatRequest
    {
        public string Model { get; set; } = string.Empty;
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public int? MaxCompletionTokens { get; set; }
        public int? Seed { get; set; }
    }

    private sealed class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        public ChatChoice[]? Choices { get; set; }
        public ChatUsage? Usage { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatMessage? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private sealed class ChatUsage
    {
        [JsonPropertyName("prompt_tokens")]     public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")]      public int TotalTokens { get; set; }
    }
}
