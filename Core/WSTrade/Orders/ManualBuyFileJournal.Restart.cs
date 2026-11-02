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
    public ManualSellRecord? FindSell(Guid request) => FindReceipt(request, ".sell", ReadSell);

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
    private sealed record SellScope(RestartHolding Holding, HashSet<Guid> Requests, Guid? UnattributedFill = null)
    {
        internal bool Conflicts(ManualSellRecord sale, string identity) =>
            sale.Intent.IdentityId != identity || !sale.IsResolved ||
            (sale.Latest?.FilledQuantity > 0 && !(sale.Latest.LastFilledAt < Holding.EntryUtc));
    }

    private sealed record SellIndex(int Version, string Identity, string Account, Dictionary<string, SellScope> Scopes,
        Guid Generation = default);

    // Outside the current watch list, keep outstanding/ambiguous IDs and just the latest dated fill per security.
    // This catalog is needed when adding a position. Unchanged startup reads only the current watch list.
    private sealed class SaleEvidence
    {
        public HashSet<Guid> Pending { get; init; } = [];
        public DateTimeOffset? LastFill { get; set; }
        public Guid? LastRequest { get; set; }

        internal static bool NeedsReceipt(ManualSellRecord sale, string identity) =>
            sale.Intent.IdentityId != identity || !sale.IsResolved ||
            (sale.Latest?.FilledQuantity > 0 && sale.Latest.LastFilledAt == null);

        internal void Observe(ManualSellRecord sale, string identity)
        {
            var id = sale.Intent.RequestId;
            if (NeedsReceipt(sale, identity))
            {
                Pending.Add(id);
                return;
            }
            Pending.Remove(id);
            if (sale.Latest?.FilledQuantity > 0 && sale.Latest.LastFilledAt is { } at && (LastFill == null || at > LastFill))
            {
                LastFill = at;
                LastRequest = id;
            }
        }
    }
    private sealed record SaleCatalog(Guid Generation, Dictionary<string, SaleEvidence> Securities);

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
            var catalog = new SaleCatalog(Guid.NewGuid(), new(StringComparer.Ordinal));
            foreach (var sale in Directory.EnumerateFiles(directory, "*.sell").Select(ReadSell))
            {
                if (sale.Intent.AccountId != account) continue;
                Observe(catalog, sale, identity);
                if (scopes.TryGetValue(sale.Intent.SecurityId, out var scope) && scope.Conflicts(sale, identity))
                    scope.Requests.Add(sale.Intent.RequestId);
            }
            WriteProtected(SellIndexPath(account) + ".catalog", catalog);
            WriteSellIndex(new(2, identity, account, scopes, catalog.Generation));
        }
    }

    private static void Observe(SaleCatalog catalog, ManualSellRecord sale, string identity)
    {
        string security = sale.Intent.SecurityId;
        if (!catalog.Securities.TryGetValue(security, out var evidence))
            catalog.Securities[security] = evidence = new();
        evidence.Observe(sale, identity);
        if (evidence.Pending.Count == 0 && evidence.LastFill == null) catalog.Securities.Remove(security);
    }

    internal void AlignRestartHoldings(string identity, string account, IReadOnlyList<RestartHolding> holdings)
    {
        lock (sync)
        {
            Open();
            var index = ReadSellIndex(account);
            if (index is not { Version: 2 } || index.Identity != identity)
                throw new InvalidOperationException("Prepare the current account's journal lookup before updating its holdings.");
            var unique = holdings.GroupBy(h => h.Security, StringComparer.Ordinal).Where(g => g.Count() == 1)
                .Select(g => g.Single()).ToArray(); // Ambiguous overlapping cycles cannot authorize a sale.
            if (index.Scopes.Count == unique.Length && unique.All(h => index.Scopes.GetValueOrDefault(h.Security)?.Holding == h)) return;
            var next = new Dictionary<string, SellScope>(StringComparer.Ordinal);
            SaleCatalog? catalog = null;
            foreach (var holding in unique)
            {
                var old = index.Scopes.GetValueOrDefault(holding.Security);
                if (old?.Holding == holding) { next.Add(holding.Security, old); continue; }
                catalog ??= ReadCatalog(index);
                var scope = new SellScope(holding, []);
                if (catalog.Securities.TryGetValue(holding.Security, out var evidence))
                {
                    scope.Requests.UnionWith(evidence.Pending);
                    if (evidence.LastFill >= holding.EntryUtc) scope = scope with { UnattributedFill = evidence.LastRequest };
                }
                next.Add(holding.Security, scope);
            }
            WriteSellIndex(index with { Scopes = next });
        }
    }

    private SaleCatalog ReadCatalog(SellIndex index)
    {
        var clear = protector.Unprotect(File.ReadAllBytes(SellIndexPath(index.Account) + ".catalog"));
        try
        {
            var catalog = JsonSerializer.Deserialize<SaleCatalog>(clear);
            if (catalog is not { Securities: not null } || catalog.Generation != index.Generation ||
                catalog.Securities.Values.Any(e => e?.Pending == null || e.Pending.Contains(Guid.Empty) ||
                    e.LastFill.HasValue != e.LastRequest.HasValue || e.LastRequest == Guid.Empty))
                throw new InvalidDataException("The prepared sale catalog needs recovery.");
            return catalog;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
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
            if (index is not { Version: 2 } || index.Identity != identity || !index.Scopes.TryGetValue(security, out var scope) ||
                scope.Holding.Position != position || scope.Holding.EntryUtc != entryUtc)
                throw new InvalidOperationException("The journal lookup for this holding needs preparation.");
            if (scope.UnattributedFill.HasValue)
                throw new InvalidOperationException("Reconcile the journal fill on these shares before dispatching the protective exit.");
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
            if (index is not { Version: 1 or 2, Scopes: not null } || (index.Version == 2 && index.Generation == Guid.Empty) ||
                index.Account != account || string.IsNullOrWhiteSpace(index.Identity) ||
                index.Scopes.Any(p => p.Value?.Holding is not { } h || p.Key != h.Security || h.Position == Guid.Empty ||
                    h.EntryUtc.Kind != DateTimeKind.Utc || p.Value.Requests == null || p.Value.Requests.Contains(Guid.Empty)))
                throw new InvalidDataException("Invalid protected journal sale lookup.");
            return index;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private void WriteSellIndex(SellIndex index) => WriteProtected(SellIndexPath(index.Account), index);

    private void WriteProtected<T>(string path, T value)
    {
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(value);
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
