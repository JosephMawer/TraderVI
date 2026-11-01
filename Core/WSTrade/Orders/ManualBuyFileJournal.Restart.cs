#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.WSTrade.Orders;

public sealed partial class ManualBuyFileJournal
{
    internal ManualBuyRecord? FindBuy(Guid request) => FindReceipt(request, ".order", Read);
    internal ManualSellRecord? FindSell(Guid request) => FindReceipt(request, ".sell", ReadSell);

    private T? FindReceipt<T>(Guid request, string extension, Func<string, T> read) where T : class
    {
        lock (sync)
        {
            Open();
            if (request == Guid.Empty) throw new ArgumentException("Missing original journal request ID.");
            string path = Path.Combine(directory, request.ToString("N") + extension);
            try { return read(path); }
            catch (FileNotFoundException error) when (error.FileName == path) { return null; }
        }
    }

    internal sealed record RestartHolding(string Security, Guid Position, DateTime EntryUtc);
    private sealed record SellScope(RestartHolding Holding, HashSet<Guid> Requests)
    {
        internal bool Conflicts(ManualSellRecord sale, string identity) =>
            sale.Intent.IdentityId != identity || !sale.IsResolved ||
            (sale.Latest?.FilledQuantity > 0 && !(sale.Latest.LastFilledAt < Holding.EntryUtc));
    }

    private sealed record SellIndex(int Version, string Identity, string Account, Dictionary<string, SellScope> Scopes);

    // Explicit preparation under the original writer lease. Replace the watched holdings atomically.
    // Ordinary restart never enumerates historical receipts or silently rebuilds missing coverage.
    internal void PrepareRestartSellScopes(string identity, string account, params RestartHolding[] holdings)
    {
        lock (sync)
        {
            Open();
            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(account) || holdings.Any(h =>
                string.IsNullOrWhiteSpace(h.Security) || h.Position == Guid.Empty || h.EntryUtc == default || h.EntryUtc.Kind != DateTimeKind.Utc))
                throw new ArgumentException("Missing journal position boundary.");
            var scopes = holdings.ToDictionary(h => h.Security, h => new SellScope(h, []), StringComparer.Ordinal);
            foreach (var sale in Directory.EnumerateFiles(directory, "*.sell").Select(ReadSell))
                if (sale.Intent.AccountId == account && scopes.TryGetValue(sale.Intent.SecurityId, out var scope) && scope.Conflicts(sale, identity))
                    scope.Requests.Add(sale.Intent.RequestId);
            WriteSellIndex(new(1, identity, account, scopes));
        }
    }

    internal void RequireClearForRestartSell(string identity, string account, string security, Guid position,
        DateTime entryUtc, Guid originalRequest)
    {
        lock (sync)
        {
            Open();
            if (FindSell(originalRequest) != null || FindBuy(originalRequest) != null)
                throw new InvalidOperationException("The original request is already in the order journal. Reconcile its original ID.");
            var index = ReadSellIndex(account);
            if (index == null || index.Identity != identity || !index.Scopes.TryGetValue(security, out var scope) ||
                scope.Holding.Position != position || scope.Holding.EntryUtc != entryUtc)
                throw new InvalidOperationException("The journal lookup for this holding needs preparation.");
            foreach (var request in scope.Requests)
            {
                var sale = FindSell(request) ?? throw new InvalidOperationException("A journal sale pointer needs reconciliation.");
                if (sale.Intent.AccountId != account || sale.Intent.SecurityId != security || scope.Conflicts(sale, identity))
                    throw new InvalidOperationException("Reconcile the journal sale on these shares before dispatching the protective exit.");
            }
        }
    }

    private string SellIndexPath(string account) => Path.Combine(directory, "restart-sales-" +
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(account))) + ".scope");

    private SellIndex? ReadSellIndex(string account)
    {
        byte[] encrypted;
        try { encrypted = File.ReadAllBytes(SellIndexPath(account)); }
        catch (FileNotFoundException) { return null; }
        var clear = protector.Unprotect(encrypted);
        try
        {
            var index = JsonSerializer.Deserialize<SellIndex>(clear);
            if (index is not { Version: 1, Scopes: not null } || index.Account != account || string.IsNullOrWhiteSpace(index.Identity) ||
                index.Scopes.Any(p => p.Value?.Holding is not { } h || p.Key != h.Security || h.Position == Guid.Empty ||
                    h.EntryUtc.Kind != DateTimeKind.Utc || p.Value.Requests == null || p.Value.Requests.Contains(Guid.Empty)))
                throw new InvalidDataException("Invalid protected journal sale lookup.");
            return index;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private void WriteSellIndex(SellIndex index)
    {
        string path = SellIndexPath(index.Account);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(index);
        try
        {
            byte[] encrypted = protector.Protect(clear);
            using (var stream = new FileStream(path + ".pending", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(encrypted);
                stream.Flush(true);
            }
            File.Move(path + ".pending", path, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}
