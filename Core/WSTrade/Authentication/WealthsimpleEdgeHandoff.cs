using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

/// <summary>One operator-started Edge login, paired to a user-only pipe for at most five minutes.</summary>
public sealed class WealthsimpleEdgeHandoff : IDisposable
{
    public string PairingId { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private readonly NamedPipeServerStream pipe;
    public WealthsimpleEdgeHandoff() => pipe = new(WealthsimpleEdgeBridgeProtocol.PipeName(PairingId),
        PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    // The callback takes ownership of the candidate even if verification fails.
    public async Task<bool> ReceiveAsync(Func<WealthsimpleAuthClient, Task<bool>> connect, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        await pipe.WaitForConnectionAsync(timeout.Token);
        var body = await WealthsimpleEdgeBridgeProtocol.ReadAsync(pipe, timeout.Token);
        bool connected = false;
        try
        {
            var candidate = Parse(body, PairingId);
            connected = await connect(candidate);
        }
        finally { CryptographicOperations.ZeroMemory(body); }
        // Handoff has already completed even if Edge closes before reading its receipt.
        try { await WealthsimpleEdgeBridgeProtocol.WriteAsync(pipe,
            JsonSerializer.SerializeToUtf8Bytes(new { connected }), timeout.Token); }
        catch (IOException) { }
        return connected;
    }

    internal static WealthsimpleAuthClient Parse(byte[] body, string pairing)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (!WealthsimpleEdgeBridgeProtocol.IsPairingId(pairing) || root.GetProperty("pairing").GetString() != pairing ||
                root.GetProperty("url").GetString() != WealthsimpleAuthClient.TokenUri.AbsoluteUri ||
                root.GetProperty("method").GetString() != "POST" || root.GetProperty("status").GetInt32() != 200)
                throw new InvalidDataException("Unsupported Edge login handoff.");
            var request = root.GetProperty("request");
            // Only public metadata crosses this boundary, never credentials or challenge claims.
            foreach (var property in request.EnumerateObject())
                if (property.Name is not ("grant_type" or "client_id")) throw new InvalidDataException("Unsupported Edge login metadata.");
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request);
            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(root.GetProperty("response"));
            try
            {
                using var requestStream = new MemoryStream(requestBytes);
                using var responseStream = new MemoryStream(responseBytes);
                return WealthsimpleAuthClient.FromBrowserExchange(WealthsimpleAuthClient.TokenUri, "POST", 200,
                    requestStream, responseStream, Optional(root, "deviceId"), Optional(root, "sessionId"));
            }
            finally { CryptographicOperations.ZeroMemory(requestBytes); CryptographicOperations.ZeroMemory(responseBytes); }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException or FormatException)
        { throw new InvalidDataException("The Edge login handoff is incomplete or unsupported."); }
    }
    private static string Optional(JsonElement root, string name) => root.TryGetProperty(name, out var item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    public void Dispose() => pipe.Dispose();
}
