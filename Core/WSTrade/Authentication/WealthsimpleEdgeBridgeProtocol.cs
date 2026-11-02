using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Core.WSTrade.Authentication;

// Linked into the standalone native-messaging host; no broker or application dependencies.
public static class WealthsimpleEdgeBridgeProtocol
{
    public const string ExtensionId = "ikocfanmafmhmmidhampgihpjnjpiffd";
    public const string HostName = "com.tradervi.wealthsimple_login";
    public const int MaximumMessageBytes = 128 * 1024;
    public static bool IsPairingId(string value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string PipeName(string pairing) => IsPairingId(pairing)
        ? "TraderVI.EdgeLogin." + pairing : throw new InvalidDataException("Invalid login pairing.");

    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > MaximumMessageBytes) throw new InvalidDataException("Invalid login message size.");
        var body = new byte[length];
        try { await stream.ReadExactlyAsync(body, ct); return body; }
        catch { CryptographicOperations.ZeroMemory(body); throw; }
    }

    public static async Task WriteAsync(Stream stream, byte[] body, CancellationToken ct)
    {
        if (body.Length <= 0 || body.Length > MaximumMessageBytes) throw new InvalidDataException("Invalid login message size.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, body.Length);
        await stream.WriteAsync(prefix, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
