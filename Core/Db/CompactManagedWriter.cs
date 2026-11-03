#nullable enable
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Orders;
using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

internal sealed class CompactReceiptProtector : IManualBuyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TraderVI.Engine.CompactReceipt.v1");
    public byte[] Protect(byte[] data) => OperatingSystem.IsWindows() ? ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser)
        : throw new PlatformNotSupportedException("Installed compact receipts use Windows account protection.");
    public byte[] Unprotect(byte[] data) => OperatingSystem.IsWindows() ? ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser)
        : throw new PlatformNotSupportedException("Installed compact receipts use Windows account protection.");
}

internal static class CompactManagedWriter
{
    internal static string DirectoryPath => Path.Combine(EnginePaths.DirectoryPath, "Compact");

    internal static async Task PublishAsync(SqlConnection connection, SqlTransaction transaction, BrokerManagedSession? prior,
        BrokerManagedSession next, CancellationToken ct)
    {
        // Legacy financial agreements never open this storage. The first explicit transfer runs separately.
        if (prior?.HasReviewedCompactRestart != true) return;
        var db = await EngineContinuityRepository.IdentityAsync(connection, transaction, ct);
        await PublishAsync(SqlTradingStorageDatabase.InTransaction(connection, transaction), prior, next, db,
            DirectoryPath, new CompactReceiptProtector(), ct, (binding, buy, filledAt, token) =>
                CompactOpeningProtection.ReadAsync(connection, transaction, binding, buy, filledAt, token));
    }

    internal static async Task PublishAsync(TradingCurrentStore store, BrokerManagedSession prior, BrokerManagedSession next,
        EngineDatabaseIdentity database, string directory, IManualBuyProtector protector, CancellationToken ct,
        CompactOpeningProtection.ReadIntent? readIntent = null)
    {
        var current = await store.ReadAsync(next.BindingId, ct);
        if (current == null) throw new InvalidOperationException("First transfer the reviewed compact account before advancing its strategy.");
        var sealedBook = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(directory, current.Account.Version), protector);
        if (sealedBook.ManagedRevision != prior.RevisionId || !sealedBook.Matches(next.BindingId, database, current))
            throw new InvalidDataException("The strategy and compact account do not share their last committed revision.");
        TradingOrder? settledSale = null;
        if (next.Event == CompactManagedSaleHandoff.Event)
        {
            var original = prior.Orders.Zip(next.Orders).Single(pair => !pair.First.Reconciled && pair.Second.Reconciled).First;
            settledSale = await store.ReadOrderAsync(next.BindingId, original.RequestId, ct);
            if (settledSale is not { Side: "SELL", Status: "Filled", NeedsWork: false } ||
                settledSale.PermissionId != current.Settings.PermissionId || current.Positions.Any(p => p.PositionId == original.PositionId))
                throw new InvalidDataException("The terminal sell and closed position must precede the managed handoff.");
        }
        var (write, approved) = next.Event == "ImplementationReviewed"
            ? await CompactBuildSuccessor.PrepareAsync(prior, next, current, sealedBook, readIntent, ct)
            : (CompactManagedPublication.Prepare(prior, next, current, settledSale), sealedBook.Assignment);
        var committing = store.WithCheckpointWriter(updated =>
        {
            if (next.Orders.All(o => o.Reconciled) &&
                updated.Account.ReservedPurchaseCost + updated.Account.CommittedPurchaseCost != next.CommittedPurchaseCost)
                throw new InvalidDataException("The original-order reservations and remaining purchase allowance disagree.");
            var active = updated.Orders.Where(o => o.Side == "BUY").Select(o => o.RequestId).ToHashSet();
            var terms = approved.Opening.Where(t => active.Contains(t.RequestId)).ToDictionary(t => t.RequestId);
            foreach (var buy in next.Orders.Where(o => active.Contains(o.RequestId) && !terms.ContainsKey(o.RequestId) &&
                o.Kind is "Initial" or "Reentry" && !o.PositionId.HasValue))
                terms.Add(buy.RequestId, new(next.BindingId, updated.Settings.PermissionId, buy.RequestId,
                    next.Strategy.VersionId.ToString("D"), "None", null, buy.PositionTarget)
                    { Progressive = next.Strategy.Strategy.ReadLivePolicy().UseProgressiveProfitProtection });
            var assignment = approved with { Opening = [.. terms.Values.OrderBy(t => t.RequestId)] };
            var seal = CompactDelphiCheckpoint.From(next.BindingId, database, assignment, updated) with { ManagedRevision = next.RevisionId };
            seal.Write(CompactAccountPreparation.Slot(directory, updated.Account.Version), protector);
        });
        await committing.SaveAsync(write, ct);
    }
}
