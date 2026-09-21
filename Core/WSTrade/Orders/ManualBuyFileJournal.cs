#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Core.WSTrade.Accounts;

namespace Core.WSTrade.Orders;

public interface IManualBuyProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>Exclusive, protected, atomic order storage. Never contains authentication secrets.</summary>
public sealed class ManualBuyFileJournal : IManualSellJournal, IBrokerReconciliationJournal
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
    private static string ReconciliationStem(string identity, string account) =>
        "account-" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { identity, account })));
    private string? LatestReconciliationPath(string identity, string account)
    {
        string stem = ReconciliationStem(identity, account);
        var paths = Directory.EnumerateFiles(directory, stem + "-*.reconciliation").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        foreach (var path in paths)
        {
            string suffix = Path.GetFileNameWithoutExtension(path)[(stem.Length + 1)..];
            if (suffix.Length != 12 || !long.TryParse(suffix, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var sequence) || sequence <= 0)
                throw new InvalidDataException("Account history requires recovery.");
        }
        return paths.LastOrDefault();
    }
    public BrokerAccountReconciliation? ReadReconciliation(string identityId, string accountId)
    {
        lock (sync)
        {
            Open(); var path = LatestReconciliationPath(identityId, accountId);
            if (path == null) return null;
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Account history requires recovery.");
            byte[] plaintext = protector.Unprotect(File.ReadAllBytes(path));
            try
            {
                var saved = JsonSerializer.Deserialize<ReconciliationEnvelope>(plaintext);
                if (saved?.Version != 1 || saved.Account?.IdentityId != identityId || saved.Account.AccountId != accountId ||
                    saved.Account.Orders == null || saved.Account.Holdings == null || saved.Account.Blocks == null || saved.Account.HistoryConflicts == null)
                    throw new InvalidDataException("Account history requires recovery.");
                return saved.Account;
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }
    public void SaveReconciliation(BrokerAccountReconciliation result)
    {
        lock (sync)
        {
            Open();
            if (string.IsNullOrWhiteSpace(result.IdentityId) || string.IsNullOrWhiteSpace(result.AccountId))
                throw new InvalidDataException("The account history identity is missing.");
            var previous = ReadReconciliation(result.IdentityId, result.AccountId);
            if (previous != null && (result.CheckedAt < previous.CheckedAt || previous.HistoryConflicts.Except(result.HistoryConflicts).Any()))
                throw new InvalidOperationException("Saved reconciliation history cannot be rolled back or lose an unresolved conflict.");
            string stem = ReconciliationStem(result.IdentityId, result.AccountId);
            var latest = LatestReconciliationPath(result.IdentityId, result.AccountId);
            long sequence = latest == null ? 1 : checked(long.Parse(Path.GetFileNameWithoutExtension(latest)[(stem.Length + 1)..]) + 1);
            if (sequence > 999999999999) throw new InvalidDataException("Account history requires recovery.");
            var path = Path.Combine(directory, stem + "-" + sequence.ToString("D12", System.Globalization.CultureInfo.InvariantCulture) + ".reconciliation");
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new ReconciliationEnvelope(1, result));
            byte[] encrypted;
            try { encrypted = protector.Protect(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            var pending = path + ".pending";
            using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(encrypted); stream.Flush(flushToDisk: true); }
            // Append observations so later reconciliation never erases the account's earlier facts.
            File.Move(pending, path, overwrite: false);
        }
    }
    public void Dispose() { lock (sync) { disposed = true; lease?.Dispose(); lease = null; } }
    private sealed record Envelope(int Version, ManualBuyRecord Order);
    private sealed record SellEnvelope(int Version, ManualSellRecord Order);
    private sealed record ReconciliationEnvelope(int Version, BrokerAccountReconciliation Account);
}
