#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Core.WSTrade.Orders;

/// <summary>
/// Write-once, per-Windows-user transmission fence. Stores hashes only, before any mutation is sent.
/// It is not a fill ledger or proof of broker idempotency. Never remove a marker to retry an order.
/// </summary>
internal sealed class WealthsimpleOrderAttemptJournal
{
    private readonly string directory;
    internal WealthsimpleOrderAttemptJournal() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TraderVI", "Wealthsimple", "OrderAttempts")) { }
    internal WealthsimpleOrderAttemptJournal(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute order-attempt directory is required.");
        this.directory = directory;
    }
    internal bool TryBegin(string identity, WealthsimpleMarketBuy order) => TryBegin(identity, WealthsimpleMarketOrder.From(order));
    internal bool TryBegin(string identity, WealthsimpleMarketSell order) => TryBegin(identity, WealthsimpleMarketOrder.From(order));
    internal bool TryBegin(string identity, WealthsimpleMarketOrder order)
    {
        Directory.CreateDirectory(directory);
        // Account/quantity/side changes under the same identity/request ID must not bypass the fence.
        string key = Hash(identity + "\n" + order.ExternalId);
        string path = Path.Combine(directory, key + ".attempt");
        FileStream file;
        try { file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough); }
        catch (IOException) when (File.Exists(path)) { return false; }
        using (file)
        {
            string fingerprint = Hash(order.Account.Id + "\n" + order.Security.Id + "\n" + order.Quantity + "\nMARKET\n" + order.OrderType + "\nDAY");
            file.Write(Encoding.ASCII.GetBytes("TraderVI.OrderAttempt.v1\n" + fingerprint + "\n"));
            file.Flush(flushToDisk: true);
        }
        return true;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
