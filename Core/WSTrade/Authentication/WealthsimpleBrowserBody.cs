using System;
using System.IO;
using System.Security.Cryptography;

namespace Core.WSTrade.Authentication;

/// <summary>Adapts WebView2 COM streams without relying on Length, Seek, Read(Span), or nonzero offsets,
/// which the runtime's stream does not implement. Keeps response size bounded.</summary>
public static class WealthsimpleBrowserBody
{
    public static MemoryStream Copy(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        const int maximum = 128 * 1024;
        var buffer = new byte[maximum + 1];
        var scratch = new byte[4096];
        var used = 0;
        try
        {
            int count;
            while ((count = source.Read(scratch, 0, scratch.Length)) > 0)
            {
                if (count > maximum - used) throw new InvalidDataException("The sign-in message exceeds the supported size.");
                Buffer.BlockCopy(scratch, 0, buffer, used, count); used += count;
            }
            return new ClearedMemoryStream(buffer, used);
        }
        catch { CryptographicOperations.ZeroMemory(buffer); throw; }
        finally { CryptographicOperations.ZeroMemory(scratch); }
    }

    private sealed class ClearedMemoryStream : MemoryStream
    {
        private readonly byte[] storage;
        public ClearedMemoryStream(byte[] buffer, int count) : base(buffer, 0, count, false, false) => storage = buffer;
        protected override void Dispose(bool disposing)
        { CryptographicOperations.ZeroMemory(storage); base.Dispose(disposing); }
    }
}
