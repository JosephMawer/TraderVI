using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

/// <summary>
/// Experimental direct auth, derived from the September 10, 2026 public web application.
/// Read-only TFSA queries share this session. Session export requires an explicit protector.
/// Browser bearer sessions can be handed off through a locally supplied HAR, then verified.
/// One instance owns one login attempt/session and its DPoP key; dispose only after calls finish.
/// </summary>
public sealed partial class WealthsimpleAuthClient : IDisposable
{
    internal static readonly Uri TokenUri = new("https://api.production.wealthsimple.com/v1/oauth/v2/token");
    internal static readonly Uri InfoUri = new("https://api.production.wealthsimple.com/v1/oauth/v2/token/info");
    private readonly string clientId;
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly bool requestDpop;
    private string deviceId;
    private string sessionId;
    private string webClientHeader;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly WealthsimpleDpopKey proofKey = new();
    private Session session;
    private PendingLogin pending;
    private bool disposed;

    // Public production OAuth client ID observed in Wealthsimple's shipped web bundle (not a secret).
    public static WealthsimpleAuthClient CreateForDesktop() => new("4da53ac2b03225bed1550eba8e4611e086c7b905a3855e6ed12ea08c246758fa");

    public WealthsimpleAuthClient(string clientId, bool requestDpop = false)
        : this(clientId, NewHandler(), TimeProvider.System, requestDpop) { }

    private static HttpClientHandler NewHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    /// <summary>Reads only a local user-selected capture. Call VerifyAsync to prove connectivity.
    /// Does not replay the password, OTP, Turnstile value, or any recorded request.</summary>
    public static WealthsimpleAuthClient FromBrowserHar(Stream capture) =>
        FromBrowserHar(capture, NewHandler(), TimeProvider.System);

    internal static WealthsimpleAuthClient FromBrowserHar(Stream capture, HttpMessageHandler handler, TimeProvider clock)
    {
        WealthsimpleBrowserSessionCapture parsed;
        try { parsed = WealthsimpleBrowserSessionCapture.Read(capture); }
        catch { handler.Dispose(); throw; }
        var client = new WealthsimpleAuthClient(parsed.ClientId, handler, clock)
        {
            session = new Session(parsed.AccessToken, parsed.RefreshToken, "Bearer", parsed.ExpiresAt),
            deviceId = parsed.DeviceId,
            sessionId = parsed.SessionId,
            webClientHeader = parsed.WebClientHeader
        };
        return client;
    }

