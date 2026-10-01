#nullable enable
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Core.Oracle.Llm;

public sealed record AiModelAccessResult(int HttpStatus, bool RequestedModelVisible, TimeSpan Elapsed);

/// <summary>Explicit metadata check only. Does not qualify generation, billing or review readiness.</summary>
public static class AiMomentumApiAccess
{
    public static async Task<AiModelAccessResult> CheckAsync(IConfiguration configuration, CancellationToken ct = default)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20) };
        return await CheckAsync(configuration, http, ct);
    }

    internal static async Task<AiModelAccessResult> CheckAsync(IConfiguration configuration, HttpClient http,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.openai.com/v1/models/" + AiSpendPolicy.Model);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", LlmClientFactory.GetOpenAiKey(configuration));
        var elapsed = Stopwatch.StartNew();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        bool visible = false;
        if (response.IsSuccessStatusCode)
        {
            // Never retain/print provider errors, credentials or arbitrary response bodies.
            await response.Content.LoadIntoBufferAsync(65_536, ct);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            visible = body.RootElement.ValueKind == JsonValueKind.Object &&
                body.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                id.GetString() == AiSpendPolicy.Model &&
                body.RootElement.TryGetProperty("object", out var kind) && kind.ValueKind == JsonValueKind.String &&
                kind.GetString() == "model";
        }
        return new((int)response.StatusCode, visible, elapsed.Elapsed);
    }
}
