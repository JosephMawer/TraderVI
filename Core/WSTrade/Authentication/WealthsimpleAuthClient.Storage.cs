using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

public sealed partial class WealthsimpleAuthClient
{
    /// <summary>Consumes only an observed successful token exchange, never replays credentials.
    /// The caller must restrict observation to the exact production token URI and HTTP 200.</summary>
    public static WealthsimpleAuthClient FromBrowserExchange(Uri uri, string method, int status,
        Stream requestBody, Stream responseBody, string deviceId = null, string sessionId = null)
    {
        if (uri != TokenUri || method != "POST" || status != 200)
            throw new InvalidDataException("This is not a successful Wealthsimple token exchange.");
        try
        {
            using var request = JsonDocument.Parse(requestBody);
            using var response = JsonDocument.Parse(responseBody);
            var grant = StringValue(request.RootElement, "grant_type");
            if (grant is not ("password" or "refresh_token" or "webauthn"))
                throw new InvalidDataException("A completed user login is required.");
            var root = response.RootElement;
            var type = StringValue(root, "token_type");
            if (!string.Equals(type, "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This browser session needs a signing key that TraderVI cannot yet use.");
            var created = root.GetProperty("created_at").GetInt64();
            var seconds = root.GetProperty("expires_in").GetInt64();
            if (seconds <= 0 || seconds > 31 * 86400 || DateTimeOffset.FromUnixTimeSeconds(created) > DateTimeOffset.UtcNow.AddMinutes(5))
                throw new FormatException();
            return RestoreFields(StringValue(request.RootElement, "client_id"), StringValue(root, "access_token"),
                StringValue(root, "refresh_token"), DateTimeOffset.FromUnixTimeSeconds(created).AddSeconds(seconds),
                deviceId, sessionId, "@wealthsimple/wealthsimple");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException or OverflowException)
        { throw new InvalidDataException("The Wealthsimple login response is incomplete or unsupported."); }
    }

    /// <summary>Returns ciphertext only; credentials, OTP claims and browser cookies are excluded.
    /// DPoP cannot be saved without its original key, so export is limited to bearer sessions.</summary>
    public async Task<byte[]> ProtectSessionAsync(IWealthsimpleSessionProtector protector, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(ct);
        byte[] plaintext = null;
        try
        {
            if (session == null || session.Type != "Bearer")
                throw new InvalidOperationException("There is no supported session to remember.");
            plaintext = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1, clientId, access = session.AccessToken, refresh = session.RefreshToken,
                expires = session.ExpiresAt, deviceId, sessionId, webClientHeader
            });
            return protector.Protect(plaintext);
        }
        finally
        {
            if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext);
            gate.Release();
        }
    }

    /// <summary>Restoring a saved session does not prove authentication. Verify or refresh before use.</summary>
    public static WealthsimpleAuthClient FromProtectedSession(byte[] ciphertext, IWealthsimpleSessionProtector protector)
    {
        byte[] plaintext = null;
        try
        {
            if (ciphertext == null || ciphertext.Length > 128 * 1024) throw new FormatException();
            plaintext = protector.Unprotect(ciphertext);
            if (plaintext.Length > 128 * 1024) throw new FormatException();
            using var json = JsonDocument.Parse(plaintext);
            var root = json.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) throw new FormatException();
            return RestoreFields(StringValue(root, "clientId"), StringValue(root, "access"), StringValue(root, "refresh"),
                root.GetProperty("expires").GetDateTimeOffset(), StringValue(root, "deviceId"),
                StringValue(root, "sessionId"), StringValue(root, "webClientHeader"));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or CryptographicException or System.Collections.Generic.KeyNotFoundException)
        { throw new InvalidDataException("The saved Wealthsimple session could not be unlocked. Sign in again."); }
        finally { if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static WealthsimpleAuthClient RestoreFields(string id, string access, string refresh,
        DateTimeOffset expires, string device, string browserSession, string library)
    {
        if (!SafeHeaderValue(id) || id.Length > 256 || !SafeHeaderValue(access) ||
            (refresh != null && !SafeHeaderValue(refresh)) || (device != null && !SafeHeaderValue(device)) ||
            (browserSession != null && !SafeHeaderValue(browserSession)) || expires > DateTimeOffset.UtcNow.AddDays(32))
            throw new FormatException();
        return new WealthsimpleAuthClient(id)
        {
            session = new Session(access, refresh, "Bearer", expires), deviceId = device,
            sessionId = browserSession, webClientHeader = library == "@wealthsimple/wealthsimple" ? library : null
        };
    }
}