    // Tests can supply an offline transport; production destinations cannot be configured.
    internal WealthsimpleAuthClient(string clientId, HttpMessageHandler handler, TimeProvider clock, bool requestDpop = false)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 256 || clientId.Any(char.IsControl))
            throw new ArgumentException("A public web client identifier is required.", nameof(clientId));
        this.clientId = clientId;
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.requestDpop = requestDpop;
        http = new HttpClient(handler, true) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 128 * 1024 };
    }

    public Task<WealthsimpleAuthResult> BeginLoginAsync(string username, string password,
        CancellationToken cancellationToken = default) => BeginLoginAsync(username, password, null, cancellationToken);

    public Task<WealthsimpleAuthResult> BeginLoginAsync(string username, string password,
        WealthsimpleLoginContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("An email and password are required; their values are not logged.");
        return LockedAsync(async () =>
        {
            session = null;
            tfsaAccounts.Clear();
            deviceId = context?.DeviceId;
            sessionId = context?.SessionId;
            webClientHeader = null;
            pending = new PendingLogin(username, password, clock.GetUtcNow()) { TurnstileToken = context?.TurnstileToken };
            return await PasswordExchangeAsync(null, cancellationToken);
        }, cancellationToken);
    }

    public Task<WealthsimpleAuthResult> CompleteOtpAsync(string code, CancellationToken cancellationToken = default)
    {
        // A code is data, never a way to inject remember/skip flags into the OTP header.
        if (string.IsNullOrWhiteSpace(code) || code.Length > 32 || !code.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("Enter only the verification code.", nameof(code));
        return LockedAsync(async () =>
        {
            if (pending == null || !pending.AwaitingOtp || clock.GetUtcNow() - pending.StartedAt > TimeSpan.FromMinutes(10))
            {
                pending = null;
                return new(WealthsimpleAuthStatus.NotAuthenticated);
            }
            return await PasswordExchangeAsync(code, cancellationToken);
        }, cancellationToken);
    }

    public Task<WealthsimpleAuthResult> RefreshAsync(CancellationToken cancellationToken = default) =>
        LockedAsync(async () =>
        {
            var previous = session;
            if (previous?.RefreshToken == null) return new(WealthsimpleAuthStatus.NotAuthenticated);
            // A timeout may follow server-side token rotation. Do not reuse uncertain credentials.
            session = null;
            var body = new Dictionary<string, object>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = previous.RefreshToken,
                ["client_id"] = clientId
            };
            using var response = await ExchangeAsync(TokenUri, body, null, null, cancellationToken);
            if (!response.IsSuccessStatusCode) return ClassifyFailure(response);
            return await AcceptSessionAsync(response, previous.RefreshToken, cancellationToken);
        }, cancellationToken);

    public Task<WealthsimpleAuthResult> VerifyAsync(CancellationToken cancellationToken = default) =>
        LockedAsync(async () =>
        {
            if (session == null) return new(WealthsimpleAuthStatus.NotAuthenticated);
            if (session.ExpiresAt <= clock.GetUtcNow())
            {
                // Expired access is never sent, but explicit refresh may still renew this session.
                return new(WealthsimpleAuthStatus.NotAuthenticated, ExpiresAt: session.ExpiresAt,
                    CanRefresh: session.RefreshToken != null);
            }
            var result = await VerifySessionAsync(session, cancellationToken);
            if (result.Status != WealthsimpleAuthStatus.Authenticated) session = null;
            return result;
        }, cancellationToken);

    public Task<WealthsimpleAuthResult> ForgetAsync(CancellationToken cancellationToken = default) =>
        LockedAsync(() =>
        {
            tfsaAccounts.Clear(); catalogIdentity = null;
            session = null;
            pending = null;
            return Task.FromResult(new WealthsimpleAuthResult(WealthsimpleAuthStatus.NotAuthenticated));
        }, cancellationToken);

    private async Task<WealthsimpleAuthResult> PasswordExchangeAsync(string code, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["grant_type"] = "password", ["username"] = pending.Username, ["password"] = pending.Password,
            ["skip_provision"] = true, ["scope"] = "read write", ["client_id"] = clientId
        };
        var headers = new Dictionary<string, string>();
        if (code != null) headers["x-wealthsimple-otp"] = code;
        if (pending.TurnstileToken != null) headers["x-wealthsimple-turnstile"] = pending.TurnstileToken;
        if (pending.OtpAuthenticatedClaim != null)
            headers["x-wealthsimple-otp-authenticated-claim"] = pending.OtpAuthenticatedClaim;

        using var response = await ExchangeAsync(TokenUri, body, headers, null, ct);
        if (response.IsSuccessStatusCode)
        {
            pending = null;
            return await AcceptSessionAsync(response, null, ct);
        }

        var result = ClassifyFailure(response);
        if (result.Status is WealthsimpleAuthStatus.OtpRequired or WealthsimpleAuthStatus.InvalidOtp)
        {
            pending.AwaitingOtp = true;
            var claim = Header(response, "x-wealthsimple-otp-authenticated-claim");
            if (claim != null && !SafeHeaderValue(claim))
            {
                pending = null;
                return new(WealthsimpleAuthStatus.InvalidResponse, (int)response.StatusCode);
            }
            pending.OtpAuthenticatedClaim = claim ?? pending.OtpAuthenticatedClaim;
        }
        else pending = null;
        return result;
    }

    private async Task<WealthsimpleAuthResult> AcceptSessionAsync(HttpResponseMessage response,
        string previousRefreshToken, CancellationToken ct)
    {
        using var json = ReadJson(response);
        var root = json?.RootElement ?? default;
        var token = StringValue(root, "access_token");
        var type = StringValue(root, "token_type");
        if (!SafeHeaderValue(token) || type is not ("Bearer" or "bearer" or "DPoP" or "dpop") ||
            (!requestDpop && type.Equals("DPoP", StringComparison.OrdinalIgnoreCase)) ||
            !root.TryGetProperty("expires_in", out var expiry) || expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetInt64(out var seconds) ||
            seconds <= 0 || seconds > 31 * 24 * 60 * 60)
            return new(WealthsimpleAuthStatus.InvalidResponse, (int)response.StatusCode);

        var createdAt = clock.GetUtcNow();
        if (root.TryGetProperty("created_at", out var created))
        {
            if (created.ValueKind != JsonValueKind.Number || !created.TryGetInt64(out var timestamp))
                return new(WealthsimpleAuthStatus.InvalidResponse);
            try { createdAt = DateTimeOffset.FromUnixTimeSeconds(timestamp); }
            catch (ArgumentOutOfRangeException) { return new(WealthsimpleAuthStatus.InvalidResponse); }
            if (createdAt > clock.GetUtcNow().AddMinutes(5)) return new(WealthsimpleAuthStatus.InvalidResponse);
        }
        var refresh = StringValue(root, "refresh_token") ?? previousRefreshToken;
        if (refresh != null && !SafeHeaderValue(refresh)) return new(WealthsimpleAuthStatus.InvalidResponse);
        DateTimeOffset expiresAt;
        try { expiresAt = createdAt.AddSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return new(WealthsimpleAuthStatus.InvalidResponse); }
        if (expiresAt <= clock.GetUtcNow()) return new(WealthsimpleAuthStatus.InvalidResponse);
        var candidate = new Session(token, refresh, type.Equals("DPoP", StringComparison.OrdinalIgnoreCase) ? "DPoP" : "Bearer", expiresAt);
        var verification = await VerifySessionAsync(candidate, ct);
        if (verification.Status == WealthsimpleAuthStatus.Authenticated) session = candidate;
        return verification;
    }

    private async Task<WealthsimpleAuthResult> VerifySessionAsync(Session candidate, CancellationToken ct)
    {
        using var response = await ExchangeAsync(InfoUri, null, null, candidate, ct);
        if (!response.IsSuccessStatusCode) return ClassifyFailure(response);
        using var json = ReadJson(response);
        if (json?.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object ||
            !profiles.EnumerateObject().Any())
            return new(WealthsimpleAuthStatus.InvalidResponse, (int)response.StatusCode);
        if (candidate.ExpiresAt <= clock.GetUtcNow()) return new(WealthsimpleAuthStatus.NotAuthenticated);
        var identity = StringValue(json.RootElement, "identity_canonical_id");
        candidate.IdentityId = SafeHeaderValue(identity) && identity.Length <= 512 ? identity : null;
        return new(WealthsimpleAuthStatus.Authenticated, (int)response.StatusCode,
            ExpiresAt: candidate.ExpiresAt, CanRefresh: candidate.RefreshToken != null);
    }

    private async Task<HttpResponseMessage> ExchangeAsync(Uri uri, Dictionary<string, object> body,
        Dictionary<string, string> headers, Session auth, CancellationToken ct, bool allowNonceRetry = true)
    {
        // Nonces are local to this exchange; never mix authorization-server and resource-server nonces.
        string nonce = null;
        for (var attempt = 0; ; attempt++)
        {
            var method = body == null ? HttpMethod.Get : HttpMethod.Post;
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Date = clock.GetUtcNow();
            if (deviceId != null) request.Headers.Add("X-WS-Device-ID", deviceId);
            if (sessionId != null) request.Headers.Add("X-WS-Session-ID", sessionId);
            if (webClientHeader != null) request.Headers.Add("X-Wealthsimple-Client", webClientHeader);
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            if (auth != null) request.Headers.Authorization = new AuthenticationHeaderValue(auth.Type, auth.AccessToken);
            if (headers != null)
                foreach (var item in headers) request.Headers.Add(item.Key, item.Value);
            var useProof = auth == null ? requestDpop : auth.Type == "DPoP";
            if (useProof)
                request.Headers.Add("DPoP", proofKey.CreateProof(method.Method, uri, clock.GetUtcNow(), auth?.AccessToken, nonce));
            var response = await http.SendAsync(request, ct);
            var suppliedNonce = Header(response, "DPoP-Nonce");
            if (allowNonceRetry && useProof && attempt == 0 && SafeHeaderValue(suppliedNonce) &&
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized && IsNonceChallenge(response))
            {
                nonce = suppliedNonce;
                response.Dispose();
                continue;
            }
            return response;
        }
    }

    private static bool IsNonceChallenge(HttpResponseMessage response)
    {
        using var json = ReadJson(response);
        return StringValue(json?.RootElement ?? default, "error") == "use_dpop_nonce" ||
            response.Headers.WwwAuthenticate.Any(h => h.Scheme.Equals("DPoP", StringComparison.OrdinalIgnoreCase) &&
                (h.Parameter ?? "").Contains("error=\"use_dpop_nonce\"", StringComparison.Ordinal));
    }

    private static WealthsimpleAuthResult ClassifyFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status == 429) return new(WealthsimpleAuthStatus.RateLimited, status);
        if (status >= 500) return new(WealthsimpleAuthStatus.ServiceUnavailable, status);
        using var json = ReadJson(response);
        var error = StringValue(json?.RootElement ?? default, "error");
        // Unsupported security challenges are surfaced, never converted to an OTP fallback or bypass.
        if (error is "login_challenge_required" or "turnstile_required" ||
            Header(response, "cf-mitigated") == "challenge" ||
            Header(response, "x-wealthsimple-password-authenticated-claim") != null)
            return new(WealthsimpleAuthStatus.BrowserInteractionRequired, status);

        var otp = (Header(response, "x-wealthsimple-otp") ?? "").Split(';', StringSplitOptions.TrimEntries);
        var method = otp.FirstOrDefault(p => p.StartsWith("method=", StringComparison.Ordinal))?[7..];
        if (method is not ("app" or "sms" or "email" or "recovery_sms")) method = null;
        if (status == 401 && otp.Contains("invalid")) return new(WealthsimpleAuthStatus.InvalidOtp, status, method);
        if (status == 401 && otp.Contains("required")) return new(WealthsimpleAuthStatus.OtpRequired, status, method);
        return new(WealthsimpleAuthStatus.Rejected, status);
    }

    private async Task<WealthsimpleAuthResult> LockedAsync(Func<Task<WealthsimpleAuthResult>> action, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(ct);
        try { return await action(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            session = null; pending = null;
            throw new OperationCanceledException("Wealthsimple authentication was cancelled.", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.IO.IOException)
        {
            session = null; pending = null;
            // Exception messages and server bodies may contain secrets. Never forward them.
            return new(WealthsimpleAuthStatus.TransportFailure);
        }
        finally { gate.Release(); }
    }

    private static JsonDocument ReadJson(HttpResponseMessage response)
    {
        try { return JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()); }
        catch (JsonException) { return null; }
    }

    private static string StringValue(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    private static bool SafeHeaderValue(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 32 * 1024 && value.All(c => c is >= '!' and <= '~');

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        tfsaAccounts.Clear(); catalogIdentity = null;
        session = null; pending = null;
        http.Dispose(); proofKey.Dispose(); gate.Dispose();
    }

    private sealed class PendingLogin(string username, string password, DateTimeOffset startedAt)
    {
        internal string Username { get; } = username;
        internal string Password { get; } = password;
        internal DateTimeOffset StartedAt { get; } = startedAt;
        internal bool AwaitingOtp { get; set; }
        internal string OtpAuthenticatedClaim { get; set; }
        internal string TurnstileToken { get; set; }
        public override string ToString() => "Pending Wealthsimple login (credentials hidden)";
    }

    private sealed class Session(string accessToken, string refreshToken, string type, DateTimeOffset expiresAt)
    {
        internal string AccessToken { get; } = accessToken;
        internal string RefreshToken { get; } = refreshToken;
        internal string Type { get; } = type;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal string IdentityId { get; set; }
        public override string ToString() => "Wealthsimple session (tokens hidden)";
    }
}
