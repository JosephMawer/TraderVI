using Core.WSTrade.Authentication;
using System;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

// stdout is exclusively the native-messaging protocol. Never print exception or session values.
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
byte[] body = null;
try
{
    if (args.Length < 1 || args[0] != "chrome-extension://" + WealthsimpleEdgeBridgeProtocol.ExtensionId + "/")
        return 1;
    body = await WealthsimpleEdgeBridgeProtocol.ReadAsync(Console.OpenStandardInput(), timeout.Token);
    string pairing;
    using (var json = JsonDocument.Parse(body)) pairing = json.RootElement.GetProperty("pairing").GetString();
    using var pipe = new NamedPipeClientStream(".", WealthsimpleEdgeBridgeProtocol.PipeName(pairing),
        PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(10000, timeout.Token);
    await WealthsimpleEdgeBridgeProtocol.WriteAsync(pipe, body, timeout.Token);
    var receipt = await WealthsimpleEdgeBridgeProtocol.ReadAsync(pipe, timeout.Token);
    try { await WealthsimpleEdgeBridgeProtocol.WriteAsync(Console.OpenStandardOutput(), receipt, timeout.Token); }
    finally { CryptographicOperations.ZeroMemory(receipt); }
    return 0;
}
catch
{
    await WealthsimpleEdgeBridgeProtocol.WriteAsync(Console.OpenStandardOutput(),
        JsonSerializer.SerializeToUtf8Bytes(new { connected = false }), CancellationToken.None);
    return 1;
}
finally { if (body != null) CryptographicOperations.ZeroMemory(body); }
