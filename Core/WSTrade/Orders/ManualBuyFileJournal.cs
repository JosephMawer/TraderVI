#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.WSTrade.Orders;

public interface IManualBuyProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>Exclusive, protected, atomic order storage. Never contains authentication secrets.</summary>
public sealed class ManualBuyFileJournal : IManualSellJournal
{
    private readonly string directory;
    private readonly IManualBuyProtector protector;
    private FileStream? lease;
    private bool disposed;
    private readonly object sync = new();
    public ManualBuyFileJournal(string directory, IManualBuyProtector protector)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute journal path is required.");
        this.directory = Path.GetFullPath(directory); this.protector = protector;
    }
    private void Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (lease != null) return;
        Directory.CreateDirectory(directory);
        lease = new FileStream(Path.Combine(directory, "orders.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public IReadOnlyList<ManualBuyRecord> ReadAll()
    {
        lock (sync)
        {
            Open();
            return Directory.EnumerateFiles(directory, "*.order").Select(Read).OrderByDescending(x => x.Intent.CreatedAt).ToArray();
        }
    }
    // Both sides share the same directory lease and atomic storage protocol. Existing v1 buy
    // records keep their extension and envelope, so adding sell support requires no migration.
    public bool HasUnresolvedOrders() => ReadAll().Any(o => !o.IsResolved) || ReadSells().Any(o => !o.IsResolved);
    public IReadOnlyList<ManualSellRecord> ReadSells()
    {
        lock (sync)
        {
            Open();
            return Directory.EnumerateFiles(directory, "*.sell").Select(ReadSell).OrderByDescending(x => x.Intent.CreatedAt).ToArray();
        }
    }
    private ManualSellRecord ReadSell(string path)
    {
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) || new FileInfo(path).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("A sell record needs recovery.");
        byte[] plaintext = protector.Unprotect(File.ReadAllBytes(path));
        try
        {
            var saved = JsonSerializer.Deserialize<SellEnvelope>(plaintext);
            if (saved?.Version != 1 || saved.Order == null || saved.Order.Intent.RequestId != id || saved.Order.Observations == null)
                throw new InvalidDataException("A sell record needs recovery.");
            saved.Order.Intent.Validate();
            return saved.Order;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public void SaveSell(ManualSellRecord record)
    {
        lock (sync)
        {
            Open(); record.Intent.Validate();
            var path = Path.Combine(directory, record.Intent.RequestId.ToString("N") + ".sell");
            if (File.Exists(path) && ReadSell(path).Intent != record.Intent)
                throw new InvalidOperationException("A sell's reviewed intent cannot be replaced.");
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new SellEnvelope(1, record));
            byte[] encrypted;
            try { encrypted = protector.Protect(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            var pending = path + ".pending";
            using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(encrypted); stream.Flush(flushToDisk: true);
            }
            File.Move(pending, path, overwrite: true);
        }
    }
    private ManualBuyRecord Read(string path)
    {
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) || new FileInfo(path).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("An order record needs recovery.");
        byte[] plaintext = protector.Unprotect(File.ReadAllBytes(path));
        try
        {
            var saved = JsonSerializer.Deserialize<Envelope>(plaintext);
            if (saved?.Version != 1 || saved.Order == null || saved.Order.Intent.RequestId != id || saved.Order.Observations == null)
                throw new InvalidDataException("An order record needs recovery.");
            saved.Order.Intent.Validate();
            return saved.Order;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public void Save(ManualBuyRecord record)
    {
        lock (sync)
        {
            Open(); record.Intent.Validate();
            var path = Path.Combine(directory, record.Intent.RequestId.ToString("N") + ".order");
            if (File.Exists(path) && Read(path).Intent != record.Intent)
                throw new InvalidOperationException("An order's reviewed intent cannot be replaced.");
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, record));
            byte[] encrypted;
            try { encrypted = protector.Protect(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            var pending = path + ".pending";
            // No transmission is permitted until the flushed initial record has been renamed.
            // An orphan pending file is never authoritative; an earlier committed record survives
            // an interrupted status update. The next save can replace that pending file safely.
            using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(encrypted); stream.Flush(flushToDisk: true);
            }
            File.Move(pending, path, overwrite: true);
        }
    }
    public void Dispose() { lock (sync) { disposed = true; lease?.Dispose(); lease = null; } }
    private sealed record Envelope(int Version, ManualBuyRecord Order);
    private sealed record SellEnvelope(int Version, ManualSellRecord Order);
}
