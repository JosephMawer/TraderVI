#nullable enable
using Core.WSTrade.Authentication;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TraderVI.WPF.Services;

public sealed class WindowsWealthsimpleSessionStore : IWealthsimpleSessionStore, IWealthsimpleSessionProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TraderVI.Wealthsimple.Session.v1");
    private readonly string directory;
    private FileStream? lease;
    public WindowsWealthsimpleSessionStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Wealthsimple")) { }
    public WindowsWealthsimpleSessionStore(string directory) => this.directory = Path.GetFullPath(directory);
    private string SessionPath => Path.Combine(directory, "session.dat");

    public void Open()
    {
        if (lease != null) return;
        Directory.CreateDirectory(directory);
        // One process owns refresh-token rotation, including when multiple dashboards are launched.
        lease = new FileStream(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private void RequireLease() { if (lease == null) throw new InvalidOperationException("Session storage is not open."); }
    public WealthsimpleAuthClient? Load()
    {
        RequireLease();
        if (!File.Exists(SessionPath)) return null;
        try
        {
            if (new FileInfo(SessionPath).Length > 128 * 1024) throw new InvalidDataException();
            return WealthsimpleAuthClient.FromProtectedSession(File.ReadAllBytes(SessionPath), this);
        }
        catch { Clear(); throw new InvalidDataException("The saved sign-in could not be opened."); }
    }
    public async Task SaveAsync(WealthsimpleAuthClient client, CancellationToken ct)
    {
        RequireLease();
        byte[] encrypted = await client.ProtectSessionAsync(this, ct);
        var temporary = Path.Combine(directory, "session.pending");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(encrypted, ct);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, SessionPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Clear()
    {
        RequireLease();
        File.Delete(SessionPath);
        File.Delete(Path.Combine(directory, "session.pending"));
    }
    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] ciphertext) => ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
    public void Dispose() { lease?.Dispose(); lease = null; }
}
