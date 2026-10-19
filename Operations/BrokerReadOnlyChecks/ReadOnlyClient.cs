using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Core.WSTrade.Accounts;

namespace Core.WSTrade.Authentication;

// This replaces the general authentication implementation in this standalone build.
// No password/OTP/refresh/export/order mutation methods are compiled into this executable.
public sealed partial class WealthsimpleAuthClient : IDisposable
{
    private readonly HttpClient http;
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly BrokerReadOnlyChecks.ReadOnlyTransport transport;
    private readonly string? deviceId, sessionId, webClientHeader;
    private Session session;
    private bool disposed;

    private WealthsimpleAuthClient(string access, DateTimeOffset expires, string? device, string? browserSession,
        string? library, BrokerReadOnlyChecks.ReadOnlyTransport transport)
    {
        session = new(access, expires);
        this.transport = transport;
        deviceId = device; sessionId = browserSession; webClientHeader = library;
        http = new HttpClient(transport, true) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 128 * 1024 };
    }

    internal static WealthsimpleAuthClient FromReadOnlySession(byte[] plaintext, HttpMessageHandler handler)
    {
        using var json = JsonDocument.Parse(plaintext);
        var root = json.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException();
        var access = StringValue(root, "access");
        var expiry = root.GetProperty("expires").GetDateTimeOffset();
        if (!SafeHeader(access) || expiry > DateTimeOffset.UtcNow.AddDays(32)) throw new InvalidDataException();
        string? HeaderField(string name)
        {
            var value = StringValue(root, name);
            if (value is not null && (!SafeHeader(value) || value.Length > 512)) throw new InvalidDataException();
            return value;
        }
        // Deliberately never read/store the refresh-token property.
        return new(access!, expiry, HeaderField("deviceId"), HeaderField("sessionId"),
            StringValue(root, "webClientHeader") == "@wealthsimple/wealthsimple" ? "@wealthsimple/wealthsimple" : null,
            new BrokerReadOnlyChecks.ReadOnlyTransport(access!, handler));
    }

    internal int RequestCount => transport.RequestCount;
    internal async Task<bool> VerifyReadOnlyAsync(CancellationToken ct)
    {
        if (session.ExpiresAt <= clock.GetUtcNow()) return false;
        using var response = await ExchangeAsync(BrokerReadOnlyChecks.ReadOnlyTransport.InfoUri, null, null, session, ct);
        if (!response.IsSuccessStatusCode) return false;
        using var json = ReadJson(response);
        if (json is null || !json.RootElement.TryGetProperty("profiles", out var profiles)
            || profiles.ValueKind != JsonValueKind.Object || !profiles.EnumerateObject().Any()) return false;
        var identity = StringValue(json.RootElement, "identity_canonical_id");
        if (!SafeHeader(identity) || identity!.Length > 512 || session.ExpiresAt <= clock.GetUtcNow()) return false;
        session.IdentityId = identity;
        transport.BindIdentity(identity);
        return true;
    }

    internal void SelectReadOnlyAccount(WealthsimpleTfsaAccount account)
    {
        if (tfsaAccounts.Count != 1 || !tfsaAccounts.Contains(account)) throw new InvalidDataException();
        transport.BindAccount(account.Id);
    }

    private async Task<HttpResponseMessage> ExchangeAsync(Uri uri, Dictionary<string, object>? body,
        Dictionary<string, string>? headers, Session auth, CancellationToken ct)
    {
        if (!ReferenceEquals(auth, session) || auth.ExpiresAt <= clock.GetUtcNow()) throw new InvalidDataException();
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Date = clock.GetUtcNow();
        if (deviceId is not null) request.Headers.Add("X-WS-Device-ID", deviceId);
        if (sessionId is not null) request.Headers.Add("X-WS-Session-ID", sessionId);
        if (webClientHeader is not null) request.Headers.Add("X-Wealthsimple-Client", webClientHeader);
        if (headers is not null) foreach (var item in headers) request.Headers.Add(item.Key, item.Value);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await http.SendAsync(request, ct);
    }

    internal static string ReadOnlyPendingQuery => PendingOrdersQuery;
    internal static IReadOnlyList<string> ReadOnlyPendingStatuses => PendingOrderStatuses;
    private static bool SafeHeader(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 32768 && text.All(c => c is >= '!' and <= '~');
    private static string? StringValue(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static JsonDocument? ReadJson(HttpResponseMessage response)
    {
        try { return JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()); }
        catch (JsonException) { return null; }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        session = null!; tfsaAccounts.Clear(); catalogIdentity = null;
        http.Dispose(); gate.Dispose();
    }
    private sealed class Session(string access, DateTimeOffset expires)
    {
        internal string AccessToken { get; } = access;
        internal DateTimeOffset ExpiresAt { get; } = expires;
        internal string IdentityId { get; set; } = null!;
    }
}
