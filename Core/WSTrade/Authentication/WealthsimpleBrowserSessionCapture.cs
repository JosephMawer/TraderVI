using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Core.WSTrade.Authentication;

/// <summary>Strictly parses a user-selected local HAR; ignores unrelated requests and never replays a capture.</summary>
internal sealed class WealthsimpleBrowserSessionCapture
{
    internal string ClientId { get; private init; }
    internal string AccessToken { get; private init; }
    internal string RefreshToken { get; private init; }
    internal string DeviceId { get; private init; }
    internal string SessionId { get; private init; }
    internal string WebClientHeader { get; private init; }
    internal DateTimeOffset ExpiresAt { get; private init; }

    internal static WealthsimpleBrowserSessionCapture Read(Stream capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        try { return Parse(capture); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or OverflowException or KeyNotFoundException)
        {
            // Parsing exceptions can quote the source text. Intentionally omit the inner exception.
            throw new InvalidDataException("The capture has no supported complete Wealthsimple bearer login. Capture a fresh login with response content included.");
        }
    }

    private static WealthsimpleBrowserSessionCapture Parse(Stream capture)
    {
        using var document = JsonDocument.Parse(capture, new JsonDocumentOptions { MaxDepth = 100 });
        WealthsimpleBrowserSessionCapture newest = null;
        DateTimeOffset? newestAt = null;
        var latestWasUnsupported = false;
        foreach (var entry in document.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray())
        {
            var request = entry.GetProperty("request");
            if (Text(request, "method") != "POST" || !Uri.TryCreate(Text(request, "url"), UriKind.Absolute, out var uri) ||
                uri != WealthsimpleAuthClient.TokenUri) continue;
            var response = entry.GetProperty("response");
            if (response.GetProperty("status").GetInt32() != 200) continue;
            using var body = JsonDocument.Parse(request.GetProperty("postData").GetProperty("text").GetString());
            var grant = Text(body.RootElement, "grant_type");
            if (grant is not ("password" or "refresh_token" or "webauthn")) continue;
            var capturedAt = entry.GetProperty("startedDateTime").GetDateTimeOffset();
            if (newestAt != null && capturedAt < newestAt) continue;
            newestAt = capturedAt;
            latestWasUnsupported = true;
            var content = response.GetProperty("content");
            var responseText = content.GetProperty("text").GetString();
            if (Text(content, "encoding") == "base64")
                responseText = Encoding.UTF8.GetString(Convert.FromBase64String(responseText));
            using var tokens = JsonDocument.Parse(responseText);
            var root = tokens.RootElement;
            // Browser DPoP tokens cannot be used with our independently generated private key.
            if (!string.Equals(Text(root, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase)) continue;
            var seconds = root.GetProperty("expires_in").GetInt64();
            if (seconds <= 0 || seconds > 31 * 24 * 60 * 60) continue;
            var createdAt = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("created_at").GetInt64());
            if (createdAt > capturedAt.AddMinutes(5)) continue;
            newest = new WealthsimpleBrowserSessionCapture
            {
                ClientId = Required(Text(body.RootElement, "client_id"), 256),
                AccessToken = Required(Text(root, "access_token")),
                RefreshToken = Optional(Text(root, "refresh_token")),
                ExpiresAt = createdAt.AddSeconds(seconds),
                DeviceId = Optional(Header(request, "x-ws-device-id")),
                SessionId = Optional(Header(request, "x-ws-session-id")),
                // Only the observed public library identifier is accepted; arbitrary header data is not copied.
                WebClientHeader = Header(request, "x-wealthsimple-client") == "@wealthsimple/wealthsimple" ? "@wealthsimple/wealthsimple" : null
            };
            latestWasUnsupported = false;
        }
        if (latestWasUnsupported || newest == null) throw new FormatException();
        return newest;
    }

    private static string Required(string value, int maximum = 32 * 1024)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum || value.Any(c => c < '!' || c > '~')) throw new FormatException();
        return value;
    }
    private static string Optional(string value) => value == null ? null : Required(value);
    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static string Header(JsonElement request, string name)
    {
        if (!request.TryGetProperty("headers", out var headers)) return null;
        foreach (var header in headers.EnumerateArray())
            if (string.Equals(Text(header, "name"), name, StringComparison.OrdinalIgnoreCase)) return Text(header, "value");
        return null;
    }
    public override string ToString() => "Wealthsimple browser capture (session values hidden)";
}
