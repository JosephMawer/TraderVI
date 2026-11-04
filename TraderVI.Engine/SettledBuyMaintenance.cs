#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Services;

namespace TraderVI.Engine;

// Exclusive, pinned, already-filled buy handoff. No scheduler, strategy evaluation or order transport.
internal static class SettledBuyMaintenance
{
    private sealed record Target(string InstalledDirectory, Guid RevisionId, Guid RequestId);
    internal static async Task<int> RunAsync(string packet)
    {
        string stage = "Target";
        try
        {
            var target = JsonSerializer.Deserialize<Target>(File.ReadAllText(packet));
            if (target == null || target.RevisionId == Guid.Empty || target.RequestId == Guid.Empty ||
                !Path.IsPathFullyQualified(target.InstalledDirectory) || !File.Exists(Path.Combine(target.InstalledDirectory, "TraderVI.Engine.exe"))) return 64;
            string build = "CoreSha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(target.InstalledDirectory, "Core.dll"))));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var ct = timeout.Token;
            stage = "Ownership";
            using var owner = new EngineOwnershipLease(EnginePaths.OwnershipPath);
            string orders = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Wealthsimple", "ManualOrders");
            var protector = new WindowsManualBuyProtector();
            using var journal = new ManualBuyFileJournal(orders, protector);
            var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(orders, "execution-host.dat"), protector);
            using var fence = grants.AcquireFence();
            var grant = grants.Read() ?? throw new InvalidDataException();
            var continuity = await new EngineContinuityRepository().ReadAsync(ct);
            var witness = new EngineContinuityFile(Path.Combine(EnginePaths.DirectoryPath, "continuity.dat"), protector).Read();
            if (witness is not { Clean: true } || continuity.Database != witness.Database || continuity.Stamp != witness) throw new InvalidDataException();
            var calendar = Core.Trader.DelphiLive.ReviewedTsxSessionCalendar.Load(
                Environment.GetEnvironmentVariable(DelphiLiveDesktopService.CalendarPathEnvironmentVariable)!);
            await using var lease = await new BrokerManagedSessionRepository(calendar, sharedStorage: true).AcquireAsync(grant.BindingId, ct);
            var saved = lease.Current ?? throw new InvalidDataException();
            var store = new SqlTradingStorageDatabase(EngineIntegrityRepository.OpenAsync).CreateStore();
            var book = await store.ReadAsync(grant.BindingId, ct) ?? throw new InvalidDataException();
            var seal = CompactDelphiCheckpoint.Read(CompactAccountPreparation.Slot(CompactManagedWriter.DirectoryPath, book.Account.Version), new CompactReceiptProtector());
            stage = "PriorAssignment";
            if (seal.ManagedRevision != saved.RevisionId || !seal.Matches(grant.BindingId, continuity.Database, book) ||
                !saved.HasReviewedCompactRestart || !book.Orders.IsEmpty || !book.Restrictions.IsEmpty ||
                saved.ReviewInvalidated || saved.TrialOrderReviewRequired) throw new InvalidDataException();
            void CheckAssignment()
            {
                if (grants.Read() != grant) throw new InvalidDataException();
                BrokerExecutionHost.RequireMatchingAssignment(grant, saved, build, DateTimeOffset.UtcNow);
                seal.Assignment.RequireAssignment(book, grant, build, DateTimeOffset.UtcNow, allowReadRecovery: true);
            }
            CheckAssignment();
            if (saved.Event == SettledBuyTransition.Event && saved.PreviousRevisionId == target.RevisionId &&
                saved.RevisionId == BrokerManagedStrategy.StableId($"{target.RevisionId:D}:{target.RequestId:D}:{SettledBuyTransition.Event}") &&
                saved.Orders.Any(o => o.RequestId == target.RequestId && o.Side == "BUY" && o.Reconciled))
            { Console.WriteLine("OriginalBuyAlreadyReconciled"); return 0; }
            if (saved.RevisionId != target.RevisionId || saved.Orders.Count(o => !o.Reconciled) != 1 ||
                saved.Orders.Single(o => !o.Reconciled).RequestId != target.RequestId) throw new InvalidDataException();
            var receipt = await store.ReadOrderAsync(grant.BindingId, target.RequestId, ct) ?? throw new InvalidDataException();
            var record = new RestartOrderJournal(journal, grant.BindingId, seal.Assignment.Identity, seal.Assignment.Account).FindOriginal(receipt);
            if (record is not { IsResolved: true, Latest: { Status: "FILLED" or "POSTED" } latest } ||
                latest.FilledQuantity != receipt.AppliedFilledQuantity || latest.FilledNetValue?.Amount != receipt.AppliedNetValue)
                throw new InvalidDataException();
            await using var integrity = await new EngineIntegrityRepository().AcquireProtectionLeaseAsync(grant.BindingId, ct);
            if (integrity.BlockReason != null) throw new InvalidDataException();
            stage = "BrokerOriginalBuy";
            using var credentials = new WindowsWealthsimpleSessionStore();
            var broker = new WealthsimpleConnection(credentials);
            try
            {
                await broker.StartAsync();
                long login = broker.SessionRevision;
                var catalog = await broker.ReadTfsaAccountsAsync(ct);
                if (catalog.Status != WealthsimpleReadStatus.Success || catalog.Data == null) throw new InvalidDataException();
                var account = BrokerManagedSessionComposition.MatchAccount(saved, catalog.Data);
                var read = await broker.ReadOriginalOrderAsync(account, seal.Assignment.Identity, receipt, ct);
                if (read.Status != WealthsimpleReadStatus.Success || read.Data == null) throw new InvalidDataException();
                stage = "BrokerCurrentAccount";
                using var gate = new SemaphoreSlim(1, 1);
                var reconciliation = new BrokerReconciliationWorkflow(broker, journal, journal, gate);
                var imports = new BrokerAccountImportRepository();
                var synchronized = await new BrokerAccountSynchronizationWorkflow(new BrokerImportWorkflow(imports)).RefreshAsync(
                    token => reconciliation.ReadAsync(account, login, token), ct);
                var imported = synchronized.Saved ?? throw new InvalidDataException();
                if (synchronized.Read.PendingOrders.Orders.Count != 0) throw new InvalidDataException();
                var evidence = await new BrokerOngoingRiskEvidenceSource(saved.OngoingManagement!, calendar,
                    (_, _, _) => throw new NotSupportedException("Maintenance cannot request market data."), preserveRiskHistory: true)
                    .ReadAsync(imported, saved.Account.Risk, saved.Orders, ct);
                if (!evidence.Blocks.IsEmpty || !evidence.Continuations.IsEmpty) throw new InvalidDataException();
                var binding = (await imports.ReadAsync(ct)).Binding ?? throw new InvalidDataException();
                var projected = BrokerAccountProjector.Project(binding, imported, saved.Account.Risk, evidence.Coverage, DateTimeOffset.UtcNow);
                stage = "OriginalBuyHandoff";
                var next = SettledBuyTransition.Prepare(saved, book, receipt, read.Data, projected,
                    target.RevisionId, target.RequestId, DateTimeOffset.UtcNow);
                if (!broker.IsConnected || broker.SessionRevision != login) throw new InvalidDataException();
                CheckAssignment(); ct.ThrowIfCancellationRequested();
                await lease.AppendAsync(next, ct);
                Console.WriteLine("OriginalBuyReconciledAndProtectionPreserved");
                return 0;
            }
            finally { await broker.StopAsync(); }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SettledBuyMaintenance/{stage}/{ex.GetType().Name}");
            return 2;
        }
    }
}
