#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Oracle.Llm;

/// <summary>
/// Explicitly composed Astra client. Exact input count, bounded output (including reasoning),
/// durable reservation and one transmission. Never configured by Oracle's legacy provider switch.
/// </summary>
public sealed class OpenAiResponsesLlmClient : ILlmClient, IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly string apiKey;
    private readonly AiCallJournal journal;
    private readonly AiSpendPolicy policy;
    private readonly TimeProvider clock;
    public string Provider => "openai-responses";
    public string Model => AiSpendPolicy.Model;

    public OpenAiResponsesLlmClient(string apiKey, AiCallJournal journal, AiSpendPolicy policy,
        HttpClient? http = null, TimeProvider? clock = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("OpenAI API key is required.");
        this.apiKey = apiKey;
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        this.clock = clock ?? TimeProvider.System;
        ownsHttp = http is null;
        // Never forward a credential through a redirect or silently retransmit a paid request.
        this.http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        Guid callId = request.CallId ?? throw new ArgumentException("A stable AI call ID is required.");
        journal.CheckNewCall(callId, policy, clock.GetUtcNow());
        if (string.IsNullOrWhiteSpace(request.SystemPrompt) || string.IsNullOrWhiteSpace(request.UserPrompt) ||
            request.SystemPrompt.Length + (long)request.UserPrompt.Length > 1_000_000 ||
            request.OutputSchema is null || string.IsNullOrWhiteSpace(request.OutputSchema.Name) ||
            request.OutputSchema.Schema.ValueKind != JsonValueKind.Object || request.Seed != null ||
            request.ReasoningEffort is not ("low" or "medium" or "high" or "xhigh" or "max"))
            throw new ArgumentException("AI request needs bounded text, a trusted output schema and supported reasoning settings.");
        int outputLimit = request.MaxOutputTokens ?? policy.MaxOutputTokens;
        if (outputLimit <= 0 || outputLimit > policy.MaxOutputTokens)
            throw new ArgumentException("AI output limit exceeds its spending policy.");

        var format = new { type = "json_schema", name = request.OutputSchema.Name, strict = true, schema = request.OutputSchema.Schema };
        var input = new[] { new { role = "user", content = request.UserPrompt } };
        var text = new { format };
        var reasoning = new { effort = request.ReasoningEffort, mode = "standard" };
        string countJson = JsonSerializer.Serialize(new { model = Model, instructions = request.SystemPrompt, input, text, reasoning });
        string requestJson = JsonSerializer.Serialize(new
        {
            model = Model, instructions = request.SystemPrompt, input, text,
            reasoning, max_output_tokens = outputLimit,
            store = false, truncation = "disabled", service_tier = "default"
        });
        var elapsed = Stopwatch.StartNew();
        string countResponse = await PostAsync("responses/input_tokens", countJson, ct);
        int counted;
        using (var count = Parse(countResponse))
        {
            if (!count.RootElement.TryGetProperty("input_tokens", out var value) ||
                !value.TryGetInt32(out counted) || counted <= 0 || counted > policy.MaxInputTokens)
                throw new InvalidDataException("AI input count is missing or exceeds its policy.");
        }
        ct.ThrowIfCancellationRequested();
        journal.Reserve(callId, policy, clock.GetUtcNow(), requestJson, counted, outputLimit);

        // Any failure from this point keeps the reservation. A caller must not retry this call ID.
        string raw = await PostAsync("responses", requestJson, ct);
        journal.RecordResponse(callId, raw);
        using var response = Parse(raw);
        var root = response.RootElement;
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty("input_tokens", out var inputCount) || !inputCount.TryGetInt32(out int usedInput) || usedInput <= 0 ||
            !usage.TryGetProperty("output_tokens", out var outputCount) || !outputCount.TryGetInt32(out int usedOutput) || usedOutput < 0)
            throw new InvalidDataException("AI response has no valid usage; its reservation remains held.");

        string status = GetString(root, "status");
        string returnedModel = GetString(root, "model");
        bool modelMatches = returnedModel == Model || returnedModel.StartsWith(Model + "-", StringComparison.Ordinal);
        if (!modelMatches || GetString(root, "service_tier") != "default")
        {
            journal.Block();
            throw new InvalidDataException("AI model or billing tier differs from its priced reservation.");
        }
        journal.Settle(callId, usedInput, usedOutput, status == "completed" ? "ModelCompleted" : "RejectedResponse", clock.GetUtcNow());
        if (status != "completed" || string.IsNullOrEmpty(GetString(root, "id")))
            throw new InvalidDataException("AI response is incomplete or has an unexpected identity.");
        string? answer = null;
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("AI response has no output.");
        foreach (var item in output.EnumerateArray())
        {
            if (GetString(item, "type") == "reasoning") continue;
            if (GetString(item, "type") != "message" || GetString(item, "role") != "assistant" ||
                GetString(item, "status") != "completed" ||
                !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("AI returned an unsupported output item.");
            foreach (var part in content.EnumerateArray())
            {
                if (GetString(part, "type") != "output_text" || answer != null)
                    throw new InvalidDataException("AI refused the request or returned ambiguous output.");
                answer = GetString(part, "text");
            }
        }
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidDataException("AI response is empty.");
        return new(answer, new LlmUsage(usedInput, usedOutput, policy.CostUsd(usedInput, usedOutput)),
            elapsed.Elapsed, Provider, returnedModel);
    }

    private async Task<string> PostAsync(string endpoint, string json, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/" + endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            // Error bodies may echo private inputs. Do not put them in exception messages or logs.
            throw new HttpRequestException($"OpenAI {endpoint} failed with HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        await response.Content.LoadIntoBufferAsync(2_000_000, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static JsonDocument Parse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { throw new InvalidDataException("OpenAI returned invalid JSON."); }
    }
    private static string GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : string.Empty;

    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
